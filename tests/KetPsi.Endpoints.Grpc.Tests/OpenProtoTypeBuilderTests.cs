using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Abstractions;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class OpenProtoTypeBuilderTests
{
    private sealed class Wrapper<T>
    {
        public T? Value { get; set; }
    }

    private sealed class OpenConfig : IOpenProtoTypeConfiguration
    {
        public void Configure(IOpenProtoTypeBuilder builder)
        {
            builder.ConfigureGeneric(typeof(Wrapper<>), meta => meta.Property("Value", 1));
        }
    }

    [Fact]
    public void ConfigureGeneric_DoesNotThrow()
    {
        var builder = new OpenProtoTypeBuilder();
        Assert.Null(Record.Exception(() => new OpenConfig().Configure(builder)));
    }

    [Fact]
    public void ApplyClosedGeneric_IgnoresOpenOrNonGeneric()
    {
        var builder = new OpenProtoTypeBuilder();
        Assert.Null(Record.Exception(() =>
        {
            builder.ApplyClosedGeneric(typeof(Wrapper<>));
            builder.ApplyClosedGeneric(typeof(string));
            builder.ApplyClosedGeneric(null!);
            builder.ApplyClosedGeneric(typeof(Wrapper<int>));
        }));
    }
}
