using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Codec.Tests.Fixtures;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Codec.Tests;

/// <summary>
/// Round-trip tests for generated message codecs (requires MapGrpc* so generator emits GeneratedProtoCodecs).
/// </summary>
public class GeneratedCodecRoundtripTests
{
    static GeneratedCodecRoundtripTests()
    {
        // Force generator side-effects / type init if any
        _ = typeof(HostBootstrap);
    }

    private static AllScalarsMessage CreateFullScalars() => new()
    {
        Boolean = true,
        Byte = 200,
        SByte = -100,
        Int16 = -32000,
        UInt16 = 65000,
        Int32 = -1_000_000,
        UInt32 = 3_000_000_000,
        Int64 = long.MinValue / 2,
        UInt64 = ulong.MaxValue - 1,
        NativeInt = (nint)(-42),
        NativeUInt = (nuint)42,
        Single = 1.5f,
        Double = -2.5d,
        Decimal = 12345.6789m,
        Char = 'Z',
        String = "Hello 世界",
        Bytes = new byte[] { 0x00, 0xFF, 0x10, 0x20 },
        Guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        DateTime = new DateTime(2024, 6, 15, 12, 30, 45, DateTimeKind.Utc),
        DateTimeOffset = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.Zero),
        TimeSpan = TimeSpan.FromHours(1.5),
        Status = SampleStatus.Active
    };

    [Fact]
    public void AllScalars_RoundTrip_PreservesValues()
    {
        var original = CreateFullScalars();
        var bytes = GeneratedProtoCodecs.Serialize_AllScalarsMessage(original);
        Assert.NotEmpty(bytes);

        var copy = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(bytes);

        Assert.Equal(original.Boolean, copy.Boolean);
        Assert.Equal(original.Byte, copy.Byte);
        Assert.Equal(original.SByte, copy.SByte);
        Assert.Equal(original.Int16, copy.Int16);
        Assert.Equal(original.UInt16, copy.UInt16);
        Assert.Equal(original.Int32, copy.Int32);
        Assert.Equal(original.UInt32, copy.UInt32);
        Assert.Equal(original.Int64, copy.Int64);
        Assert.Equal(original.UInt64, copy.UInt64);
        Assert.Equal(original.NativeInt, copy.NativeInt);
        Assert.Equal(original.NativeUInt, copy.NativeUInt);
        Assert.Equal(original.Single, copy.Single);
        Assert.Equal(original.Double, copy.Double);
        Assert.Equal(original.Decimal, copy.Decimal);
        Assert.Equal(original.Char, copy.Char);
        Assert.Equal(original.String, copy.String);
        Assert.Equal(original.Bytes, copy.Bytes);
        Assert.Equal(original.Guid, copy.Guid);
        Assert.Equal(original.DateTime, copy.DateTime);
        // Timestamp is UTC; offset reduced to UTC instant
        Assert.Equal(original.DateTimeOffset.UtcDateTime, copy.DateTimeOffset.UtcDateTime);
        Assert.Equal(original.TimeSpan, copy.TimeSpan);
        Assert.Equal(original.Status, copy.Status);
    }

    [Fact]
    public void AllScalars_Default_RoundTrip()
    {
        var original = new AllScalarsMessage();
        var copy = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(
            GeneratedProtoCodecs.Serialize_AllScalarsMessage(original));

        Assert.False(copy.Boolean);
        Assert.Equal(0, copy.Byte);
        Assert.Equal(0, copy.Int32);
        Assert.Equal("", copy.String ?? "");
        Assert.Equal(SampleStatus.Unknown, copy.Status);
        Assert.Equal(Guid.Empty, copy.Guid);
    }

    [Fact]
    public void NullableScalars_AllNull_RoundTrip()
    {
        var original = new NullableScalarsMessage();
        var copy = GeneratedProtoCodecs.Deserialize_NullableScalarsMessage(
            GeneratedProtoCodecs.Serialize_NullableScalarsMessage(original));

        Assert.Null(copy.Boolean);
        Assert.Null(copy.Byte);
        Assert.Null(copy.Int32);
        Assert.Null(copy.String);
        Assert.Null(copy.Guid);
        Assert.Null(copy.DateTime);
        Assert.Null(copy.Status);
        Assert.Null(copy.Decimal);
        Assert.Null(copy.Char);
    }

    [Fact]
    public void NullableScalars_AllSet_RoundTrip()
    {
        var original = new NullableScalarsMessage
        {
            Boolean = false,
            Byte = 1,
            SByte = -1,
            Int16 = 2,
            UInt16 = 3,
            Int32 = 4,
            UInt32 = 5,
            Int64 = 6,
            UInt64 = 7,
            Single = 1.25f,
            Double = 2.5,
            Decimal = 9.99m,
            Char = 'x',
            String = "set",
            Guid = Guid.NewGuid(),
            DateTime = DateTime.UtcNow,
            TimeSpan = TimeSpan.FromMinutes(3),
            Status = SampleStatus.Pending
        };

        var copy = GeneratedProtoCodecs.Deserialize_NullableScalarsMessage(
            GeneratedProtoCodecs.Serialize_NullableScalarsMessage(original));

        Assert.Equal(original.Boolean, copy.Boolean);
        Assert.Equal(original.Byte, copy.Byte);
        Assert.Equal(original.SByte, copy.SByte);
        Assert.Equal(original.Int16, copy.Int16);
        Assert.Equal(original.UInt16, copy.UInt16);
        Assert.Equal(original.Int32, copy.Int32);
        Assert.Equal(original.UInt32, copy.UInt32);
        Assert.Equal(original.Int64, copy.Int64);
        Assert.Equal(original.UInt64, copy.UInt64);
        Assert.Equal(original.Single, copy.Single);
        Assert.Equal(original.Double, copy.Double);
        Assert.Equal(original.Decimal, copy.Decimal);
        Assert.Equal(original.Char, copy.Char);
        Assert.Equal(original.String, copy.String);
        Assert.Equal(original.Guid, copy.Guid);
        Assert.Equal(original.Status, copy.Status);
        Assert.Equal(original.TimeSpan, copy.TimeSpan);
        Assert.NotNull(copy.DateTime);
    }

    [Fact]
    public void Collections_IntArray_RoundTrip()
    {
        var original = new CollectionsMessage
        {
            IntArray = new[] { 1, 2, 3, -4, 0 },
            IntList = new List<int> { 10, 20 },
            StringList = new List<string> { "a", "b", "" },
            EnumList = new List<SampleStatus> { SampleStatus.Active, SampleStatus.Inactive },
            RawBytes = new byte[] { 1, 2, 3 }
        };

        var copy = GeneratedProtoCodecs.Deserialize_CollectionsMessage(
            GeneratedProtoCodecs.Serialize_CollectionsMessage(original));

        Assert.Equal(original.IntArray, copy.IntArray);
        Assert.Equal(original.IntList, copy.IntList);
        Assert.Equal(original.StringList, copy.StringList);
        Assert.Equal(original.EnumList, copy.EnumList);
        Assert.Equal(original.RawBytes, copy.RawBytes);
    }

    [Fact]
    public void Collections_Empty_RoundTrip()
    {
        var original = new CollectionsMessage();
        var copy = GeneratedProtoCodecs.Deserialize_CollectionsMessage(
            GeneratedProtoCodecs.Serialize_CollectionsMessage(original));

        Assert.NotNull(copy.IntArray);
        Assert.Empty(copy.IntArray);
        Assert.NotNull(copy.IntList);
        Assert.Empty(copy.IntList);
    }

    [Fact]
    public void Collections_NestedListAndArray_RoundTrip()
    {
        var original = new CollectionsMessage
        {
            NestedList =
            [
                new NestedItem { Id = 1, Name = "one", Amount = 1.5m, Status = SampleStatus.Active },
                new NestedItem { Id = 2, Name = "two", Amount = 2.5m, Status = SampleStatus.Inactive }
            ],
            NestedArray =
            [
                new NestedItem { Id = 3, Name = "three", Amount = 3.5m, Status = SampleStatus.Pending }
            ]
        };

        var copy = GeneratedProtoCodecs.Deserialize_CollectionsMessage(
            GeneratedProtoCodecs.Serialize_CollectionsMessage(original));

        Assert.Equal(2, copy.NestedList.Count);
        Assert.Equal(1, copy.NestedList[0].Id);
        Assert.Equal("one", copy.NestedList[0].Name);
        Assert.Equal(1.5m, copy.NestedList[0].Amount);
        Assert.Equal(SampleStatus.Active, copy.NestedList[0].Status);
        Assert.Single(copy.NestedArray);
        Assert.Equal(3, copy.NestedArray[0].Id);
        Assert.Equal("three", copy.NestedArray[0].Name);
    }

    [Fact]
    public void NestedRoot_RequiredAndOptional_RoundTrip()
    {
        var original = new NestedRootMessage
        {
            Child = new NestedItem { Id = 9, Name = "child", Amount = 99.99m, Status = SampleStatus.Active },
            OptionalChild = new NestedItem { Id = 8, Name = "opt", Amount = 1m, Status = SampleStatus.Pending },
            Label = "root"
        };

        var copy = GeneratedProtoCodecs.Deserialize_NestedRootMessage(
            GeneratedProtoCodecs.Serialize_NestedRootMessage(original));

        Assert.Equal("root", copy.Label);
        Assert.Equal(9, copy.Child.Id);
        Assert.Equal("child", copy.Child.Name);
        Assert.Equal(99.99m, copy.Child.Amount);
        Assert.NotNull(copy.OptionalChild);
        Assert.Equal(8, copy.OptionalChild!.Id);
        Assert.Equal("opt", copy.OptionalChild.Name);
    }

    [Fact]
    public void NestedRoot_OptionalNull_RoundTrip()
    {
        var original = new NestedRootMessage
        {
            Child = new NestedItem { Id = 1, Name = "only", Amount = 0m, Status = SampleStatus.Unknown },
            OptionalChild = null,
            Label = "no-opt"
        };

        var copy = GeneratedProtoCodecs.Deserialize_NestedRootMessage(
            GeneratedProtoCodecs.Serialize_NestedRootMessage(original));

        Assert.Equal("no-opt", copy.Label);
        Assert.Equal(1, copy.Child.Id);
        Assert.Null(copy.OptionalChild);
    }

    [Fact]
    public void NestedItem_Decimal_RoundTrip()
    {
        var original = new NestedItem { Id = 1, Name = "d", Amount = -123.456789m, Status = SampleStatus.Active };
        var copy = GeneratedProtoCodecs.Deserialize_NestedItem(
            GeneratedProtoCodecs.Serialize_NestedItem(original));
        Assert.Equal(original.Amount, copy.Amount);
    }

    [Theory]
    [InlineData(SampleStatus.Unknown)]
    [InlineData(SampleStatus.Active)]
    [InlineData(SampleStatus.Inactive)]
    [InlineData(SampleStatus.Pending)]
    public void Enum_AllValues_RoundTrip(SampleStatus status)
    {
        var original = new AllScalarsMessage { Status = status };
        var copy = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(
            GeneratedProtoCodecs.Serialize_AllScalarsMessage(original));
        Assert.Equal(status, copy.Status);
    }

    [Fact]
    public void Guid_EmptyAndNonEmpty_RoundTrip()
    {
        foreach (var g in new[] { Guid.Empty, Guid.NewGuid() })
        {
            var original = new AllScalarsMessage { Guid = g };
            var copy = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(
                GeneratedProtoCodecs.Serialize_AllScalarsMessage(original));
            Assert.Equal(g, copy.Guid);
        }
    }

    [Fact]
    public void Char_SpecialChars_RoundTrip()
    {
        foreach (var c in new[] { 'A', '0', '\n', '世', '\0' })
        {
            var original = new AllScalarsMessage { Char = c };
            var copy = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(
                GeneratedProtoCodecs.Serialize_AllScalarsMessage(original));
            Assert.Equal(c, copy.Char);
        }
    }

    [Fact]
    public void DoubleSerialize_IsIdempotent()
    {
        var original = CreateFullScalars();
        var once = GeneratedProtoCodecs.Serialize_AllScalarsMessage(original);
        var twice = GeneratedProtoCodecs.Serialize_AllScalarsMessage(
            GeneratedProtoCodecs.Deserialize_AllScalarsMessage(once));
        // Wire bytes may differ for default fields; values must match
        var a = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(once);
        var b = GeneratedProtoCodecs.Deserialize_AllScalarsMessage(twice);
        Assert.Equal(a.Int32, b.Int32);
        Assert.Equal(a.String, b.String);
        Assert.Equal(a.Guid, b.Guid);
        Assert.Equal(a.Decimal, b.Decimal);
        Assert.Equal(a.Status, b.Status);
    }

    [Fact]
    public void Marshallers_Exist_ForGeneratedMessages()
    {
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_AllScalarsMessage);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_NullableScalarsMessage);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_CollectionsMessage);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_NestedRootMessage);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_EchoResponse);
    }
}
