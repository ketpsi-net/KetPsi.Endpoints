using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>Minimal protobuf binary writer (no protobuf-net). Ref struct — do not capture in Func/Action.</summary>
public ref struct ProtoWireWriter
{
    private IBufferWriter<byte> _output;

    public ProtoWireWriter(IBufferWriter<byte> output)
    {
        _output = output;
    }

    public void Dispose() { }

    public void WriteTag(int fieldNumber, ProtoWireType wireType)
        => WriteVarint((uint)((fieldNumber << 3) | (int)wireType));

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

    public void WriteVarint(uint value) => WriteVarint((ulong)value);

    /// <summary>
    /// Protobuf encodes negative int32 as a 64-bit sign-extended varint (up to 10 bytes).
    /// </summary>
    public void WriteInt32(int value) => WriteVarint(unchecked((ulong)(long)value));

    public void WriteInt64(long value) => WriteVarint(unchecked((ulong)value));
    public void WriteUInt32(uint value) => WriteVarint(value);
    public void WriteUInt64(ulong value) => WriteVarint(value);
    public void WriteBool(bool value) => WriteVarint(value ? 1u : 0u);

    public void WriteFixed32(uint value)
    {
        var span = _output.GetSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _output.Advance(4);
    }

    public void WriteFixed64(ulong value)
    {
        var span = _output.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _output.Advance(8);
    }

    public void WriteFloat(float value) => WriteFixed32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    public void WriteDouble(double value) => WriteFixed64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    /// <summary>
    /// Length-delimited payload: varint length prefix + raw bytes.
    /// Used after <c>WriteTag(n, LengthDelimited)</c> for nested messages / bytes fields.
    /// </summary>
    public void WriteLengthDelimitedBytes(ReadOnlySpan<byte> data)
        => WriteBytes(data);

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        WriteVarint((uint)data.Length);
        var span = _output.GetSpan(data.Length);
        data.CopyTo(span);
        _output.Advance(data.Length);
    }

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

public enum ProtoWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5
}
