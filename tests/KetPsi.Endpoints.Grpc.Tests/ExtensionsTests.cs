using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Abstractions;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class ExtensionsTests
{
    private sealed class Sample
    {
        public int Id { get; set; }
    }

    private sealed class SampleConfig : IProtoTypeConfiguration<Sample>
    {
        public void Configure(IProtoTypeBuilder<Sample> builder) => builder.Property(x => x.Id, 1);
    }

    [Fact]
    public void ApplyProtoConfiguration_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => new SampleConfig().ApplyProtoConfiguration()));
    }
}
