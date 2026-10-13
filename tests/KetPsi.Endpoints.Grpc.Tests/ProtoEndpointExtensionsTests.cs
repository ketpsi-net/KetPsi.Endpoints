using KetPsi.Endpoints.Grpc;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class ProtoEndpointExtensionsTests
{
    [Fact]
    public void AddProtoEndpoint_ReturnsSameCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddProtoEndpoint();
        Assert.Same(services, result);
    }

    [Fact]
    public void UseProtoEndpoint_WithoutGenerator_IsNoOpAndReturnsApp()
    {
        // When no MapGrpc* calls exist, the hand-written fallback is used.
        // We only verify it does not throw and returns the same instance.
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        var result = app.UseProtoEndpoint();
        Assert.Same(app, result);
    }
}
