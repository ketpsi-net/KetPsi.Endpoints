namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>Minimal protobuf binary reader (no protobuf-net).</summary>
public ref struct ProtoWireReader
{
    private ReadOnlySpan<byte> _data;
    private int _pos;

    public ProtoWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public bool IsAtEnd => _pos >= _data.Length;
    public int Position => _pos;
    public int Length => _data.Length;

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

    public uint ReadVarint32()
    {
        // Allow full 64-bit varint then truncate — negative int32 arrives as 10-byte varint.
        return unchecked((uint)ReadVarint64());
    }

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

    public long ReadInt64() => unchecked((long)ReadVarint64());
    public uint ReadUInt32() => unchecked((uint)ReadVarint64());
    public ulong ReadUInt64() => ReadVarint64();
    public bool ReadBool() => ReadVarint64() != 0;

    public uint ReadFixed32()
    {
        if (_pos + 4 > _data.Length) throw new InvalidOperationException("Truncated fixed32.");
        var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_pos));
        _pos += 4;
        return v;
    }

    public ulong ReadFixed64()
    {
        if (_pos + 8 > _data.Length) throw new InvalidOperationException("Truncated fixed64.");
        var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_pos));
        _pos += 8;
        return v;
    }

    public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)ReadFixed32()));
    public double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadFixed64()));

    public ReadOnlySpan<byte> ReadBytes()
    {
        var len = (int)ReadVarint32();
        if (len < 0 || _pos + len > _data.Length) throw new InvalidOperationException("Truncated bytes.");
        var slice = _data.Slice(_pos, len);
        _pos += len;
        return slice;
    }

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
