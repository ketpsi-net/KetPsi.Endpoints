using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.Endpoints.Http.Generator;

internal sealed record HelperRef(string Key, string Name);

internal static class Parsing
{
    private const string MapExtensionsType = "KetPsi.Endpoints.Http.MapExtensions";

    public static bool IsMapMethodName(string name) => name is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch";

    // ------------------------------------------------------------------
    // Map* call sites
    // ------------------------------------------------------------------

    public static bool IsMapCallCandidate(SyntaxNode node, CancellationToken _)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } name }
        } && IsMapMethodName(name.Identifier.ValueText);

    public static MapCallResult? ParseMapCall(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var diagnostics = new List<DiagnosticInfo>();
        try
        {
            return ParseMapCallCore(invocation, ctx.SemanticModel, diagnostics, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.CallSiteCrashed, LocationInfo.From(invocation), ex.GetType().Name + ": " + ex.Message));
            return new MapCallResult(null, false, diagnostics.ToEquatableArray());
        }
    }

    private static MapCallResult? ParseMapCallCore(InvocationExpressionSyntax invocation, SemanticModel model, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var symbolInfo = model.GetSymbolInfo(invocation, ct);
        var method = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault(m => IsMapMethodName(m.Name) && m.IsGenericMethod);

        if (method is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.CouldNotResolveMethodSymbol, LocationInfo.From(invocation), invocation.Expression.ToString()));
            return new MapCallResult(null, false, diagnostics.ToEquatableArray());
        }

        var definition = method.ReducedFrom ?? method;
        if (definition.ContainingType?.ToDisplayString() != MapExtensionsType || !method.IsGenericMethod)
            return null;

        var location = LocationInfo.From(invocation);

        // Chained conventions after the Map* call.
        var skip = false;
        var helperMethod = GetEnclosingMethod(model, invocation, ct);
        var chainParts = new List<string>();
        var chainOk = true;
        for (var current = invocation; current.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax parentInvocation } ma && ma.Expression == current; current = parentInvocation)
        {
            if (ma.Name.Identifier.ValueText == "SkipEndpointGeneration")
            {
                skip = true;
                continue;
            }

            var part = Portable.WriteChainCall(parentInvocation, ma, model, helperMethod, diagnostics, ct);
            if (part is null)
                chainOk = false;
            else
                chainParts.Add(part);
        }

        if (skip)
            return new MapCallResult(null, true, EquatableArray<DiagnosticInfo>.Empty);

        var handlerType = method.TypeArguments[0];
        var execute = FindExecuteAsync(handlerType);
        if (execute is null)
        {
            // Source handlers report KP103 from the handler provider; only report for external types here.
            var isSourceEndpoint = handlerType.Locations.Any(l => l.IsInSource) && handlerType.AllInterfaces.Any(i => i.Name == "IEndpoint");
            if (!isSourceEndpoint)
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MissingExecuteAsyncMethod, location, handlerType.Name));
            return new MapCallResult(null, false, diagnostics.ToEquatableArray());
        }

        // Route argument (first positional or 'route:').
        var routeArgument = invocation.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "route")
            ?? invocation.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon is null);
        if (routeArgument is null)
            return new MapCallResult(null, false, diagnostics.ToEquatableArray());

        var route = Portable.Write(routeArgument.Expression, model, helperMethod, diagnostics, ct);

        var receiver = AnalyzeChain(memberAccess.Expression, model, helperMethod, diagnostics, ct, out var unresolvedReason);

        var handlerFqn = handlerType.ToDisplayString(SymbolFormats.FqnNoAnnotations);
        if (receiver.Root == ChainRootKind.Unresolved)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnresolvedMapCall, location, handlerFqn, unresolvedReason ?? "the route builder receiver could not be resolved"));
            return new MapCallResult(null, false, diagnostics.ToEquatableArray());
        }

        if (route is null || !chainOk)
            return new MapCallResult(null, false, diagnostics.ToEquatableArray());

        var wk = WellKnownTypes.Get(model.Compilation);
        var parameters = new List<ParameterModel>();
        foreach (var p in execute.Parameters)
        {
            var value = TypeAnalysis.AnalyzeParameter(p, wk, out _);
            var dto = value.Type.IsComplex ? TypeAnalysis.AnalyzeDto(p.Type, wk) : null;
            parameters.Add(new ParameterModel(p.Ordinal, value, dto, false));
        }

        var obsolete = execute.GetAttributes().FirstOrDefault(a => WellKnownTypes.Is(a.AttributeClass, wk.Obsolete))
                       ?? handlerType.GetAttributes().FirstOrDefault(a => WellKnownTypes.Is(a.AttributeClass, wk.Obsolete));
        var obsoleteMessage = obsolete is { ConstructorArguments.Length: > 0 } ? obsolete.ConstructorArguments[0].Value as string : null;

        var classDeclaration = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        var extensionClassName = classDeclaration?.Identifier.ValueText ?? "DefaultEndpoints";

        var endpoint = new EndpointModel(
            handlerFqn,
            Sanitize(handlerType.Name),
            execute.IsStatic,
            definition.Name,
            route,
            string.Concat(chainParts),
            receiver,
            extensionClassName,
            CollectUsings(invocation, model, ct),
            parameters.ToEquatableArray(),
            AnalyzeReturn(execute.ReturnType, wk),
            obsoleteMessage,
            obsolete is not null,
            TypeAnalysis.GetAccessibility(handlerType),
            GetActivation(handlerType),
            location);

        return new MapCallResult(endpoint, false, diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Handlers whose only constructor is a public parameterless one do not need DI (and therefore no request scope):
    /// they are created directly, and cached when they hold no instance state and are not disposable.
    /// </summary>
    private static HandlerActivation GetActivation(ITypeSymbol handlerType)
    {
        if (handlerType is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false, IsGenericType: false } named)
            return HandlerActivation.Services;

        var ctors = named.InstanceConstructors;
        if (ctors.Length != 1 || ctors[0].Parameters.Length != 0 || ctors[0].DeclaredAccessibility != Accessibility.Public)
            return HandlerActivation.Services;

        if (named.AllInterfaces.Any(i => i.ToDisplayString() is "System.IDisposable" or "System.IAsyncDisposable"))
            return HandlerActivation.New;

        for (INamedTypeSymbol? current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic && !f.IsConst))
                return HandlerActivation.New;
        }

        return HandlerActivation.Shared;
    }

    public static IMethodSymbol? FindExecuteAsync(ITypeSymbol handlerType)
        => handlerType.GetMembers("ExecuteAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.DeclaredAccessibility == Accessibility.Public);

    private static ReturnModel AnalyzeReturn(ITypeSymbol returnType, WellKnownTypes wk)
    {
        var declared = returnType.ToFqn();
        ReturnShape shape;
        ITypeSymbol? result = null;
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            shape = ReturnShape.Void;
        }
        else if (WellKnownTypes.Is(returnType, wk.Task) && returnType is INamedTypeSymbol { IsGenericType: false })
        {
            shape = ReturnShape.Task;
        }
        else if (WellKnownTypes.Is(returnType, wk.ValueTask) && returnType is INamedTypeSymbol { IsGenericType: false })
        {
            shape = ReturnShape.ValueTask;
        }
        else if (returnType is INamedTypeSymbol named && WellKnownTypes.Is(named, wk.TaskOfT))
        {
            shape = ReturnShape.TaskOfT;
            result = named.TypeArguments[0];
        }
        else if (returnType is INamedTypeSymbol namedVt && WellKnownTypes.Is(namedVt, wk.ValueTaskOfT))
        {
            shape = ReturnShape.ValueTaskOfT;
            result = namedVt.TypeArguments[0];
        }
        else
        {
            shape = ReturnShape.Value;
            result = returnType;
        }

        var kind = ResultKind.None;
        var isMetadataProvider = false;
        if (result is not null)
        {
            if (result.SpecialType == SpecialType.System_String) kind = ResultKind.String;
            else if (result.SpecialType == SpecialType.System_Object) kind = ResultKind.Object;
            else if (WellKnownTypes.Implements(result, wk.Result)) kind = ResultKind.Result;
            else kind = ResultKind.Json;

            isMetadataProvider = result.TypeKind is TypeKind.Class or TypeKind.Struct && !result.IsAbstract && WellKnownTypes.Implements(result, wk.MetadataProvider);
        }

        return new ReturnModel(shape, kind, declared, result?.ToFqn(), result?.ToDisplayString(SymbolFormats.FqnNoAnnotations), isMetadataProvider);
    }

    // ------------------------------------------------------------------
    // Receiver chains (groups / helper parameters)
    // ------------------------------------------------------------------

    public static ChainModel AnalyzeChain(
        ExpressionSyntax receiver,
        SemanticModel model,
        IMethodSymbol? helperMethod,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string? unresolvedReason)
    {
        unresolvedReason = null;
        var wk = WellKnownTypes.Get(model.Compilation);
        var steps = new List<string>(); // innermost (closest to the Map call) first
        var current = receiver;

        for (var guard = 0; guard < 32; guard++)
        {
            while (current is ParenthesizedExpressionSyntax paren)
                current = paren.Expression;
            if (current is CastExpressionSyntax cast)
            {
                current = cast.Expression;
                continue;
            }

            if (current is IdentifierNameSyntax identifier)
            {
                var symbol = model.GetSymbolInfo(identifier, ct).Symbol;
                if (symbol is ILocalSymbol local)
                {
                    if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } initializer }
                        && initializer.SyntaxTree == model.SyntaxTree)
                    {
                        current = initializer;
                        continue;
                    }
                }
                else if (symbol is IParameterSymbol parameter)
                {
                    if (parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Ordinary } method
                        && helperMethod is not null
                        && SymbolEqualityComparer.Default.Equals(method, helperMethod)
                        && method.DeclaringSyntaxReferences.Length > 0)
                    {
                        steps.Reverse();
                        return new ChainModel(
                            ChainRootKind.HelperParameter,
                            HelperKey(method),
                            method.Name,
                            parameter.Ordinal,
                            RouteBuilderKind(parameter.Type, wk),
                            steps.ToEquatableArray());
                    }

                    if (!IsAppType(parameter.Type, wk))
                    {
                        unresolvedReason = $"the route builder is the parameter '{parameter.Name}' of a lambda or local function";
                        return Unresolved();
                    }
                }
            }

            if (current is InvocationExpressionSyntax)
            {
                var calls = new List<(InvocationExpressionSyntax Invocation, MemberAccessExpressionSyntax MemberAccess)>();
                ExpressionSyntax node = current;
                while (node is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
                {
                    calls.Add((inv, ma));
                    node = ma.Expression;
                }

                if (calls.Any(c => c.MemberAccess.Name.Identifier.ValueText is "MapGroup" or "MapApiVersionedGroup"))
                {
                    var text = new System.Text.StringBuilder();
                    for (var i = calls.Count - 1; i >= 0; i--)
                    {
                        var part = Portable.WriteChainCall(calls[i].Invocation, calls[i].MemberAccess, model, helperMethod, diagnostics, ct);
                        if (part is null)
                        {
                            unresolvedReason = "a route group convention could not be re-emitted";
                            return Unresolved();
                        }
                        text.Append(part);
                    }
                    steps.Add(text.ToString());
                    current = node;
                    continue;
                }
            }

            var type = model.GetTypeInfo(current, ct).Type;
            if (IsAppType(type, wk))
            {
                steps.Reverse();
                return new ChainModel(ChainRootKind.App, null, null, -1, null, steps.ToEquatableArray());
            }

            unresolvedReason = $"the route builder '{current}' is not a WebApplication, a MapGroup chain or a parameter of the enclosing method";
            return Unresolved();
        }

        unresolvedReason = "the route group chain is too deep";
        return Unresolved();

        static ChainModel Unresolved() => new(ChainRootKind.Unresolved, null, null, -1, null, EquatableArray<string>.Empty);
    }

    private static bool IsAppType(ITypeSymbol? type, WellKnownTypes wk)
        => type is not null && (WellKnownTypes.InheritsFrom(type, wk.WebApplication)
            || (WellKnownTypes.Implements(type, wk.EndpointRouteBuilder) && !WellKnownTypes.InheritsFrom(type, wk.RouteGroupBuilder)));

    private static string RouteBuilderKind(ITypeSymbol type, WellKnownTypes wk)
    {
        if (WellKnownTypes.InheritsFrom(type, wk.WebApplication)) return "WebApplication";
        if (WellKnownTypes.InheritsFrom(type, wk.RouteGroupBuilder)) return "RouteGroupBuilder";
        if (WellKnownTypes.Implements(type, wk.EndpointRouteBuilder)) return "IEndpointRouteBuilder";
        return "Other";
    }

    public static bool IsRouteBuilder(ITypeSymbol type, WellKnownTypes wk)
        => WellKnownTypes.InheritsFrom(type, wk.WebApplication) || WellKnownTypes.Implements(type, wk.EndpointRouteBuilder);

    public static string HelperKey(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        return definition.GetDocumentationCommentId() ?? definition.ToDisplayString();
    }

    public static IMethodSymbol? GetEnclosingMethod(SemanticModel model, SyntaxNode node, CancellationToken ct)
    {
        var symbol = model.GetEnclosingSymbol(node.SpanStart, ct);
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
            symbol = symbol.ContainingSymbol;
        return symbol as IMethodSymbol;
    }

    private static EquatableArray<string> CollectUsings(SyntaxNode node, SemanticModel model, CancellationToken ct)
    {
        var usings = new SortedSet<string>(StringComparer.Ordinal);
        var root = node.SyntaxTree.GetRoot(ct);
        foreach (var u in root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax).OfType<UsingDirectiveSyntax>())
        {
            if (!u.GlobalKeyword.IsKind(SyntaxKind.None))
                continue;
            usings.Add(u.WithoutTrivia().NormalizeWhitespace().ToFullString());
        }

        // Extension methods declared in the namespace of the mapping code are in scope there; keep them in scope.
        var ns = model.GetEnclosingSymbol(node.SpanStart, ct)?.ContainingNamespace;
        for (; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            usings.Add("using " + ns.ToDisplayString() + ";");

        return usings.ToEquatableArray();
    }

    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "_";
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var result = new string(chars);
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    // ------------------------------------------------------------------
    // Helper call sites (cross-file, once per compilation, only for known helper names)
    // ------------------------------------------------------------------

    public static HelperScanResult ScanHelperCallSites(EquatableArray<HelperRef> helpers, Compilation compilation, CancellationToken ct)
    {
        if (helpers.IsEmpty)
            return new HelperScanResult(EquatableArray<HelperCallSite>.Empty);

        var wk = WellKnownTypes.Get(compilation);
        var results = new List<HelperCallSite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new List<HelperRef>();
        foreach (var h in helpers)
        {
            if (seen.Add(h.Key))
                pending.Add(h);
        }

        for (var round = 0; round < 8 && pending.Count > 0; round++)
        {
            var keys = new HashSet<string>(pending.Select(p => p.Key), StringComparer.Ordinal);
            var names = new HashSet<string>(pending.Select(p => p.Name), StringComparer.Ordinal);
            pending = new List<HelperRef>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();
                SemanticModel? model = null;
                foreach (var invocation in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var invokedName = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                        SimpleNameSyntax sn => sn.Identifier.ValueText,
                        MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
                        _ => null,
                    };
                    if (invokedName is null || !names.Contains(invokedName))
                        continue;

                    model ??= compilation.GetSemanticModel(tree);
                    var info = model.GetSymbolInfo(invocation, ct);
                    if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol invoked)
                        continue;
                    var key = HelperKey(invoked);
                    if (!keys.Contains(key))
                        continue;

                    var site = AnalyzeHelperCallSite(invocation, invoked, model, wk, ct, out var nested);
                    results.Add(site);
                    foreach (var n in nested)
                    {
                        if (seen.Add(n.Key))
                            pending.Add(n);
                    }
                }
            }
        }

        return new HelperScanResult(results.ToEquatableArray());
    }

    private static HelperCallSite AnalyzeHelperCallSite(
        InvocationExpressionSyntax invocation,
        IMethodSymbol invoked,
        SemanticModel model,
        WellKnownTypes wk,
        CancellationToken ct,
        out List<HelperRef> nestedHelpers)
    {
        nestedHelpers = new List<HelperRef>();
        var definition = (invoked.ReducedFrom ?? invoked).OriginalDefinition;
        var reduced = invoked.ReducedFrom is not null;
        var enclosing = GetEnclosingMethod(model, invocation, ct);
        var expressions = new Dictionary<int, ExpressionSyntax>();

        if (reduced && invocation.Expression is MemberAccessExpressionSyntax receiverAccess)
            expressions[0] = receiverAccess.Expression;

        var positional = 0;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            int ordinal;
            if (argument.NameColon is not null)
            {
                var parameter = definition.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.ValueText);
                if (parameter is null)
                    continue;
                ordinal = parameter.Ordinal;
            }
            else
            {
                ordinal = positional + (reduced ? 1 : 0);
                positional++;
            }

            if (ordinal < definition.Parameters.Length)
                expressions[ordinal] = argument.Expression;
        }

        var arguments = new List<HelperArgument>();
        foreach (var parameter in definition.Parameters)
        {
            var diagnostics = new List<DiagnosticInfo>();
            if (!expressions.TryGetValue(parameter.Ordinal, out var expression))
            {
                var defaultText = parameter.HasExplicitDefaultValue ? Literals.Constant(parameter.ExplicitDefaultValue, parameter.Type) : null;
                arguments.Add(new HelperArgument(parameter.Ordinal, defaultText, null, EquatableArray<DiagnosticInfo>.Empty));
                continue;
            }

            if (IsRouteBuilder(parameter.Type, wk))
            {
                var chain = AnalyzeChain(expression, model, enclosing, diagnostics, ct, out var reason);
                if (chain.Root == ChainRootKind.Unresolved)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnresolvedMapCall, LocationInfo.From(expression), definition.Name, reason ?? "the route builder argument could not be resolved"));
                }
                else if (chain.Root == ChainRootKind.HelperParameter && chain.HelperKey is not null && chain.HelperName is not null)
                {
                    nestedHelpers.Add(new HelperRef(chain.HelperKey, chain.HelperName));
                }
                arguments.Add(new HelperArgument(parameter.Ordinal, null, chain, diagnostics.ToEquatableArray()));
            }
            else
            {
                var portable = Portable.Write(expression, model, enclosing, diagnostics, ct);
                arguments.Add(new HelperArgument(parameter.Ordinal, portable, null, diagnostics.ToEquatableArray()));
            }
        }

        return new HelperCallSite(HelperKey(definition), arguments.ToEquatableArray(), LocationInfo.From(invocation));
    }

    // ------------------------------------------------------------------
    // IEndpoint handler classes (fluent builder stubs)
    // ------------------------------------------------------------------

    public static bool IsClassWithBaseList(SyntaxNode node, CancellationToken _)
        => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 };

    public static HandlerResult? ParseHandler(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSymbol || classSymbol.IsAbstract)
            return null;
        if (!classSymbol.AllInterfaces.Any(i => i.Name == "IEndpoint" && i.ContainingNamespace?.ToDisplayString() == "KetPsi.Endpoints.Abstractions"))
            return null;

        var execute = FindExecuteAsync(classSymbol);
        if (execute is null)
        {
            return new HandlerResult(null, EquatableArray<DiagnosticInfo>.From(new[]
            {
                DiagnosticInfo.Create(Diagnostics.MissingExecuteAsyncMethod, LocationInfo.From(classDecl.Identifier.GetLocation()), classSymbol.Name),
            }));
        }

        var parameters = execute.Parameters
            .Select(p => new BuilderParameterModel(p.Type.ToFqn(), p.Name, TypeAnalysis.GetAccessibility(p.Type)))
            .ToEquatableArray();

        return new HandlerResult(
            new HandlerBuilderModel(classSymbol.ToDisplayString(SymbolFormats.FqnNoAnnotations), Sanitize(classSymbol.Name), TypeAnalysis.GetAccessibility(classSymbol), parameters),
            EquatableArray<DiagnosticInfo>.Empty);
    }

    // ------------------------------------------------------------------
    // IHttpEndpointConfiguration<T> classes (parsed syntactically, never executed)
    // ------------------------------------------------------------------

    public static ConfigModel? ParseConfig(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSymbol || classSymbol.IsAbstract)
            return null;

        var configInterface = classSymbol.AllInterfaces.FirstOrDefault(i => i.Name == "IHttpEndpointConfiguration" && i.TypeArguments.Length == 1);
        if (configInterface is null)
            return null;

        var handlerType = configInterface.TypeArguments[0];
        var execute = FindExecuteAsync(handlerType);
        Dictionary<string, string>? pascalToName = null;
        if (execute is not null)
        {
            pascalToName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in execute.Parameters)
                pascalToName[ToPascalCase(p.Name)] = p.Name;
        }

        var diagnostics = new List<DiagnosticInfo>();
        var configs = new Dictionary<string, (bool AsParameters, BindingSpec? Direct, List<PropertyConfigModel> Props)>(StringComparer.Ordinal);
        var configure = classDecl.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Configure" && m.ParameterList.Parameters.Count == 1);

        if (configure is not null)
        {
            var builderName = configure.ParameterList.Parameters[0].Identifier.ValueText;
            foreach (var invocation in configure.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;
                var rootPascal = FindTargetParameterMethodName(memberAccess.Expression, builderName);
                if (rootPascal is null)
                    continue;

                string key;
                if (pascalToName is not null)
                {
                    if (!pascalToName.TryGetValue(rootPascal, out key!))
                        continue;
                }
                else
                {
                    key = rootPascal;
                }

                if (!configs.TryGetValue(key, out var entry))
                    entry = (false, null, new List<PropertyConfigModel>());

                var methodName = memberAccess.Name.Identifier.ValueText;
                if (methodName == "AsParameters")
                {
                    entry.AsParameters = true;
                }
                else if (methodName == "Property" && invocation.ArgumentList.Arguments.Count == 2)
                {
                    entry.AsParameters = true;
                    var propName = ExtractPropertyName(invocation.ArgumentList.Arguments[0].Expression);
                    if (propName is not null)
                    {
                        foreach (var inner in invocation.ArgumentList.Arguments[1].Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                        {
                            if (inner.Expression is MemberAccessExpressionSyntax innerAccess)
                            {
                                var spec = MapConfigurationCall(innerAccess.Name.Identifier.ValueText, inner, ctx.SemanticModel, diagnostics, ct);
                                if (spec is not null)
                                {
                                    entry.Props.RemoveAll(p => string.Equals(p.PropertyName, propName, StringComparison.OrdinalIgnoreCase));
                                    entry.Props.Add(new PropertyConfigModel(propName, spec));
                                }
                            }
                        }
                    }
                }
                else if (!IsInsideLambda(invocation, configure))
                {
                    var spec = MapConfigurationCall(methodName, invocation, ctx.SemanticModel, diagnostics, ct);
                    if (spec is not null)
                        entry.Direct = spec;
                }

                configs[key] = entry;
            }
        }

        var parameters = configs
            .Select(kv => new ParameterConfigModel(kv.Key, kv.Value.AsParameters, kv.Value.Direct, kv.Value.Props.ToEquatableArray()))
            .ToEquatableArray();

        return new ConfigModel(handlerType.ToDisplayString(SymbolFormats.FqnNoAnnotations), parameters, diagnostics.ToEquatableArray());
    }

    private static BindingSpec? MapConfigurationCall(string methodName, InvocationExpressionSyntax invocation, SemanticModel model, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var source = methodName switch
        {
            "FromRoute" => BindingSource.Route,
            "FromQuery" => BindingSource.Query,
            "FromHeader" => BindingSource.Header,
            "FromForm" => BindingSource.Form,
            "FromBody" => BindingSource.Body,
            "FromServices" => BindingSource.Services,
            "FromKeyedServices" => BindingSource.KeyedServices,
            _ => (BindingSource?)null,
        };
        if (source is null)
            return null;

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument is null)
            return new BindingSpec(source.Value, null, null, false);

        var constant = model.GetConstantValue(argument.Expression, ct);
        if (!constant.HasValue)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NonConstantConfiguration, LocationInfo.From(argument), argument.Expression.ToString(), methodName));
            return new BindingSpec(source.Value, null, null, false);
        }

        if (source == BindingSource.KeyedServices)
        {
            var type = model.GetTypeInfo(argument.Expression, ct).Type;
            return new BindingSpec(source.Value, null, constant.Value is null ? null : Literals.Constant(constant.Value, type), false);
        }

        return new BindingSpec(source.Value, constant.Value as string is { Length: > 0 } s ? s : null, null, false);
    }

    private static bool IsInsideLambda(SyntaxNode node, SyntaxNode stopAt)
    {
        for (var current = node.Parent; current is not null && current != stopAt; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax)
                return true;
        }
        return false;
    }

    private static string? ExtractPropertyName(ExpressionSyntax selector)
        => selector is LambdaExpressionSyntax lambda
            ? lambda.Body.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().LastOrDefault()?.Name.Identifier.ValueText
            : null;

    private static string? FindTargetParameterMethodName(ExpressionSyntax expression, string builderParamName)
    {
        var current = expression;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ma })
            {
                if (ma.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == builderParamName)
                    return ma.Name.Identifier.ValueText;
                current = ma.Expression;
            }
            else if (current is MemberAccessExpressionSyntax outer)
            {
                current = outer.Expression;
            }
            else
            {
                break;
            }
        }

        // `builder.Filter()` itself (the root call) – the method name is the target.
        return null;
    }

    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsUpper(name[0]))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
