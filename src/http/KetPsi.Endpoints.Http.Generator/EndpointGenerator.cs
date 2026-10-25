using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Http.Generator;

/// <summary>
/// Turns <c>app.MapGet&lt;THandler&gt;(...)</c> call sites into real Minimal API endpoints
/// (classic lambdas, or the KetPsi request-delegate path under <c>PublishAot</c> / <c>EnableRequestDelegateGenerator</c>).
/// <para>
/// Pipeline (all intermediate values are value-equatable records, so unrelated edits are cached):
/// Map* calls (semantic, tight predicate) → helper names → helper call sites (one scan per compilation, only
/// for trees that invoke a known helper name) → combined with handlers, configurations and options →
/// pure-data output step (no <see cref="Compilation"/>).
/// </para>
/// </summary>
[Generator]
public sealed class EndpointGenerator : IIncrementalGenerator
{
    internal const string MapCallsStep = "KetPsi.MapCalls";
    internal const string HelperCallSitesStep = "KetPsi.HelperCallSites";
    internal const string HandlersStep = "KetPsi.Handlers";
    internal const string ConfigsStep = "KetPsi.Configs";
    internal const string OptionsStep = "KetPsi.Options";
    internal const string CombinedStep = "KetPsi.Combined";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mapCalls = context.SyntaxProvider
            .CreateSyntaxProvider(Parsing.IsMapCallCandidate, Parsing.ParseMapCall)
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .WithTrackingName(MapCallsStep)
            .Collect();

        var helperRefs = mapCalls.Select(static (calls, _) => calls
            .Select(c => c.Endpoint?.Receiver)
            .Where(r => r is { Root: ChainRootKind.HelperParameter, HelperKey: not null, HelperName: not null })
            .Select(r => new HelperRef(r!.HelperKey!, r.HelperName!))
            .Distinct()
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .ToEquatableArray());

        // Cross-file call-site resolution runs once per compilation and only touches trees that invoke a helper by name.
        var helperCallSites = helperRefs
            .Combine(context.CompilationProvider)
            .Select(static (pair, ct) => Parsing.ScanHelperCallSites(pair.Left, pair.Right, ct))
            .WithTrackingName(HelperCallSitesStep);

        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(Parsing.IsClassWithBaseList, Parsing.ParseHandler)
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .WithTrackingName(HandlersStep)
            .Collect();

        var configs = context.SyntaxProvider
            .CreateSyntaxProvider(Parsing.IsClassWithBaseList, Parsing.ParseConfig)
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .WithTrackingName(ConfigsStep)
            .Collect();

        var options = context.AnalyzerConfigOptionsProvider
            .Select(static (o, _) => ReadOptions(o))
            .WithTrackingName(OptionsStep);

        var combined = mapCalls
            .Combine(helperCallSites)
            .Combine(handlers)
            .Combine(configs)
            .Combine(options)
            .Select(static (x, _) => new GeneratorInput(
                x.Left.Left.Left.Left.ToEquatableArray(),
                x.Left.Left.Left.Right,
                x.Left.Left.Right.ToEquatableArray(),
                x.Left.Right.ToEquatableArray(),
                x.Right))
            .WithTrackingName(CombinedStep);

