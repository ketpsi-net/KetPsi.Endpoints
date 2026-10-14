using KetPsi.Endpoints.Grpc;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class ProtoEndpointExtensionsTests
{
    [Fact]
    public void AddProtoEndpoint_IsIdempotentNoOp()
    {
        var services = new ServiceCollection();
        var result = services.AddProtoEndpoint();
        Assert.Same(services, result);
        Assert.Same(services, services.AddProtoEndpoint());
    }
}
