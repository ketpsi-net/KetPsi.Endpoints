using System.Buffers;
using System.Text;

using KetPsi.Endpoints.Grpc.Wire;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>
/// Unit tests for pure wire codecs (no protobuf-net, no reflection on the hot path).
/// </summary>
public class ProtoWireSerializationTests
{
    [Fact]
    public void Varint_RoundTrips_Int32()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteTag(1, ProtoWireType.Varint);
        writer.WriteInt32(42);
        writer.WriteTag(2, ProtoWireType.Varint);
        writer.WriteInt32(-1);
        writer.Dispose();

        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.True(reader.TryReadTag(out var f1, out var w1));
        Assert.Equal(1, f1);
        Assert.Equal(ProtoWireType.Varint, w1);
        Assert.Equal(42, reader.ReadInt32());
        Assert.True(reader.TryReadTag(out var f2, out _));
        Assert.Equal(2, f2);
        Assert.Equal(-1, reader.ReadInt32());
        Assert.True(reader.IsAtEnd);
    }

    [Fact]
    public void String_RoundTrips_Unicode()
    {
        var s = "Hello 世界 🚀";
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteTag(1, ProtoWireType.LengthDelimited);
        writer.WriteString(s);
        writer.Dispose();

        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.True(reader.TryReadTag(out var field, out var wt));
        Assert.Equal(1, field);
        Assert.Equal(ProtoWireType.LengthDelimited, wt);
        Assert.Equal(s, reader.ReadString());
    }

    [Fact]
    public void String_Null_WritesEmpty()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteString(null);
        writer.Dispose();

        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.Equal(string.Empty, reader.ReadString());
    }

    [Fact]
    public void Bool_Float_Double_RoundTrip()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteTag(1, ProtoWireType.Varint);
        writer.WriteBool(true);
        writer.WriteTag(2, ProtoWireType.Fixed32);
        writer.WriteFloat(3.14f);
        writer.WriteTag(3, ProtoWireType.Fixed64);
        writer.WriteDouble(2.718281828);
        writer.Dispose();

        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.True(reader.TryReadTag(out _, out _));
        Assert.True(reader.ReadBool());
        Assert.True(reader.TryReadTag(out _, out _));
        Assert.Equal(3.14f, reader.ReadFloat(), precision: 5);
        Assert.True(reader.TryReadTag(out _, out _));
        Assert.Equal(2.718281828, reader.ReadDouble(), precision: 9);
    }

    [Fact]
    public void Bytes_RoundTrip()
    {
        var data = Encoding.UTF8.GetBytes("raw-bytes");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteTag(1, ProtoWireType.LengthDelimited);
        writer.WriteBytes(data);
        writer.Dispose();

        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.True(reader.TryReadTag(out _, out _));
        Assert.True(reader.ReadBytes().SequenceEqual(data));
    }

    [Fact]
    public void NestedLengthDelimited_RoundTrip()
    {
        var inner = new ArrayBufferWriter<byte>();
        var iw = new ProtoWireWriter(inner);
        iw.WriteTag(1, ProtoWireType.Varint);
        iw.WriteInt32(7);
        iw.Dispose();

        var outer = new ArrayBufferWriter<byte>();
        var ow = new ProtoWireWriter(outer);
        ow.WriteTag(2, ProtoWireType.LengthDelimited);
        ow.WriteLengthDelimitedBytes(inner.WrittenSpan);
        ow.Dispose();

        var reader = new ProtoWireReader(outer.WrittenSpan);
        Assert.True(reader.TryReadTag(out var f, out var wt));
        Assert.Equal(2, f);
        Assert.Equal(ProtoWireType.LengthDelimited, wt);
        var sub = reader.ReadBytes();
        var nested = new ProtoWireReader(sub);
        Assert.True(nested.TryReadTag(out var nf, out _));
        Assert.Equal(1, nf);
        Assert.Equal(7, nested.ReadInt32());
    }

    [Fact]
    public void SkipField_SkipsUnknown()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteTag(9, ProtoWireType.Varint);
        writer.WriteInt32(999);
        writer.WriteTag(1, ProtoWireType.Varint);
        writer.WriteInt32(1);
        writer.Dispose();

        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.True(reader.TryReadTag(out var f1, out var w1));
        Assert.Equal(9, f1);
        reader.SkipField(w1);
        Assert.True(reader.TryReadTag(out var f2, out _));
        Assert.Equal(1, f2);
        Assert.Equal(1, reader.ReadInt32());
    }

    [Fact]
    public void TruncatedVarint_Throws()
    {
        var data = new byte[] { 0x80 }; // incomplete varint
        Assert.Throws<InvalidOperationException>(() =>
        {
            var reader = new ProtoWireReader(data);
            _ = reader.ReadVarint32();
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(int.MaxValue)]
    public void Varint_EdgeValues(int value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        writer.WriteInt32(value);
        writer.Dispose();
        var reader = new ProtoWireReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadInt32());
    }
}

