using KetPsi.Endpoints.Abstractions;
using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc.Codec.Tests.Fixtures;

/// <summary>
/// Exhaustive message covering every CLR type the generator maps to protobuf.
/// </summary>
public sealed class AllScalarsMessage
{
    public bool Boolean { get; set; }
    public byte Byte { get; set; }
    public sbyte SByte { get; set; }
    public short Int16 { get; set; }
    public ushort UInt16 { get; set; }
    public int Int32 { get; set; }
    public uint UInt32 { get; set; }
    public long Int64 { get; set; }
    public ulong UInt64 { get; set; }
    public nint NativeInt { get; set; }
    public nuint NativeUInt { get; set; }
    public float Single { get; set; }
    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public char Char { get; set; }
    public string String { get; set; } = "";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public Guid Guid { get; set; }
    public DateTime DateTime { get; set; }
    public DateTimeOffset DateTimeOffset { get; set; }
    public TimeSpan TimeSpan { get; set; }
    public SampleStatus Status { get; set; }
}

public sealed class NullableScalarsMessage
{
    public bool? Boolean { get; set; }
    public byte? Byte { get; set; }
    public sbyte? SByte { get; set; }
    public short? Int16 { get; set; }
    public ushort? UInt16 { get; set; }
    public int? Int32 { get; set; }
    public uint? UInt32 { get; set; }
    public long? Int64 { get; set; }
    public ulong? UInt64 { get; set; }
    public float? Single { get; set; }
    public double? Double { get; set; }
    public decimal? Decimal { get; set; }
    public char? Char { get; set; }
    public string? String { get; set; }
    public Guid? Guid { get; set; }
    public DateTime? DateTime { get; set; }
    public TimeSpan? TimeSpan { get; set; }
    public SampleStatus? Status { get; set; }
}

public sealed class CollectionsMessage
{
    public int[] IntArray { get; set; } = Array.Empty<int>();
    public List<int> IntList { get; set; } = new();
    public List<string> StringList { get; set; } = new();
    public List<SampleStatus> EnumList { get; set; } = new();
    public List<NestedItem> NestedList { get; set; } = new();
    public NestedItem[] NestedArray { get; set; } = Array.Empty<NestedItem>();
    public byte[] RawBytes { get; set; } = Array.Empty<byte>();
}

public sealed class NestedItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public SampleStatus Status { get; set; }
}

public sealed class NestedRootMessage
{
    public NestedItem Child { get; set; } = new();
    public NestedItem? OptionalChild { get; set; }
    public string Label { get; set; } = "";
}

public enum SampleStatus
{
    Unknown = 0,
    Active = 1,
    Inactive = 2,
    Pending = 3
}

public sealed class EchoResponse
{
    public string Ok { get; set; } = "ok";
}

public sealed class AllScalarsMessageConfig : IProtoTypeConfiguration<AllScalarsMessage>
{
    public void Configure(IProtoTypeBuilder<AllScalarsMessage> builder)
    {
        var n = 1;
        builder
            .Property(x => x.Boolean, n++).Property(x => x.Byte, n++).Property(x => x.SByte, n++)
            .Property(x => x.Int16, n++).Property(x => x.UInt16, n++).Property(x => x.Int32, n++)
            .Property(x => x.UInt32, n++).Property(x => x.Int64, n++).Property(x => x.UInt64, n++)
            .Property(x => x.NativeInt, n++).Property(x => x.NativeUInt, n++)
            .Property(x => x.Single, n++).Property(x => x.Double, n++).Property(x => x.Decimal, n++)
            .Property(x => x.Char, n++).Property(x => x.String, n++).Property(x => x.Bytes, n++)
            .Property(x => x.Guid, n++).Property(x => x.DateTime, n++).Property(x => x.DateTimeOffset, n++)
            .Property(x => x.TimeSpan, n++).Property(x => x.Status, n++);
    }
}

public sealed class NullableScalarsMessageConfig : IProtoTypeConfiguration<NullableScalarsMessage>
{
    public void Configure(IProtoTypeBuilder<NullableScalarsMessage> builder)
    {
        var n = 1;
        builder
            .Property(x => x.Boolean, n++).Property(x => x.Byte, n++).Property(x => x.SByte, n++)
            .Property(x => x.Int16, n++).Property(x => x.UInt16, n++).Property(x => x.Int32, n++)
            .Property(x => x.UInt32, n++).Property(x => x.Int64, n++).Property(x => x.UInt64, n++)
            .Property(x => x.Single, n++).Property(x => x.Double, n++).Property(x => x.Decimal, n++)
            .Property(x => x.Char, n++).Property(x => x.String, n++).Property(x => x.Guid, n++)
            .Property(x => x.DateTime, n++).Property(x => x.TimeSpan, n++).Property(x => x.Status, n++);
    }
}

public sealed class CollectionsMessageConfig : IProtoTypeConfiguration<CollectionsMessage>
{
    public void Configure(IProtoTypeBuilder<CollectionsMessage> builder)
    {
        builder
            .Property(x => x.IntArray, 1)
            .Property(x => x.IntList, 2)
            .Property(x => x.StringList, 3)
            .Property(x => x.EnumList, 4)
            .Property(x => x.NestedList, 5)
            .Property(x => x.NestedArray, 6)
            .Property(x => x.RawBytes, 7);
    }
}

public sealed class NestedItemConfig : IProtoTypeConfiguration<NestedItem>
{
    public void Configure(IProtoTypeBuilder<NestedItem> builder)
    {
        builder.Property(x => x.Id, 1).Property(x => x.Name, 2).Property(x => x.Amount, 3).Property(x => x.Status, 4);
    }
}

public sealed class NestedRootMessageConfig : IProtoTypeConfiguration<NestedRootMessage>
{
    public void Configure(IProtoTypeBuilder<NestedRootMessage> builder)
    {
        builder.Property(x => x.Child, 1).Property(x => x.OptionalChild, 2).Property(x => x.Label, 3);
    }
}

public sealed class EchoResponseConfig : IProtoTypeConfiguration<EchoResponse>
{
    public void Configure(IProtoTypeBuilder<EchoResponse> builder)
        => builder.Property(x => x.Ok, 1);
}

public sealed class AllScalarsHandler : IEndpoint
{
    public Task<EchoResponse> ExecuteAsync(AllScalarsMessage request, CancellationToken ct)
        => Task.FromResult(new EchoResponse { Ok = "scalars" });
}

public sealed class NullableScalarsHandler : IEndpoint
{
    public Task<EchoResponse> ExecuteAsync(NullableScalarsMessage request, CancellationToken ct)
        => Task.FromResult(new EchoResponse { Ok = "nullable" });
}

public sealed class CollectionsHandler : IEndpoint
{
    public Task<EchoResponse> ExecuteAsync(CollectionsMessage request, CancellationToken ct)
        => Task.FromResult(new EchoResponse { Ok = "collections" });
}

public sealed class NestedRootHandler : IEndpoint
{
    public Task<EchoResponse> ExecuteAsync(NestedRootMessage request, CancellationToken ct)
        => Task.FromResult(new EchoResponse { Ok = "nested" });
}
