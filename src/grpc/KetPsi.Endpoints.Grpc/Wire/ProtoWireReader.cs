namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>Minimal protobuf binary reader (no protobuf-net).</summary>
public ref struct ProtoWireReader
{
    private ReadOnlySpan<byte> _data;
    private int _pos;

    /// <summary>Creates a reader over <paramref name="data"/>.</summary>
    public ProtoWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    /// <summary>True when all bytes have been consumed.</summary>
    public bool IsAtEnd => _pos >= _data.Length;
    /// <summary>Current read offset.</summary>
    public int Position => _pos;
    /// <summary>Total buffer length in bytes.</summary>
    public int Length => _data.Length;

    /// <summary>Reads the next field tag, or returns false at end of buffer.</summary>
    public bool TryReadTag(out int fieldNumber, out ProtoWireType wireType)
    {
        fieldNumber = 0;
        wireType = default;
        if (IsAtEnd) return false;
        var tag = ReadVarint32();
        fieldNumber = (int)(tag >> 3);
        wireType = (ProtoWireType)(tag & 7);
        return fieldNumber != 0;
    }

    /// <summary>Reads a varint truncated to 32 bits.</summary>
    public uint ReadVarint32()
    {
        // Allow full 64-bit varint then truncate — negative int32 arrives as 10-byte varint.
        return unchecked((uint)ReadVarint64());
    }

    /// <summary>Reads a full varint (up to 10 bytes).</summary>
    public ulong ReadVarint64()
    {
        ulong result = 0;
        var shift = 0;
        while (shift < 70)
        {
            if (_pos >= _data.Length) throw new InvalidOperationException("Truncated varint.");
            var b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
        throw new InvalidOperationException("Malformed varint.");
    }

    /// <summary>int32 on the wire is a varint; negatives are sign-extended to 64 bits.</summary>
    public int ReadInt32() => unchecked((int)(long)ReadVarint64());

    /// <summary>Reads a signed int64 varint.</summary>
    public long ReadInt64() => unchecked((long)ReadVarint64());
    /// <summary>Reads an unsigned int32 varint.</summary>
    public uint ReadUInt32() => unchecked((uint)ReadVarint64());
    /// <summary>Reads an unsigned int64 varint.</summary>
    public ulong ReadUInt64() => ReadVarint64();
    /// <summary>Reads a bool as a 0/1 varint.</summary>
    public bool ReadBool() => ReadVarint64() != 0;

    /// <summary>Reads a little-endian fixed32 (4 bytes).</summary>
    public uint ReadFixed32()
    {
        if (_pos + 4 > _data.Length) throw new InvalidOperationException("Truncated fixed32.");
        var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_pos));
        _pos += 4;
        return v;
    }

    /// <summary>Reads a little-endian fixed64 (8 bytes).</summary>
    public ulong ReadFixed64()
    {
        if (_pos + 8 > _data.Length) throw new InvalidOperationException("Truncated fixed64.");
        var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_pos));
        _pos += 8;
        return v;
    }

    /// <summary>Reads a float encoded as fixed32.</summary>
    public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)ReadFixed32()));
    /// <summary>Reads a double encoded as fixed64.</summary>
    public double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadFixed64()));

    /// <summary>Reads a length-delimited byte span.</summary>
    public ReadOnlySpan<byte> ReadBytes()
    {
        var len = (int)ReadVarint32();
        if (len < 0 || _pos + len > _data.Length) throw new InvalidOperationException("Truncated bytes.");
        var slice = _data.Slice(_pos, len);
        _pos += len;
        return slice;
    }

    /// <summary>Reads a length-delimited UTF-8 string.</summary>
    public string ReadString()
    {
        var bytes = ReadBytes();
        return bytes.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads a packed repeated scalar block (length-delimited). Caller supplies a per-element reader
    /// that advances this reader until <paramref name="end"/> is reached.
    /// </summary>
    public void ReadPacked(int endExclusive, System.Action readOne)
    {
        while (_pos < endExclusive)
            readOne();
    }

    /// <summary>Mark end position after reading a length-delimited packed payload length.</summary>
    public int BeginPacked()
    {
        var len = (int)ReadVarint32();
        if (len < 0 || _pos + len > _data.Length) throw new InvalidOperationException("Truncated packed field.");
        return _pos + len;
    }

    /// <summary>Skips the current field payload for the given wire type.</summary>
    public void SkipField(ProtoWireType wireType)
    {
        switch (wireType)
        {
            case ProtoWireType.Varint:
                _ = ReadVarint64();
                break;
            case ProtoWireType.Fixed32:
                if (_pos + 4 > _data.Length) throw new InvalidOperationException("Truncated fixed32.");
                _pos += 4;
                break;
            case ProtoWireType.Fixed64:
                if (_pos + 8 > _data.Length) throw new InvalidOperationException("Truncated fixed64.");
                _pos += 8;
                break;
            case ProtoWireType.LengthDelimited:
                _ = ReadBytes();
                break;
            default:
                throw new InvalidOperationException($"Unknown wire type {(int)wireType}.");
        }
    }
}
