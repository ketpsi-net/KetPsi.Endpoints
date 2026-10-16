using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Optional DI registration for the human-readable protobuf schema endpoint (/proto).
/// The source generator emits <c>UseProtoEndpoint</c> that serves a compile-time schema string.
/// </summary>
public static class ProtoEndpointExtensions
{
    /// <summary>
    /// Optional DI registration hook for the proto schema endpoint.
    /// Currently a no-op; exists so callers can write a uniform
    /// <c>builder.Services.AddProtoEndpoint()</c> and later add options if needed.
    /// </summary>
    public static IServiceCollection AddProtoEndpoint(this IServiceCollection services)
        => services;
}
