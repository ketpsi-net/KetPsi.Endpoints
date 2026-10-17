using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Emits DI registration, MapGrpcEndpoints, and the optional UseProtoEndpoint extension.
/// </summary>
[Generator]
public sealed class GrpcRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsMapGrpcInvocation(s),
                transform: static (ctx, _) => GetEndpointModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        var typeConfigs = context.CompilationProvider
            .Select(static (compilation, _) => GetTypeConfigsFromCompilation(compilation));

        var combined = endpoints.Combine(typeConfigs).Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, data) =>
            Execute(spc, data.Left.Left!, data.Left.Right, data.Right));
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

        var closedGenerics = new HashSet<string>(StringComparer.Ordinal);
        var format = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        var executeMethod = handlerType.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == "ExecuteAsync");

        string requestTypeString = "";
        string returnTypeString = "";
        bool isStream = false;

        if (executeMethod is not null)
        {
            if (executeMethod.Parameters.Length > 0)
                requestTypeString = executeMethod.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            returnTypeString = executeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            void ExtractOnlyFromSignature(ITypeSymbol? typeSymbol)
            {
                if (typeSymbol is null)
                    return;

                var isEligibleDataContract =
                    (typeSymbol.TypeKind is TypeKind.Class or TypeKind.Struct) && !typeSymbol.IsAbstract;

                if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
                {
                    var baseName = namedType.ConstructedFrom.ToDisplayString();

                    // Unwrap system wrappers without registering them
                    if (baseName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                        baseName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) ||
                        baseName.StartsWith("System.Collections.Generic.IEnumerable", StringComparison.Ordinal) ||
                        baseName.StartsWith("System.Collections.Generic.IAsyncEnumerable", StringComparison.Ordinal) ||
                        baseName.StartsWith("System.Collections.Generic.List", StringComparison.Ordinal))
                    {
                        foreach (var arg in namedType.TypeArguments)
                            ExtractOnlyFromSignature(arg);
                        return;
                    }

                    if (!namedType.IsUnboundGenericType && isEligibleDataContract)
                    {
                        var fqn = namedType.ToDisplayString(format);

                        if (!fqn.StartsWith("global::System", StringComparison.Ordinal) &&
                            !fqn.StartsWith("global::Microsoft", StringComparison.Ordinal))
                        {
                            closedGenerics.Add(fqn);
                        }
                    }

                    foreach (var arg in namedType.TypeArguments)
                        ExtractOnlyFromSignature(arg);
                }
            }

            foreach (var p in executeMethod.Parameters)
                ExtractOnlyFromSignature(p.Type);
            ExtractOnlyFromSignature(executeMethod.ReturnType);

            isStream = returnTypeString.Contains("IAsyncEnumerable") ||
                       returnTypeString.Contains("IEnumerable");
        }

        ITypeSymbol? requestSymbol = null;
        ITypeSymbol? responseSymbol = null;
        if (executeMethod is not null)
        {
            if (executeMethod.Parameters.Length > 0)
                requestSymbol = executeMethod.Parameters[0].Type;
            responseSymbol = executeMethod.ReturnType;
        }

        var requestFields = ProtoSchemaEmitter.ExtractFields(requestSymbol);
        var responseFields = ProtoSchemaEmitter.ExtractFields(responseSymbol);
        var (nested, enums) = ProtoSchemaEmitter.CollectTypeGraph(requestSymbol, responseSymbol);

        return new GrpcEndpointModel(
            handlerType.Name,
            handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            requestTypeString,
            returnTypeString,
            finalGrpcRoute,
            methodName,
            isStream,
            closedGenerics.ToImmutableArray(),
            package,
            serviceName,
            ProtoSchemaEmitter.MessageName(requestSymbol),
            ProtoSchemaEmitter.MessageName(responseSymbol),
            requestFields.Fields.ToImmutableArray(),
            responseFields.Fields.ToImmutableArray(),
            nested,
            enums);
    }

    private static ImmutableArray<TypeConfigModel> GetTypeConfigsFromCompilation(Compilation compilation)
    {
        var visitor = new ServerGlobalTypeVisitor(compilation);
        visitor.Visit(compilation.GlobalNamespace);
        return visitor.TypeConfigs.ToImmutableArray();
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<GrpcEndpointModel?> endpoints,
        ImmutableArray<TypeConfigModel> typeConfigs,
        Compilation compilation)
    {
        var validEndpoints = endpoints.Where(e => e is not null).Cast<GrpcEndpointModel>().ToImmutableArray();

        // No MapGrpc in this compilation → do not emit registration extensions (avoids noise and CS0121).
        if (validEndpoints.IsDefaultOrEmpty)
        {
            return;
        }

        var asmName = compilation.Assembly.Name ?? "Assembly";
        var asmSuffix = SanitizeName(asmName);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using KetPsi.Endpoints.Grpc;");
        sb.AppendLine("using Grpc.AspNetCore.Server;");
        sb.AppendLine();
        sb.AppendLine($"namespace KetPsi.Endpoints.Grpc.Generated.{asmSuffix}");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Per-assembly registration for <c>{asmName}</c>. Registered into <see cref=\"GrpcGeneratedRegistrationHub\"/> at load time.</summary>");
        sb.AppendLine("    internal static class Registration");
        sb.AppendLine("    {");
        sb.AppendLine("        [ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        sb.AppendLine("            GrpcGeneratedRegistrationHub.Register(AddServices, MapEndpoints, UseProto);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void AddServices(IServiceCollection services)");
        sb.AppendLine("        {");

        var distinctHandlers = validEndpoints.Select(e => e.HandlerFullyQualifiedName).Distinct().ToList();
        foreach (var handler in distinctHandlers)
        {
            sb.AppendLine($"            services.AddScoped<{GlobalType(handler)}>();");
        }

        var proxyNames = validEndpoints
            .GroupBy(e => e.FinalGrpcRoute)
            .Select(g => g.Key.Replace(".", "_") + "_Proxy")
            .Distinct()
            .ToList();
        foreach (var proxy in proxyNames)
        {
            sb.AppendLine($"            services.AddScoped<global::KetPsi.Endpoints.Grpc.{proxy}>();");
            var svc = proxy.Replace("_Proxy", "_GrpcService");
            sb.AppendLine($"            services.AddScoped<global::KetPsi.Endpoints.Grpc.{svc}>();");
        }

        sb.AppendLine("            global::KetPsi.Endpoints.Grpc.GeneratedGrpcServiceMethodProviders.AddGeneratedGrpcServiceMethodProviders(services);");
        foreach (var interceptorFqn in CollectAllInterceptorTypes(typeConfigs))
        {
            sb.AppendLine($"            services.AddScoped<{GlobalType(interceptorFqn)}>();");
        }
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void MapEndpoints(WebApplication app)");
        sb.AppendLine("        {");
        sb.AppendLine("            global::KetPsi.Endpoints.Grpc.GeneratedProtoCodecs.Register();");
        sb.AppendLine();

        var distinctServices = validEndpoints
            .GroupBy(e => e.FinalGrpcRoute)
            .Select(g => new
            {
                ServiceRoute = g.Key,
                SanitizedName = g.Key.Replace(".", "_")
            })
            .ToList();

        foreach (var service in distinctServices)
        {
            sb.AppendLine("            {");
            sb.AppendLine($"                var __grpcEp = app.MapGrpcService<global::KetPsi.Endpoints.Grpc.{service.SanitizedName}_GrpcService>();");
            sb.AppendLine($"                global::KetPsi.Endpoints.Grpc.GrpcEndpointConventionRegistry.Apply(\"{service.ServiceRoute}\", __grpcEp);");
            sb.AppendLine("            }");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void UseProto(WebApplication app)");
        sb.AppendLine("        {");

        var endpointsForSchema = ProtoSchemaEmitter.ApplyTypeConfigFields(validEndpoints, typeConfigs);
        var schema = ProtoSchemaEmitter.Emit(endpointsForSchema);
        var escaped = schema.TrimStart().Replace("\"", "\"\"");
        sb.Append("            const string schema = @\"");
        sb.Append(escaped);
        sb.AppendLine("\";");
        sb.AppendLine("            app.MapGet(\"/proto\", static () => Microsoft.AspNetCore.Http.Results.Text(schema, \"text/plain\"));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedGrpcRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        // Generated serialize/deserialize codecs from the same field maps as /proto
        ProtoCodecEmitter.Emit(context, validEndpoints, typeConfigs, compilation);

        EmitGrpcServices(context, validEndpoints, typeConfigs, compilation);
    }

    /// <summary>
    /// Visits the compilation solely to discover IProtoTypeConfiguration / IOpenProtoTypeConfiguration implementations.
    /// </summary>

    private static void EmitGrpcServices(SourceProductionContext context, ImmutableArray<GrpcEndpointModel> endpoints, ImmutableArray<TypeConfigModel> typeConfigs, Compilation compilation)
    {
        if (endpoints.IsDefaultOrEmpty)
            return;

        var groups = endpoints.GroupBy(e => e.FinalGrpcRoute).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Grpc.AspNetCore.Server;");
        sb.AppendLine("using Grpc.AspNetCore.Server.Model;");
        sb.AppendLine("using Grpc.Core;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();
        sb.AppendLine("namespace KetPsi.Endpoints.Grpc");
        sb.AppendLine("{");

        foreach (var g in groups)
        {
            var route = g.Key;
            var sanitized = route.Replace(".", "_");
            var methods = g.GroupBy(m => m.MethodName, StringComparer.Ordinal).Select(x => x.First()).ToList();
            var handlers = methods.Select(m => m.HandlerFullyQualifiedName).Distinct().ToList();

            // Service implementation (DI-created; never null when handling calls)
            sb.AppendLine($"    internal sealed class {sanitized}_GrpcService");
            sb.AppendLine("    {");
            for (var i = 0; i < handlers.Count; i++)
                sb.AppendLine($"        private readonly {GlobalType(handlers[i])} _handler_{i};");
            var ctorParams = string.Join(", ", handlers.Select((h, i) => GlobalType(h) + " handler_" + i));
            sb.AppendLine($"        public {sanitized}_GrpcService({ctorParams})");
            sb.AppendLine("        {");
            for (var i = 0; i < handlers.Count; i++)
                sb.AppendLine($"            _handler_{i} = handler_{i};");
            sb.AppendLine("        }");
            foreach (var m in methods)
            {
                var hi = handlers.IndexOf(m.HandlerFullyQualifiedName);
                var req = GlobalType(m.RequestTypeName);
                var res = GlobalType(UnwrapTaskName(m.ResponseTypeName));
                sb.AppendLine($"        public async Task<{res}> {m.MethodName}({req} request, ServerCallContext context)");
                sb.AppendLine("        {");
                sb.AppendLine($"            GrpcMetadataBinderGenerated.Apply_{SanitizeName(m.RequestMessageName)}(request, context);");
                sb.AppendLine($"            return await _handler_{hi}.ExecuteAsync(request, context.CancellationToken).ConfigureAwait(false);");
                sb.AppendLine("        }");
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            // Public Method descriptors for clients (no protobuf-net CreateGrpcService)
            sb.AppendLine($"    internal static class {sanitized}_GrpcMethods");
            sb.AppendLine("    {");
            foreach (var m in methods)
            {
                var req = GlobalType(m.RequestTypeName);
                var res = GlobalType(UnwrapTaskName(m.ResponseTypeName));
                var reqSafe = SanitizeName(m.RequestMessageName);
                var resSafe = SanitizeName(m.ResponseMessageName);
                sb.AppendLine($"        public static readonly Method<{req}, {res}> {m.MethodName} = new Method<{req}, {res}>(");
                sb.AppendLine("            MethodType.Unary,");
                sb.AppendLine($"            \"{route}\",");
                sb.AppendLine($"            \"{m.MethodName}\",");
                sb.AppendLine($"            GeneratedProtoCodecs.Marshaller_{reqSafe},");
                sb.AppendLine($"            GeneratedProtoCodecs.Marshaller_{resSafe});");
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            // IServiceMethodProvider — no BindServiceMethod reflection, no null service instance
            sb.AppendLine($"    internal sealed class {sanitized}_ServiceMethodProvider : IServiceMethodProvider<{sanitized}_GrpcService>");
            sb.AppendLine("    {");
            sb.AppendLine($"        void IServiceMethodProvider<{sanitized}_GrpcService>.OnServiceMethodDiscovery(ServiceMethodProviderContext<{sanitized}_GrpcService> context)");
            sb.AppendLine("        {");
            var interceptorsByHandler = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
            foreach (var c in typeConfigs)
            {
                if (c.Interceptors.IsDefaultOrEmpty) continue;
                interceptorsByHandler[c.TargetTypeName] = c.Interceptors;
                var simple = c.TargetTypeName;
                var dot = simple.LastIndexOf('.');
                if (dot >= 0) simple = simple.Substring(dot + 1);
                if (!interceptorsByHandler.ContainsKey(simple))
                    interceptorsByHandler[simple] = c.Interceptors;
            }

            foreach (var m in methods)
            {
                var req = GlobalType(m.RequestTypeName);
                var res = GlobalType(UnwrapTaskName(m.ResponseTypeName));
                ImmutableArray<string> interceptors = ImmutableArray<string>.Empty;
                if (!interceptorsByHandler.TryGetValue(m.HandlerFullyQualifiedName, out interceptors))
                {
                    var hSimple = m.HandlerFullyQualifiedName;
                    var hd = hSimple.LastIndexOf('.');
                    if (hd >= 0) hSimple = hSimple.Substring(hd + 1);
                    interceptorsByHandler.TryGetValue(hSimple, out interceptors);
                    if (interceptors.IsDefault) interceptors = ImmutableArray<string>.Empty;
                }

                sb.AppendLine($"            context.AddUnaryMethod(");
                sb.AppendLine($"                {sanitized}_GrpcMethods.{m.MethodName},");
                sb.AppendLine("                new List<object>(),");
                if (interceptors.IsDefaultOrEmpty)
                {
                    sb.AppendLine($"                static ({sanitized}_GrpcService service, {req} request, ServerCallContext ctx) => service.{m.MethodName}(request, ctx));");
                }
                else
                {
                    sb.AppendLine($"                static ({sanitized}_GrpcService service, {req} request, ServerCallContext ctx) =>");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    UnaryServerMethod<{req}, {res}> next = (r, c) => service.{m.MethodName}(r, c);");
                    for (var ii = interceptors.Length - 1; ii >= 0; ii--)
                    {
                        var ic = interceptors[ii];
                        sb.AppendLine($"                    {{");
                        sb.AppendLine($"                        var __prev = next;");
                        sb.AppendLine($"                        var __ic = ctx.GetHttpContext().RequestServices.GetRequiredService<{GlobalType(ic)}>();");
                        sb.AppendLine($"                        next = (r, c) => __ic.UnaryServerHandler(r, c, __prev);");
                        sb.AppendLine($"                    }}");
                    }
                    sb.AppendLine("                    return next(request, ctx);");
                    sb.AppendLine("                });");
                }
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // DI registration helper for providers
        sb.AppendLine("    internal static class GeneratedGrpcServiceMethodProviders");
        sb.AppendLine("    {");
        sb.AppendLine("        public static IServiceCollection AddGeneratedGrpcServiceMethodProviders(this IServiceCollection services)");
        sb.AppendLine("        {");
        foreach (var g in groups)
        {
            var sanitized = g.Key.Replace(".", "_");
            sb.AppendLine($"            services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceMethodProvider<{sanitized}_GrpcService>, {sanitized}_ServiceMethodProvider>());");
        }
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        context.AddSource("GeneratedGrpcServices.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        EmitMetadataBinders(context, endpoints, typeConfigs, compilation);
    }

    /// <summary>
    /// Emits no-op or real Apply_* helpers that copy FromMetadata values onto the request DTO.
    /// </summary>




    private static string? TryReadPropertyName(ExpressionSyntax firstArg)
    {
        if (firstArg is SimpleLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member })
            return member.Name.Identifier.Text;
        if (firstArg is ParenthesizedLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member2 })
            return member2.Name.Identifier.Text;
        if (firstArg is LiteralExpressionSyntax nameLit && nameLit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
            return nameLit.Token.ValueText;
        return null;
    }

    private static List<(string RequestTypeName, string Prop, string Source, string Key, string ClrHint)> DiscoverEndpointBindings(Compilation compilation)
    {
        var result = new List<(string, string, string, string, string)>();

        void Visit(INamespaceSymbol ns)
        {
            if (ns.Name is "System" or "Microsoft") return;
            foreach (var type in ns.GetTypeMembers()) VisitType(type);
            foreach (var child in ns.GetNamespaceMembers()) Visit(child);
        }

        void VisitType(INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Class && !type.IsAbstract)
            {
                foreach (var i in type.AllInterfaces)
                {
                    if (i.Name != "IGrpcEndpointConfiguration" || i.TypeArguments.Length != 1)
                        continue;
                    // Parse Request<TRequest>(req => req.Property(x => x.Foo, p => p.FromHeader("k")))
                    foreach (var syntaxRef in type.DeclaringSyntaxReferences)
                    {
                        if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl)
                            continue;
                        var model = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                        foreach (var inv in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                        {
                            var methodName = inv.Expression switch
                            {
                                MemberAccessExpressionSyntax ma => ma.Name is GenericNameSyntax gn ? gn.Identifier.Text : ma.Name.Identifier.Text,
                                _ => null
                            };
                            // Look for FromHeader / FromPath invocations; walk up to Property
                            if (methodName is not "FromHeader")
                                continue;

                            var source = "Header";
                            string? key = null;
                            if (inv.ArgumentList.Arguments.Count >= 1)
                            {
                                var a0 = inv.ArgumentList.Arguments[0].Expression;
                                if (a0 is LiteralExpressionSyntax lit && lit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                                    key = lit.Token.ValueText;
                            }

                            // Find enclosing Property(x => x.Name, ...) call
                            var propInv = inv.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault(a =>
                            {
                                var n = a.Expression switch
                                {
                                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                                    _ => null
                                };
                                return n == "Property" && a.ArgumentList.Arguments.Count >= 1;
                            });
                            if (propInv is null) continue;
                            var propName = TryReadPropertyName(propInv.ArgumentList.Arguments[0].Expression);
                            if (propName is null) continue;

                            // Request type from Request<TRequest>
                            string requestType = "object";
                            string clrHint = "string";
                            var requestInv = propInv.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault(a =>
                            {
                                if (a.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax gn })
                                    return gn.Identifier.Text == "Request";
                                return false;
                            });
                            if (requestInv?.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax reqGn } &&
                                reqGn.TypeArgumentList.Arguments.Count == 1)
                            {
                                var ti = model.GetTypeInfo(reqGn.TypeArgumentList.Arguments[0]);
                                if (ti.Type is not null)
                                {
                                    requestType = ti.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                    var propSym = ti.Type.GetMembers(propName).OfType<IPropertySymbol>().FirstOrDefault();
                                    if (propSym is not null)
                                    {
                                        var pt = propSym.Type;
                                        if (pt is INamedTypeSymbol nn && nn.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                                            pt = nn.TypeArguments[0];
                                        clrHint = pt.ToDisplayString();
                                        if (clrHint.StartsWith("global::"))
                                            clrHint = clrHint.Substring("global::".Length);
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(key))
                                key = propName;

                            result.Add((requestType, propName, source, key ?? "", clrHint));
                        }
                    }
                }
            }
            foreach (var nested in type.GetTypeMembers())
                VisitType(nested);
        }

        Visit(compilation.GlobalNamespace);
        return result;
    }

    private static List<string> DiscoverInterceptorTypes(Compilation compilation)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Visit(INamespaceSymbol ns)
        {
            if (ns.Name is "System" or "Microsoft")
                return;
            foreach (var type in ns.GetTypeMembers())
                VisitType(type);
            foreach (var child in ns.GetNamespaceMembers())
                Visit(child);
        }

        void VisitType(INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Class && !type.IsAbstract &&
                type.AllInterfaces.Any(i => i.Name == "IGrpcEndpointConfiguration"))
            {
                foreach (var syntaxRef in type.DeclaringSyntaxReferences)
                {
                    if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl)
                        continue;
                    var model = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                    foreach (var inv in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (inv.Expression is not MemberAccessExpressionSyntax ma)
                            continue;
                        if (ma.Name is not GenericNameSyntax gns || gns.Identifier.Text != "AddInterceptor")
                            continue;
                        if (gns.TypeArgumentList.Arguments.Count != 1)
                            continue;
                        var typeInfo = model.GetTypeInfo(gns.TypeArgumentList.Arguments[0]);
                        var sym = typeInfo.Type as ITypeSymbol;
                        if (sym is null)
                            continue;
                        var fqn = sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (seen.Add(fqn))
                            result.Add(fqn);
                    }
                }
            }
            foreach (var nested in type.GetTypeMembers())
                VisitType(nested);
        }

        Visit(compilation.GlobalNamespace);
        return result;
    }


    private static List<string> CollectAllInterceptorTypes(ImmutableArray<TypeConfigModel> typeConfigs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var c in typeConfigs)
        {
            if (c.Interceptors.IsDefaultOrEmpty) continue;
            foreach (var i in c.Interceptors)
            {
                if (seen.Add(i))
                    list.Add(i);
            }
        }
        return list;
    }

    private static void EmitBindingAssign(StringBuilder sb, string prop, string source, string key, string clrHint)
    {
        // Header-only binding (Path/Query removed)
        var clr = (clrHint ?? "string").Trim();
        if (clr.StartsWith("global::", StringComparison.Ordinal))
            clr = clr.Substring("global::".Length);
        var isNullable = clr.EndsWith("?", StringComparison.Ordinal);
        if (isNullable)
            clr = clr.Substring(0, clr.Length - 1);

        void Block(string getExpr, string assignExpr)
        {
            sb.AppendLine("            {");
            sb.AppendLine($"                var __mv = {getExpr};");
            sb.AppendLine("                if (__mv is not null)");
            sb.AppendLine($"                    request.{prop} = {assignExpr};");
            sb.AppendLine("            }");
        }

        var simple = clr;
        var lastDot = simple.LastIndexOf('.');
        if (lastDot >= 0)
            simple = simple.Substring(lastDot + 1);

        switch (simple)
        {
            case "String":
            case "string":
                Block($"GrpcMetadataBinder.GetString(context, \"{key}\")", "__mv");
                break;
            case "Int32":
            case "int":
                Block($"GrpcMetadataBinder.GetInt32(context, \"{key}\")", isNullable ? "__mv" : "__mv.Value");
                break;
            case "Int64":
            case "long":
                Block($"GrpcMetadataBinder.GetInt64(context, \"{key}\")", isNullable ? "__mv" : "__mv.Value");
                break;
            case "Boolean":
            case "bool":
                Block($"GrpcMetadataBinder.GetBoolean(context, \"{key}\")", isNullable ? "__mv" : "__mv.Value");
                break;
            case "Guid":
                Block($"GrpcMetadataBinder.GetGuid(context, \"{key}\")", isNullable ? "__mv" : "__mv.Value");
                break;
            default:
                sb.AppendLine("            {");
                sb.AppendLine($"                var __mv = GrpcMetadataBinder.GetString(context, \"{key}\");");
                sb.AppendLine("                if (__mv is not null)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    if (typeof({clr}).IsEnum)");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        if (System.Enum.TryParse(typeof({clr}), __mv, true, out var __ev) && __ev is not null)");
                sb.AppendLine($"                            request.{prop} = ({clr})__ev;");
                sb.AppendLine("                    }");
                sb.AppendLine("                    else");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        request.{prop} = ({clr})System.Convert.ChangeType(__mv, typeof({clr}), System.Globalization.CultureInfo.InvariantCulture);");
                sb.AppendLine("                    }");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                break;
        }
    }


    private static void EmitMetadataBinders(
        SourceProductionContext context,
        ImmutableArray<GrpcEndpointModel> endpoints,
        ImmutableArray<TypeConfigModel> typeConfigs,
        Compilation compilation)
    {
        var byTarget = new Dictionary<string, TypeConfigModel>(StringComparer.Ordinal);
        foreach (var c in typeConfigs)
        {
            if (string.IsNullOrEmpty(c.TargetTypeName) || c.TargetTypeName == "IOpenProtoTypeConfiguration")
                continue;
            byTarget[c.TargetTypeName] = c;
            var simple = c.TargetTypeName;
            var idx = simple.LastIndexOf('.');
            if (idx >= 0) simple = simple.Substring(idx + 1);
            if (!byTarget.ContainsKey(simple))
                byTarget[simple] = c;
        }

        // Merge FromHeader / FromPath bindings from IGrpcEndpointConfiguration
        foreach (var group in DiscoverEndpointBindings(compilation).GroupBy(x => x.RequestTypeName))
        {
            var bindings = group.Select(x => (x.Prop, x.Source, x.Key, x.ClrHint)).ToImmutableArray();
            var existing = byTarget.TryGetValue(group.Key, out var prev) ? prev : null;
            var fields = existing?.Fields ?? ImmutableArray<(string, int)>.Empty;
            var merged = new TypeConfigModel(
                existing?.ConfigClassName ?? "EndpointBinding",
                group.Key,
                fields,
                bindings);
            byTarget[group.Key] = merged;
            var simple = group.Key;
            var dot = simple.LastIndexOf('.');
            if (dot >= 0) simple = simple.Substring(dot + 1);
            byTarget[simple] = merged;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Grpc.Core;");
        sb.AppendLine();
        sb.AppendLine("namespace KetPsi.Endpoints.Grpc");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Generated metadata binders for FromMetadata properties.</summary>");
        sb.AppendLine("    internal static class GrpcMetadataBinderGenerated");
        sb.AppendLine("    {");

        foreach (var ep in endpoints)
        {
            var reqMsg = ep.RequestMessageName;
            if (string.IsNullOrEmpty(reqMsg) || !seen.Add(reqMsg))
                continue;
            var reqType = ep.RequestTypeName;
            TypeConfigModel? cfg = null;
            if (!string.IsNullOrEmpty(reqType))
            {
                byTarget.TryGetValue(reqType, out cfg);
                if (cfg is null)
                {
                    var simple = reqType;
                    var dot = simple.LastIndexOf('.');
                    if (dot >= 0) simple = simple.Substring(dot + 1);
                    byTarget.TryGetValue(simple, out cfg);
                }
            }

            var safe = SanitizeName(reqMsg);
            sb.AppendLine($"        public static void Apply_{safe}({reqType} request, ServerCallContext context)");
            sb.AppendLine("        {");
                        if (cfg is not null && !cfg.MetadataBindings.IsDefaultOrEmpty)
            {
                foreach (var (prop, source, key, clr) in cfg.MetadataBindings)
                {
                    EmitBindingAssign(sb, prop, source, key, clr);
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        context.AddSource("GeneratedGrpcMetadataBinders.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string UnwrapTaskName(string typeName)
    {
        foreach (var prefix in new[]
                 {
                     "global::System.Threading.Tasks.Task<",
                     "System.Threading.Tasks.Task<",
                     "global::System.Threading.Tasks.ValueTask<",
                     "System.Threading.Tasks.ValueTask<"
                 })
        {
            var idx = typeName.IndexOf(prefix, System.StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = idx + prefix.Length;
                var end = typeName.LastIndexOf('>');
                if (end > start)
                    return typeName.Substring(start, end - start).Trim();
            }
        }
        return typeName;
    }

    /// <summary>Always emit global:: type names so nested generated namespaces cannot shadow user namespaces.</summary>
    private static string GlobalType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;
        typeName = typeName.Trim();
        if (typeName.StartsWith("global::", StringComparison.Ordinal))
            return typeName;
        // FullyQualifiedFormat already uses global:: — keep as-is if present after normalize
        return "global::" + typeName;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Message";
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
                chars[i] = '_';
        }
        return new string(chars);
    }

    private sealed class ServerGlobalTypeVisitor : SymbolVisitor
    {
        public List<TypeConfigModel> TypeConfigs { get; } = new();
        private readonly Compilation _compilation;

        private readonly SymbolDisplayFormat _fqnFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        public ServerGlobalTypeVisitor(Compilation compilation) => _compilation = compilation;

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            if (symbol.Name is "System" or "Microsoft")
                return;

            foreach (var member in symbol.GetMembers())
                member.Accept(this);
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            if (symbol.TypeKind == TypeKind.Class && !symbol.IsAbstract)
            {
                foreach (var i in symbol.AllInterfaces)
                {
                    if (i.Name == "IGrpcEndpointConfiguration" && i.TypeArguments.Length == 1)
                    {
                        var interceptors = ExtractInterceptors(symbol);
                        TypeConfigs.Add(new TypeConfigModel(
                            symbol.ToDisplayString(_fqnFormat),
                            i.TypeArguments[0].ToDisplayString(_fqnFormat),
                            interceptors: interceptors));
                        // continue — may also implement other interfaces
                    }

                    if (i.Name.Contains("IProtoTypeConfiguration") && i.TypeArguments.Length > 0)
                    {
                        var (fields, metadata, factoryMethod, factoryDeclaring) = ExtractConfigMaps(symbol, i.TypeArguments[0]);
                        TypeConfigs.Add(new TypeConfigModel(
                            symbol.ToDisplayString(_fqnFormat),
                            i.TypeArguments[0].ToDisplayString(_fqnFormat),
                            fields,
                            metadata,
                            factoryMethodName: factoryMethod,
                            factoryDeclaringTypeName: factoryDeclaring));
                        break;
                    }

                    if (i.Name.Contains("IOpenProtoTypeConfiguration"))
                    {
                        foreach (var openCfg in ExtractOpenGenericConfigs(symbol))
                            TypeConfigs.Add(openCfg);
                        break;
                    }
                }
            }

            foreach (var nested in symbol.GetTypeMembers())
                nested.Accept(this);
        }

        /// <summary>
        /// Reads <c>builder.Property(x => x.Name, 1)</c> / <c>Property("Name", 1)</c> from Configure methods.
        /// Used for compile-time /proto schema without requiring [ProtoMember].
        /// </summary>

        private ImmutableArray<string> ExtractInterceptors(INamedTypeSymbol configClass)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var syntaxRef in configClass.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl)
                    continue;
                var model = _compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                foreach (var inv in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (inv.Expression is not MemberAccessExpressionSyntax ma)
                        continue;
                    if (ma.Name is not GenericNameSyntax gns || gns.Identifier.Text != "AddInterceptor")
                        continue;
                    if (gns.TypeArgumentList.Arguments.Count != 1)
                        continue;
                    var ti = model.GetTypeInfo(gns.TypeArgumentList.Arguments[0]);
                    if (ti.Type is null) continue;
                    var fqn = ti.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (seen.Add(fqn))
                        list.Add(fqn);
                }
            }
            return list.ToImmutableArray();
        }



        /// <summary>
        /// Parses InstantiateWith(string), InstantiateWith(Type, string), InstantiateWith(nameof(...)),
        /// or InstantiateWith(SomeType.Factory) method-group / Delegate argument.
        /// </summary>
        private void ParseInstantiateWithInvocation(
            InvocationExpressionSyntax inv,
            SemanticModel model,
            ref string? factoryMethod,
            ref string? factoryDeclaring)
        {
            if (inv.ArgumentList.Arguments.Count == 0)
                return;

            var a0 = inv.ArgumentList.Arguments[0].Expression;

            // InstantiateWith("Create")
            if (a0 is LiteralExpressionSyntax lit &&
                lit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
            {
                factoryMethod = lit.Token.ValueText;
                factoryDeclaring = null;
                if (inv.ArgumentList.Arguments.Count >= 2 &&
                    inv.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax lit2 &&
                    inv.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax)
                {
                    // shouldn't happen — string first
                }
                return;
            }

            // InstantiateWith(typeof(Helper), "Create")
            if (inv.ArgumentList.Arguments.Count >= 2 &&
                a0 is TypeOfExpressionSyntax toe &&
                inv.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax litName)
            {
                factoryMethod = litName.Token.ValueText;
                factoryDeclaring = model.GetTypeInfo(toe.Type).Type?
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return;
            }

            // InstantiateWith(nameof(ResponseWrapper<>.Create)) or nameof(Type.Create)
            if (a0 is InvocationExpressionSyntax nameofInv)
            {
                var exprText = nameofInv.Expression.ToString();
                if (exprText == "nameof" || exprText.EndsWith(".nameof", StringComparison.Ordinal))
                {
                    if (nameofInv.ArgumentList.Arguments.Count > 0)
                    {
                        var target = nameofInv.ArgumentList.Arguments[0].Expression.ToString();
                        factoryMethod = target.Split('.').Last().Trim();
                        factoryDeclaring = null;
                    }
                    return;
                }
            }

            // InstantiateWith(Type.Create) / method group → Delegate
            var symbol = model.GetSymbolInfo(a0).Symbol
                         ?? model.GetSymbolInfo(a0).CandidateSymbols.FirstOrDefault();
            if (symbol is IMethodSymbol method)
            {
                factoryMethod = method.Name;
                factoryDeclaring = method.ContainingType?
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                // For open generic method on ResponseWrapper<T>, ContainingType may be ResponseWrapper<T>
                // Prefer calling on the concrete closed type at emit time when declaring is the open def
                if (method.ContainingType is { IsGenericType: true } gt && gt.IsDefinition == false)
                {
                    // constructed — ok
                }
                else if (method.ContainingType is { IsGenericType: true } openGt && openGt.IsDefinition)
                {
                    // leave declaring null so emit uses concrete closed typeName
                    factoryDeclaring = null;
                }
                return;
            }

            // Fallback: MemberAccess ...Create
            if (a0 is MemberAccessExpressionSyntax ma)
            {
                factoryMethod = ma.Name.Identifier.Text;
                factoryDeclaring = null;
            }
        }

        private List<TypeConfigModel> ExtractOpenGenericConfigs(INamedTypeSymbol configClass)
        {
            var results = new List<TypeConfigModel>();
            foreach (var syntaxRef in configClass.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl)
                    continue;
                var model = _compilation.GetSemanticModel(syntaxRef.SyntaxTree);

                foreach (var invocation in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var methodName = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                        IdentifierNameSyntax id => id.Identifier.Text,
                        _ => null
                    };
                    if (methodName != "ConfigureGeneric" || invocation.ArgumentList.Arguments.Count < 2)
                        continue;
                    if (invocation.ArgumentList.Arguments[0].Expression is not TypeOfExpressionSyntax toe)
                        continue;
                    var openType = model.GetTypeInfo(toe.Type).Type as INamedTypeSymbol;
                    if (openType is null)
                        continue;

                    var fields = new List<(string Name, int Number)>();
                    string? factoryMethod = null;
                    string? factoryDeclaring = null;

                    SyntaxNode? body = invocation.ArgumentList.Arguments[1].Expression switch
                    {
                        SimpleLambdaExpressionSyntax s => (SyntaxNode?)s.Body,
                        ParenthesizedLambdaExpressionSyntax pl => pl.Body,
                        _ => null
                    };
                    if (body is null)
                        continue;

                    foreach (var inv in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                    {
                        var mn = inv.Expression switch
                        {
                            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                            IdentifierNameSyntax id => id.Identifier.Text,
                            _ => null
                        };
                        if (mn == "Property" && inv.ArgumentList.Arguments.Count >= 2)
                        {
                            var a0 = inv.ArgumentList.Arguments[0].Expression;
                            string? propName = null;
                            if (a0 is LiteralExpressionSyntax lp && lp.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                                propName = lp.Token.ValueText;
                            else
                            {
                                var txt = a0.ToString();
                                if (txt.Contains("nameof"))
                                    propName = txt.Replace("nameof", "").Replace("(", "").Replace(")", "").Trim().Split('.').Last();
                            }
                            if (propName is not null &&
                                inv.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax ln &&
                                int.TryParse(ln.Token.ValueText, out var num))
                                fields.Add((propName, num));
                        }
                        else if (mn == "InstantiateWith")
                        {
                            ParseInstantiateWithInvocation(inv, model, ref factoryMethod, ref factoryDeclaring);
                        }
                    }

                    var def = openType.OriginalDefinition;
                    var display = def.ToDisplayString();
                    results.Add(new TypeConfigModel(
                        configClass.ToDisplayString(_fqnFormat),
                        display,
                        fields.ToImmutableArray(),
                        factoryMethodName: factoryMethod,
                        factoryDeclaringTypeName: factoryDeclaring));

                    var tick = (def.ContainingNamespace is { IsGlobalNamespace: false } ns
                        ? ns.ToDisplayString() + "."
                        : "") + def.Name + "`" + def.Arity;
                    if (!string.Equals(tick, display, StringComparison.Ordinal))
                    {
                        results.Add(new TypeConfigModel(
                            configClass.ToDisplayString(_fqnFormat),
                            tick,
                            fields.ToImmutableArray(),
                            factoryMethodName: factoryMethod,
                            factoryDeclaringTypeName: factoryDeclaring));
                    }
                }
            }
            return results;
        }

        private (ImmutableArray<(string Name, int Number)> Fields, ImmutableArray<(string Name, string Source, string Key, string ClrTypeHint)> Metadata, string? FactoryMethod, string? FactoryDeclaringType) ExtractConfigMaps(INamedTypeSymbol configClass, ITypeSymbol? targetType)
        {
            var fields = new List<(string Name, int Number)>();
            var metadata = new List<(string Name, string Source, string Key, string ClrTypeHint)>();
            string? factoryMethod = null;
            string? factoryDeclaring = null;

            foreach (var syntaxRef in configClass.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl)
                    continue;

                var model = _compilation.GetSemanticModel(syntaxRef.SyntaxTree);

                foreach (var invocation in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var methodName = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax ma => ma.Name is GenericNameSyntax gn ? gn.Identifier.Text : ma.Name.Identifier.Text,
                        IdentifierNameSyntax id => id.Identifier.Text,
                        _ => null
                    };

                    if (methodName is null)
                        continue;

                    if (methodName == "InstantiateWith")
                    {
                        ParseInstantiateWithInvocation(invocation, model, ref factoryMethod, ref factoryDeclaring);
                        continue;
                    }

                    if (invocation.ArgumentList.Arguments.Count < 2)
                        continue;

                    var propName = TryReadPropertyName(invocation.ArgumentList.Arguments[0].Expression);
                    if (propName is null)
                        continue;

                    if (methodName == "Property")
                    {
                        var numberArg = invocation.ArgumentList.Arguments[1].Expression;
                        if (numberArg is not LiteralExpressionSyntax lit || !lit.Token.ValueText.All(char.IsDigit))
                            continue;
                        if (!int.TryParse(lit.Token.ValueText, out var number))
                            continue;
                        fields.Add((propName, number));
                    }
                }
            }

            return (fields.ToImmutableArray(), metadata.ToImmutableArray(), factoryMethod, factoryDeclaring);
        }

        private static string? TryReadPropertyName(ExpressionSyntax firstArg)
        {
            // Property(x => x.Foo, n) / FromMetadata(x => x.Foo, "key")
            if (firstArg is SimpleLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member })
                return member.Name.Identifier.Text;
            if (firstArg is ParenthesizedLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member2 })
                return member2.Name.Identifier.Text;
            // Property("Foo", n)
            if (firstArg is LiteralExpressionSyntax nameLit && nameLit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                return nameLit.Token.ValueText;
            return null;
        }
}
}
