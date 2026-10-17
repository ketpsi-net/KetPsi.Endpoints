using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Abstractions;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class FromMetadataPathAndInterceptorTests
{
    private sealed class SampleRequest
    {
        public string Body { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string? TraceId { get; set; }
        public string? FullPath { get; set; }
        public string? MethodName { get; set; }
    }

    private sealed class SampleEndpointConfig : IGrpcEndpointConfiguration<GetUserByIdHandler>
    {
        public void Configure(IGrpcEndpointBuilder<GetUserByIdHandler> builder)
        {
            builder.AddInterceptor<NoOpInterceptor>();
            builder.Request<SampleRequest>(req =>
            {
                req.Property(x => x.TenantId, p => p.FromMetadata("x-tenant-id"));
                req.Property(x => x.TraceId, p => p.FromMetadata());
                
                
                
            });
        }
    }

    [Fact]
    public void ProtoTypeBuilder_NoLongerHasFromMetadata()
    {
        var builder = new ProtoTypeBuilder<SampleRequest>();
        var same = builder.Property(x => x.Body, 1);
        Assert.Same(builder, same);
    }

    [Fact]
    public void EndpointConfig_FromMetadataAndFromPath_IsFluent()
    {
        var builder = new GrpcEndpointBuilder<GetUserByIdHandler>();
        builder.Request<SampleRequest>(req =>
        {
            req.Property(x => x.TenantId, p => p.FromMetadata("x-tenant-id"));
        });
        Assert.Equal(1, builder.Bindings.Count);
        Assert.Contains(builder.Bindings, b => b.Source == GrpcBindingSource.Metadata && b.Key == "x-tenant-id");
        
        
    }

    

    [Fact]
    public void SampleEndpointConfig_ConfiguresWithoutThrowing()
    {
        new SampleEndpointConfig().Configure(new GrpcEndpointBuilder<GetUserByIdHandler>());
    }

    [Fact]
    public void GrpcMetadataBinder_GetString_ReadsHeader()
    {
        var headers = new Metadata { { "x-tenant-id", "acme" } };
        Assert.Equal("acme", GrpcMetadataBinder.GetString(headers, "x-tenant-id"));
        Assert.Null(GrpcMetadataBinder.GetString(headers, "missing"));
    }

    [Fact]
    public void GrpcMetadataBinder_GetInt32_Parses()
    {
        var headers = new Metadata { { "x-count", "42" } };
        Assert.Equal(42, GrpcMetadataBinder.GetInt32(headers, "x-count"));
        Assert.Null(GrpcMetadataBinder.GetInt32(headers, "x-count-missing"));
    }

    [Fact]
    public void GrpcMetadataBinder_GetGuid_Parses()
    {
        var id = Guid.NewGuid();
        var headers = new Metadata { { "x-id", id.ToString() } };
        Assert.Equal(id, GrpcMetadataBinder.GetGuid(headers, "x-id"));
    }

    [Fact]
    public void AddGrpcInterceptor_RegistersWithoutThrow()
    {
        var services = new ServiceCollection();
        services.AddGrpc();
        services.AddGrpcInterceptor<NoOpInterceptor>();
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp);
    }

    [Fact]
    public void GrpcEndpointBuilder_AddInterceptor_CollectsTypes()
    {
        var builder = new GrpcEndpointBuilder();
        builder.AddInterceptor<NoOpInterceptor>();
        builder.AddInterceptor(typeof(NoOpInterceptor));
        Assert.Equal(2, builder.InterceptorTypes.Count);
        Assert.All(builder.InterceptorTypes, t => Assert.True(typeof(Interceptor).IsAssignableFrom(t)));
    }

    [Fact]
    public void GrpcEndpointBuilder_AddInterceptor_RejectsNonInterceptor()
    {
        var builder = new GrpcEndpointBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddInterceptor(typeof(string)));
    }

    public sealed class NoOpInterceptor : Interceptor
    {
    }
}
