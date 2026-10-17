using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Placeholder <c>MapGrpc*</c> extension methods detected by the source generator (name prefix <c>MapGrpc</c>).
/// Defined on <see cref="WebApplication"/> and <see cref="IEndpointConventionBuilder"/> so chains with
/// <c>MapGroup</c> / <c>WithGroupName</c> compile. Returns <see cref="IEndpointConventionBuilder"/> for
/// <c>RequireAuthorization()</c> and other endpoint conventions.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>Maps a unary (or default) gRPC endpoint for handler <typeparamref name="T"/>.</summary>
    /// <param name="app">The application builder.</param>
    /// <param name="route">Relative route (<c>Service/Method</c> or longer); combined with <c>MapGroup</c> prefixes.</param>
    /// <param name="configure">Optional interceptors / <c>FromMetadata</c> binding (also discovered from <see cref="IGrpcEndpointConfiguration{T}"/>).</param>
    public static IEndpointConventionBuilder MapGrpc<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpcCore(route, configure);

    /// <summary>Maps a unary gRPC endpoint (alias of <see cref="MapGrpc{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>).</summary>
    public static IEndpointConventionBuilder MapGrpcUnary<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    /// <summary>Placeholder for client-streaming mapping (generator treats like unary until stream shape is fully emitted).</summary>
    public static IEndpointConventionBuilder MapGrpcClientStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    /// <summary>Placeholder for server-streaming mapping.</summary>
    public static IEndpointConventionBuilder MapGrpcServerStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    /// <summary>Placeholder for duplex streaming mapping.</summary>
    public static IEndpointConventionBuilder MapGrpcDuplexStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(app, route, configure);

    /// <summary>Maps an endpoint using an explicit <typeparamref name="TConfig"/> configuration type.</summary>
    public static IEndpointConventionBuilder MapGrpc<TEndpoint, TConfig>(
        this WebApplication app,
        string route)
        where TEndpoint : class, IEndpoint
        where TConfig : IGrpcEndpointConfiguration<TEndpoint>, new()
        => GrpcEndpointConventionRegistry.Register(route);

    /// <summary>Maps a gRPC endpoint on a route group or after <c>WithGroupName</c>.</summary>
    public static IEndpointConventionBuilder MapGrpc<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpcCore(route, configure);

    /// <inheritdoc cref="MapGrpcUnary{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcUnary<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    /// <inheritdoc cref="MapGrpcClientStream{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcClientStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    /// <inheritdoc cref="MapGrpcServerStream{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcServerStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    /// <inheritdoc cref="MapGrpcDuplexStream{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcDuplexStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    /// <summary>Maps an endpoint with explicit configuration type on a route group.</summary>
    public static IEndpointConventionBuilder MapGrpc<TEndpoint, TConfig>(
        this IEndpointConventionBuilder endpoints,
        string route)
        where TEndpoint : class, IEndpoint
        where TConfig : IGrpcEndpointConfiguration<TEndpoint>, new()
        => GrpcEndpointConventionRegistry.Register(route);

    private static IEndpointConventionBuilder MapGrpcCore<T>(
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure)
        where T : class, IEndpoint
    {
        if (configure is not null)
        {
            var builder = new GrpcEndpointBuilder<T>();
            configure(builder);
        }

        return GrpcEndpointConventionRegistry.Register(route);
    }
}
