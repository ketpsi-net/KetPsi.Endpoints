using System.Diagnostics.CodeAnalysis;

using Grpc.AspNetCore.Server;
using Grpc.Core.Interceptors;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Registers global gRPC interceptors for all mapped services.
/// Per-endpoint interceptors are configured with <c>AddInterceptor</c> on
/// <see cref="IGrpcEndpointConfiguration{TEndpoint}"/> or the <c>MapGrpc</c> configure lambda instead.
/// </summary>
public static class GrpcInterceptorExtensions
{
    internal const DynamicallyAccessedMemberTypes InterceptorMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods;

    /// <summary>Adds a global interceptor type to <see cref="GrpcServiceOptions"/>.</summary>
    public static IServiceCollection AddGrpcInterceptor<[DynamicallyAccessedMembers(InterceptorMembers)] TInterceptor>(this IServiceCollection services)
        where TInterceptor : Interceptor
    {
        services.Configure<GrpcServiceOptions>(options => options.Interceptors.Add<TInterceptor>());
        return services;
    }

    /// <summary>Convenience on <see cref="WebApplicationBuilder"/>.</summary>
    public static WebApplicationBuilder AddGrpcInterceptor<[DynamicallyAccessedMembers(InterceptorMembers)] TInterceptor>(this WebApplicationBuilder builder)
        where TInterceptor : Interceptor
    {
        builder.Services.AddGrpcInterceptor<TInterceptor>();
        return builder;
    }
}
