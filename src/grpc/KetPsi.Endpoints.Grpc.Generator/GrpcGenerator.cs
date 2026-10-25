using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Incremental generator for KetPsi gRPC endpoints. Symbol work happens in the per-node transforms;
/// the output stage consumes value-equatable models only (no Compilation), so unrelated edits are cached.
/// </summary>
[Generator]
public sealed class GrpcGenerator : IIncrementalGenerator
{
    internal const string CallSitesStep = "KetPsi.Grpc.CallSites";
    internal const string ConfigsStep = "KetPsi.Grpc.Configs";
    internal const string AssemblyStep = "KetPsi.Grpc.Assembly";
    internal const string CombinedStep = "KetPsi.Grpc.Combined";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(CallSiteParser.IsCandidate, CallSiteParser.Transform)
            .Where(static r => r is not null)
            .Select(static (r, _) => r!)
            .WithTrackingName(CallSitesStep);

        var configs = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (n, _) => n is TypeDeclarationSyntax { BaseList: not null } and (ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax),
                ConfigParser.Transform)
            .Where(static r => r is not null)
            .Select(static (r, _) => r!)
            .WithTrackingName(ConfigsStep);

        var assembly = context.CompilationProvider
            .Select(static (c, ct) => GetAssemblyInfo(c, ct))
            .WithTrackingName(AssemblyStep);

        var combined = callSites.Collect()
            .Combine(configs.Collect())
            .Combine(assembly)
            .WithTrackingName(CombinedStep);

        context.RegisterSourceOutput(combined, static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right));
    }

    private static AssemblyInfo GetAssemblyInfo(Compilation compilation, CancellationToken ct)
    {
        var referencesRuntime = compilation.GetTypeByMetadataName("KetPsi.Endpoints.Grpc.GrpcGeneratedRegistrationHub") is not null;
        var registrations = new List<string>();
        if (referencesRuntime)
        {
            foreach (var asm in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                ct.ThrowIfCancellationRequested();
                if (!asm.Modules.Any(m => m.ReferencedAssemblies.Any(r => r.Name == "KetPsi.Endpoints.Grpc")))
                    continue;
                foreach (var attr in asm.GetAttributes())
                {
                    if (attr.AttributeClass?.ToDisplayString() == "KetPsi.Endpoints.Grpc.GrpcGeneratedRegistrationAttribute"
                        && attr.ConstructorArguments.Length == 1
                        && attr.ConstructorArguments[0].Value is INamedTypeSymbol t
                        && t.DeclaredAccessibility == Accessibility.Public)
                    {
                        registrations.Add(t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }
        }
        registrations.Sort(StringComparer.Ordinal);
        return new AssemblyInfo(compilation.AssemblyName ?? "Assembly", referencesRuntime, registrations.ToEquatableArray());
    }

    private static void Execute(SourceProductionContext spc, ImmutableArray<CallSiteResult> callSites,
        ImmutableArray<ConfigResult> configs, AssemblyInfo assembly)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        void Report(DiagnosticInfo d)
        {
            if (reported.Add(d.Id + "|" + d.Location + "|" + string.Join("|", d.Args)))
                spc.ReportDiagnostic(d.ToDiagnostic());
        }

        foreach (var cs in callSites)
            foreach (var d in cs.Diagnostics)
                Report(d);
        foreach (var c in configs)
            foreach (var d in c.Diagnostics)
                Report(d);

        if (!assembly.ReferencesRuntime)
            return;

        var endpoints = callSites
            .Where(c => c.Endpoint is not null)
            .Select(c => c.Endpoint!)
            .OrderBy(e => e.Location?.FilePath ?? "", StringComparer.Ordinal)
            .ThenBy(e => e.Location?.Span.Start ?? 0)
            .ToList();

        if (endpoints.Count == 0)
        {
            if (assembly.ReferencedRegistrations.Length > 0)
                spc.AddSource("KetPsiGrpcRegistration.g.cs", SourceText.From(EmitRegistration(assembly, null, null), Encoding.UTF8));
            return;
        }

        var plan = OutputPlanner.Build(endpoints, configs.ToList());
        foreach (var d in plan.Diagnostics)
            spc.ReportDiagnostic(d);

        spc.AddSource("KetPsiGrpcCodecs.g.cs", SourceText.From(CodecEmitter.Emit(plan), Encoding.UTF8));
        spc.AddSource("KetPsiGrpcServices.g.cs", SourceText.From(ServiceEmitter.Emit(plan), Encoding.UTF8));
        EmitConventions(spc, plan);
        var schema = SchemaEmitter.Emit(plan);
        spc.AddSource("KetPsiGrpcRegistration.g.cs", SourceText.From(EmitRegistration(assembly, plan, schema), Encoding.UTF8));
    }

    private static string AssemblyIdent(string assemblyName)
        => string.Join(".", assemblyName.Split('.').Select(s => OutputPlanner.Sanitize(s)));

    private static string EmitRegistration(AssemblyInfo assembly, OutputPlan? plan, List<SchemaEmitter.ProtoFile>? schema)
    {
        var ns = "KetPsi.Endpoints.Grpc.Generated." + AssemblyIdent(assembly.AssemblyName);
        var w = new CodeWriter();
        w.Line("// <auto-generated/>");
        w.Line("#nullable disable");
        w.Line($"[assembly: global::KetPsi.Endpoints.Grpc.GrpcGeneratedRegistrationAttribute(typeof(global::{ns}.KetPsiGrpcRegistration))]");
        w.Line();
        w.Open($"namespace {ns}");
        w.Line("/// <summary>");
        w.Line($"/// Generated gRPC registration for <c>{assembly.AssemblyName}</c>. <see cref=\"Ensure\"/> registers this assembly and,");
        w.Line("/// explicitly, every referenced assembly with generated registrations (deterministic, AOT-safe; no reliance on");
        w.Line("/// module initializers of assemblies that were never loaded).");
        w.Line("/// </summary>");
        w.Line("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        w.Open("public static class KetPsiGrpcRegistration");
        w.Line("private static int s_ensured;");
        w.Line();
        w.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        w.Line("internal static void Initialize() => Ensure();");
        w.Line();
        w.Line("/// <summary>Registers this assembly (and referenced ones) with the hub. Idempotent.</summary>");
        w.Open("public static void Ensure()");
        w.Line("if (global::System.Threading.Interlocked.Exchange(ref s_ensured, 1) != 0) return;");
        foreach (var r in assembly.ReferencedRegistrations)
            w.Line($"{r}.Ensure();");
        if (plan is not null)
        {
            w.Line("global::KetPsi.Endpoints.Grpc.GrpcGeneratedRegistrationHub.Register(");
            w.Line($"    {Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(assembly.AssemblyName, quote: true)}, AddServices, MapEndpoints, ProtoFiles());");
        }
        w.Close();

        if (plan is not null)
        {
            w.Line();
            const string di = "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";
            w.Open("private static void AddServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            foreach (var h in plan.Endpoints.Select(e => e.Model.HandlerType).Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal))
                w.Line($"{di}.TryAddScoped<{h}>(services);");
            foreach (var i in plan.Endpoints.SelectMany(e => e.Interceptors).Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal))
                w.Line($"{di}.TryAddScoped<{i}>(services);");
            foreach (var svc in ServiceEmitter.Services(plan))
            {
                var id = "global::KetPsi.Endpoints.Grpc." + ServiceEmitter.ServiceIdent(svc.Key);
                w.Line($"{di}.TryAddSingleton<{id}_GrpcService>(services);");
                w.Line($"{di}.TryAddEnumerable(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<global::Grpc.AspNetCore.Server.Model.IServiceMethodProvider<{id}_GrpcService>, {id}_ServiceMethodProvider>());");
            }
            w.Close();
            w.Line();

            w.Open("private static void MapEndpoints(global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)");
            foreach (var svc in ServiceEmitter.Services(plan))
            {
                var id = "global::KetPsi.Endpoints.Grpc." + ServiceEmitter.ServiceIdent(svc.Key);
                var withConventions = svc.Where(e => e.Conventions.Count > 0).ToList();
                if (withConventions.Count == 0)
                {
                    w.Line($"global::Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService<{id}_GrpcService>(app);");
                    continue;
                }
                w.Open();
                w.Line($"var __service = global::Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService<{id}_GrpcService>(app);");
                foreach (var ep in withConventions)
                {
                    foreach (var c in ep.Conventions)
                    {
                        var cls = ConventionClass(c);
                        w.Line($"{cls}.Apply(new global::KetPsi.Endpoints.Grpc.GrpcMethodConventionBuilder(__service, \"{svc.Key}\", \"{ep.Model.Method}\"));");
                    }
                }
                w.Close();
            }
            w.Close();
            w.Line();

            w.Open("private static global::KetPsi.Endpoints.Grpc.GrpcProtoFile[] ProtoFiles() => new global::KetPsi.Endpoints.Grpc.GrpcProtoFile[]");
            foreach (var f in schema!)
                w.Line($"new global::KetPsi.Endpoints.Grpc.GrpcProtoFile(\"{f.FileName}\", \"{f.Package}\", @\"{f.Content.Replace("\"", "\"\"")}\"),");
            w.Close(";");
        }

        w.Close();
        w.Close();
        return w.ToString();
    }

    private static string ConventionClass(ConventionModel c)
        => "global::" + (c.Namespace.Length > 0 ? c.Namespace + "." : "") + "__KetPsiGrpcConventions_" + c.UniqueId;

    /// <summary>
    /// Conventions chained at MapGrpc call sites (and on MapGroup receiver chains) are re-emitted verbatim
    /// in the call site's namespace with its using directives (one file per call site), then applied to that single RPC.
    /// </summary>
    private static void EmitConventions(SourceProductionContext spc, OutputPlan plan)
    {
        var conventions = plan.Endpoints.SelectMany(e => e.Conventions)
            .GroupBy(c => c.UniqueId, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(c => c.UniqueId, StringComparer.Ordinal);
        foreach (var c in conventions)
        {
            var w = new CodeWriter();
            w.Line("// <auto-generated/>");
            w.Line("#nullable enable");
            foreach (var u in c.RootUsings)
                w.Line(u);
            w.Line();
            var hasNs = c.Namespace.Length > 0;
            if (hasNs)
                w.Open("namespace " + c.Namespace);
            foreach (var u in c.NamespaceUsings)
                w.Line(u);
            w.Line("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            w.Open("internal static class __KetPsiGrpcConventions_" + c.UniqueId);
            w.Open("internal static void Apply(global::Microsoft.AspNetCore.Builder.IEndpointConventionBuilder __builder)");
            w.Line("__builder" + c.Chain + ";");
            w.Close();
            w.Close();
            if (hasNs)
                w.Close();
            spc.AddSource("KetPsiGrpcConvention_" + c.UniqueId + ".g.cs", SourceText.From(w.ToString(), Encoding.UTF8));
        }
    }
}
