using KetPsi.Endpoints.Grpc;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Host-facing registration. Forwards to every assembly that registered itself via
    /// <see cref="GrpcGeneratedRegistrationHub"/> (only assemblies that call <c>MapGrpc</c>).
    /// </summary>
    public static class KetPsiGeneratedGrpcServiceCollectionExtensions
    {
        /// <summary>Registers handlers, proxies, and gRPC service types from all MapGrpc assemblies.</summary>
        public static IServiceCollection AddGrpcServices(this IServiceCollection services)
            => GrpcGeneratedRegistrationHub.ApplyServices(services);
    }
}

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Host-facing endpoint mapping. Safe to call once from the host; aggregates all MapGrpc assemblies.
    /// </summary>
    public static class KetPsiGeneratedGrpcApplicationBuilderExtensions
    {
        /// <summary>Maps all generated gRPC services discovered across referenced assemblies.</summary>
        public static WebApplication MapGrpcEndpoints(this WebApplication app)
            => GrpcGeneratedRegistrationHub.ApplyMapEndpoints(app);

        /// <summary>
        /// Maps <c>GET /proto</c> for compile-time schemas from every assembly that registered a schema action.
        /// Prefer a single host assembly owning MapGrpc when possible.
        /// </summary>
        public static WebApplication UseProtoEndpoint(this WebApplication app)
            => GrpcGeneratedRegistrationHub.ApplyProtoEndpoint(app);
    }
}
