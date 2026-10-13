using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Abstractions;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class ProtoTypeBuilderTests
{
    private sealed class Sample
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class SampleConfig : IProtoTypeConfiguration<Sample>
    {
        public void Configure(IProtoTypeBuilder<Sample> builder)
        {
            builder.Property(x => x.Id, 1).Property(x => x.Name, 2);
        }
    }

    [Fact]
    public void Configure_DoesNotThrow()
    {
        var builder = new ProtoTypeBuilder<Sample>();
        var config = new SampleConfig();
        Assert.Null(Record.Exception(() => config.Configure(builder)));
    }
}
