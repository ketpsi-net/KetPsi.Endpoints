using Grpc.AspNetCore.Server;
using Grpc.Core.Interceptors;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Registers global gRPC interceptors for all mapped services.
/// </summary>
public static class GrpcInterceptorExtensions
{
    /// <summary>
    /// Adds a global interceptor type to <see cref="GrpcServiceOptions"/>.
    /// Call after <c>AddGrpc()</c>.
    /// </summary>
    public static IServiceCollection AddGrpcInterceptor<TInterceptor>(this IServiceCollection services)
        where TInterceptor : Interceptor
    {
        services.Configure<GrpcServiceOptions>(options =>
        {
            options.Interceptors.Add<TInterceptor>();
        });
        return services;
    }

    /// <summary>
    /// Convenience on <see cref="WebApplicationBuilder"/>.
    /// </summary>
    public static WebApplicationBuilder AddGrpcInterceptor<TInterceptor>(this WebApplicationBuilder builder)
        where TInterceptor : Interceptor
    {
        builder.Services.AddGrpcInterceptor<TInterceptor>();
        return builder;
    }
}
