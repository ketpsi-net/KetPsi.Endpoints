using KetPsi.Endpoints.Grpc;

using Microsoft.AspNetCore.Builder;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Host-facing registration. Applies every assembly whose generated registration ran
    /// (the host's generated code explicitly ensures referenced KetPsi gRPC assemblies are registered).
    /// </summary>
    public static class KetPsiGeneratedGrpcServiceCollectionExtensions
    {
        /// <summary>
        /// Registers ASP.NET Core gRPC, handlers, generated services, method providers and per-endpoint
        /// interceptors. Idempotent per <see cref="IServiceCollection"/>.
        /// </summary>
        public static IServiceCollection AddGrpcServices(this IServiceCollection services)
            => GrpcGeneratedRegistrationHub.ApplyServices(services);
    }
}

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>Host-facing endpoint mapping (idempotent per application).</summary>
    public static class KetPsiGeneratedGrpcApplicationBuilderExtensions
    {
        /// <summary>Maps all generated gRPC services (with their call-site conventions) once per application.</summary>
        public static WebApplication MapGrpcEndpoints(this WebApplication app)
            => GrpcGeneratedRegistrationHub.ApplyMapEndpoints(app);

        /// <summary>
        /// Maps <c>GET /proto</c> (compile-time proto3 schema) and <c>GET /proto/{file}</c>. Idempotent per application.
        /// </summary>
        public static WebApplication UseProtoEndpoint(this WebApplication app)
            => GrpcGeneratedRegistrationHub.ApplyProtoEndpoint(app);
    }
}