        context.RegisterSourceOutput(combined, static (spc, input) =>
        {
            try
            {
                Execute(spc, input);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GenerationFailed, Location.None, ex.GetType().Name + ": " + ex.Message));
            }
        });
    }

    internal sealed record GeneratorInput(
        EquatableArray<MapCallResult> MapCalls,
        HelperScanResult Helpers,
        EquatableArray<HandlerResult> Handlers,
        EquatableArray<ConfigModel> Configs,
        GeneratorOptions Options);

    private static GeneratorOptions ReadOptions(AnalyzerConfigOptionsProvider provider)
    {
        // PublishAot / EnableRequestDelegateGenerator are exposed through build/KetPsi.Endpoints.Http.props
        // (CompilerVisibleProperty). Microsoft's RDG does not intercept third-party generated Map* calls, so the
        // same flag opts into the KetPsi request-delegate path.
        provider.GlobalOptions.TryGetValue("build_property.PublishAot", out var rawPublish);
        provider.GlobalOptions.TryGetValue("build_property.EnableRequestDelegateGenerator", out var rawRdg);
        var publish = IsTrue(rawPublish);
        var rdg = IsTrue(rawRdg);
        return new GeneratorOptions(publish || rdg, publish, rdg, rawPublish ?? "<missing>", rawRdg ?? "<missing>");

        static bool IsTrue(string? value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void Execute(SourceProductionContext spc, GeneratorInput input)
    {
        spc.AddSource("KetPsiRdgActivation.g.cs", SourceText.From(Emitter.Activation(input.Options), System.Text.Encoding.UTF8));

        foreach (var handler in input.Handlers)
            Report(spc, handler.Diagnostics);
        foreach (var call in input.MapCalls)
            Report(spc, call.Diagnostics);
        foreach (var config in input.Configs)
            Report(spc, config.Diagnostics);

        // Fluent builder stubs (one per handler type).
        var builders = new Dictionary<string, HandlerBuilderModel>(StringComparer.Ordinal);
        foreach (var h in input.Handlers)
        {
            if (h.Handler is not null && !builders.ContainsKey(h.Handler.HandlerFqn))
                builders[h.Handler.HandlerFqn] = h.Handler;
        }
        foreach (var call in input.MapCalls)
        {
            var ep = call.Endpoint;
            if (ep is not null && !builders.ContainsKey(ep.HandlerFqn))
            {
                builders[ep.HandlerFqn] = new HandlerBuilderModel(
                    ep.HandlerFqn,
                    ep.HandlerShortName,
                    ep.HandlerAccessibility,
                    ep.Parameters.Select(p => new BuilderParameterModel(p.Value.Type.Fqn, p.Value.Name, "public")).ToEquatableArray());
            }
        }

        var usedBuilderNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var builder in builders.Values.OrderBy(b => b.HandlerFqn, StringComparer.Ordinal))
        {
            var name = Unique(builder.ShortName, usedBuilderNames);
            spc.AddSource($"{name}HttpEndpointBuilder.g.cs", Emitter.BuilderExtensions(name, builder));
        }

        var configs = MergeConfigs(input.Configs);
        var callSites = input.Helpers.CallSites
            .GroupBy(c => c.HelperKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var endpoints = new List<EndpointModel>();
        foreach (var call in input.MapCalls)
        {
            if (call.Endpoint is null)
                continue;
            var configured = ApplyConfigs(call.Endpoint, configs);
            foreach (var resolved in Resolve(spc, configured, callSites))
            {
                if (input.Options.UseRequestDelegate && !Validate(spc, resolved))
                    continue;
                endpoints.Add(resolved);
            }
        }

        var distinct = endpoints.Distinct().ToList();
        if (distinct.Count == 0)
        {
            if (!input.MapCalls.IsEmpty)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoEndpointToGenerate, Location.None));
            return;
        }

        var usedClassNames = new HashSet<string>(StringComparer.Ordinal);
        var classNames = new List<string>();
        foreach (var group in distinct.GroupBy(e => e.ExtensionClassName, StringComparer.Ordinal))
        {
            var className = Unique(Parsing.Sanitize(group.Key), usedClassNames);
            classNames.Add(className);
            try
            {
                spc.AddSource($"{className}HttpEndpoints.g.cs", Emitter.MappingClass(className, group.ToList(), input.Options.UseRequestDelegate));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GenerationFailed, group.First().Location?.ToLocation() ?? Location.None, ex.GetType().Name + ": " + ex.Message));
            }
        }

        spc.AddSource("HttpEndpoints.g.cs", Emitter.Master(classNames));
    }

    private static void Report(SourceProductionContext spc, EquatableArray<DiagnosticInfo> diagnostics)
    {
        foreach (var d in diagnostics)
            spc.ReportDiagnostic(d.ToDiagnostic());
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var i = 1; !used.Add(candidate); i++)
            candidate = name + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }

    // ------------------------------------------------------------------
    // External configuration (IHttpEndpointConfiguration<T>) – merged without mutating cached models
    // ------------------------------------------------------------------

    private sealed class MergedParameterConfig
    {
        public bool AsParameters;
        public BindingSpec? Direct;
        public Dictionary<string, BindingSpec> Properties = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Dictionary<string, MergedParameterConfig>> MergeConfigs(EquatableArray<ConfigModel> configs)
    {
        var result = new Dictionary<string, Dictionary<string, MergedParameterConfig>>(StringComparer.Ordinal);
        foreach (var config in configs)
        {
            if (!result.TryGetValue(config.HandlerFqn, out var byParam))
                result[config.HandlerFqn] = byParam = new Dictionary<string, MergedParameterConfig>(StringComparer.Ordinal);
            foreach (var p in config.Parameters)
            {
                if (!byParam.TryGetValue(p.Key, out var merged))
                    byParam[p.Key] = merged = new MergedParameterConfig();
                merged.AsParameters |= p.AsParameters;
                if (p.Direct is not null)
                    merged.Direct = p.Direct;
                foreach (var prop in p.Properties)
                    merged.Properties[prop.PropertyName] = prop.Binding;
            }
        }
        return result;
    }

    private static EndpointModel ApplyConfigs(EndpointModel ep, Dictionary<string, Dictionary<string, MergedParameterConfig>> configs)
    {
        if (!configs.TryGetValue(ep.HandlerFqn, out var byParam))
            return ep;

        var parameters = new List<ParameterModel>();
        foreach (var p in ep.Parameters)
        {
            if (!byParam.TryGetValue(p.Value.Name, out var config) && !byParam.TryGetValue(Parsing.ToPascalCase(p.Value.Name), out config))
            {
                parameters.Add(p);
                continue;
            }

            var value = p.Value;
            if (config.AsParameters)
            {
                value = value with { Binding = new BindingSpec(BindingSource.AsParameters, null, null, false) };
                var dto = p.Dto;
                if (dto is not null && config.Properties.Count > 0)
                {
                    MemberModel Override(MemberModel m) => config.Properties.TryGetValue(m.Value.Name, out var spec)
                        ? m with { Value = m.Value with { Binding = spec } }
                        : m;
                    dto = dto with
                    {
                        ConstructorMembers = dto.ConstructorMembers.Select(Override).ToEquatableArray(),
                        PropertyMembers = dto.PropertyMembers.Select(Override).ToEquatableArray(),
                    };
                }
                parameters.Add(p with { Value = value, Dto = dto, UseSurrogate = dto is not null && config.Properties.Count > 0 });
                continue;
            }

            if (config.Direct is not null)
                value = value with { Binding = config.Direct };
            parameters.Add(p with { Value = value });
        }

        return ep with { Parameters = parameters.ToEquatableArray() };
    }

    // ------------------------------------------------------------------
    // Helper resolution: endpoints mapped on helper parameters are expanded per call site
    // ------------------------------------------------------------------

    private static IEnumerable<EndpointModel> Resolve(SourceProductionContext spc, EndpointModel ep, Dictionary<string, List<HelperCallSite>> callSites)
    {
        var results = new List<EndpointModel>();
        ResolveCore(spc, ep, ep.Receiver, ep.Receiver.Steps.ToList(), ep.RouteTemplate, ep.ChainTemplate, 0, results, callSites);
        return results;
    }

    private static void ResolveCore(
        SourceProductionContext spc,
        EndpointModel ep,
        ChainModel chain,
        List<string> steps,
        string route,
        string conventions,
        int depth,
        List<EndpointModel> results,
        Dictionary<string, List<HelperCallSite>> callSites)
    {
        void Fail(string reason, LocationInfo? location = null)
            => spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnresolvedMapCall, (location ?? ep.Location)?.ToLocation() ?? Location.None, ep.HandlerFqn, reason));

        void Done()
        {
            if (Portable.HasPlaceholders(route) || Portable.HasPlaceholders(conventions) || steps.Any(Portable.HasPlaceholders))
            {
                Fail("it uses parameters of the enclosing method that could not be resolved from a call site");
                return;
            }
            results.Add(ep with
            {
                RouteTemplate = route,
                ChainTemplate = conventions,
                Receiver = new ChainModel(ChainRootKind.App, null, null, -1, null, steps.ToEquatableArray()),
            });
        }

        if (chain.Root == ChainRootKind.App)
        {
            Done();
            return;
        }

        if (depth > 8)
        {
            Fail("helper methods are nested too deeply (or recursive)");
            return;
        }

        if (chain.HelperKey is null || !callSites.TryGetValue(chain.HelperKey, out var sites) || sites.Count == 0)
        {
            switch (chain.HelperParameterType)
            {
                case "WebApplication":
                    Done();
                    return;
                case "IEndpointRouteBuilder":
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.HelperWithoutCallSites, ep.Location?.ToLocation() ?? Location.None, chain.HelperName ?? "?", ep.HandlerFqn));
                    Done();
                    return;
                default:
                    Fail($"no call sites were found for '{chain.HelperName}', so its route group cannot be determined");
                    return;
            }
        }

        foreach (var site in sites)
        {
            var args = new Dictionary<int, string>();
            foreach (var a in site.Arguments)
            {
                if (a.Portable is not null)
                    args[a.Ordinal] = a.Portable;
            }

            var newRoute = Portable.Substitute(route, args);
            var newConventions = Portable.Substitute(conventions, args);
            var newSteps = steps.Select(s => Portable.Substitute(s, args)).ToList();
            if (newRoute is null || newConventions is null || newSteps.Any(s => s is null))
            {
                foreach (var a in site.Arguments)
                {
                    foreach (var d in a.Diagnostics)
                        spc.ReportDiagnostic(d.ToDiagnostic());
                }
                Fail($"an argument passed to '{chain.HelperName}' is not a constant or static member", site.Location);
                continue;
            }

            var receiverArg = site.Arguments.FirstOrDefault(a => a.Ordinal == chain.HelperOrdinal);
            if (receiverArg?.Chain is null || receiverArg.Chain.Root == ChainRootKind.Unresolved)
            {
                if (receiverArg is not null)
                {
                    foreach (var d in receiverArg.Diagnostics)
                        spc.ReportDiagnostic(d.ToDiagnostic());
                }
                Fail($"the route builder passed to '{chain.HelperName}' could not be resolved", site.Location);
                continue;
            }

            var combinedSteps = receiverArg.Chain.Steps.Concat(newSteps!).ToList();
            ResolveCore(spc, ep, receiverArg.Chain, combinedSteps!, newRoute, newConventions, depth + 1, results, callSites);
        }
    }

    // ------------------------------------------------------------------
    // Request-delegate path validation: anything that cannot be bound is a compile error (never silently `default`).
    // ------------------------------------------------------------------

    private static bool Validate(SourceProductionContext spc, EndpointModel ep)
    {
        var ok = true;
        var bodyAllowed = ValuePlan.BodyAllowed(ep.HttpMethod);
        var explicitBodies = new List<string>();
        var hasForm = false;

        void Error(ValueModel v, string reason)
        {
            ok = false;
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedParameter, (v.Location ?? ep.Location)?.ToLocation() ?? Location.None, v.Name, v.Type.DisplayName, ep.HandlerFqn, reason));
        }

        void Check(ValueModel v, bool isMember)
        {
            var t = v.Type;
            var kind = ValuePlan.Compute(v, bodyAllowed);
            switch (kind)
            {
                case PlanKind.Special:
                    if (t.Special is SpecialKind.FormFile or SpecialKind.FormFileCollection or SpecialKind.FormCollection)
                        hasForm = true;
                    break;
                case PlanKind.Route:
                    if (!t.IsParsableScalar)
                        Error(v, "only string and parsable types (TryParse / IParsable<T>) can be bound from the route");
                    break;
                case PlanKind.Query:
                case PlanKind.Header:
                    if (!t.IsParsableScalar && !t.IsParsableCollection)
                        Error(v, $"only string, StringValues, parsable types and arrays / List<T> of them can be bound from the {(kind == PlanKind.Query ? "query string" : "header")}");
                    break;
                case PlanKind.Form:
                    hasForm = true;
                    if (!t.IsParsableScalar && !t.IsParsableCollection)
                        Error(v, "complex [FromForm] binding is not supported on the KetPsi request-delegate path; bind individual form fields or IFormCollection");
                    break;
                case PlanKind.Body:
                    explicitBodies.Add(v.Name);
                    break;
                case PlanKind.KeyedServices:
                    if (v.Binding.KeyLiteral is null)
                    {
                        ok = false;
                        spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.KeyedServiceWithoutKey, (v.Location ?? ep.Location)?.ToLocation() ?? Location.None, v.Name, ep.HandlerFqn));
                    }
                    break;
                case PlanKind.BindAsync:
                    if (isMember && t.BindAsync != BindAsyncKind.Context)
                        Error(v, "BindAsync(HttpContext, ParameterInfo) is not supported on AsParameters members");
                    break;
                case PlanKind.AsParameters:
                    if (isMember)
                        Error(v, "nested AsParameters is not supported");
                    break;
            }
        }

        foreach (var p in ep.Parameters)
        {
            if (p.Value.Binding.Source == BindingSource.AsParameters)
            {
                if (p.Dto is null)
                {
                    Error(p.Value, "AsParameters requires a non-abstract class or struct with a public constructor");
                    continue;
                }
                foreach (var m in p.Dto.ConstructorMembers.Concat(p.Dto.PropertyMembers))
                    Check(m.Value, true);
                continue;
            }
            Check(p.Value, false);
        }

        if (explicitBodies.Count > 1 || (explicitBodies.Count == 1 && hasForm))
        {
            ok = false;
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MultipleBodyParameters, ep.Location?.ToLocation() ?? Location.None, ep.HandlerFqn,
                string.Join(", ", explicitBodies.Concat(hasForm ? new[] { "form" } : Array.Empty<string>()))));
        }

        return ok;
    }
}
