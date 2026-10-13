using System.Buffers;
using System.Text;

using KetPsi.Endpoints.Grpc.Wire;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Codec.Tests;

/// <summary>
/// Low-level protobuf wire format: tags, varints, fixed, length-delimited, skip, errors.
/// </summary>
public class ProtoWireReaderWriterTests
{
    private static byte[] Write(Action<ProtoWireWriter> action)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        var writer = new ProtoWireWriter(buffer);
        action(writer);
        writer.Dispose();
        return buffer.WrittenSpan.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Int32_RoundTrip(int value)
    {
        var bytes = Write(w =>
        {
            w.WriteTag(1, ProtoWireType.Varint);
            w.WriteInt32(value);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f, out var t));
        Assert.Equal(1, f);
        Assert.Equal(ProtoWireType.Varint, t);
        Assert.Equal(value, r.ReadInt32());
        Assert.True(r.IsAtEnd);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void UInt64_RoundTrip(ulong value)
    {
        var bytes = Write(w =>
        {
            w.WriteTag(2, ProtoWireType.Varint);
            w.WriteUInt64(value);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out _, out _));
        Assert.Equal(value, r.ReadUInt64());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bool_RoundTrip(bool value)
    {
        var bytes = Write(w =>
        {
            w.WriteTag(1, ProtoWireType.Varint);
            w.WriteBool(value);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out _, out _));
        Assert.Equal(value, r.ReadBool());
    }

    [Fact]
    public void Float_Double_RoundTrip()
    {
        var bytes = Write(w =>
        {
            w.WriteTag(1, ProtoWireType.Fixed32);
            w.WriteFloat(3.14159f);
            w.WriteTag(2, ProtoWireType.Fixed64);
            w.WriteDouble(2.718281828459045);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f1, out var t1));
        Assert.Equal(1, f1);
        Assert.Equal(ProtoWireType.Fixed32, t1);
        Assert.Equal(3.14159f, r.ReadFloat(), precision: 5);
        Assert.True(r.TryReadTag(out var f2, out var t2));
        Assert.Equal(2, f2);
        Assert.Equal(ProtoWireType.Fixed64, t2);
        Assert.Equal(2.718281828459045, r.ReadDouble(), precision: 12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Hello 世界 🚀")]
    [InlineData("line1\nline2\t")]
    public void String_RoundTrip(string value)
    {
        var bytes = Write(w =>
        {
            w.WriteTag(1, ProtoWireType.LengthDelimited);
            w.WriteString(value);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out _, out var t));
        Assert.Equal(ProtoWireType.LengthDelimited, t);
        Assert.Equal(value, r.ReadString());
    }

    [Fact]
    public void String_Null_WritesEmpty()
    {
        var bytes = Write(w => w.WriteString(null));
        var r = new ProtoWireReader(bytes);
        Assert.Equal(string.Empty, r.ReadString());
    }

    [Fact]
    public void Bytes_RoundTrip()
    {
        var payload = Encoding.UTF8.GetBytes("binary\0data");
        var bytes = Write(w =>
        {
            w.WriteTag(3, ProtoWireType.LengthDelimited);
            w.WriteBytes(payload);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f, out _));
        Assert.Equal(3, f);
        Assert.True(r.ReadBytes().SequenceEqual(payload));
    }

    [Fact]
    public void NestedLengthDelimited_RoundTrip()
    {
        var inner = Write(w =>
        {
            w.WriteTag(1, ProtoWireType.Varint);
            w.WriteInt32(42);
        });
        var outer = Write(w =>
        {
            w.WriteTag(5, ProtoWireType.LengthDelimited);
            w.WriteLengthDelimitedBytes(inner);
        });
        var r = new ProtoWireReader(outer);
        Assert.True(r.TryReadTag(out var f, out var t));
        Assert.Equal(5, f);
        Assert.Equal(ProtoWireType.LengthDelimited, t);
        var sub = new ProtoWireReader(r.ReadBytes());
        Assert.True(sub.TryReadTag(out var sf, out _));
        Assert.Equal(1, sf);
        Assert.Equal(42, sub.ReadInt32());
    }

    [Fact]
    public void MultipleFields_PreserveOrder()
    {
        var bytes = Write(w =>
        {
            w.WriteTag(1, ProtoWireType.Varint);
            w.WriteInt32(10);
            w.WriteTag(2, ProtoWireType.LengthDelimited);
            w.WriteString("two");
            w.WriteTag(3, ProtoWireType.Varint);
            w.WriteBool(true);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f1, out _));
        Assert.Equal(1, f1);
        Assert.Equal(10, r.ReadInt32());
        Assert.True(r.TryReadTag(out var f2, out _));
        Assert.Equal(2, f2);
        Assert.Equal("two", r.ReadString());
        Assert.True(r.TryReadTag(out var f3, out _));
        Assert.Equal(3, f3);
        Assert.True(r.ReadBool());
        Assert.True(r.IsAtEnd);
    }

    [Fact]
    public void SkipField_UnknownVarint()
    {
        var bytes = Write(w =>
        {
            w.WriteTag(99, ProtoWireType.Varint);
            w.WriteInt32(12345);
            w.WriteTag(1, ProtoWireType.Varint);
            w.WriteInt32(7);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f, out var t));
        Assert.Equal(99, f);
        r.SkipField(t);
        Assert.True(r.TryReadTag(out var f2, out _));
        Assert.Equal(1, f2);
        Assert.Equal(7, r.ReadInt32());
    }

    [Fact]
    public void SkipField_UnknownLengthDelimited()
    {
        var bytes = Write(w =>
        {
            w.WriteTag(50, ProtoWireType.LengthDelimited);
            w.WriteString("skip-me");
            w.WriteTag(1, ProtoWireType.Varint);
            w.WriteInt32(1);
        });
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out _, out var t));
        r.SkipField(t);
        Assert.True(r.TryReadTag(out var f, out _));
        Assert.Equal(1, f);
        Assert.Equal(1, r.ReadInt32());
    }

    [Fact]
    public void TruncatedVarint_Throws()
    {
        var r = new ProtoWireReader(new byte[] { 0x80 });
        try
        {
            r.ReadVarint32();
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException)
        {
            Assert.True(true);
        }
        catch (Exception)
        {
            Assert.Fail("Expected InvalidOperationException, but a different exception was thrown.");
        }
    }

    [Fact]
    public void TruncatedFixed32_Throws()
    {
        var r = new ProtoWireReader(new byte[] { 0x01, 0x02 });

        try
        {
            r.ReadFixed32();
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException)
        {
            Assert.True(true);
        }
        catch (Exception)
        {
            Assert.Fail("Expected InvalidOperationException, but a different exception was thrown.");
        }
    }

    [Fact]
    public void EmptyBuffer_TryReadTag_False()
    {
        var r = new ProtoWireReader(ReadOnlySpan<byte>.Empty);
        Assert.False(r.TryReadTag(out _, out _));
        Assert.True(r.IsAtEnd);
    }

    [Fact]
    public void Tag_EncodesFieldNumberAndWireType()
    {
        var bytes = Write(w => w.WriteTag(15, ProtoWireType.LengthDelimited));
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f, out var t));
        Assert.Equal(15, f);
        Assert.Equal(ProtoWireType.LengthDelimited, t);
    }
}
