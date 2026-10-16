using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Placeholder extension methods detected by the source generator via name prefix "MapGrpc".
/// Returns <see cref="IEndpointConventionBuilder"/> so callers can chain
/// <c>RequireAuthorization()</c>; conventions apply when <c>MapGrpcEndpoints</c> maps the service.
/// </summary>
public static class WebApplicationExtensions
{
    public static IEndpointConventionBuilder MapGrpc<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
    {
        // configure is discovered at compile time from IGrpcEndpointConfiguration / MapGrpc call sites
        // Runtime configure still applies interceptor types via endpoint builder for DI registration side effects only
        if (configure is not null)
        {
            var builder = new GrpcEndpointBuilder<T>();
            configure(builder);
            // Interceptors from runtime configure are recorded on the builder but pipeline is compile-time;
            // prefer IGrpcEndpointConfiguration for interceptors so the generator can isolate per method.
        }

        return GrpcEndpointConventionRegistry.Register(route);
    }

    public static IEndpointConventionBuilder MapGrpcUnary<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    public static IEndpointConventionBuilder MapGrpcClientStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    public static IEndpointConventionBuilder MapGrpcServerStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    public static IEndpointConventionBuilder MapGrpcDuplexStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    public static IEndpointConventionBuilder MapGrpc<TEndpoint, TConfig>(this WebApplication app, string route)
        where TEndpoint : class, IEndpoint
        where TConfig : IGrpcEndpointConfiguration<TEndpoint>, new()
        => GrpcEndpointConventionRegistry.Register(route);
}
