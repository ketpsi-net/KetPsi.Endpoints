using KetPsi.Endpoints.Grpc;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class OpenProtoMetaTypeWrapperTests
{
    [Fact]
    public void Stub_Property_IsFluent()
    {
        var builder = new OpenProtoTypeBuilder();
        Assert.Null(Record.Exception(() =>
            builder.ConfigureGeneric(typeof(List<>), m => m.Property("Capacity", 1).UseConstrcutor(false))));
    }
}
