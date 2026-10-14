using Grpc.AspNetCore.Server;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Collects interceptors registered via <c>MapGrpc&lt;T&gt;(..., configure)</c> or
/// <c>MapGrpc&lt;TEndpoint, TConfig&gt;</c> so <c>AddGrpcServices</c> can apply them
/// to <see cref="GrpcServiceOptions"/>.
/// </summary>
public static class GrpcEndpointInterceptorRegistry
{
    private static readonly object Gate = new();
    private static readonly HashSet<Type> Interceptors = new();

    public static void Register(Type endpointType, IEnumerable<Type> interceptorTypes)
    {
        if (interceptorTypes is null)
            return;
        lock (Gate)
        {
            foreach (var t in interceptorTypes)
            {
                if (t is not null && typeof(Interceptor).IsAssignableFrom(t))
                    Interceptors.Add(t);
            }
        }
    }

    public static IReadOnlyCollection<Type> Snapshot()
    {
        lock (Gate)
            return Interceptors.ToArray();
    }

    /// <summary>
    /// Applies all registered interceptor types onto <see cref="GrpcServiceOptions"/>.
    /// </summary>
    public static void ApplyTo(IServiceCollection services)
    {
        foreach (var t in Snapshot())
        {
            var type = t;
            services.Configure<GrpcServiceOptions>(options =>
            {
                options.Interceptors.Add(type);
            });
        }
    }
}
