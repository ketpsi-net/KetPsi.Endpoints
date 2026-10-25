using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Http.Benchmarks.Harness;

/// <summary>
/// Drives one <see cref="RequestDelegate"/> (an endpoint, or the whole routing pipeline) with a single pooled
/// <see cref="DefaultHttpContext"/> — the same reuse Kestrel performs on a kept-alive connection
/// (<c>DefaultHttpContext.Initialize</c>/<c>Uninitialize</c>). No sockets, no HttpClient, no TestServer.
/// </summary>
/// <remarks>
/// Per request the runner does what the server/hosting layer does and nothing more:
/// rewind the request body, clear response state and per-request features, re-initialize the context with the app's
/// <see cref="IServiceScopeFactory"/> (so <c>RequestServices</c> creates a real scope lazily), invoke the delegate,
/// start the response if nothing was written, and run OnCompleted callbacks (which dispose the request scope).
/// All of that is allocation free, which <see cref="RunHarnessOnlyAsync"/> demonstrates.
/// </remarks>
public sealed class InMemoryRequestRunner
{
    private readonly InMemoryServerFeatures _features = new();
    private readonly DefaultHttpContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RequestDelegate _app;
    private readonly Endpoint? _endpoint;
    private readonly RouteValueDictionary? _routeValues;

    /// <param name="services">The application's root service provider.</param>
    /// <param name="app">Endpoint request delegate or full pipeline.</param>
    /// <param name="endpoint">When invoking an endpoint directly: the endpoint routing would have selected.</param>
    /// <param name="routeValues">When invoking an endpoint directly: the route values routing would have produced.</param>
    public InMemoryRequestRunner(IServiceProvider services, RequestDelegate app, Endpoint? endpoint, RouteValueDictionary? routeValues)
    {
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _app = app;
        _endpoint = endpoint;
        _routeValues = routeValues;
        _context = new DefaultHttpContext(_features);
        _context.Uninitialize();
    }

    public InMemoryServerFeatures Features => _features;

    public void SetRequest(BenchRequest request)
        => _features.SetRequest(request.Method, request.Path, request.Query, request.CreateHeaders(), request.Body);

    /// <summary>One request. Returns a completed task (no allocation) when the endpoint completes synchronously.</summary>
    public Task RunAsync()
    {
        var context = BeginRequest();

        Task task;
        try
        {
            task = _app(context);
        }
        catch (Exception ex)
        {
            task = Task.FromException(ex);
        }

        return task.IsCompletedSuccessfully ? EndRequest() : AwaitAndEnd(task);
    }

    /// <summary>Only the per-request reset/initialize/complete work: the harness overhead (expected ≈ 0 B).</summary>
    public Task RunHarnessOnlyAsync()
    {
        BeginRequest();
        return EndRequest();
    }

    private DefaultHttpContext BeginRequest()
    {
        _features.ResetForRequest(_endpoint, _routeValues);
        _context.Initialize(_features);
        _context.ServiceScopeFactory = _scopeFactory;
        return _context;
    }

    private Task EndRequest()
    {
        _features.EnsureStarted();
        var completed = _features.FireOnCompletedAsync();
        if (!completed.IsCompletedSuccessfully)
            return AwaitCompletedAndUninitialize(completed);

        _context.Uninitialize();
        return Task.CompletedTask;
    }

    private async Task AwaitAndEnd(Task task)
    {
        await task.ConfigureAwait(false);
        await EndRequest().ConfigureAwait(false);
    }

    private async Task AwaitCompletedAndUninitialize(Task completed)
    {
        await completed.ConfigureAwait(false);
        _context.Uninitialize();
    }

    /// <summary>Runs one request and captures status, content type and body (for the equivalence check).</summary>
    public async Task<CapturedResponse> CaptureAsync()
    {
        var context = BeginRequest();
        await _app(context).ConfigureAwait(false);
        _features.EnsureStarted();
        var captured = new CapturedResponse(
            _features.StatusCode,
            _features.ResponseContentType,
            Encoding.UTF8.GetString(_features.ResponseBody));
        await _features.FireOnCompletedAsync().ConfigureAwait(false);
        _context.Uninitialize();
        return captured;
    }
}

public sealed record CapturedResponse(int StatusCode, string? ContentType, string Body)
{
    public override string ToString()
        => $"{StatusCode} [{ContentType ?? "-"}] {(Body.Length > 80 ? Body[..80] + "…" : Body)}";
}
