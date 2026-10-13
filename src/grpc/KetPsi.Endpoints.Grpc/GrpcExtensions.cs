using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

public static class GrpcInfrastructureExtensions
{
    /// <summary>
    /// Registers ASP.NET Core gRPC services (no protobuf-net).
    /// </summary>
    public static WebApplicationBuilder AddGrpc(this WebApplicationBuilder hostApplicationBuilder)
    {
        hostApplicationBuilder.Services.AddGrpc();
        return hostApplicationBuilder;
    }
}
