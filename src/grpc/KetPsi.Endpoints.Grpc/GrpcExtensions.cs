using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Host infrastructure helpers for enabling ASP.NET Core gRPC.
/// </summary>
public static class GrpcInfrastructureExtensions
{
    /// <summary>
    /// Registers ASP.NET Core gRPC services (code-first codecs are provided by the generator).
    /// </summary>
    /// <param name="hostApplicationBuilder">The web application builder.</param>
    /// <returns>The same <paramref name="hostApplicationBuilder"/> for chaining.</returns>
    public static WebApplicationBuilder AddGrpc(this WebApplicationBuilder hostApplicationBuilder)
    {
        hostApplicationBuilder.Services.AddGrpc();
        return hostApplicationBuilder;
    }
}
