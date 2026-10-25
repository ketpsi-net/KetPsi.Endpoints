using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Inert <c>MapGrpc*</c> markers read by the source generator at compile time. They map nothing,
/// never invoke <c>configure</c>, keep no state and do not allocate; the generator emits the real
/// mapping into <c>MapGrpcEndpoints()</c> and re-applies conventions chained directly on the call
/// (e.g. <c>.RequireAuthorization()</c>) to that RPC only.
/// The route and <c>configure</c> lambda must be compile-time readable (string constant / inline lambda).
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>Maps a gRPC endpoint for handler <typeparamref name="T"/>; the method kind is inferred from the handler signature.</summary>
    /// <param name="app">The application builder.</param>
    /// <param name="route">Relative route (<c>Service/Method</c> or longer); combined with <c>MapGroup</c> prefixes.</param>
    /// <param name="configure">Optional inline lambda: interceptors / <c>FromMetadata</c> binding (read at compile time, never executed).</param>
    public static IEndpointConventionBuilder MapGrpc<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps a unary gRPC endpoint (handler must be <c>Task&lt;TRes&gt; ExecuteAsync(TReq, CancellationToken)</c>).</summary>
    public static IEndpointConventionBuilder MapGrpcUnary<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps a client-streaming endpoint (handler takes <c>IAsyncEnumerable&lt;TReq&gt;</c>, returns <c>Task&lt;TRes&gt;</c>).</summary>
    public static IEndpointConventionBuilder MapGrpcClientStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps a server-streaming endpoint (handler takes <c>TReq</c>, returns <c>IAsyncEnumerable&lt;TRes&gt;</c>).</summary>
    public static IEndpointConventionBuilder MapGrpcServerStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps a duplex-streaming endpoint (handler takes <c>IAsyncEnumerable&lt;TReq&gt;</c>, returns <c>IAsyncEnumerable&lt;TRes&gt;</c>).</summary>
    public static IEndpointConventionBuilder MapGrpcDuplexStream<T>(
        this WebApplication app,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps an endpoint using an explicit <typeparamref name="TConfig"/> configuration type.</summary>
    public static IEndpointConventionBuilder MapGrpc<TEndpoint, TConfig>(
        this WebApplication app,
        string route)
        where TEndpoint : class, IEndpoint
        where TConfig : IGrpcEndpointConfiguration<TEndpoint>, new()
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps a gRPC endpoint on a route group or after <c>WithGroupName</c>.</summary>
    public static IEndpointConventionBuilder MapGrpc<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <inheritdoc cref="MapGrpcUnary{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcUnary<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <inheritdoc cref="MapGrpcClientStream{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcClientStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <inheritdoc cref="MapGrpcServerStream{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcServerStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <inheritdoc cref="MapGrpcDuplexStream{T}(WebApplication, string, Action{IGrpcEndpointBuilder{T}}?)"/>
    public static IEndpointConventionBuilder MapGrpcDuplexStream<T>(
        this IEndpointConventionBuilder endpoints,
        string route,
        Action<IGrpcEndpointBuilder<T>>? configure = null)
        where T : class, IEndpoint
        => NoopEndpointConventionBuilder.Instance;

    /// <summary>Maps an endpoint with explicit configuration type on a route group.</summary>
    public static IEndpointConventionBuilder MapGrpc<TEndpoint, TConfig>(
        this IEndpointConventionBuilder endpoints,
        string route)
        where TEndpoint : class, IEndpoint
        where TConfig : IGrpcEndpointConfiguration<TEndpoint>, new()
        => NoopEndpointConventionBuilder.Instance;
}
