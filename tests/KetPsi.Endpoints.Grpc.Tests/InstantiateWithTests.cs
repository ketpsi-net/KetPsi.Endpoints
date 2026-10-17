using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Abstractions;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>
/// Domain-style result with internal ctor + static factory (deserializer uses InstantiateWith).
/// </summary>
public sealed class SampleApiResult<T>
{
    public int StatusCode { get; }
    public T? Value { get; }
    public string? Error { get; }

    internal SampleApiResult(int statusCode, T? value, string? error)
    {
        StatusCode = statusCode;
        Value = value;
        Error = error;
    }

    public static SampleApiResult<T> Create(int statusCode, T? value, string? error)
        => new(statusCode, value, error);
}

public sealed class SampleApiResultConfig : IOpenProtoTypeConfiguration
{
    public void Configure(IOpenProtoTypeBuilder builder)
    {
        // Preferred: method group (Delegate) — same idea as Minimal APIs
        builder.ConfigureGeneric(typeof(SampleApiResult<>), config => config
            .Property(nameof(SampleApiResult<int>.StatusCode), 1)
            .Property(nameof(SampleApiResult<int>.Value), 2)
            .Property(nameof(SampleApiResult<int>.Error), 3)
            .InstantiateWith(SampleApiResult<int>.Create));
    }
}

public sealed class ClosedFactoryDto
{
    public int Id { get; }
    public string Name { get; }

    internal ClosedFactoryDto(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public static ClosedFactoryDto Create(int id, string name) => new(id, name);
}

public sealed class ClosedFactoryDtoConfig : IProtoTypeConfiguration<ClosedFactoryDto>
{
    public void Configure(IProtoTypeBuilder<ClosedFactoryDto> builder)
    {
        builder.Property(x => x.Id, 1)
               .Property(x => x.Name, 2)
               .InstantiateWith(ClosedFactoryDto.Create);
    }
}

public sealed class InstantiateWithTests
{
    [Fact]
    public void OpenProto_InstantiateWith_Delegate_IsFluent()
    {
        var called = false;
        new OpenProtoTypeBuilder().ConfigureGeneric(typeof(SampleApiResult<>), m =>
        {
            m.Property("StatusCode", 1)
             .Property("Value", 2)
             .Property("Error", 3)
             .InstantiateWith(SampleApiResult<string>.Create);
            called = true;
        });
        Assert.True(called);
    }

    [Fact]
    public void OpenProto_InstantiateWith_String_IsFluent()
    {
        var called = false;
        new OpenProtoTypeBuilder().ConfigureGeneric(typeof(SampleApiResult<>), m =>
        {
            m.InstantiateWith("Create");
            called = true;
        });
        Assert.True(called);
    }

    [Fact]
    public void Closed_InstantiateWith_Delegate_IsFluent()
    {
        var builder = new ProtoTypeBuilder<ClosedFactoryDto>();
        var result = builder
            .Property(x => x.Id, 1)
            .Property(x => x.Name, 2)
            .InstantiateWith(ClosedFactoryDto.Create);
        Assert.Same(builder, result);
    }

    [Fact]
    public void Closed_InstantiateWith_Nameof_IsFluent()
    {
        var builder = new ProtoTypeBuilder<ClosedFactoryDto>();
        Assert.Same(builder, builder.InstantiateWith(nameof(ClosedFactoryDto.Create)));
    }

    [Fact]
    public void Closed_InstantiateWith_DeclaringType_IsFluent()
    {
        var builder = new ProtoTypeBuilder<ClosedFactoryDto>();
        Assert.Same(builder, builder.InstantiateWith(typeof(ClosedFactoryDto), nameof(ClosedFactoryDto.Create)));
    }

    [Fact]
    public void SampleApiResult_Create_BuildsViaFactory()
    {
        var original = SampleApiResult<string>.Create(200, "ok", null);
        Assert.Equal(200, original.StatusCode);
        Assert.Equal("ok", original.Value);
        Assert.Null(original.Error);
    }
}
