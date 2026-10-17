using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>Minimal protobuf binary writer (no protobuf-net). Ref struct — do not capture in Func/Action.</summary>
public ref struct ProtoWireWriter
{
    private IBufferWriter<byte> _output;

    /// <summary>Creates a writer that advances <paramref name="output"/> as tags and payloads are written.</summary>
    public ProtoWireWriter(IBufferWriter<byte> output)
    {
        _output = output;
    }

    /// <summary>No-op dispose (ref struct lifetime is the caller's responsibility).</summary>
    public void Dispose() { }

    /// <summary>Writes a protobuf field tag (field number + wire type).</summary>
    public void WriteTag(int fieldNumber, ProtoWireType wireType)
        => WriteVarint((uint)((fieldNumber << 3) | (int)wireType));

    /// <summary>Writes an unsigned varint (base-128).</summary>
    public void WriteVarint(ulong value)
    {
        var span = _output.GetSpan(10);
        var i = 0;
        while (value >= 0x80)
        {
            span[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        span[i++] = (byte)value;
        _output.Advance(i);
    }

    /// <summary>Writes an unsigned 32-bit value as a varint.</summary>
    public void WriteVarint(uint value) => WriteVarint((ulong)value);

    /// <summary>
    /// Protobuf encodes negative int32 as a 64-bit sign-extended varint (up to 10 bytes).
    /// </summary>
    public void WriteInt32(int value) => WriteVarint(unchecked((ulong)(long)value));

    /// <summary>Writes a signed int64 as a varint.</summary>
    public void WriteInt64(long value) => WriteVarint(unchecked((ulong)value));
    /// <summary>Writes a uint32 as a varint.</summary>
    public void WriteUInt32(uint value) => WriteVarint(value);
    /// <summary>Writes a uint64 as a varint.</summary>
    public void WriteUInt64(ulong value) => WriteVarint(value);
    /// <summary>Writes a bool as a varint 0/1.</summary>
    public void WriteBool(bool value) => WriteVarint(value ? 1u : 0u);

    /// <summary>Writes a fixed32 (little-endian 4 bytes).</summary>
    public void WriteFixed32(uint value)
    {
        var span = _output.GetSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _output.Advance(4);
    }

    /// <summary>Writes a fixed64 (little-endian 8 bytes).</summary>
    public void WriteFixed64(ulong value)
    {
        var span = _output.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _output.Advance(8);
    }

    /// <summary>Writes a float as fixed32.</summary>
    public void WriteFloat(float value) => WriteFixed32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    /// <summary>Writes a double as fixed64.</summary>
    public void WriteDouble(double value) => WriteFixed64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    /// <summary>
    /// Length-delimited payload: varint length prefix + raw bytes.
    /// Used after <c>WriteTag(n, LengthDelimited)</c> for nested messages / bytes fields.
    /// </summary>
    public void WriteLengthDelimitedBytes(ReadOnlySpan<byte> data)
        => WriteBytes(data);

    /// <summary>Writes length-delimited raw bytes (length varint + payload).</summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        WriteVarint((uint)data.Length);
        var span = _output.GetSpan(data.Length);
        data.CopyTo(span);
        _output.Advance(data.Length);
    }

    /// <summary>Writes a length-delimited UTF-8 string (null is written as empty).</summary>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteVarint(0u);
            return;
        }
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarint((uint)byteCount);
        var span = _output.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(value, span);
        _output.Advance(byteCount);
    }
}

/// <summary>Protobuf wire types used by <see cref="ProtoWireWriter"/> and <see cref="ProtoWireReader"/>.</summary>
public enum ProtoWireType
{
    /// <summary>Base-128 varint (int32, int64, bool, enum).</summary>
    Varint = 0,
    /// <summary>Fixed 64-bit little-endian (fixed64, double).</summary>
    Fixed64 = 1,
    /// <summary>Length-prefixed payload (string, bytes, embedded messages).</summary>
    LengthDelimited = 2,
    /// <summary>Fixed 32-bit little-endian (fixed32, float).</summary>
    Fixed32 = 5
}
