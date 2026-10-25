using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using KetPsi.Endpoints.Http.Benchmarks.Harness;
using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Http.Benchmarks;

/// <summary>
/// Shared setup: builds the three hosts (never started), verifies they answer every scenario identically, then
/// prepares one pooled-context runner per host.
/// </summary>
public abstract class InMemoryBenchmarkBase
{
    private readonly List<WebApplication> _apps = [];

    [ParamsAllValues]
    public Scenario Scenario { get; set; }

    protected InMemoryRequestRunner KetPsi = null!;
    protected InMemoryRequestRunner AspNetRdg = null!;
    protected InMemoryRequestRunner AspNetRdf = null!;

    protected abstract InMemoryRequestRunner CreateRunner(WebApplication app, Scenario scenario);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        KetPsi = CreateRunner(Build(HostKind.KetPsi), Scenario);
        AspNetRdg = CreateRunner(Build(HostKind.AspNetRdg), Scenario);
        AspNetRdf = CreateRunner(Build(HostKind.AspNetRdf), Scenario);

        // Equivalence check: refuse to benchmark hosts that do different work.
        await EquivalenceCheck.AssertEquivalentAsync(Scenario, KetPsi, AspNetRdg, AspNetRdf);
    }

    private WebApplication Build(HostKind kind)
    {
        var app = BenchHosts.Build(kind);
        _apps.Add(app);
        return app;
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        foreach (var app in _apps)
            await app.DisposeAsync();
        _apps.Clear();
    }
}

/// <summary>
/// Endpoint only: invokes the selected endpoint's <c>RequestDelegate</c> with route values already set (as routing
/// would leave them). Measures parameter binding + handler + result writing, plus the lazily created per-request
/// objects a real server also creates (query collection, request service scope, …).
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams)]
[BenchmarkCategory("Endpoint")]
public class EndpointBenchmarks : InMemoryBenchmarkBase
{
    protected override InMemoryRequestRunner CreateRunner(WebApplication app, Scenario scenario)
        => BenchHosts.CreateEndpointRunner(app, scenario);

    /// <summary>Per-request reset/initialize/complete only — the harness overhead every other row includes (≈ 0 B).</summary>
    [Benchmark(Description = "Harness only")]
    public Task Harness() => KetPsi.RunHarnessOnlyAsync();

    [Benchmark(Description = "KetPsi (generated RD)")]
    public Task KetPsiEndpoint() => KetPsi.RunAsync();

    [Benchmark(Baseline = true, Description = "ASP.NET RDG")]
    public Task AspNetRdgEndpoint() => AspNetRdg.RunAsync();

    [Benchmark(Description = "ASP.NET RDF (runtime)")]
    public Task AspNetRdfEndpoint() => AspNetRdf.RunAsync();
}

/// <summary>
/// Full pipeline: <c>UseRouting</c> (route matching, route values, HTTP method policy) + <c>UseEndpoints</c> + the
/// endpoint. Still in-memory: no Kestrel parsing, sockets or HttpClient.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams)]
[BenchmarkCategory("Pipeline")]
public class PipelineBenchmarks : InMemoryBenchmarkBase
{
    protected override InMemoryRequestRunner CreateRunner(WebApplication app, Scenario scenario)
        => BenchHosts.CreatePipelineRunner(app, BenchHosts.BuildPipeline(app), scenario);

    [Benchmark(Description = "Harness only")]
    public Task Harness() => KetPsi.RunHarnessOnlyAsync();

    [Benchmark(Description = "KetPsi (generated RD)")]
    public Task KetPsiPipeline() => KetPsi.RunAsync();

    [Benchmark(Baseline = true, Description = "ASP.NET RDG")]
    public Task AspNetRdgPipeline() => AspNetRdg.RunAsync();

    [Benchmark(Description = "ASP.NET RDF (runtime)")]
    public Task AspNetRdfPipeline() => AspNetRdf.RunAsync();
}

public static class EquivalenceCheck
{
    public static async Task AssertEquivalentAsync(Scenario scenario, InMemoryRequestRunner ketPsi, InMemoryRequestRunner rdg, InMemoryRequestRunner rdf)
    {
        var k = await ketPsi.CaptureAsync();
        var g = await rdg.CaptureAsync();
        var f = await rdf.CaptureAsync();

        var problem = Compare(scenario, k, g, "AspNetRdg") ?? Compare(scenario, k, f, "AspNetRdf");
        if (problem is not null)
        {
            throw new InvalidOperationException(
                $"Hosts are not equivalent for {scenario}: {problem}\n  KetPsi:    {k}\n  AspNetRdg: {g}\n  AspNetRdf: {f}");
        }
    }

    private static string? Compare(Scenario scenario, CapturedResponse expected, CapturedResponse actual, string name)
    {
        if (expected.StatusCode != actual.StatusCode)
            return $"{name} status {actual.StatusCode} != KetPsi {expected.StatusCode}";

        if (Scenarios.CompareStatusOnly(scenario))
            return null;

        if (!string.Equals(expected.ContentType, actual.ContentType, StringComparison.OrdinalIgnoreCase))
            return $"{name} content-type '{actual.ContentType}' != KetPsi '{expected.ContentType}'";

        if (!string.Equals(expected.Body, actual.Body, StringComparison.Ordinal))
            return $"{name} body differs";

        return null;
    }

    /// <summary>Prints the response of every host for every scenario; returns false if any scenario differs.</summary>
    public static async Task<bool> RunAllAsync(TextWriter output)
    {
        var ok = true;
        await using var ketPsi = BenchHosts.Build(HostKind.KetPsi);
        await using var rdg = BenchHosts.Build(HostKind.AspNetRdg);
        await using var rdf = BenchHosts.Build(HostKind.AspNetRdf);

        foreach (var scenario in Enum.GetValues<Scenario>())
        {
            var k = BenchHosts.CreateEndpointRunner(ketPsi, scenario);
            var g = BenchHosts.CreateEndpointRunner(rdg, scenario);
            var f = BenchHosts.CreateEndpointRunner(rdf, scenario);
            try
            {
                await AssertEquivalentAsync(scenario, k, g, f);
                output.WriteLine($"[ok]   {scenario,-14} {await k.CaptureAsync()}");
                if (Scenarios.CompareStatusOnly(scenario))
                {
                    output.WriteLine($"         (status-only) AspNetRdg: {await g.CaptureAsync()}");
                }
            }
            catch (InvalidOperationException ex)
            {
                ok = false;
                output.WriteLine($"[FAIL] {ex.Message}");
            }
        }

        return ok;
    }
}
