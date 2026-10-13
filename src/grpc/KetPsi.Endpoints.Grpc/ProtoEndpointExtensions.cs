using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Optional registration / mapping of the human-readable protobuf schema endpoint (/proto).
/// The source generator emits a concrete <c>UseProtoEndpoint</c> that serves a
/// <strong>compile-time</strong> schema string (no <c>SchemaGenerator</c> / runtime reflection).
/// </summary>
public static class ProtoEndpointExtensions
{
    /// <summary>
    /// Optional DI registration hook for the proto schema endpoint.
    /// Currently a no-op; exists so callers can write a uniform
    /// <c>builder.Services.AddProtoEndpoint()</c> and later add options if needed.
    /// </summary>
    public static IServiceCollection AddProtoEndpoint(this IServiceCollection services)
    {
        return services;
    }

    
}
