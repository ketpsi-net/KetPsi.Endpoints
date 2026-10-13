using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Codec.Tests.Fixtures;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc.Codec.Tests;

/// <summary>
/// Ensures the source generator sees MapGrpc* call sites so codecs and /proto are emitted.
/// </summary>
public static class HostBootstrap
{
    public static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddGrpc();
        builder.Services.AddGrpcServices();
        builder.Services.AddProtoEndpoint();

        var app = builder.Build();
        app.MapGrpc<AllScalarsHandler>("Codec/Scalars/Echo");
        app.MapGrpc<NullableScalarsHandler>("Codec/Nullable/Echo");
        app.MapGrpc<CollectionsHandler>("Codec/Collections/Echo");
        app.MapGrpc<NestedRootHandler>("Codec/Nested/Echo");
        app.MapGrpcEndpoints();
        app.UseProtoEndpoint();
        return app;
    }
}
