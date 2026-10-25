using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Minimal, allocation-free protobuf binary reader. All malformed input raises <see cref="ProtoWireException"/>.
/// Nested messages are read through <see cref="ReadMessage"/>, which returns a length-bounded sub-reader and
/// enforces <see cref="ProtoWireLimits.MaxDepth"/>.
/// </summary>
public ref struct ProtoWireReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlySpan<byte> _data;
    private readonly int _depth;
    private int _pos;
    private int _lastFieldNumber;

    /// <summary>Creates a reader over <paramref name="data"/> (top-level message, depth 0).</summary>
    public ProtoWireReader(ReadOnlySpan<byte> data) : this(data, 0) { }

    private ProtoWireReader(ReadOnlySpan<byte> data, int depth)
    {
        _data = data;
        _depth = depth;
        _pos = 0;
        _lastFieldNumber = 0;
    }

    /// <summary>True when all bytes have been consumed.</summary>
    public readonly bool IsAtEnd => _pos >= _data.Length;
    /// <summary>Current read offset.</summary>
    public readonly int Position => _pos;
    /// <summary>Total buffer length in bytes.</summary>
    public readonly int Length => _data.Length;
    /// <summary>Message nesting depth of this reader (0 = top-level).</summary>
    public readonly int Depth => _depth;

    /// <summary>
    /// Reads the next field tag, or returns <see langword="false"/> at the end of the buffer.
    /// Field number 0 and tags wider than 32 bits are rejected.
    /// </summary>
    public bool TryReadTag(out int fieldNumber, out ProtoWireType wireType)
    {
        if (_pos >= _data.Length)
        {
            fieldNumber = 0;
            wireType = default;
            return false;
        }

        var tag = ReadVarint64();
        if (tag > uint.MaxValue)
            throw new ProtoWireException("Invalid tag: value exceeds 32 bits.");
        fieldNumber = (int)((uint)tag >> 3);
        wireType = (ProtoWireType)(tag & 7);
        if (fieldNumber == 0)
            throw new ProtoWireException("Invalid tag: field number 0.");
        _lastFieldNumber = fieldNumber;
        return true;
    }

    /// <summary>Reads a varint truncated to 32 bits (negative int32 values arrive as 10-byte varints).</summary>
    public uint ReadVarint32() => unchecked((uint)ReadVarint64());

    /// <summary>Reads a full varint (up to 10 bytes; the 10th byte may only carry the top bit).</summary>
    public ulong ReadVarint64()
    {
        var data = _data;
        var pos = _pos;
        if ((uint)pos < (uint)data.Length)
        {
            var first = data[pos];
            if (first < 0x80)
            {
                _pos = pos + 1;
                return first;
            }
        }

        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if ((uint)pos >= (uint)data.Length)
                throw new ProtoWireException("Truncated varint.");
            var b = data[pos++];
            if (shift == 63 && b > 1)
                throw new ProtoWireException("Malformed varint: value exceeds 64 bits.");
            result |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
            {
                _pos = pos;
                return result;
            }
        }

        throw new ProtoWireException("Malformed varint: more than 10 bytes.");
    }

    /// <summary>int32 on the wire is a varint; negatives are sign-extended to 64 bits and truncated here.</summary>
    public int ReadInt32() => unchecked((int)ReadVarint64());
    /// <summary>Reads a signed int64 varint.</summary>
    public long ReadInt64() => unchecked((long)ReadVarint64());
    /// <summary>Reads an unsigned int32 varint.</summary>
    public uint ReadUInt32() => unchecked((uint)ReadVarint64());
    /// <summary>Reads an unsigned int64 varint.</summary>
    public ulong ReadUInt64() => ReadVarint64();
    /// <summary>Reads a ZigZag-encoded sint32.</summary>
    public int ReadSInt32()
    {
        var v = unchecked((uint)ReadVarint64());
        return unchecked((int)(v >> 1) ^ -(int)(v & 1));
    }
    /// <summary>Reads a ZigZag-encoded sint64.</summary>
    public long ReadSInt64()
    {
        var v = ReadVarint64();
        return unchecked((long)(v >> 1) ^ -(long)(v & 1));
    }
    /// <summary>Reads a bool varint (any non-zero value is <see langword="true"/>).</summary>
    public bool ReadBool() => ReadVarint64() != 0;

    /// <summary>Reads a <see cref="char"/> encoded as a uint32 UTF-16 code unit.</summary>
    public char ReadChar()
    {
        var v = ReadVarint64();
        if (v > char.MaxValue)
            throw new ProtoWireException("Invalid char: value exceeds U+FFFF.");
        return (char)v;
    }

    /// <summary>Reads a little-endian fixed32 (4 bytes).</summary>
    public uint ReadFixed32()
    {
        if (_data.Length - _pos < 4) throw new ProtoWireException("Truncated fixed32.");
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_pos));
        _pos += 4;
        return v;
    }

    /// <summary>Reads a little-endian fixed64 (8 bytes).</summary>
    public ulong ReadFixed64()
    {
        if (_data.Length - _pos < 8) throw new ProtoWireException("Truncated fixed64.");
        var v = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_pos));
        _pos += 8;
        return v;
    }

    /// <summary>Reads an sfixed32.</summary>
    public int ReadSFixed32() => unchecked((int)ReadFixed32());
    /// <summary>Reads an sfixed64.</summary>
    public long ReadSFixed64() => unchecked((long)ReadFixed64());
    /// <summary>Reads a float encoded as fixed32.</summary>
    public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)ReadFixed32()));
    /// <summary>Reads a double encoded as fixed64.</summary>
    public double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadFixed64()));

    /// <summary>Reads a length prefix and validates it against the remaining bytes (no int overflow).</summary>
    public int ReadLength()
    {
        var len = ReadVarint64();
        if (len > (ulong)(_data.Length - _pos))
            throw new ProtoWireException("Truncated length-delimited field.");
        return (int)len;
    }

    /// <summary>Reads a length-delimited byte span (a slice of the input; no copy).</summary>
    public ReadOnlySpan<byte> ReadBytes()
    {
        var len = ReadLength();
        var slice = _data.Slice(_pos, len);
        _pos += len;
        return slice;
    }

    /// <summary>Reads a length-delimited UTF-8 string. Invalid UTF-8 is rejected.</summary>
    public string ReadString()
    {
        var bytes = ReadBytes();
        if (bytes.Length == 0)
            return string.Empty;
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ProtoWireException("Invalid UTF-8 in string field.", ex);
        }
    }

    /// <summary>
    /// Reads a length-delimited embedded message and returns a reader bounded to it.
    /// Throws when the nesting depth exceeds <see cref="ProtoWireLimits.MaxDepth"/>.
    /// </summary>
    public ProtoWireReader ReadMessage()
    {
        var len = ReadLength();
        if (_depth >= ProtoWireLimits.MaxDepth)
            throw ProtoWireLimits.DepthExceeded();
        var sub = new ProtoWireReader(_data.Slice(_pos, len), _depth + 1);
        _pos += len;
        return sub;
    }

    /// <summary>Reads a packed repeated payload and returns a reader bounded to it.</summary>
    public ProtoWireReader ReadPacked()
    {
        var len = ReadLength();
        var sub = new ProtoWireReader(_data.Slice(_pos, len), _depth);
        _pos += len;
        return sub;
    }

    /// <summary>Counts complete varints remaining in this reader (used to pre-size packed collections).</summary>
    public readonly int CountRemainingVarints()
    {
        var count = 0;
        var rest = _data.Slice(_pos);
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] < 0x80)
                count++;
        }
        return count;
    }

    /// <summary>Remaining unread bytes.</summary>
    public readonly int Remaining => _data.Length - _pos;

    /// <summary>
    /// Reads a <see cref="Guid"/> written as a length-delimited UTF-8 string (canonical "D" format).
    /// An empty string yields <see cref="Guid.Empty"/>; anything else that does not parse is rejected.
    /// </summary>
    public Guid ReadGuid()
    {
        var bytes = ReadBytes();
        if (bytes.Length == 0)
            return Guid.Empty;
        if (Utf8Parser.TryParse(bytes, out Guid value, out var consumed, 'D') && consumed == bytes.Length)
            return value;
        if (bytes.Length <= 68)
        {
            Span<char> chars = stackalloc char[68];
            var n = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] >= 0x80)
                    throw new ProtoWireException("Invalid Guid string.");
                chars[n++] = (char)bytes[i];
            }
            if (Guid.TryParse(chars.Slice(0, n), out value))
                return value;
        }
        throw new ProtoWireException("Invalid Guid string.");
    }

    /// <summary>
    /// Reads a <see cref="decimal"/> written as a length-delimited invariant-culture string
    /// (exponent notation accepted). An empty string yields 0.
    /// </summary>
    public decimal ReadDecimal()
    {
        var bytes = ReadBytes();
        if (bytes.Length == 0)
            return 0m;
        if (decimal.TryParse(bytes, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new ProtoWireException("Invalid decimal string.");
    }

    /// <summary>Reads a google.protobuf.Timestamp as a UTC <see cref="DateTime"/>.</summary>
    public DateTime ReadTimestamp() => new(ReadTimestampTicks(), DateTimeKind.Utc);

    /// <summary>Reads a google.protobuf.Timestamp as a <see cref="DateTimeOffset"/> (offset zero).</summary>
    public DateTimeOffset ReadTimestampOffset() => new(ReadTimestampTicks(), TimeSpan.Zero);

    /// <summary>Reads a google.protobuf.Timestamp as a <see cref="DateOnly"/> (UTC date part).</summary>
    public DateOnly ReadDateOnly() => DateOnly.FromDateTime(new DateTime(ReadTimestampTicks(), DateTimeKind.Utc));

    /// <summary>Reads a google.protobuf.Duration as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan ReadDuration() => new(ReadDurationTicks());

    /// <summary>Reads a google.protobuf.Duration (time since midnight) as a <see cref="TimeOnly"/>.</summary>
    public TimeOnly ReadTimeOnly()
    {
        var ticks = ReadDurationTicks();
        if (ticks < 0 || ticks >= TimeSpan.TicksPerDay)
            throw new ProtoWireException("Invalid TimeOnly: duration must be within [0, 24h).");
        return new TimeOnly(ticks);
    }

    private long ReadTimestampTicks()
    {
        ReadSecondsNanos(out var seconds, out var nanos);
        if (seconds < ProtoWellKnown.MinTimestampSeconds || seconds > ProtoWellKnown.MaxTimestampSeconds
            || nanos < 0 || nanos > 999_999_999)
            throw new ProtoWireException("Timestamp out of range.");
        return ProtoWellKnown.UnixEpochTicks + seconds * TimeSpan.TicksPerSecond + nanos / 100;
    }

    private long ReadDurationTicks()
    {
        ReadSecondsNanos(out var seconds, out var nanos);
        if (seconds < -ProtoWellKnown.MaxDurationSeconds || seconds > ProtoWellKnown.MaxDurationSeconds
            || nanos < -999_999_999 || nanos > 999_999_999
            || (seconds > 0 && nanos < 0) || (seconds < 0 && nanos > 0))
            throw new ProtoWireException("Duration out of range.");
        return seconds * TimeSpan.TicksPerSecond + nanos / 100;
    }

    private void ReadSecondsNanos(out long seconds, out int nanos)
    {
        seconds = 0;
        nanos = 0;
        var sub = ReadMessage();
        while (sub.TryReadTag(out var field, out var wt))
        {
            if (field == 1 && wt == ProtoWireType.Varint)
                seconds = sub.ReadInt64();
            else if (field == 2 && wt == ProtoWireType.Varint)
                nanos = sub.ReadInt32();
            else
                sub.SkipField(wt);
        }
    }

    /// <summary>Skips the current field payload for the given wire type (including groups).</summary>
    public void SkipField(ProtoWireType wireType)
    {
        switch (wireType)
        {
            case ProtoWireType.Varint:
                _ = ReadVarint64();
                break;
            case ProtoWireType.Fixed32:
                if (_data.Length - _pos < 4) throw new ProtoWireException("Truncated fixed32.");
                _pos += 4;
                break;
            case ProtoWireType.Fixed64:
                if (_data.Length - _pos < 8) throw new ProtoWireException("Truncated fixed64.");
                _pos += 8;
                break;
            case ProtoWireType.LengthDelimited:
            {
                var len = ReadLength();
                _pos += len;
                break;
            }
            case ProtoWireType.StartGroup:
                SkipGroup(_lastFieldNumber, _depth + 1);
                break;
            case ProtoWireType.EndGroup:
                throw new ProtoWireException("Unexpected end-group tag.");
            default:
                throw new ProtoWireException($"Invalid wire type {(int)wireType}.");
        }
    }

    private void SkipGroup(int fieldNumber, int depth)
    {
        if (depth > ProtoWireLimits.MaxDepth)
            throw ProtoWireLimits.DepthExceeded();
        while (true)
        {
            if (!TryReadTag(out var field, out var wt))
                throw new ProtoWireException("Truncated group.");
            if (wt == ProtoWireType.EndGroup)
            {
                if (field != fieldNumber)
                    throw new ProtoWireException("Mismatched end-group tag.");
                return;
            }
            if (wt == ProtoWireType.StartGroup)
                SkipGroup(field, depth + 1);
            else
                SkipField(wt);
        }
    }
}

internal static class ProtoWellKnown
{
    public const long UnixEpochTicks = 621_355_968_000_000_000L;
    public const long MinTimestampSeconds = -62_135_596_800L;  // 0001-01-01T00:00:00Z
    public const long MaxTimestampSeconds = 253_402_300_799L;  // 9999-12-31T23:59:59Z
    public const long MaxDurationSeconds = 315_576_000_000L;   // ~10,000 years

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TimestampFromTicks(long utcTicks, out long seconds, out int nanos)
    {
        var t = utcTicks - UnixEpochTicks;
        seconds = t / TimeSpan.TicksPerSecond;
        var rem = t % TimeSpan.TicksPerSecond;
        if (rem < 0)
        {
            rem += TimeSpan.TicksPerSecond;
            seconds--;
        }
        nanos = (int)(rem * 100);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DurationFromTicks(long ticks, out long seconds, out int nanos)
    {
        seconds = ticks / TimeSpan.TicksPerSecond;
        nanos = (int)(ticks % TimeSpan.TicksPerSecond * 100);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long UtcTicks(DateTime value)
        => value.Kind == DateTimeKind.Local ? value.ToUniversalTime().Ticks : value.Ticks;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SecondsNanosBodySize(long seconds, int nanos)
        => (seconds != 0 ? 1 + ProtoWireSize.Int64(seconds) : 0) + (nanos != 0 ? 1 + ProtoWireSize.Int32(nanos) : 0);
}