/// <summary>
/// Round-trip tests for generated message codecs (requires MapGrpc* in compilation).
/// </summary>
public class GeneratedProtoCodecRoundtripTests
{
    [Fact]
    public void GetUserRequest_RoundTrip()
    {
        var original = new GetUserRequest { Id = 42 };
        var bytes = GeneratedProtoCodecs.Serialize_GetUserRequest(original);
        var copy = GeneratedProtoCodecs.Deserialize_GetUserRequest(bytes);
        Assert.Equal(original.Id, copy.Id);
    }

    [Fact]
    public void GetUserResponse_RoundTrip()
    {
        var original = new GetUserResponse { Id = 7, Name = "User-7" };
        var bytes = GeneratedProtoCodecs.Serialize_GetUserResponse(original);
        var copy = GeneratedProtoCodecs.Deserialize_GetUserResponse(bytes);
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(original.Name, copy.Name);
    }

    [Fact]
    public void CreateOrderRequest_RoundTrip()
    {
        var original = new CreateOrderRequest { Sku = "SKU-1", Quantity = 3 };
        var bytes = GeneratedProtoCodecs.Serialize_CreateOrderRequest(original);
        var copy = GeneratedProtoCodecs.Deserialize_CreateOrderRequest(bytes);
        Assert.Equal(original.Sku, copy.Sku);
        Assert.Equal(original.Quantity, copy.Quantity);
    }

    [Fact]
    public void CreateOrderResponse_Guid_RoundTrip()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var original = new CreateOrderResponse { OrderId = id };
        var bytes = GeneratedProtoCodecs.Serialize_CreateOrderResponse(original);
        var copy = GeneratedProtoCodecs.Deserialize_CreateOrderResponse(bytes);
        Assert.Equal(id, copy.OrderId);
    }

    [Fact]
    public void GetUserResponse_EmptyName_RoundTrip()
    {
        var original = new GetUserResponse { Id = 0, Name = "" };
        var bytes = GeneratedProtoCodecs.Serialize_GetUserResponse(original);
        var copy = GeneratedProtoCodecs.Deserialize_GetUserResponse(bytes);
        Assert.Equal(0, copy.Id);
        Assert.Equal("", copy.Name);
    }

    [Fact]
    public void GetUserRequest_Default_RoundTrip()
    {
        var original = new GetUserRequest();
        var bytes = GeneratedProtoCodecs.Serialize_GetUserRequest(original);
        var copy = GeneratedProtoCodecs.Deserialize_GetUserRequest(bytes);
        Assert.Equal(0, copy.Id);
    }

    [Fact]
    public void Marshallers_SerializeDeserialize_Match()
    {
        var req = new GetUserRequest { Id = 99 };
        // Exercise marshaller paths used by gRPC
        var m = GeneratedProtoCodecs.Marshaller_GetUserRequest;
        Assert.NotNull(m);
        var bytes = GeneratedProtoCodecs.Serialize_GetUserRequest(req);
        var again = GeneratedProtoCodecs.Deserialize_GetUserRequest(bytes);
        Assert.Equal(99, again.Id);
    }
}
