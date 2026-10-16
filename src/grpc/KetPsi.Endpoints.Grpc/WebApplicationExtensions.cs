using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Placeholder MapGrpc extensions detected by the source generator (name prefix "MapGrpc").
/// <list type="bullet">
/// <item><description><see cref="WebApplication"/> — top-level <c>app.MapGrpc&lt;T&gt;(...)</c></description></item>
/// <item><description><see cref="IEndpointConventionBuilder"/> — after <c>MapGroup</c> / <c>WithGroupName</c>
/// (return type of those APIs is <see cref="IEndpointConventionBuilder"/>)</description></item>
/// </list>
/// Use <c>WithGroupName("v1")</c> so the generator appends the version to the proto package name.
/// </summary>
public static class WebApplicationExtensions
{
    // ---- WebApplication (top-level) ----

    public static IEndpointConventionBuilder MapGrpc<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpcCore(route, configure);

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

    public static IEndpointConventionBuilder MapGrpc<TEndpoint, TConfig>(
        this WebApplication app,
        string route)
        where TEndpoint : class, IEndpoint
        where TConfig : IGrpcEndpointConfiguration<TEndpoint>, new()
        => GrpcEndpointConventionRegistry.Register(route);

    // ---- IEndpointConventionBuilder (MapGroup / WithGroupName chain) ----
    // WithGroupName returns IEndpointConventionBuilder, so MapGrpc must be defined here
    // or the chain app.MapGroup("api").WithGroupName("v1").MapGrpc<T>(...) does not compile.

    public static IEndpointConventionBuilder MapGrpc<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpcCore(route, configure);

    public static IEndpointConventionBuilder MapGrpcUnary<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    public static IEndpointConventionBuilder MapGrpcClientStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    public static IEndpointConventionBuilder MapGrpcServerStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

    public static IEndpointConventionBuilder MapGrpcDuplexStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => MapGrpc(endpoints, route, configure);

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
