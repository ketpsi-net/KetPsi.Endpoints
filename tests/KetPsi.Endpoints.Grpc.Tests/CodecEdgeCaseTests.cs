using System.Buffers;
using System.Collections.Immutable;
using System.Text;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using KetPsi.Endpoints.Grpc.Tests.Scenarios;
using KetPsi.Endpoints.Grpc.Wire;

using Xunit;

using G = KetPsi.Interop.Google;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>Wire-format edge cases, malicious input, and byte-level interop with Google.Protobuf.</summary>
public sealed class CodecEdgeCaseTests
{
    private delegate void WriteAction(ref ProtoWireWriter w);

    private static byte[] Write(WriteAction action)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new ProtoWireWriter(buffer);
        action(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    internal static byte[] NestedTree(int depth)
    {
        var current = new byte[] { 0x08, 0x01 };
        for (var i = 0; i < depth; i++)
        {
            var len = Write((ref ProtoWireWriter w) => w.WriteLength(current.Length));
            current = new byte[] { 0x12 }.Concat(len).Concat(current).ToArray();
        }
        return current;
    }

    // ---------------------------------------------------------------- reader / writer primitives

    [Fact]
    public void NegativeInt32_IsTenByteVarint()
    {
        var bytes = Write((ref ProtoWireWriter w) => w.WriteInt32(-1));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }, bytes);
        Assert.Equal(10, ProtoWireSize.Int32(-1));
        var r = new ProtoWireReader(bytes);
        Assert.Equal(-1, r.ReadInt32());
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(-1, new byte[] { 0x01 })]
    [InlineData(1, new byte[] { 0x02 })]
    [InlineData(-2, new byte[] { 0x03 })]
    [InlineData(int.MaxValue, new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0x0F })]
    [InlineData(int.MinValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
    public void ZigZag_MatchesSpec(int value, byte[] expected)
    {
        var bytes = Write((ref ProtoWireWriter w) => w.WriteSInt32(value));
        Assert.Equal(expected, bytes);
        Assert.Equal(expected.Length, ProtoWireSize.SInt32(value));
        var r = new ProtoWireReader(bytes);
        Assert.Equal(value, r.ReadSInt32());
        var b64 = Write((ref ProtoWireWriter w) => w.WriteSInt64(value));
        var r64 = new ProtoWireReader(b64);
        Assert.Equal(value, r64.ReadSInt64());
    }

    [Fact]
    public void Fixed_AreLittleEndian()
    {
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, Write((ref ProtoWireWriter w) => w.WriteFixed32(0x12345678)));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, Write((ref ProtoWireWriter w) => w.WriteSFixed64(-1)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.0)]
    [InlineData(double.Epsilon)]
    public void FloatingPoint_SpecialValues_RoundTripBitExact(double value)
    {
        var bytes = Write((ref ProtoWireWriter w) => { w.WriteDouble(value); w.WriteFloat((float)value); });
        var r = new ProtoWireReader(bytes);
        Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(r.ReadDouble()));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)value), BitConverter.SingleToInt32Bits(r.ReadFloat()));
    }

    [Fact]
    public void Varint_TenthByteMustBeZeroOrOne()
    {
        var ok = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };
        var r = new ProtoWireReader(ok);
        Assert.Equal(ulong.MaxValue, r.ReadVarint64());
        Assert.Throws<ProtoWireException>(() =>
        {
            var bad = new ProtoWireReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 });
            return bad.ReadVarint64();
        });
        Assert.Throws<ProtoWireException>(() =>
        {
            var tooLong = new ProtoWireReader(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 });
            return tooLong.ReadVarint64();
        });
    }

    [Fact]
    public void FieldNumberZero_Throws()
    {
        Assert.Throws<ProtoWireException>(() =>
        {
            var r = new ProtoWireReader(new byte[] { 0x00, 0x01 });
            return r.TryReadTag(out _, out _);
        });
    }

    [Fact]
    public void InvalidWireTypes_Throw()
    {
        foreach (var wt in new[] { 4, 6, 7 })
        {
            Assert.Throws<ProtoWireException>(() =>
            {
                var r = new ProtoWireReader(new byte[] { (byte)((1 << 3) | wt) });
                r.TryReadTag(out _, out var t);
                r.SkipField(t);
                return 0;
            });
        }
    }

    [Fact]
    public void Groups_AreSkipped_IncludingNested()
    {
        // field 5 start-group { field 6 start-group { field 1 varint 1 } end 6, field 2 fixed32 } end 5, field 1 varint 7
        var bytes = new byte[] { 0x2B, 0x33, 0x08, 0x01, 0x34, 0x15, 1, 2, 3, 4, 0x2C, 0x08, 0x07 };
        var r = new ProtoWireReader(bytes);
        Assert.True(r.TryReadTag(out var f, out var wt));
        Assert.Equal(5, f);
        r.SkipField(wt);
        Assert.True(r.TryReadTag(out f, out _));
        Assert.Equal(1, f);
        Assert.Equal(7, r.ReadInt32());
    }

    [Fact]
    public void Groups_MismatchedOrUnterminated_Throw()
    {
        Assert.Throws<ProtoWireException>(() =>
        {
            var r = new ProtoWireReader(new byte[] { 0x2B, 0x34 }); // start 5, end 6
            r.TryReadTag(out _, out var wt);
            r.SkipField(wt);
            return 0;
        });
        Assert.Throws<ProtoWireException>(() =>
        {
            var r = new ProtoWireReader(new byte[] { 0x2B, 0x08, 0x01 }); // never ends
            r.TryReadTag(out _, out var wt);
            r.SkipField(wt);
            return 0;
        });
    }

    [Fact]
    public void HugeLengthPrefix_ThrowsWithoutOverflow()
    {
        foreach (var payload in new[]
                 {
                     new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0x00 },                         // 4 GB
                     new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x00 },                         // int.MaxValue
                     new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }, // ulong.MaxValue
                 })
        {
            Assert.Throws<ProtoWireException>(() =>
            {
                var r = new ProtoWireReader(payload);
                return r.ReadBytes().Length;
            });
        }
    }

    [Fact]
    public void PackedReader_IsBoundedToItsLength()
    {
        // packed [1, 2] (len 2) followed by field 2 varint 9
        var r = new ProtoWireReader(new byte[] { 0x02, 0x01, 0x02, 0x10, 0x09 });
        var packed = r.ReadPacked();
        Assert.Equal(2, packed.CountRemainingVarints());
        Assert.Equal(1, packed.ReadInt32());
        Assert.Equal(2, packed.ReadInt32());
        Assert.True(packed.IsAtEnd);
        Assert.True(r.TryReadTag(out var f, out _));
        Assert.Equal(2, f);
    }

    [Fact]
    public void InvalidUtf8_Throws()
    {
        Assert.Throws<ProtoWireException>(() =>
        {
            var r = new ProtoWireReader(new byte[] { 0x02, 0xC3, 0x28 });
            return r.ReadString();
        });
    }

    [Fact]
    public void EmptyAndUnicodeStrings_RoundTrip()
    {
        foreach (var s in new[] { "", "a", "Ünïcödé 😀 漢字", new string('x', 100_000) })
        {
            var bytes = Write((ref ProtoWireWriter w) => w.WriteString(s));
            Assert.Equal(bytes.Length, ProtoWireSize.String(s));
            var r = new ProtoWireReader(bytes);
            Assert.Equal(s, r.ReadString());
        }
    }

    [Fact]
    public void ReaderDepthLimit_Throws()
    {
        Assert.Throws<ProtoWireException>(() => GeneratedProtoCodecs.Deserialize_TreeNode(NestedTree(ProtoWireLimits.MaxDepth + 5)));
        var ok = GeneratedProtoCodecs.Deserialize_TreeNode(NestedTree(50));
        var depth = 0;
        for (var n = ok; n.Child is not null; n = n.Child) depth++;
        Assert.Equal(50, depth);
    }

    [Fact]
    public void WriterDepthLimit_DetectsCycles()
    {
        var node = new TreeNode { Value = 1 };
        node.Child = node;
        Assert.Throws<ProtoWireException>(() => GeneratedProtoCodecs.Serialize_TreeNode(node));
    }

    [Fact]
    public void Guid_IsCanonicalString_AndRejectsGarbage()
    {
        var g = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var bytes = Write((ref ProtoWireWriter w) => w.WriteGuid(g));
        Assert.Equal(36, bytes[0]);
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", Encoding.ASCII.GetString(bytes, 1, 36));
        var r = new ProtoWireReader(bytes);
        Assert.Equal(g, r.ReadGuid());

        var empty = new ProtoWireReader(new byte[] { 0x00 });
        Assert.Equal(Guid.Empty, empty.ReadGuid());

        var n = Encoding.ASCII.GetBytes(g.ToString("N"));
        var nf = new ProtoWireReader(new byte[] { (byte)n.Length }.Concat(n).ToArray());
        Assert.Equal(g, nf.ReadGuid());

        Assert.Throws<ProtoWireException>(() =>
        {
            var bad = new ProtoWireReader(new byte[] { 0x03, 0x01, 0x02, 0x03 });
            return bad.ReadGuid();
        });
    }

    [Fact]
    public void Char_OutOfRange_Throws()
    {
        Assert.Throws<ProtoWireException>(() =>
        {
            var r = new ProtoWireReader(new byte[] { 0x80, 0x80, 0x04 }); // 0x10000
            return r.ReadChar();
        });
    }

    [Fact]
    public void ProtoArrayBuilder_GrowsAndReturnsExactArray()
    {
        var b = new ProtoArrayBuilder<int>();
        Assert.Empty(b.ToArray());
        b.EnsureCapacity(2);
        for (var i = 0; i < 1000; i++)
            b.Add(i);
        var arr = b.ToArray();
        Assert.Equal(1000, arr.Length);
        Assert.Equal(999, arr[999]);
    }

    // ---------------------------------------------------------------- generated codecs: exact bytes

    [Fact]
    public void Message_ImplicitPresence_And_ExactEncoding()
    {
        Assert.Equal(new byte[] { 0x08, 0x96, 0x01 }, GeneratedProtoCodecs.Serialize_NumberMessage(new NumberMessage { Value = 150 }));
        Assert.Empty(GeneratedProtoCodecs.Serialize_NumberMessage(new NumberMessage { Value = 0 }));
        Assert.Equal(11, GeneratedProtoCodecs.Serialize_NumberMessage(new NumberMessage { Value = -1 }).Length);
    }

    [Fact]
    public void RepeatedScalars_AreWrittenPacked_AndBothFormsAreRead()
    {
        var bytes = GeneratedProtoCodecs.Serialize_EdgeMessage(new EdgeMessage { Arr = new[] { 1, 2, 3 } });
        Assert.Equal(new byte[] { 0x72, 0x03, 0x01, 0x02, 0x03 }, bytes);

        // unpacked form of the same field (wire type 0) plus a packed chunk: concatenated
        var mixed = new byte[] { 0x70, 0x05, 0x70, 0x06, 0x72, 0x01, 0x07 };
        Assert.Equal(new[] { 5, 6, 7 }, GeneratedProtoCodecs.Deserialize_EdgeMessage(mixed).Arr);
    }

    [Fact]
    public void WrongWireTypeForKnownField_IsSkipped()
    {
        // field 7 (int32 I) sent as length-delimited, then field 1 (Text) "ok"
        var bytes = new byte[] { 0x3A, 0x01, 0x41, 0x0A, 0x02, (byte)'o', (byte)'k' };
        var msg = GeneratedProtoCodecs.Deserialize_EdgeMessage(bytes);
        Assert.Equal(0, msg.I);
        Assert.Equal("ok", msg.Text);
    }

    [Fact]
    public void UnknownFields_IncludingGroups_AreSkipped()
    {
        var bytes = new byte[] { 0xF8, 0x07, 0x01, 0x9B, 0x06, 0x08, 0x01, 0x9C, 0x06, 0x38, 0x05 };
        // field 127 varint, field 99 group { field 1 varint }, field 7 = 5
        Assert.Equal(5, GeneratedProtoCodecs.Deserialize_EdgeMessage(bytes).I);
    }

    [Fact]
    public void TruncatedMessage_Throws()
    {
        var full = GeneratedProtoCodecs.Serialize_EdgeMessage(new EdgeMessage { Text = "hello", I = 300 });
        for (var cut = 1; cut < full.Length; cut++)
        {
            var part = full.AsSpan(0, cut).ToArray();
            if (cut == 7) continue; // boundary between complete fields
            Assert.Throws<ProtoWireException>(() => GeneratedProtoCodecs.Deserialize_EdgeMessage(part));
        }
    }

    [Fact]
    public void SizeMatchesSerializedLength()
    {
        var m = SampleEdge();
        Assert.Equal(GeneratedProtoCodecs.Serialize_EdgeMessage(m).Length, GeneratedProtoCodecs.Size_EdgeMessage(m, 0));
        var t = SampleTemporal();
        Assert.Equal(GeneratedProtoCodecs.Serialize_TemporalMessage(t).Length, GeneratedProtoCodecs.Size_TemporalMessage(t, 0));
    }

    // ---------------------------------------------------------------- generated codecs: shapes

    [Fact]
    public void InheritedProperties_AreSerialized()
    {
        var copy = GeneratedProtoCodecs.Deserialize_DerivedDto(GeneratedProtoCodecs.Serialize_DerivedDto(
            new DerivedDto { BaseId = 9, BaseName = "base", Extra = "x" }));
        Assert.Equal(9, copy.BaseId);
        Assert.Equal("base", copy.BaseName);
        Assert.Equal("x", copy.Extra);
    }

    [Fact]
    public void ConstructorPlusInitProperties_AreAllRestored()
    {
        var original = new CtorInitRecord(5, "n") { B = "init", Values = new List<int> { 1, 2 } };
        var copy = GeneratedProtoCodecs.Deserialize_CtorInitRecord(GeneratedProtoCodecs.Serialize_CtorInitRecord(original));
        Assert.Equal(5, copy.A);
        Assert.Equal("n", copy.Name);
        Assert.Equal("init", copy.B);
        Assert.Equal(new[] { 1, 2 }, copy.Values);
    }

    private static TemporalMessage SampleTemporal() => new()
    {
        Date = new DateOnly(2024, 2, 29),
        Time = new TimeOnly(23, 59, 59, 999).Add(TimeSpan.FromTicks(9)),
        MaybeDate = new DateOnly(1, 1, 1),
        MaybeTime = TimeOnly.MinValue,
        When = new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc).AddTicks(1234567),
        At = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.FromHours(3)).AddTicks(1),
        Span = -TimeSpan.FromTicks(15_000_001),
        Money = 79228162514264337593543950335m,
        MaybeMoney = 0.0000000000000000000000000001m,
        Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        MaybeId = Guid.Empty,
        Letter = 'é',
    };

    [Fact]
    public void TemporalAndSpecialTypes_RoundTripLosslessly()
    {
        var original = SampleTemporal();
        var copy = GeneratedProtoCodecs.Deserialize_TemporalMessage(GeneratedProtoCodecs.Serialize_TemporalMessage(original));
        Assert.Equal(original.Date, copy.Date);
        Assert.Equal(original.Time, copy.Time);
        Assert.Equal(original.MaybeDate, copy.MaybeDate);
        Assert.Equal(original.MaybeTime, copy.MaybeTime);
        Assert.Equal(original.When, copy.When);
        Assert.Equal(DateTimeKind.Utc, copy.When.Kind);
        Assert.Equal(original.At.UtcTicks, copy.At.UtcTicks);
        Assert.Equal(original.Span, copy.Span);
        Assert.Equal(original.Money, copy.Money);
        Assert.Equal(original.MaybeMoney, copy.MaybeMoney);
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(Guid.Empty, copy.MaybeId);
        Assert.Equal(original.Letter, copy.Letter);

        var empty = GeneratedProtoCodecs.Deserialize_TemporalMessage(GeneratedProtoCodecs.Serialize_TemporalMessage(new TemporalMessage()));
        Assert.Null(empty.MaybeDate);
        Assert.Null(empty.MaybeMoney);
        Assert.Null(empty.MaybeId);
        Assert.Equal(0m, empty.Money);
    }

    [Fact]
    public void Decimal_AcceptsExponentNotation_AndRejectsGarbage()
    {
        var w = Write((ref ProtoWireWriter x) => x.WriteBytes(Encoding.ASCII.GetBytes("1.5E+3")));
        var r = new ProtoWireReader(w);
        Assert.Equal(1500m, r.ReadDecimal());
        Assert.Throws<ProtoWireException>(() =>
        {
            var bad = new ProtoWireReader(Write((ref ProtoWireWriter x) => x.WriteString("12,5")));
            return bad.ReadDecimal();
        });
    }

    [Fact]
    public void InvalidTimestamp_Throws()
    {
        // Timestamp { nanos = -1 }
        var ts = new byte[] { 0x0B, 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };
        Assert.Throws<ProtoWireException>(() =>
        {
            var r = new ProtoWireReader(ts);
            return r.ReadTimestamp();
        });
    }

    [Fact]
    public void Enums_ZeroNoZeroAndNegative_RoundTrip()
    {
        var original = new EnumMessage { P = Priority.High, D = Delta.Minus, Ds = new List<Delta> { Delta.Minus, Delta.Zero, Delta.Plus } };
        var bytes = GeneratedProtoCodecs.Serialize_EnumMessage(original);
        var copy = GeneratedProtoCodecs.Deserialize_EnumMessage(bytes);
        Assert.Equal(Priority.High, copy.P);
        Assert.Equal(Delta.Minus, copy.D);
        Assert.Equal(original.Ds, copy.Ds);

        // A missing enum field reads as the proto3 default (0), even when the CLR enum has no 0 member
        Assert.Equal((Priority)0, GeneratedProtoCodecs.Deserialize_EnumMessage(Array.Empty<byte>()).P);
    }

    [Fact]
    public void Maps_MissingKeyOrValue_YieldDefaults()
    {
        // Map (field 9, string → NumberMessage): entry with only a key, entry with only a value
        // LongMap (field 10, int64 → string): entry with only a key
        var bytes = new byte[]
        {
            0x4A, 0x03, 0x0A, 0x01, (byte)'k',   // { key: "k" }
            0x4A, 0x04, 0x12, 0x02, 0x08, 0x05,  // { value: { Value: 5 } }
            0x52, 0x02, 0x08, 0x07,              // { key: 7 }
            0x52, 0x02, 0x08, 0x07, 0x52, 0x05, 0x08, 0x07, 0x12, 0x01, (byte)'z', // duplicate key: last wins
        };
        var m = GeneratedProtoCodecs.Deserialize_EdgeMessage(bytes);
        Assert.NotNull(m.Map["k"]);
        Assert.Equal(0, m.Map["k"].Value);
        Assert.Equal(5, m.Map[""].Value);
        Assert.Equal("z", m.LongMap[7]);
    }

    [Fact]
    public void CollectionsAndOptionalString_RoundTrip()
    {
        var original = SampleEdge();
        var copy = GeneratedProtoCodecs.Deserialize_EdgeMessage(GeneratedProtoCodecs.Serialize_EdgeMessage(original));
        Assert.Equal(original.Text, copy.Text);
        Assert.Equal(original.Data, copy.Data);
        Assert.Equal(original.Doubles, copy.Doubles);
        Assert.Equal(original.Tags.OrderBy(x => x), copy.Tags.OrderBy(x => x));
        Assert.Equal(original.RoList, copy.RoList);
        Assert.Equal(original.Imm.ToArray(), copy.Imm.ToArray());
        Assert.Equal(original.Arr, copy.Arr);
        Assert.Equal(3, copy.Map["three"].Value);
        Assert.Equal("minus", copy.LongMap[-1]);
        Assert.Equal("", copy.Optional);   // explicit presence: empty string survives
        Assert.Null(GeneratedProtoCodecs.Deserialize_EdgeMessage(Array.Empty<byte>()).Optional);
        Assert.True(GeneratedProtoCodecs.Deserialize_EdgeMessage(Array.Empty<byte>()).Imm.IsEmpty);
    }

    [Fact]
    public void StructMessages_RoundTrip()
    {
        var copy = GeneratedProtoCodecs.Deserialize_StructHolder(GeneratedProtoCodecs.Serialize_StructHolder(
            new StructHolder { P = new PointStruct { X = 1, Y = -2 }, Q = new PointStruct { X = 3 } }));
        Assert.Equal(1, copy.P.X);
        Assert.Equal(-2, copy.P.Y);
        Assert.Equal(3, copy.Q!.Value.X);
        Assert.Null(GeneratedProtoCodecs.Deserialize_StructHolder(GeneratedProtoCodecs.Serialize_StructHolder(new StructHolder())).Q);
    }

    [Fact]
    public void SameSimpleNames_AndGenericInstantiations_DoNotCollide()
    {
        var original = new SameNameRequest
        {
            A = new Scenarios.Alpha.Req { A = 1 },
            B = new Scenarios.Beta.Req { B = "b" },
            W1 = new Wrapper<List<int>> { Value = new List<int> { 1, 2 } },
            W2 = new Wrapper<List<string>> { Value = new List<string> { "x" } },
            I1 = new Outer1.Inner { X = 4 },
            I2 = new Outer2.Inner { Y = "y" },
        };
        var copy = GeneratedProtoCodecs.Deserialize_SameNameRequest(GeneratedProtoCodecs.Serialize_SameNameRequest(original));
        Assert.Equal(1, copy.A.A);
        Assert.Equal("b", copy.B.B);
        Assert.Equal(new[] { 1, 2 }, copy.W1.Value);
        Assert.Equal(new[] { "x" }, copy.W2.Value);
        Assert.Equal(4, copy.I1.X);
        Assert.Equal("y", copy.I2.Y);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_Alpha_Req);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_Beta_Req);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_Wrapper_List_Int32);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_Wrapper_List_String);
    }

    // ---------------------------------------------------------------- marshaller

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(byte[] data, Segment? previous)
        {
            Memory = data;
            if (previous is not null)
            {
                RunningIndex = previous.RunningIndex + previous.Memory.Length;
                previous.Next = this;
            }
        }
    }

    private static ReadOnlySequence<byte> Split(byte[] bytes)
    {
        var first = new Segment(bytes.Take(1).ToArray(), null);
        var last = new Segment(bytes.Skip(1).ToArray(), first);
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    [Fact]
    public void Marshaller_MultiSegmentPayload_IsRead()
    {
        var bytes = GeneratedProtoCodecs.Serialize_EdgeMessage(SampleEdge());
        var copy = ProtoMarshaller.Deserialize(Split(bytes), GeneratedProtoCodecs.Read_EdgeMessage);
        Assert.Equal("text ✓", copy.Text);
    }

    [Fact]
    public void Marshaller_MalformedPayload_IsInvalidArgument()
    {
        var ex = Assert.Throws<RpcException>(() => ProtoMarshaller.Deserialize(Split(new byte[] { 0x0A, 0x05, 0x01 }), GeneratedProtoCodecs.Read_EdgeMessage));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ---------------------------------------------------------------- interop with Google.Protobuf

    private static EdgeMessage SampleEdge() => new()
    {
        Text = "text ✓",
        Data = new byte[] { 0, 1, 255 },
        F = -1.25f,
        D = double.NaN,
        L = long.MinValue,
        U = ulong.MaxValue,
        I = -7,
        Doubles = new List<double> { 0.5, double.PositiveInfinity },
        Map = new Dictionary<string, NumberMessage> { ["three"] = new() { Value = 3 }, ["zero"] = new() },
        LongMap = new Dictionary<long, string> { [-1] = "minus", [0] = "" },
        Tags = new HashSet<string> { "a", "b" },
        RoList = new[] { 1, -1 },
        Imm = ImmutableArray.Create(10, 20),
        Arr = new[] { int.MinValue, int.MaxValue },
        Optional = "",
    };

    [Fact]
    public void Interop_KetPsiToGoogle()
    {
        var g = G.EdgeMessage.Parser.ParseFrom(GeneratedProtoCodecs.Serialize_EdgeMessage(SampleEdge()));
        Assert.Equal("text ✓", g.Text);
        Assert.Equal(new byte[] { 0, 1, 255 }, g.Data.ToByteArray());
        Assert.Equal(-1.25f, g.F);
        Assert.True(double.IsNaN(g.D));
        Assert.Equal(long.MinValue, g.L);
        Assert.Equal(ulong.MaxValue, g.U);
        Assert.Equal(-7, g.I);
        Assert.Equal(new[] { 0.5, double.PositiveInfinity }, g.Doubles);
        Assert.Equal(3, g.Map["three"].Value);
        Assert.Equal(0, g.Map["zero"].Value);
        Assert.Equal("minus", g.LongMap[-1]);
        Assert.Equal(new[] { "a", "b" }, g.Tags.OrderBy(x => x));
        Assert.Equal(new[] { 1, -1 }, g.RoList);
        Assert.Equal(new[] { 10, 20 }, g.Imm);
        Assert.Equal(new[] { int.MinValue, int.MaxValue }, g.Arr);
        Assert.True(g.HasOptional);
        Assert.Equal("", g.Optional);

        // Byte-identical with Google's canonical encoding for the deterministic subset
        var simple = new EdgeMessage { Text = "x", I = -1, Arr = new[] { 1, 300 }, Doubles = new List<double> { 1.5 } };
        var gs = new G.EdgeMessage { Text = "x", I = -1 };
        gs.Arr.Add(new[] { 1, 300 });
        gs.Doubles.Add(1.5);
        Assert.Equal(gs.ToByteArray(), GeneratedProtoCodecs.Serialize_EdgeMessage(simple));
    }

    [Fact]
    public void Interop_GoogleToKetPsi()
    {
        var g = new G.EdgeMessage { Text = "hi", F = 2.5f, L = -5, U = 5, I = 42, Optional = "set" };
        g.Map.Add("m", new G.NumberMessage { Value = 9 });
        g.LongMap.Add(long.MaxValue, "max");
        g.Arr.Add(new[] { -1, 0, 1 });
        g.Tags.Add("t");
        var m = GeneratedProtoCodecs.Deserialize_EdgeMessage(g.ToByteArray());
        Assert.Equal("hi", m.Text);
        Assert.Equal(2.5f, m.F);
        Assert.Equal(-5, m.L);
        Assert.Equal(5ul, m.U);
        Assert.Equal(42, m.I);
        Assert.Equal("set", m.Optional);
        Assert.Equal(9, m.Map["m"].Value);
        Assert.Equal("max", m.LongMap[long.MaxValue]);
        Assert.Equal(new[] { -1, 0, 1 }, m.Arr);
        Assert.Contains("t", m.Tags);
    }

    [Fact]
    public void Interop_TemporalAndEnums_MatchGoogleWellKnownTypes()
    {
        var original = SampleTemporal();
        var g = G.TemporalMessage.Parser.ParseFrom(GeneratedProtoCodecs.Serialize_TemporalMessage(original));
        Assert.Equal(Timestamp.FromDateTime(new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc)), g.Date);
        Assert.Equal(Duration.FromTimeSpan(original.Time.ToTimeSpan()), g.Time);
        Assert.Equal(Timestamp.FromDateTime(original.When), g.When);
        Assert.Equal(Timestamp.FromDateTimeOffset(original.At), g.At);
        Assert.Equal(Duration.FromTimeSpan(original.Span), g.Span);
        Assert.Equal("79228162514264337593543950335", g.Money);
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", g.Id);
        Assert.Equal((uint)'é', g.Letter);

        var back = new G.TemporalMessage { When = Timestamp.FromDateTime(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)), Money = "12.50", Id = Guid.Empty.ToString() };
        var m = GeneratedProtoCodecs.Deserialize_TemporalMessage(back.ToByteArray());
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), m.When);
        Assert.Equal(12.50m, m.Money);

        var ge = G.EnumMessage.Parser.ParseFrom(GeneratedProtoCodecs.Serialize_EnumMessage(
            new EnumMessage { P = Priority.High, D = Delta.Minus, Ds = new List<Delta> { Delta.Minus, Delta.Plus } }));
        Assert.Equal(G.Priority.High, ge.P);
        Assert.Equal(G.Delta.Minus, ge.D);
        Assert.Equal(new[] { G.Delta.Minus, G.Delta.Plus }, ge.Ds);
    }

    [Fact]
    public void Interop_NestedGoogleMessage_WithinLimit_IsAccepted()
    {
        var root = new G.TreeNode { Value = 1 };
        var cur = root;
        for (var i = 0; i < 90; i++)
        {
            cur.Child = new G.TreeNode { Value = i };
            cur = cur.Child;
        }
        Assert.NotNull(GeneratedProtoCodecs.Deserialize_TreeNode(root.ToByteArray()));
    }
}
