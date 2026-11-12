using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Encoded-size helpers used by generated codecs to compute message lengths up front, so nested
/// messages are written directly into the destination buffer (no intermediate buffers).
/// Length-delimited helpers include the length prefix but never the tag.
/// </summary>
public static class ProtoWireSize
{
    internal const int MaxDecimalChars = 64;

    /// <summary>Size of an unsigned varint.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Varint(ulong value) => value < 0x80 ? 1 : BitOperations.Log2(value) / 7 + 1;

    /// <summary>Size of an unsigned 32-bit varint.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Varint(uint value) => value < 0x80 ? 1 : BitOperations.Log2(value) / 7 + 1;

    /// <summary>Size of a field tag.</summary>
    public static int Tag(int fieldNumber, ProtoWireType wireType) => Varint((uint)((fieldNumber << 3) | (int)wireType));

    /// <summary>Size of an int32 varint (negative values take 10 bytes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Int32(int value) => value >= 0 ? Varint((uint)value) : 10;
    /// <summary>Size of an int64 varint.</summary>
    public static int Int64(long value) => Varint(unchecked((ulong)value));
    /// <summary>Size of a uint32 varint.</summary>
    public static int UInt32(uint value) => Varint(value);
    /// <summary>Size of a uint64 varint.</summary>
    public static int UInt64(ulong value) => Varint(value);
    /// <summary>Size of a ZigZag sint32.</summary>
    public static int SInt32(int value) => Varint(unchecked((uint)((value << 1) ^ (value >> 31))));
    /// <summary>Size of a ZigZag sint64.</summary>
    public static int SInt64(long value) => Varint(unchecked((ulong)((value << 1) ^ (value >> 63))));
    /// <summary>Size of a bool.</summary>
    public static int Bool(bool value) => 1;
    /// <summary>Size of a char (uint32 code unit).</summary>
    public static int Char(char value) => Varint((uint)value);

    /// <summary>Size of a length-prefixed payload of <paramref name="length"/> bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LengthPrefixed(int length) => Varint((uint)length) + length;

    /// <summary>Size of a length-prefixed UTF-8 string (null = empty).</summary>
    public static int String(string? value)
        => string.IsNullOrEmpty(value) ? 1 : LengthPrefixed(Encoding.UTF8.GetByteCount(value));

    /// <summary>Size of length-prefixed bytes.</summary>
    public static int Bytes(ReadOnlySpan<byte> value) => LengthPrefixed(value.Length);

    /// <summary>Size of a length-prefixed Guid string (always 37).</summary>
    public static int Guid(Guid value) => 37;

    /// <summary>Size of a length-prefixed invariant decimal string.</summary>
    public static int Decimal(decimal value)
    {
        Span<byte> tmp = stackalloc byte[MaxDecimalChars];
        value.TryFormat(tmp, out var written, default, CultureInfo.InvariantCulture);
        return LengthPrefixed(written);
    }

    /// <summary>Size of a length-prefixed google.protobuf.Timestamp.</summary>
    public static int Timestamp(DateTime value)
    {
        ProtoWellKnown.TimestampFromTicks(ProtoWellKnown.UtcTicks(value), out var s, out var n);
        return LengthPrefixed(ProtoWellKnown.SecondsNanosBodySize(s, n));
    }

    /// <summary>Size of a length-prefixed google.protobuf.Timestamp.</summary>
    public static int Timestamp(DateTimeOffset value)
    {
        ProtoWellKnown.TimestampFromTicks(value.UtcTicks, out var s, out var n);
        return LengthPrefixed(ProtoWellKnown.SecondsNanosBodySize(s, n));
    }

    /// <summary>Size of a length-prefixed google.protobuf.Timestamp (midnight UTC).</summary>
    public static int Timestamp(DateOnly value)
    {
        ProtoWellKnown.TimestampFromTicks(value.DayNumber * TimeSpan.TicksPerDay, out var s, out var n);
        return LengthPrefixed(ProtoWellKnown.SecondsNanosBodySize(s, n));
    }

    /// <summary>Size of a length-prefixed google.protobuf.Duration.</summary>
    public static int Duration(TimeSpan value)
    {
        ProtoWellKnown.DurationFromTicks(value.Ticks, out var s, out var n);
        return LengthPrefixed(ProtoWellKnown.SecondsNanosBodySize(s, n));
    }

    /// <summary>Size of a length-prefixed google.protobuf.Duration (since midnight).</summary>
    public static int Duration(TimeOnly value)
    {
        ProtoWellKnown.DurationFromTicks(value.Ticks, out var s, out var n);
        return LengthPrefixed(ProtoWellKnown.SecondsNanosBodySize(s, n));
    }
}
