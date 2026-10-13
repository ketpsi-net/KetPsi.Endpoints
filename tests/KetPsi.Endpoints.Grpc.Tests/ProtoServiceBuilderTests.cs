using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class ProtoServiceBuilderTests
{
    private sealed class SampleHandler { }

    [Fact]
    public void Defaults_AreDerivedFromTypeName()
    {
        var builder = new ProtoServiceBuilder<SampleHandler>();

        Assert.Equal("Api", builder.GrpcNamespace);
        Assert.Equal("SampleService", builder.ServiceName); // Handler → Service
        Assert.Equal("Execute", builder.MethodName);
    }

    [Fact]
    public void WithNamespace_OverridesWhenNonEmpty()
    {
        var builder = new ProtoServiceBuilder<SampleHandler>();

        builder.WithNamespace("MyNs");
        Assert.Equal("MyNs", builder.GrpcNamespace);

        builder.WithNamespace("  ");
        Assert.Equal("MyNs", builder.GrpcNamespace); // whitespace ignored
    }

    [Fact]
    public void HasServiceName_OverridesWhenNonEmpty()
    {
        var builder = new ProtoServiceBuilder<SampleHandler>();

        builder.HasServiceName("Orders");
        Assert.Equal("Orders", builder.ServiceName);

        builder.HasServiceName("");
        Assert.Equal("Orders", builder.ServiceName);
    }

    [Fact]
    public void WithName_OverridesWhenNonEmpty()
    {
        var builder = new ProtoServiceBuilder<SampleHandler>();

        builder.WithName("GetById");
        Assert.Equal("GetById", builder.MethodName);

        builder.WithName(null!);
        Assert.Equal("GetById", builder.MethodName);
    }

    [Fact]
    public void FluentChaining_ReturnsSameInstance()
    {
        var builder = new ProtoServiceBuilder<SampleHandler>();
        var result = builder.WithNamespace("X").HasServiceName("Y").WithName("Z");
        Assert.Same(builder, result);
    }
}
