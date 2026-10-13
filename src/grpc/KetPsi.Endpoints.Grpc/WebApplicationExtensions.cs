using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Placeholder extension methods that the source generator detects via name prefix "MapGrpc".
/// The generator emits the real service contracts and proxies; these stubs exist so the
/// call sites compile before generation runs and so the analyzer can validate routes.
/// </summary>
public static class WebApplicationExtensions
{
    public static WebApplication MapGrpc<T>(this WebApplication app, string route) where T : IEndpoint => app;
    public static WebApplication MapGrpcUnary<T>(this WebApplication app, string route) where T : IEndpoint => app;
    public static WebApplication MapGrpcClientStream<T>(this WebApplication app, string route) where T : IEndpoint => app;
    public static WebApplication MapGrpcServerStream<T>(this WebApplication app, string route) where T : IEndpoint => app;
    public static WebApplication MapGrpcDuplexStream<T>(this WebApplication app, string route) where T : IEndpoint => app;
}
