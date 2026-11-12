using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>Turns a <c>MapGrpc*</c> call site into an <see cref="EndpointModel"/> (all symbol work happens here).</summary>
internal static class CallSiteParser
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken _)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var n }
        } && n.StartsWith("MapGrpc", StringComparison.Ordinal) && n != "MapGrpcService" && n != "MapGrpcEndpoints";

    public static CallSiteResult? Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        var model = ctx.SemanticModel;
        if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method || !RouteResolver.IsMapGrpcMethod(method))
            return null;
        if (method.TypeArguments.Length is < 1 or > 2 || method.TypeArguments[0] is not INamedTypeSymbol handler)
            return null;

        var diags = new List<DiagnosticInfo>();
        var location = LocationInfo.From(inv);
        var callName = method.Name + "<" + handler.Name + ">";
        var result = Build(ctx, inv, method, handler, callName, location, diags, ct);
        return new CallSiteResult(result, diags.ToEquatableArray());
    }

    private static EndpointModel? Build(GeneratorSyntaxContext ctx, InvocationExpressionSyntax inv, IMethodSymbol method,
        INamedTypeSymbol handler, string callName, LocationInfo? location, List<DiagnosticInfo> diags, CancellationToken ct)
    {
        var model = ctx.SemanticModel;
        var routeArg = FindArgument(inv, method, "route", 0);
        if (routeArg is null || model.GetConstantValue(routeArg.Expression, ct).Value is not string relativeRoute)
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.RouteNotConstant, location, callName));
            return null;
        }

        var receiver = RouteResolver.ResolveReceiver(inv, model, ct);
        if (receiver.HasNonConstantPrefix)
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.RouteNotConstant, location, callName));
            return null;
        }
        if (RouteResolver.ValidateRelativeRoute(relativeRoute, RouteResolver.CountSegments(receiver.Prefixes)) is not null)
            return null; // reported by DG001
        var parsed = RouteResolver.Parse(receiver.Prefixes, relativeRoute, receiver.Version);
        if (parsed is null)
            return null; // reported by DG001

        // ---- handler shape
        var execute = FindExecute(handler);
        if (execute is null)
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedHandler, location, handler.Name,
                "no public instance ExecuteAsync method was found"));
            return null;
        }

        var parameters = execute.Parameters.ToList();
        var passesCt = parameters.Count > 0 && parameters[parameters.Count - 1].Type.ToDisplayString() == "System.Threading.CancellationToken";
        if (passesCt)
            parameters.RemoveAt(parameters.Count - 1);
        if (parameters.Count > 1 || parameters.Any(p => p.RefKind != RefKind.None))
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedHandler, location, handler.Name,
                "ExecuteAsync must take at most one request parameter (plus an optional trailing CancellationToken)"));
            return null;
        }

        ITypeSymbol? requestType = parameters.Count == 1 ? parameters[0].Type : null;
        var clientStream = false;
        if (requestType is not null && TryAsyncEnumerable(requestType, out var reqElem))
        {
            clientStream = true;
            requestType = reqElem;
        }

        var (returnKind, responseType) = ClassifyReturn(execute.ReturnType);
        if (returnKind is null)
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedHandler, location, handler.Name,
                $"return type '{execute.ReturnType.ToDisplayString()}' is not supported (use Task<T>, ValueTask<T>, T, Task, ValueTask or IAsyncEnumerable<T>)"));
            return null;
        }
        var serverStream = returnKind == HandlerReturnKind.AsyncEnumerable;
        var kind = (clientStream, serverStream) switch
        {
            (false, false) => RpcKind.Unary,
            (true, false) => RpcKind.ClientStreaming,
            (false, true) => RpcKind.ServerStreaming,
            _ => RpcKind.DuplexStreaming,
        };

        var requested = method.Name switch
        {
            "MapGrpcUnary" => RpcKind.Unary,
            "MapGrpcClientStream" => RpcKind.ClientStreaming,
            "MapGrpcServerStream" => RpcKind.ServerStreaming,
            "MapGrpcDuplexStream" => RpcKind.DuplexStreaming,
            _ => (RpcKind?)null,
        };
        if (requested is not null && requested != kind)
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.StreamingMismatch, location, callName, Describe(requested.Value), handler.Name, Describe(kind)));
            return null;
        }
        if (requestType is null && clientStream)
            return null;

        foreach (var root in new[] { requestType, responseType })
        {
            if (root is not null && root.IsValueType)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedMessageType, location, root.ToDisplayString(),
                    "gRPC request/response messages must be classes or records (structs are supported as nested messages)"));
                return null;
            }
        }

        // ---- message graph
        var graph = new TypeGraphBuilder(model.Compilation);
        var requestKey = "";
        var responseKey = "";
        if (requestType is not null)
        {
            var err = graph.AddRoot(requestType, out requestKey);
            if (err is not null)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedMessageType, location, requestType.ToDisplayString(), err));
                return null;
            }
        }
        if (responseType is not null)
        {
            var err = graph.AddRoot(responseType, out responseKey);
            if (err is not null)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedMessageType, location, responseType.ToDisplayString(), err));
                return null;
            }
        }

        // ---- inline configure lambda / explicit config type
        var interceptors = new List<string>();
        var bindings = new List<BindingModel>();
        var configureArg = FindArgument(inv, method, "configure", 1);
        if (configureArg is not null && method.TypeArguments.Length == 1)
        {
            switch (configureArg.Expression)
            {
                case LambdaExpressionSyntax lambda:
                    ConfigParser.ParseEndpointBody(lambda.Body, model, interceptors, bindings, diags, ct);
                    break;
                case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.NullLiteralExpression):
                case LiteralExpressionSyntax lit2 when lit2.IsKind(SyntaxKind.DefaultLiteralExpression):
                    break;
                default:
                    diags.Add(DiagnosticInfo.Create(Diagnostics.ConfigureNotLambda, LocationInfo.From(configureArg), callName));
                    break;
            }
        }
        string? explicitConfig = method.TypeArguments.Length == 2 ? TypeGraphBuilder.Fqn(method.TypeArguments[1]) : null;

        // ---- conventions (receiver chain + chained after the call)
        var conventions = BuildConventions(inv, receiver, model, callName, diags, ct);

        return new EndpointModel(
            parsed.Package,
            parsed.Service,
            parsed.Method,
            kind,
            TypeGraphBuilder.Fqn(handler),
            requestType is null ? "" : requestKey,
            responseType is null ? "" : responseKey,
            returnKind.Value,
            parameters.Count == 1,
            passesCt,
            graph.Messages.ToEquatableArray(),
            graph.Enums.ToEquatableArray(),
            interceptors.Distinct(StringComparer.Ordinal).ToEquatableArray(),
            bindings.ToEquatableArray(),
            explicitConfig,
            conventions,
            location);
    }

    private static string Describe(RpcKind k) => k switch
    {
        RpcKind.Unary => "unary",
        RpcKind.ClientStreaming => "client-streaming",
        RpcKind.ServerStreaming => "server-streaming",
        _ => "duplex-streaming",
    };

    private static ArgumentSyntax? FindArgument(InvocationExpressionSyntax inv, IMethodSymbol method, string name, int positionAfterThis)
    {
        var args = inv.ArgumentList.Arguments;
        foreach (var a in args)
        {
            if (a.NameColon?.Name.Identifier.ValueText == name)
                return a;
        }
        // Reduced extension call: arguments start after 'this'
        if (positionAfterThis < args.Count && args[positionAfterThis].NameColon is null)
            return args[positionAfterThis];
        return null;
    }

    private static IMethodSymbol? FindExecute(INamedTypeSymbol handler)
    {
        for (var t = handler; t is not null; t = t.BaseType)
        {
            var m = t.GetMembers("ExecuteAsync").OfType<IMethodSymbol>()
                .FirstOrDefault(x => !x.IsStatic && x.DeclaredAccessibility == Accessibility.Public && !x.IsGenericMethod);
            if (m is not null)
                return m;
        }
        return null;
    }

    private static bool TryAsyncEnumerable(ITypeSymbol type, out ITypeSymbol element)
    {
        element = type;
        if (type is INamedTypeSymbol { IsGenericType: true } n
            && n.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IAsyncEnumerable<T>")
        {
            element = n.TypeArguments[0];
            return true;
        }
        return false;
    }

    private static (HandlerReturnKind? Kind, ITypeSymbol? Response) ClassifyReturn(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void)
            return (HandlerReturnKind.Void, null);
        if (TryAsyncEnumerable(type, out var elem))
            return (HandlerReturnKind.AsyncEnumerable, elem);
        if (type is INamedTypeSymbol n)
        {
            switch (n.OriginalDefinition.ToDisplayString())
            {
                case "System.Threading.Tasks.Task<TResult>":
                    return TryAsyncEnumerable(n.TypeArguments[0], out _) ? (null, null) : (HandlerReturnKind.Task, n.TypeArguments[0]);
                case "System.Threading.Tasks.ValueTask<TResult>":
                    return TryAsyncEnumerable(n.TypeArguments[0], out _) ? (null, null) : (HandlerReturnKind.ValueTask, n.TypeArguments[0]);
                case "System.Threading.Tasks.Task":
                    return (HandlerReturnKind.TaskVoid, null);
                case "System.Threading.Tasks.ValueTask":
                    return (HandlerReturnKind.ValueTaskVoid, null);
            }
        }
        return (HandlerReturnKind.Sync, type);
    }

    // ---------------------------------------------------------------- conventions

    private static ConventionModel? BuildConventions(InvocationExpressionSyntax mapInvocation, RouteResolver.ReceiverChain receiver,
        SemanticModel model, string callName, List<DiagnosticInfo> diags, CancellationToken ct)
    {
        var segments = new List<string>();
        foreach (var (inv, ma, segModel) in receiver.Conventions)
        {
            var text = SegmentText(inv, ma, segModel, diags, ct);
            if (text is not null)
                segments.Add(text);
        }

        // Calls chained after MapGrpc(...)
        ExpressionSyntax current = mapInvocation;
        while (current.Parent is MemberAccessExpressionSyntax ma && ma.Expression == current
               && ma.Parent is InvocationExpressionSyntax parentInv)
        {
            var text = SegmentText(parentInv, ma, model, diags, ct);
            if (text is not null)
                segments.Add(text);
            current = parentInv;
        }

        var parent = current.Parent;
        while (parent is ParenthesizedExpressionSyntax)
            parent = parent.Parent;
        if (parent is not ExpressionStatementSyntax)
            diags.Add(DiagnosticInfo.Create(Diagnostics.ConventionsNotChained, LocationInfo.From(mapInvocation), callName));

        if (segments.Count == 0)
            return null;

        var root = mapInvocation.SyntaxTree.GetCompilationUnitRoot(ct);
        var rootUsings = root.Usings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.None))
            .Select(u => u.ToString()).ToEquatableArray();
        var namespaces = mapInvocation.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().ToList();
        var ns = string.Join(".", namespaces.Select(n => n.Name.ToString()));
        var nsUsings = namespaces.SelectMany(n => n.Usings).Select(u => u.ToString()).ToEquatableArray();

        var id = StableHash(mapInvocation.SyntaxTree.FilePath + ":" + mapInvocation.SpanStart);
        return new ConventionModel(ns, rootUsings, nsUsings, string.Concat(segments), id);
    }

    /// <summary>
    /// Copies one chained call (".RequireAuthorization(\"p\")") as text. Constant identifiers are inlined as
    /// literals; references to locals, parameters or instance members of the enclosing code are rejected
    /// (they would not compile in generated code and silently dropping security conventions is unsafe).
    /// </summary>
    private static string? SegmentText(InvocationExpressionSyntax inv, MemberAccessExpressionSyntax ma, SemanticModel model,
        List<DiagnosticInfo> diags, CancellationToken ct)
    {
        var source = inv.SyntaxTree.GetText(ct);
        var start = ma.OperatorToken.SpanStart;
        var end = inv.Span.End;
        var replacements = new List<(TextSpan Span, string Text)>();
        var ok = true;

        foreach (var id in inv.ArgumentList.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Parent is MemberAccessExpressionSyntax pm && pm.Name == id)
                continue; // member name on a qualified access
            if (id.Parent is NameColonSyntax || id.Parent is NameEqualsSyntax)
                continue;
            var symbol = model.GetSymbolInfo(id, ct).Symbol;
            if (symbol is null)
                continue;
            if (symbol is ILocalSymbol or IFieldSymbol)
            {
                var constant = model.GetConstantValue(id, ct);
                if (constant.HasValue)
                {
                    replacements.Add((id.Span, FormatConstant(constant.Value)));
                    continue;
                }
            }
            var declaredInside = symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == inv.SyntaxTree && inv.Span.Contains(r.Span));
            if (declaredInside)
                continue;
            if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol
                || (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol && !symbol.IsStatic && symbol.ContainingType is not null)
                || (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol && symbol.IsStatic && id.Parent is not MemberAccessExpressionSyntax))
            {
                if (symbol is IMethodSymbol { MethodKind: MethodKind.LambdaMethod or MethodKind.LocalFunction } && declaredInside)
                    continue;
                diags.Add(DiagnosticInfo.Create(Diagnostics.ConventionCapturesState, LocationInfo.From(id),
                    "." + ma.Name.ToString(), id.Identifier.ValueText));
                ok = false;
            }
        }
        foreach (var t in inv.ArgumentList.DescendantNodes().OfType<ThisExpressionSyntax>())
        {
            diags.Add(DiagnosticInfo.Create(Diagnostics.ConventionCapturesState, LocationInfo.From(t), "." + ma.Name.ToString(), "this"));
            ok = false;
        }
        if (!ok)
            return null;

        var sb = new StringBuilder();
        var pos = start;
        foreach (var (span, text) in replacements.OrderBy(r => r.Span.Start))
        {
            sb.Append(source.ToString(TextSpan.FromBounds(pos, span.Start)));
            sb.Append(text);
            pos = span.End;
        }
        sb.Append(source.ToString(TextSpan.FromBounds(pos, end)));
        return sb.ToString();
    }

    private static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string s => SymbolDisplay.FormatLiteral(s, quote: true),
        char c => SymbolDisplay.FormatLiteral(c, quote: true),
        bool b => b ? "true" : "false",
        _ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false) ?? "default",
    };

    internal static string StableHash(string text)
    {
        // FNV-1a 64-bit: deterministic across processes (string.GetHashCode is randomized)
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }
}
