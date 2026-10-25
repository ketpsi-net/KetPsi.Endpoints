#nullable enable
using KetPsi.Endpoints.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KetPsi.Endpoints.Http.Benchmarks;

public enum HostKind
{
    /// <summary>KetPsi <c>MapGet&lt;THandler&gt;</c>; the generator emits its own RequestDelegate (EnableRequestDelegateGenerator=true).</summary>
    KetPsi,

    /// <summary>Hand-written Minimal API lambdas intercepted by Microsoft's Request Delegate Generator (compile time).</summary>
    AspNetRdg,

    /// <summary>The same lambdas passed as <see cref="Delegate"/> so RDG cannot intercept them: classic runtime RequestDelegateFactory.</summary>
    AspNetRdf,
}

/// <summary>Builds the three equivalent applications. Nothing is started unless Kestrel is requested.</summary>
public static class BenchHosts
{
    public static WebApplication Build(HostKind kind, bool kestrel = false, int port = 0)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ApplicationName = typeof(BenchHosts).Assembly.GetName().Name,
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        if (kestrel)
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        // Source-generated metadata first: the same resolver chain a Native AOT app would use.
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, BenchJsonContext.Default));

        if (kind == HostKind.KetPsi)
            builder.Services.AddHttpEndpoints();

        var app = builder.Build();

        switch (kind)
        {
            case HostKind.KetPsi:
                MapKetPsi(app);
                break;
            case HostKind.AspNetRdg:
                MapAspNetRdg(app);
                break;
            case HostKind.AspNetRdf:
                MapAspNetRdf(app);
                break;
        }

        return app;
    }

    // ───────────────────────────── KetPsi ─────────────────────────────

    private static void MapKetPsi(WebApplication app)
    {
        // Inert call-site stubs: the generator reads these and emits the real mappings into MapHttpEndpoints().
        app.MapGet<CachedResultHandler>(Scenarios.ItemsPattern);
        app.MapGet<JsonResultsOkHandler>(Scenarios.UsersPattern);
        app.MapGet<JsonTypedOkHandler>(Scenarios.TypedUsersPattern);
        app.MapPost<PostJsonBodyHandler>(Scenarios.CreateUserPattern);
        app.MapGet<HeaderHandler>(Scenarios.TenantPattern);
        app.MapGet<AsParametersHandler>(Scenarios.SearchPattern);
        app.MapGet<TaskOfDtoHandler>(Scenarios.AsyncUsersPattern);
        app.MapHttpEndpoints();
    }

    // ───────────────────────────── ASP.NET + RDG (lambdas analyzed at compile time) ─────────────────────────────

    private static void MapAspNetRdg(WebApplication app)
    {
        app.MapGet(Scenarios.ItemsPattern, (int id, int a, int b) => Shared.CachedOk);
        app.MapGet(Scenarios.UsersPattern, (int id) => Results.Ok(new UserDto(id, Shared.UserName)));
        app.MapGet(Scenarios.TypedUsersPattern, (int id) => TypedResults.Ok(new UserDto(id, Shared.UserName)));
        app.MapPost(Scenarios.CreateUserPattern, (CreateUserRequest request) => Results.Ok(new UserDto(request.Age, request.Name)));
        app.MapGet(Scenarios.TenantPattern, ([Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Tenant")] string tenant) => TypedResults.Ok(new TenantDto(tenant)));
        app.MapGet(Scenarios.SearchPattern, ([AsParameters] SearchQuery query) => TypedResults.Ok(new SearchResultDto(query.Page, query.PageSize, query.Term, 42)));
        app.MapGet(Scenarios.AsyncUsersPattern, (int id) => Task.FromResult(new UserDto(id, Shared.UserName)));
    }

    // ───────────────────────────── ASP.NET + RDF (runtime, reflection/expression based) ─────────────────────────────

    private static void MapAspNetRdf(WebApplication app)
    {
        // Typed as Delegate: RDG only intercepts lambdas / method groups it can analyze, so these fall back to
        // RequestDelegateFactory at runtime (the non-AOT classic path).
        Delegate cached = (int id, int a, int b) => Shared.CachedOk;
        Delegate users = (int id) => Results.Ok(new UserDto(id, Shared.UserName));
        Delegate typed = (int id) => TypedResults.Ok(new UserDto(id, Shared.UserName));
        Delegate create = (CreateUserRequest request) => Results.Ok(new UserDto(request.Age, request.Name));
        Delegate tenant = ([Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Tenant")] string tenant) => TypedResults.Ok(new TenantDto(tenant));
        Delegate search = ([AsParameters] SearchQuery query) => TypedResults.Ok(new SearchResultDto(query.Page, query.PageSize, query.Term, 42));
        Delegate asyncUsers = (int id) => Task.FromResult(new UserDto(id, Shared.UserName));

#pragma warning disable RDG002 // intentional: these must NOT be compile-time generated
        app.MapGet(Scenarios.ItemsPattern, cached);
        app.MapGet(Scenarios.UsersPattern, users);
        app.MapGet(Scenarios.TypedUsersPattern, typed);
        app.MapPost(Scenarios.CreateUserPattern, create);
        app.MapGet(Scenarios.TenantPattern, tenant);
        app.MapGet(Scenarios.SearchPattern, search);
        app.MapGet(Scenarios.AsyncUsersPattern, asyncUsers);
#pragma warning restore RDG002
    }

    // ───────────────────────────── In-memory invocation ─────────────────────────────

    /// <summary>Finds the endpoint routing would select for <paramref name="request"/>.</summary>
    public static RouteEndpoint FindEndpoint(WebApplication app, BenchRequest request)
    {
        foreach (var dataSource in ((IEndpointRouteBuilder)app).DataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is RouteEndpoint route
                    && string.Equals(route.RoutePattern.RawText, request.Pattern, StringComparison.Ordinal)
                    && route.Metadata.GetMetadata<IHttpMethodMetadata>() is { } methods
                    && methods.HttpMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
                {
                    return route;
                }
            }
        }

        throw new InvalidOperationException($"No endpoint for {request.Method} {request.Pattern}.");
    }

    /// <summary>Runner that invokes the endpoint's RequestDelegate directly (binding + handler + result writing).</summary>
    public static Harness.InMemoryRequestRunner CreateEndpointRunner(WebApplication app, Scenario scenario)
    {
        var request = Scenarios.Get(scenario);
        var endpoint = FindEndpoint(app, request);
        var routeValues = new RouteValueDictionary();
        foreach (var (name, value) in request.RouteValues)
            routeValues[name] = value;

        var runner = new Harness.InMemoryRequestRunner(app.Services, endpoint.RequestDelegate!, endpoint, routeValues);
        runner.SetRequest(request);
        return runner;
    }

    /// <summary>Runner that invokes the app pipeline: routing middleware (matching, route values) + endpoint middleware.</summary>
    public static Harness.InMemoryRequestRunner CreatePipelineRunner(WebApplication app, RequestDelegate pipeline, Scenario scenario)
    {
        var runner = new Harness.InMemoryRequestRunner(app.Services, pipeline, endpoint: null, routeValues: null);
        runner.SetRequest(Scenarios.Get(scenario));
        return runner;
    }

    /// <summary>Builds UseRouting → UseEndpoints into a RequestDelegate without starting a server.</summary>
    public static RequestDelegate BuildPipeline(WebApplication app)
    {
        app.UseRouting();
        app.UseEndpoints(static _ => { });
        return ((IApplicationBuilder)app).Build();
    }
}

