using System.Net;

using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Tests.Fixtures;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>
/// Asserts /proto schema type resolution matches Microsoft gRPC protobuf mapping rules.
/// </summary>
public class SchemaTypeResolutionTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.AddGrpc();
        builder.Services.AddGrpcServices();
        builder.Services.AddProtoEndpoint();

        _app = builder.Build();
        _app.MapGrpc<AllScalarsHandler>("Codec/Scalars/Echo");
        _app.MapGrpc<NullableScalarsHandler>("Codec/Nullable/Echo");
        _app.MapGrpc<CollectionsHandler>("Codec/Collections/Echo");
        _app.MapGrpc<NestedRootHandler>("Codec/Nested/Echo");
        _app.MapGrpcEndpoints();
        _app.UseProtoEndpoint();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private async Task<string> GetProtoAsync()
    {
        var response = await _client!.GetAsync("/proto/Codec.proto");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("syntax = \"proto3\";", text);
        Assert.Contains("package Codec;", text);
        return text;
    }

    [Fact]
    public async Task Schema_ContainsSyntaxAndMessages()
    {
        var proto = await GetProtoAsync();
        Assert.Contains("message AllScalarsMessage", proto);
        Assert.Contains("message NullableScalarsMessage", proto);
        Assert.Contains("message CollectionsMessage", proto);
        Assert.Contains("message NestedRootMessage", proto);
        Assert.Contains("message NestedItem", proto);
        Assert.Contains("message EchoResponse", proto);
    }

    [Fact]
    public async Task Schema_Scalars_MatchMicrosoftMapping()
    {
        var proto = await GetProtoAsync();

        // bool, string, floats
        Assert.Contains("bool Boolean", proto);
        Assert.Contains("string String", proto);
        Assert.Contains("float Single", proto);
        Assert.Contains("double Double", proto);

        // integral (narrow types widen on wire)
        Assert.Contains("uint32 Byte", proto);
        Assert.Contains("int32 SByte", proto);
        Assert.Contains("int32 Int16", proto);
        Assert.Contains("uint32 UInt16", proto);
        Assert.Contains("int32 Int32", proto);
        Assert.Contains("uint32 UInt32", proto);
        Assert.Contains("int64 Int64", proto);
        Assert.Contains("uint64 UInt64", proto);

        // DateTime → Timestamp, TimeSpan → Duration (MS docs)
        Assert.Contains("google.protobuf.Timestamp DateTime", proto);
        Assert.Contains("google.protobuf.Duration TimeSpan", proto);
        Assert.Contains("import \"google/protobuf/timestamp.proto\"", proto);
        Assert.Contains("import \"google/protobuf/duration.proto\"", proto);

        // decimal → lossless invariant string; Guid → canonical "D" string; char → uint32 code unit
        Assert.Contains("string Decimal = ", proto);
        Assert.DoesNotContain("DecimalValue", proto);
        Assert.Contains("string Guid = ", proto);
        Assert.Contains("uint32 Char = ", proto);
        Assert.DoesNotContain("wrappers.proto", proto);

        // bytes
        Assert.Contains("bytes Bytes", proto);
    }

    [Fact]
    public async Task Schema_Enum_IsNamedEnum()
    {
        var proto = await GetProtoAsync();
        Assert.Contains("enum SampleStatus", proto);
        Assert.Contains("SAMPLE_STATUS_UNKNOWN = 0;", proto);
        Assert.Contains("SAMPLE_STATUS_ACTIVE = 1;", proto);
        Assert.Contains("SampleStatus Status", proto);
        // should not map enum field as bare int32 type name only without enum
        Assert.DoesNotContain("int32 Status", proto);
    }

    [Fact]
    public async Task Schema_Collections_UseRepeated()
    {
        var proto = await GetProtoAsync();
        Assert.Contains("repeated int32 IntArray", proto);
        Assert.Contains("repeated int32 IntList", proto);
        Assert.Contains("repeated string StringList", proto);
        Assert.Contains("repeated SampleStatus EnumList", proto);
        Assert.Contains("repeated NestedItem NestedList", proto);
        Assert.Contains("repeated NestedItem NestedArray", proto);
        Assert.Contains("bytes RawBytes", proto);
    }

    [Fact]
    public async Task Schema_NestedMessage_IsDefinedAndReferenced()
    {
        var proto = await GetProtoAsync();
        Assert.Contains("message NestedItem", proto);
        Assert.Contains("NestedItem Child", proto);
        Assert.Contains("NestedItem OptionalChild", proto);
        Assert.Contains("int32 Id", proto);
        Assert.Contains("string Amount", proto);
    }

    [Fact]
    public async Task Schema_NullableScalars_UseProto3Optional()
    {
        var proto = await GetProtoAsync();
        Assert.Contains("optional int32 Int32", proto);
        Assert.Contains("optional string String", proto);
        Assert.Contains("optional SampleStatus Status", proto);
        Assert.DoesNotContain("google.protobuf.Int32Value", proto);
    }

    [Fact]
    public async Task Schema_Services_AreEmitted()
    {
        var proto = await GetProtoAsync();
        Assert.Contains("service ", proto);
        Assert.Contains("rpc ", proto);
        Assert.Contains("AllScalarsMessage", proto);
        Assert.Contains("EchoResponse", proto);
    }

    [Fact]
    public async Task Schema_EmptyRequestMessage_IsDeclared()
    {
        var proto = await GetProtoAsync();
        // Empty DTOs must still emit "message EmptyRequestContract { }" so RPC references resolve
        Assert.Contains("message EmptyRequestContract", proto, StringComparison.Ordinal);
        Assert.Contains("EmptyRequestContract", proto, StringComparison.Ordinal);
        Assert.Contains("EmptyResponseContract", proto, StringComparison.Ordinal);
    }

}
