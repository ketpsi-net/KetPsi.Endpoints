using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Placeholder extension methods that the source generator detects via name prefix "MapGrpc".
/// Handlers must implement <see cref="IEndpoint"/>.
/// </summary>
public static class WebApplicationExtensions
{
    public static WebApplication MapGrpc<T>(this WebApplication app, string route) where T : class, IEndpoint => app;
    public static WebApplication MapGrpcUnary<T>(this WebApplication app, string route) where T : class, IEndpoint => app;
    public static WebApplication MapGrpcClientStream<T>(this WebApplication app, string route) where T : class, IEndpoint => app;
    public static WebApplication MapGrpcServerStream<T>(this WebApplication app, string route) where T : class, IEndpoint => app;
    public static WebApplication MapGrpcDuplexStream<T>(this WebApplication app, string route) where T : class, IEndpoint => app;
}
