using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Emits one ServiceContract interface + Proxy class per distinct gRPC service route.
/// </summary>
[Generator]
public sealed class GrpcEndpointGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsMapGrpcInvocation(s),
                transform: static (ctx, _) => GetEndpointModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        context.RegisterSourceOutput(endpoints, static (spc, list) => Execute(spc, list!));
    }

    private static bool IsMapGrpcInvocation(SyntaxNode node)
    {
        return node is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.Text.StartsWith("MapGrpc", StringComparison.Ordinal);
    }




    /// <summary>
    /// Walks the receiver chain (including locals assigned from MapGroup/WithGroupName) and collects:
    /// - MapGroup("prefix") path segments (outer → inner)
    /// - the last WithGroupName("v1") as the API version (appended to the proto package)
    /// Supports:
    ///   app.MapGroup("api").WithGroupName("v1").MapGrpc&lt;T&gt;("test/create")
    ///   var group = app.MapGroup("/api").WithGroupName("v1"); group.MapGrpc&lt;T&gt;("test/create");
    /// </summary>
    private static (string FullRoute, string? Version) ResolveRouteAndVersion(
        InvocationExpressionSyntax mapGrpcInvocation,
        string relativeRoute,
        SemanticModel semanticModel)
    {
        var prefixes = new List<string>();
        string? version = null;
        SyntaxNode? current = mapGrpcInvocation;
        var visited = new HashSet<SyntaxNode>();

        while (current is not null && visited.Add(current))
        {
            if (current is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                {
                    var name = ma.Name is GenericNameSyntax gn ? gn.Identifier.Text : ma.Name.Identifier.Text;

                    if (name == "MapGroup" && inv.ArgumentList.Arguments.Count >= 1)
                    {
                        if (TryStringLiteral(inv.ArgumentList.Arguments[0].Expression, out var prefix) &&
                            !string.IsNullOrEmpty(prefix))
                        {
                            prefixes.Add(prefix.Trim('/'));
                        }
                    }
                    else if (name == "WithGroupName" && inv.ArgumentList.Arguments.Count >= 1)
                    {
                        // First seen while walking leaf → root is the innermost (last in source) WithGroupName
                        if (version is null &&
                            TryStringLiteral(inv.ArgumentList.Arguments[0].Expression, out var ver) &&
                            !string.IsNullOrEmpty(ver))
                        {
                            version = ver.Trim();
                        }
                    }

                    current = UnwrapExpression(ma.Expression, semanticModel);
                    continue;
                }
            }

            // Local / field: var group = app.MapGroup(...); group.MapGrpc(...)
            if (current is IdentifierNameSyntax id)
            {
                current = ResolveIdentifierInitializer(id, semanticModel);
                continue;
            }

            break;
        }

        prefixes.Reverse();
        var fullRoute = prefixes.Count == 0
            ? relativeRoute
            : string.Join("/", prefixes) + "/" + relativeRoute.TrimStart('/');
        return (fullRoute, version);
    }

    private static ExpressionSyntax? UnwrapExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax p:
                    expression = p.Expression;
                    continue;
                case InvocationExpressionSyntax:
                    return expression;
                case IdentifierNameSyntax id:
                    return ResolveIdentifierInitializer(id, semanticModel) as ExpressionSyntax ?? expression;
                default:
                    return expression;
            }
        }
    }

    private static SyntaxNode? ResolveIdentifierInitializer(IdentifierNameSyntax id, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(id).Symbol
                     ?? semanticModel.GetSymbolInfo(id).CandidateSymbols.FirstOrDefault();
        if (symbol is null)
            return null;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is VariableDeclaratorSyntax vd && vd.Initializer?.Value is ExpressionSyntax init)
                return UnwrapExpression(init, semanticModel);
            if (node is PropertyDeclarationSyntax pd && pd.Initializer?.Value is ExpressionSyntax pinit)
                return UnwrapExpression(pinit, semanticModel);
        }

        return null;
    }

    private static bool TryStringLiteral(ExpressionSyntax expression, out string value)
    {
        if (expression is LiteralExpressionSyntax lit &&
            lit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
        {
            value = lit.Token.ValueText;
            return true;
        }
        value = "";
        return false;
    }

    /// <summary>
    /// Last two path segments that are not route parameters (<c>{id}</c>) are service + rpc.
    /// All preceding non-parameter segments form the package (joined with '.').
    /// If there are no preceding segments, package defaults to <c>Api</c>.
    /// </summary>
    private static (string? Namespace, string? Service, string? Method) ParseRouteSegments(string route)
    {
        var segments = route.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var meaningful = new List<string>();
        foreach (var s in segments)
        {
            if (s.Length >= 2 && s[0] == '{' && s[s.Length - 1] == '}')
                continue; // route parameter — not part of package/service/rpc
            meaningful.Add(s);
        }

        if (meaningful.Count < 2)
            return (null, null, null);

        var methodName = meaningful[meaningful.Count - 1];
        var serviceName = meaningful[meaningful.Count - 2];
        string grpcNamespace;
        if (meaningful.Count == 2)
            grpcNamespace = "Api";
        else
            grpcNamespace = string.Join(".", meaningful.GetRange(0, meaningful.Count - 2));

        return (grpcNamespace, serviceName, methodName);
    }

    private static GrpcEndpointModel? GetEndpointModel(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
            return null;

        if (!methodSymbol.Name.StartsWith("MapGrpc", StringComparison.Ordinal) || methodSymbol.TypeArguments.Length != 1)
            return null;

        if (methodSymbol.TypeArguments[0] is not INamedTypeSymbol handlerType)
            return null;

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument?.Expression is not LiteralExpressionSyntax literal)
            return null;

        var relativeRoute = literal.Token.ValueText;
        var (route, version) = ResolveRouteAndVersion(invocation, relativeRoute, context.SemanticModel);
        var (grpcNamespace, serviceName, methodName) = ParseRouteSegments(route);
        if (grpcNamespace is null || serviceName is null || methodName is null)
            return null;
        // Standard proto versioning: append last WithGroupName to the package name
        // e.g. package api.orders.v1; service remains the service segment
        var package = string.IsNullOrEmpty(version)
            ? grpcNamespace
            : $"{grpcNamespace}.{version}";
        var finalGrpcRoute = $"{package}.{serviceName}";
        var requestTypeString = "";
        var returnTypeString = "";
        var acceptsCancellationToken = false;

        var executeMethod = handlerType.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == "ExecuteAsync");

        if (executeMethod is not null)
        {
            if (executeMethod.Parameters.Length > 0)
                requestTypeString = executeMethod.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (executeMethod.Parameters.Length > 1 &&
                executeMethod.Parameters[1].Type.Name == "CancellationToken")
            {
                acceptsCancellationToken = true;
            }

            returnTypeString = executeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return new GrpcEndpointModel(
            handlerType.Name,
            handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            requestTypeString,
            returnTypeString,
            finalGrpcRoute,
            methodName,
            acceptsCancellationToken);
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<GrpcEndpointModel?> endpoints)
    {
        var valid = endpoints.Where(e => e is not null).Cast<GrpcEndpointModel>().ToImmutableArray();
        if (valid.IsDefaultOrEmpty)
            return;

        var groupedServices = valid.GroupBy(e => e.FinalGrpcRoute);

        foreach (var serviceGroup in groupedServices)
        {
            var serviceRoute = serviceGroup.Key;
            var sanitizedServiceName = serviceRoute.Replace(".", "_");

            // One operation per method name (duplicate MapGrpc* call sites must not emit invalid C#)
            var methods = serviceGroup
                .GroupBy(m => m.MethodName, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using System.ServiceModel;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace KetPsi.Endpoints.Grpc");
            sb.AppendLine("{");

            // 1. Service interface
            sb.AppendLine($"    [ServiceContract(Name = \"{serviceRoute}\")]");
            sb.AppendLine($"    internal interface I{sanitizedServiceName}_Contract");
            sb.AppendLine("    {");
            foreach (var method in methods)
            {
                sb.AppendLine($"        [OperationContract(Name = \"{method.MethodName}\")]");
                if (method.AcceptsCancellationToken)
                {
                    sb.AppendLine($"        {method.ResponseTypeName} {method.MethodName}Async({method.RequestTypeName} request, System.Threading.CancellationToken cancellationToken = default);");
                }
                else
                {
                    sb.AppendLine($"        {method.ResponseTypeName} {method.MethodName}Async({method.RequestTypeName} request);");
                }
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            // 2. Proxy class
            sb.AppendLine($"    internal class {sanitizedServiceName}_Proxy : I{sanitizedServiceName}_Contract");
            sb.AppendLine("    {");

            var distinctHandlers = methods.Select(m => m.HandlerFullyQualifiedName).Distinct().ToList();
            for (var i = 0; i < distinctHandlers.Count; i++)
            {
                sb.AppendLine($"        private readonly {distinctHandlers[i]} _handler_{i};");
            }
            sb.AppendLine();

            var ctorParams = string.Join(", ", distinctHandlers.Select((h, i) => $"{h} handler_{i}"));
            sb.AppendLine($"        public {sanitizedServiceName}_Proxy({ctorParams})");
            sb.AppendLine("        {");
            for (var i = 0; i < distinctHandlers.Count; i++)
            {
                sb.AppendLine($"            _handler_{i} = handler_{i};");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var method in methods)
            {
                var handlerIndex = distinctHandlers.IndexOf(method.HandlerFullyQualifiedName);

                if (method.AcceptsCancellationToken)
                {
                    sb.AppendLine($"        public {method.ResponseTypeName} {method.MethodName}Async({method.RequestTypeName} request, System.Threading.CancellationToken cancellationToken = default)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return _handler_{handlerIndex}.ExecuteAsync(request, cancellationToken);");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        public {method.ResponseTypeName} {method.MethodName}Async({method.RequestTypeName} request)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return _handler_{handlerIndex}.ExecuteAsync(request);");
                    sb.AppendLine("        }");
                }
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource($"{sanitizedServiceName}_GrpcService.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }
}
