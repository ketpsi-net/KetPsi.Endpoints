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

        var combined = endpoints.Combine(typeConfigs);

        context.RegisterSourceOutput(combined, static (spc, data) => Execute(spc, data.Left!, data.Right));
    }

    private static bool IsMapGrpcInvocation(SyntaxNode node)
    {
        return node is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.Text.StartsWith("MapGrpc", StringComparison.Ordinal);
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

        var route = literal.Token.ValueText;
        var segments = route.Split('/');

        if (segments.Length is < 2 or > 3)
            return null;

        var grpcNamespace = segments.Length == 3 ? segments[0] : "Api";
        var serviceName = segments.Length == 3 ? segments[1] : segments[0];
        var methodName = segments.Length == 3 ? segments[2] : segments[1];
        var finalGrpcRoute = $"{grpcNamespace}.{serviceName}";

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
                requestTypeString = executeMethod.Parameters[0].Type.ToDisplayString();

            returnTypeString = executeMethod.ReturnType.ToDisplayString();

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
            handlerType.ToDisplayString(),
            requestTypeString,
            returnTypeString,
            finalGrpcRoute,
            methodName,
            isStream,
            closedGenerics.ToImmutableArray(),
            grpcNamespace,
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
        var visitor = new ServerGlobalTypeVisitor();
        visitor.Visit(compilation.GlobalNamespace);
        return visitor.TypeConfigs.ToImmutableArray();
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<GrpcEndpointModel?> endpoints,
        ImmutableArray<TypeConfigModel> typeConfigs)
    {
        var validEndpoints = endpoints.Where(e => e is not null).Cast<GrpcEndpointModel>().ToImmutableArray();

        var closedGenerics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ep in validEndpoints)
        {
            foreach (var gen in ep.ClosedGenerics)
                closedGenerics.Add(gen);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using KetPsi.Endpoints.Grpc;");
        sb.AppendLine("using Grpc.AspNetCore.Server;");
        sb.AppendLine();
        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static class GeneratedGrpcRegistrationExtensions");
        sb.AppendLine("    {");

        // ---- AddGrpcServices ----
        sb.AppendLine("        public static IServiceCollection AddGrpcServices(this IServiceCollection services)");
        sb.AppendLine("        {");
        var distinctHandlers = validEndpoints.Select(e => e.HandlerFullyQualifiedName).Distinct().ToList();
        foreach (var handler in distinctHandlers)
        {
            sb.AppendLine($"            services.AddScoped<{handler}>();");
        }

        // Proxies must be in DI for MapGrpcService<T> resolution
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
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // ---- MapGrpcEndpoints (proxies only; /proto is UseProtoEndpoint) ----
        sb.AppendLine("        public static WebApplication MapGrpcEndpoints(this WebApplication app)");
        sb.AppendLine("        {");

        // Compile-time pure wire codecs (no protobuf-net)
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
            sb.AppendLine($"            app.MapGrpcService<global::KetPsi.Endpoints.Grpc.{service.SanitizedName}_GrpcService>();");
        }

        sb.AppendLine("            return app;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // ---- UseProtoEndpoint (optional /proto — compile-time schema, no SchemaGenerator) ----
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Maps the optional /proto endpoint that returns the aggregated protobuf schema");
        sb.AppendLine("        /// for all discovered gRPC contracts. Schema is generated at compile time.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static WebApplication UseProtoEndpoint(this WebApplication app)");
        sb.AppendLine("        {");

        if (validEndpoints.Length > 0)
        {
            var endpointsForSchema = ProtoSchemaEmitter.ApplyTypeConfigFields(validEndpoints, typeConfigs);
            var schema = ProtoSchemaEmitter.Emit(endpointsForSchema);
            var escaped = schema.Replace("\"", "\"\"");
            sb.AppendLine("            // Compile-time schema (equivalent to SchemaGenerator + TypeModel.GetSchema)");
            sb.Append("            const string schema = @\"");
            sb.Append(escaped);
            sb.AppendLine("\";");
            sb.AppendLine("            app.MapGet(\"/proto\", static () => Microsoft.AspNetCore.Http.Results.Text(schema, \"text/plain\"));");
        }
        // else: no contracts → no-op (matches the hand-written fallback)

        sb.AppendLine("            return app;");
        sb.AppendLine("        }");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedGrpcRegistrationExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        // Generated serialize/deserialize codecs from the same field maps as /proto
        ProtoCodecEmitter.Emit(context, validEndpoints, typeConfigs);

        EmitGrpcServices(context, validEndpoints);
    }

    /// <summary>
    /// Visits the compilation solely to discover IProtoTypeConfiguration / IOpenProtoTypeConfiguration implementations.
    /// </summary>

    private static void EmitGrpcServices(SourceProductionContext context, ImmutableArray<GrpcEndpointModel> endpoints)
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
            sb.AppendLine($"    public sealed class {sanitized}_GrpcService");
            sb.AppendLine("    {");
            for (var i = 0; i < handlers.Count; i++)
                sb.AppendLine($"        private readonly {handlers[i]} _handler_{i};");
            var ctorParams = string.Join(", ", handlers.Select((h, i) => h + " handler_" + i));
            sb.AppendLine($"        public {sanitized}_GrpcService({ctorParams})");
            sb.AppendLine("        {");
            for (var i = 0; i < handlers.Count; i++)
                sb.AppendLine($"            _handler_{i} = handler_{i};");
            sb.AppendLine("        }");
            foreach (var m in methods)
            {
                var hi = handlers.IndexOf(m.HandlerFullyQualifiedName);
                var req = m.RequestTypeName;
                var res = UnwrapTaskName(m.ResponseTypeName);
                sb.AppendLine($"        public Task<{res}> {m.MethodName}({req} request, ServerCallContext context)");
                sb.AppendLine($"            => _handler_{hi}.ExecuteAsync(request, context.CancellationToken);");
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            // Public Method descriptors for clients (no protobuf-net CreateGrpcService)
            sb.AppendLine($"    public static class {sanitized}_GrpcMethods");
            sb.AppendLine("    {");
            foreach (var m in methods)
            {
                var req = m.RequestTypeName;
                var res = UnwrapTaskName(m.ResponseTypeName);
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
            sb.AppendLine($"    public sealed class {sanitized}_ServiceMethodProvider : IServiceMethodProvider<{sanitized}_GrpcService>");
            sb.AppendLine("    {");
            sb.AppendLine($"        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<{sanitized}_GrpcService> context)");
            sb.AppendLine("        {");
            foreach (var m in methods)
            {
                var req = m.RequestTypeName;
                var res = UnwrapTaskName(m.ResponseTypeName);
                sb.AppendLine($"            context.AddUnaryMethod(");
                sb.AppendLine($"                {sanitized}_GrpcMethods.{m.MethodName},");
                sb.AppendLine("                new List<object>(),");
                sb.AppendLine($"                static ({sanitized}_GrpcService service, {req} request, ServerCallContext ctx) => service.{m.MethodName}(request, ctx));");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // DI registration helper for providers
        sb.AppendLine("    public static class GeneratedGrpcServiceMethodProviders");
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

        private readonly SymbolDisplayFormat _fqnFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

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
                    if (i.Name.Contains("IProtoTypeConfiguration") && i.TypeArguments.Length > 0)
                    {
                        var fields = ExtractPropertyFieldMaps(symbol);
                        TypeConfigs.Add(new TypeConfigModel(
                            symbol.ToDisplayString(_fqnFormat),
                            i.TypeArguments[0].ToDisplayString(_fqnFormat),
                            fields));
                        break;
                    }

                    if (i.Name.Contains("IOpenProtoTypeConfiguration"))
                    {
                        TypeConfigs.Add(new TypeConfigModel(
                            symbol.ToDisplayString(_fqnFormat),
                            "IOpenProtoTypeConfiguration"));
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
        private static ImmutableArray<(string Name, int Number)> ExtractPropertyFieldMaps(INamedTypeSymbol configClass)
        {
            var fields = new List<(string Name, int Number)>();

            foreach (var syntaxRef in configClass.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl)
                    continue;

                foreach (var invocation in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var methodName = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                        IdentifierNameSyntax id => id.Identifier.Text,
                        _ => null
                    };

                    if (methodName != "Property" || invocation.ArgumentList.Arguments.Count < 2)
                        continue;

                    var numberArg = invocation.ArgumentList.Arguments[1].Expression;
                    if (numberArg is not LiteralExpressionSyntax lit || !lit.Token.ValueText.All(char.IsDigit))
                        continue;
                    if (!int.TryParse(lit.Token.ValueText, out var number))
                        continue;

                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    string? propName = null;

                    // Property(x => x.Foo, n)
                    if (firstArg is SimpleLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax bodyMa })
                        propName = bodyMa.Name.Identifier.Text;
                    else if (firstArg is ParenthesizedLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax bodyMa2 })
                        propName = bodyMa2.Name.Identifier.Text;
                    // Property("Foo", n)
                    else if (firstArg is LiteralExpressionSyntax nameLit)
                        propName = nameLit.Token.ValueText;

                    if (!string.IsNullOrEmpty(propName))
                        fields.Add((propName!, number));
                }
            }

            return fields.ToImmutableArray();
        }
    }
}
