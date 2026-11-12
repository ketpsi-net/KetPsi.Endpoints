using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Minimal protobuf binary writer over an <see cref="IBufferWriter{T}"/> (no intermediate buffers).
/// Ref struct — pass by <c>ref</c>; every write advances the underlying buffer writer immediately.
/// </summary>
public ref struct ProtoWireWriter
{
    private readonly IBufferWriter<byte> _output;
    private int _depth;

    /// <summary>Creates a writer that advances <paramref name="output"/> as tags and payloads are written.</summary>
    public ProtoWireWriter(IBufferWriter<byte> output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _depth = 0;
    }

    /// <summary>No-op (all writes are committed immediately).</summary>
    public void Dispose() { }

    /// <summary>Current message nesting depth (incremented by <see cref="EnterMessage"/>).</summary>
    public readonly int Depth => _depth;

    /// <summary>
    /// Marks entry into a (nested) message. Throws when the object graph is deeper than
    /// <see cref="ProtoWireLimits.MaxDepth"/> (e.g. a cycle).
    /// </summary>
    public void EnterMessage()
    {
        if (_depth >= ProtoWireLimits.MaxDepth)
            throw ProtoWireLimits.DepthExceeded();
        _depth++;
    }

    /// <summary>Marks exit from a message started with <see cref="EnterMessage"/>.</summary>
    public void ExitMessage() => _depth--;

    /// <summary>Writes a protobuf field tag (field number + wire type).</summary>
    public void WriteTag(int fieldNumber, ProtoWireType wireType)
        => WriteVarint((uint)((fieldNumber << 3) | (int)wireType));

    /// <summary>Writes an unsigned varint (base-128).</summary>
    public void WriteVarint(ulong value)
    {
        if (value < 0x80)
        {
            var one = _output.GetSpan(1);
            one[0] = (byte)value;
            _output.Advance(1);
            return;
        }

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

    /// <summary>Writes a length prefix (varint).</summary>
    public void WriteLength(int length) => WriteVarint((uint)length);

    /// <summary>Protobuf encodes negative int32 as a 64-bit sign-extended varint (10 bytes).</summary>
    public void WriteInt32(int value) => WriteVarint(unchecked((ulong)(long)value));
    /// <summary>Writes a signed int64 as a varint.</summary>
    public void WriteInt64(long value) => WriteVarint(unchecked((ulong)value));
    /// <summary>Writes a uint32 as a varint.</summary>
    public void WriteUInt32(uint value) => WriteVarint(value);
    /// <summary>Writes a uint64 as a varint.</summary>
    public void WriteUInt64(ulong value) => WriteVarint(value);
    /// <summary>Writes a ZigZag-encoded sint32.</summary>
    public void WriteSInt32(int value) => WriteVarint(unchecked((uint)((value << 1) ^ (value >> 31))));
    /// <summary>Writes a ZigZag-encoded sint64.</summary>
    public void WriteSInt64(long value) => WriteVarint(unchecked((ulong)((value << 1) ^ (value >> 63))));
    /// <summary>Writes a bool as a varint 0/1.</summary>
    public void WriteBool(bool value) => WriteVarint(value ? 1u : 0u);
    /// <summary>Writes a <see cref="char"/> as a uint32 UTF-16 code unit.</summary>
    public void WriteChar(char value) => WriteVarint((uint)value);

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

    /// <summary>Writes an sfixed32.</summary>
    public void WriteSFixed32(int value) => WriteFixed32(unchecked((uint)value));
    /// <summary>Writes an sfixed64.</summary>
    public void WriteSFixed64(long value) => WriteFixed64(unchecked((ulong)value));
    /// <summary>Writes a float as fixed32.</summary>
    public void WriteFloat(float value) => WriteFixed32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    /// <summary>Writes a double as fixed64.</summary>
    public void WriteDouble(double value) => WriteFixed64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    /// <summary>
    /// Length-delimited payload: varint length prefix + raw bytes (same as <see cref="WriteBytes"/>).
    /// </summary>
    public void WriteLengthDelimitedBytes(scoped ReadOnlySpan<byte> data) => WriteBytes(data);

    /// <summary>Writes length-delimited raw bytes (length varint + payload).</summary>
    public void WriteBytes(scoped ReadOnlySpan<byte> data)
    {
        WriteVarint((uint)data.Length);
        WriteRaw(data);
    }

    /// <summary>Writes raw bytes without a length prefix.</summary>
    public void WriteRaw(scoped ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return;
        var span = _output.GetSpan(data.Length);
        data.CopyTo(span);
        _output.Advance(data.Length);
    }

    /// <summary>Writes a length-delimited UTF-8 string (null is written as empty).</summary>
    public void WriteString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteVarint(0u);
            return;
        }
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarint((uint)byteCount);
        var span = _output.GetSpan(byteCount);
        var written = Encoding.UTF8.GetBytes(value, span);
        _output.Advance(written);
    }

    /// <summary>Writes a <see cref="Guid"/> as a length-delimited canonical "D" UTF-8 string (36 bytes).</summary>
    public void WriteGuid(Guid value)
    {
        WriteVarint(36u);
        var span = _output.GetSpan(36);
        value.TryFormat(span, out var written, "D");
        _output.Advance(written);
    }

    /// <summary>Writes a <see cref="decimal"/> as a length-delimited invariant-culture string (lossless).</summary>
    public void WriteDecimal(decimal value)
    {
        Span<byte> tmp = stackalloc byte[ProtoWireSize.MaxDecimalChars];
        value.TryFormat(tmp, out var written, default, CultureInfo.InvariantCulture);
        WriteVarint((uint)written);
        WriteRaw(tmp.Slice(0, written));
    }

    /// <summary>Writes a length-delimited google.protobuf.Timestamp for a <see cref="DateTime"/> (Unspecified is treated as UTC).</summary>
    public void WriteTimestamp(DateTime value)
    {
        ProtoWellKnown.TimestampFromTicks(ProtoWellKnown.UtcTicks(value), out var s, out var n);
        WriteSecondsNanos(s, n);
    }

    /// <summary>Writes a length-delimited google.protobuf.Timestamp for a <see cref="DateTimeOffset"/>.</summary>
    public void WriteTimestamp(DateTimeOffset value)
    {
        ProtoWellKnown.TimestampFromTicks(value.UtcTicks, out var s, out var n);
        WriteSecondsNanos(s, n);
    }

    /// <summary>Writes a <see cref="DateOnly"/> as a google.protobuf.Timestamp at midnight UTC.</summary>
    public void WriteTimestamp(DateOnly value)
    {
        ProtoWellKnown.TimestampFromTicks(value.DayNumber * TimeSpan.TicksPerDay, out var s, out var n);
        WriteSecondsNanos(s, n);
    }

    /// <summary>Writes a length-delimited google.protobuf.Duration for a <see cref="TimeSpan"/>.</summary>
    public void WriteDuration(TimeSpan value)
    {
        ProtoWellKnown.DurationFromTicks(value.Ticks, out var s, out var n);
        WriteSecondsNanos(s, n);
    }

    /// <summary>Writes a <see cref="TimeOnly"/> as a google.protobuf.Duration since midnight.</summary>
    public void WriteDuration(TimeOnly value)
    {
        ProtoWellKnown.DurationFromTicks(value.Ticks, out var s, out var n);
        WriteSecondsNanos(s, n);
    }

    private void WriteSecondsNanos(long seconds, int nanos)
    {
        WriteVarint((uint)ProtoWellKnown.SecondsNanosBodySize(seconds, nanos));
        if (seconds != 0)
        {
            WriteTag(1, ProtoWireType.Varint);
            WriteInt64(seconds);
        }
        if (nanos != 0)
        {
            WriteTag(2, ProtoWireType.Varint);
            WriteInt32(nanos);
        }
    }
}

/// <summary>Protobuf wire types used by <see cref="ProtoWireWriter"/> and <see cref="ProtoWireReader"/>.</summary>
public enum ProtoWireType
{
    /// <summary>Base-128 varint (int32, int64, bool, enum).</summary>
    Varint = 0,
    /// <summary>Fixed 64-bit little-endian (fixed64, double).</summary>
    Fixed64 = 1,
    /// <summary>Length-prefixed payload (string, bytes, embedded messages, packed repeated).</summary>
    LengthDelimited = 2,
    /// <summary>Deprecated group start (skipped when read).</summary>
    StartGroup = 3,
    /// <summary>Deprecated group end.</summary>
    EndGroup = 4,
    /// <summary>Fixed 32-bit little-endian (fixed32, float).</summary>
    Fixed32 = 5
}
