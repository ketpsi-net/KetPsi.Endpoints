using System.Globalization;
using System.Net;

using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Net.Client;

using KetPsi.Endpoints.Grpc.Tests.Fixtures;
using KetPsi.Endpoints.Grpc.Tests.Scenarios;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>End-to-end behavior through the generated services (auth, interceptors, streaming, binding, errors, schema).</summary>
public sealed class ScenarioIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScenarioIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private GrpcChannel Channel() => GrpcTestChannel.Create(_factory);

    private static Method<TReq, TRes> M<TReq, TRes>(Method<TReq, TRes> m) => m;

    // ---------------------------------------------------------------- authorization conventions (item 1)

    [Fact]
    public async Task RequireAuthorization_AnonymousCall_IsUnauthenticated()
    {
        using var channel = Channel();
        var ex = await Assert.ThrowsAsync<RpcException>(() => channel.CreateCallInvoker()
            .AsyncUnaryCall(Secure_Vault_GrpcMethods.Read, null, default, new CountRequest()).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task RequireAuthorization_AuthenticatedCall_Succeeds()
    {
        using var channel = Channel();
        var headers = new Metadata { { "authorization", "Test ok" } };
        var res = await channel.CreateCallInvoker()
            .AsyncUnaryCall(Secure_Vault_GrpcMethods.Read, null, new CallOptions(headers), new CountRequest()).ResponseAsync;
        Assert.Equal(42, res.Value);
    }

    [Fact]
    public async Task SiblingMethodWithoutConvention_StaysAnonymous()
    {
        using var channel = Channel();
        var res = await channel.CreateCallInvoker()
            .AsyncUnaryCall(Secure_Vault_GrpcMethods.Ping, null, default, new CountRequest()).ResponseAsync;
        Assert.Equal(1, res.Value);
    }

    [Fact]
    public async Task MapGroupWithGroupName_ConventionAppliesPerMethod()
    {
        using var channel = Channel();
        var invoker = channel.CreateCallInvoker();
        Assert.Equal("grp.v1.Vault", grp_v1_Vault_GrpcMethods.Read.ServiceName);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(grp_v1_Vault_GrpcMethods.Read, null, default, new CountRequest()).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);

        // Same service, mapped through a group local without a convention -> anonymous
        var ping = await invoker.AsyncUnaryCall(grp_v1_Vault_GrpcMethods.Ping, null, default, new CountRequest()).ResponseAsync;
        Assert.Equal(1, ping.Value);
    }

    [Fact]
    public async Task GroupLevelConvention_OnReceiverChain_IsApplied()
    {
        using var channel = Channel();
        var ex = await Assert.ThrowsAsync<RpcException>(() => channel.CreateCallInvoker()
            .AsyncUnaryCall(locked_Vault_GrpcMethods.Ping, null, default, new CountRequest()).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    // ---------------------------------------------------------------- per-endpoint interceptors (item 20)

    private static async Task<string[]> InterceptorTrailers<TReq, TRes>(GrpcChannel channel, Method<TReq, TRes> method, TReq request)
        where TReq : class where TRes : class
    {
        var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, default, request);
        await call.ResponseAsync;
        return call.GetTrailers().Where(t => t.Key == "x-interceptor").Select(t => t.Value).ToArray();
    }

    [Fact]
    public async Task ConfiguredInterceptors_RunInOrder_OnlyForThatEndpoint()
    {
        using var channel = Channel();
        Assert.Equal(new[] { "A", "B" }, await InterceptorTrailers(channel, Inter_Calls_GrpcMethods.Configured, new CountRequest { Count = 3 }));
        Assert.Equal(new[] { "A" }, await InterceptorTrailers(channel, Inter_Calls_GrpcMethods.Inline, new CountRequest { Count = 3 }));
        Assert.Empty(await InterceptorTrailers(channel, Inter_Calls_GrpcMethods.Plain, new CountRequest { Count = 3 }));
    }

    [Fact]
    public async Task ExplicitConfigType_InterceptorRunsOnServerStream()
    {
        using var channel = Channel();
        using var call = channel.CreateCallInvoker().AsyncServerStreamingCall(Inter_Calls_GrpcMethods.Stream, null, default, new CountRequest { Count = 9 });
        var values = new List<int>();
        await foreach (var item in call.ResponseStream.ReadAllAsync())
            values.Add(item.Value);
        Assert.Equal(new[] { 9 }, values);
        Assert.Equal(new[] { "B" }, call.GetTrailers().Where(t => t.Key == "x-interceptor").Select(t => t.Value).ToArray());
    }

    // ---------------------------------------------------------------- streaming + Empty (item 22)

    [Fact]
    public async Task ServerStreaming_YieldsAllItems()
    {
        using var channel = Channel();
        Assert.Equal(MethodType.ServerStreaming, Stream_Numbers_GrpcMethods.Countdown.Type);
        using var call = channel.CreateCallInvoker().AsyncServerStreamingCall(Stream_Numbers_GrpcMethods.Countdown, null, default, new CountRequest { Count = 3 });
        var values = new List<int>();
        await foreach (var item in call.ResponseStream.ReadAllAsync())
            values.Add(item.Value);
        Assert.Equal(new[] { 3, 2, 1 }, values);
    }

    [Fact]
    public async Task ClientStreaming_AggregatesRequests()
    {
        using var channel = Channel();
        Assert.Equal(MethodType.ClientStreaming, Stream_Numbers_GrpcMethods.Sum.Type);
        using var call = channel.CreateCallInvoker().AsyncClientStreamingCall(Stream_Numbers_GrpcMethods.Sum, null, default);
        foreach (var v in new[] { 1, 2, 3, 4 })
            await call.RequestStream.WriteAsync(new NumberMessage { Value = v });
        await call.RequestStream.CompleteAsync();
        var res = await call.ResponseAsync;
        Assert.Equal(10, res.Total);
        Assert.Equal(4, res.Items);
    }

    [Fact]
    public async Task DuplexStreaming_EchoesTransformedItems()
    {
        using var channel = Channel();
        Assert.Equal(MethodType.DuplexStreaming, Stream_Numbers_GrpcMethods.Double.Type);
        using var call = channel.CreateCallInvoker().AsyncDuplexStreamingCall(Stream_Numbers_GrpcMethods.Double, null, default);
        foreach (var v in new[] { 5, 6 })
            await call.RequestStream.WriteAsync(new NumberMessage { Value = v });
        await call.RequestStream.CompleteAsync();
        var values = new List<int>();
        await foreach (var item in call.ResponseStream.ReadAllAsync())
            values.Add(item.Value);
        Assert.Equal(new[] { 10, 12 }, values);
    }

    [Fact]
    public async Task RequestlessHandler_UsesEmpty()
    {
        using var channel = Channel();
        var res = await channel.CreateCallInvoker().AsyncUnaryCall(Stream_Numbers_GrpcMethods.Seven, null, default, GrpcEmpty.Instance).ResponseAsync;
        Assert.Equal(7, res.Value);
    }

    [Fact]
    public async Task VoidHandler_ReturnsEmpty()
    {
        using var channel = Channel();
        var before = FireHandler.Total;
        var res = await channel.CreateCallInvoker().AsyncUnaryCall(Stream_Numbers_GrpcMethods.Fire, null, default, new NumberMessage { Value = 5 }).ResponseAsync;
        Assert.Same(GrpcEmpty.Instance, res);
        Assert.True(FireHandler.Total >= before + 5);
    }

    // ---------------------------------------------------------------- metadata binding (item 19)

    [Fact]
    public async Task FromMetadata_BindsTypedValues_WithInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // ',' decimal separator, '.' thousands
        try
        {
            using var channel = Channel();
            var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
            var headers = new Metadata
            {
                { "x-tenant", "acme" },
                { "x-count", "-12" },
                { "x-ratio", "1.5" },
                { "x-status", "pending" },
                { "CorrelationId", id.ToString() },
            };
            var res = await channel.CreateCallInvoker().AsyncUnaryCall(Meta_Tenant_GrpcMethods.Echo, null, new CallOptions(headers),
                new TenantRequest { Body = "hello", Tenant = "ignored-on-wire" }).ResponseAsync;
            Assert.Equal($"hello|acme|-12|1.5|Pending|{id}", res.Summary);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task FromMetadata_InvalidOrMissingValues_LeaveDefaults()
    {
        using var channel = Channel();
        var headers = new Metadata { { "x-count", "not-a-number" }, { "x-status", "nope" } };
        var res = await channel.CreateCallInvoker().AsyncUnaryCall(Meta_Tenant_GrpcMethods.Echo, null, new CallOptions(headers),
            new TenantRequest { Body = "b" }).ResponseAsync;
        Assert.Equal("b||0|0|Unknown|", res.Summary);
    }

    [Fact]
    public void FromMetadata_BoundProperties_AreNotOnTheWire()
    {
        var bytes = GeneratedProtoCodecs.Serialize_TenantRequest(new TenantRequest { Body = "x", Tenant = "t", Count = 5 });
        var copy = GeneratedProtoCodecs.Deserialize_TenantRequest(bytes);
        Assert.Equal("x", copy.Body);
        Assert.Null(copy.Tenant);
        Assert.Equal(0, copy.Count);
    }

    // ---------------------------------------------------------------- malformed input (items 2, 11)

    private static readonly Marshaller<byte[]> Raw = Marshallers.Create(b => b, b => b);

    private async Task<StatusCode> SendRaw(string service, string method, byte[] payload)
    {
        using var channel = Channel();
        var m = new Method<byte[], byte[]>(MethodType.Unary, service, method, Raw, Raw);
        var ex = await Assert.ThrowsAsync<RpcException>(() => channel.CreateCallInvoker().AsyncUnaryCall(m, null, default, payload).ResponseAsync);
        return ex.StatusCode;
    }

    [Fact]
    public async Task MalformedPayload_IsInvalidArgument()
    {
        // field 1 varint, value truncated
        Assert.Equal(StatusCode.InvalidArgument, await SendRaw("Api.Users", "GetById", new byte[] { 0x08, 0x80 }));
        // field number 0
        Assert.Equal(StatusCode.InvalidArgument, await SendRaw("Api.Users", "GetById", new byte[] { 0x00, 0x01 }));
        // huge length prefix
        Assert.Equal(StatusCode.InvalidArgument, await SendRaw("Api.Users", "GetById",
            new byte[] { 0x12, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }));
    }

    [Fact]
    public async Task DeeplyNestedPayload_IsRejectedNotStackOverflow()
    {
        Assert.Equal(StatusCode.InvalidArgument, await SendRaw("Edge.Tree", "Echo", CodecEdgeCaseTests.NestedTree(10_000)));
    }

    // ---------------------------------------------------------------- registration / schema (items 3, 8)

    [Fact]
    public void AddGrpcServices_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        services.AddGrpcServices();
        var count = services.Count;
        services.AddGrpcServices();
        Assert.Equal(count, services.Count);
        Assert.Single(services, d => d.ServiceType == typeof(IServiceMethodProvider<Api_Users_GrpcService>));
        Assert.Single(services, d => d.ServiceType == typeof(GetUserByIdHandler));
    }

    [Fact]
    public void MapGrpcEndpoints_CalledTwice_MapsEachMethodOnce()
    {
        var sources = _factory.Services.GetServices<EndpointDataSource>();
        var grpcEndpoints = sources.SelectMany(s => s.Endpoints)
            .Where(e => e.Metadata.GetMetadata<global::Grpc.AspNetCore.Server.GrpcMethodMetadata>() is { } m && m.Method.ServiceName == "Api.Users" && m.Method.Name == "GetById")
            .ToList();
        Assert.Single(grpcEndpoints);
    }

    [Fact]
    public void GeneratedRegistration_IsRegisteredOnceInHub()
    {
        Assert.Single(GrpcGeneratedRegistrationHub.RegisteredAssemblies, a => a == "KetPsi.Endpoints.Grpc.Tests");
    }

    [Fact]
    public async Task ProtoEndpoint_ServesOneFilePerPackage_MatchingServerRoutes()
    {
        var http = _factory.CreateClient();
        var bundle = await http.GetStringAsync("/proto");
        var files = ProtoText.SplitBundle(bundle);
        Assert.Contains("Api.proto", files.Keys);
        Assert.Contains("Codec.proto", files.Keys);
        Assert.Contains("grp.v1.proto", files.Keys);
        Assert.Contains("Stream.proto", files.Keys);

        foreach (var (name, content) in files)
        {
            var parsed = ProtoText.Parse(content);
            Assert.Equal(name, parsed.Package + ".proto");
            // Every referenced message type is defined in the same file (self-contained files)
            foreach (var t in parsed.ReferencedTypes)
                Assert.True(parsed.DefinedTypes.Contains(t) || t.StartsWith("google.protobuf.", StringComparison.Ordinal), $"{name}: undefined type {t}");
            // Every enum starts with a zero value
            foreach (var first in parsed.FirstEnumValues)
                Assert.Equal("0", first);
        }

        // Service paths in the schema equal the generated server routes
        Assert.Equal("Api.Users", Api_Users_GrpcMethods.GetById.ServiceName);
        Assert.Contains("service Users", files["Api.proto"]);
        Assert.Contains("rpc Countdown (CountRequest) returns (stream NumberMessage);", files["Stream.proto"]);
        Assert.Contains("rpc Sum (stream NumberMessage) returns (SumResponse);", files["Stream.proto"]);
        Assert.Contains("rpc Double (stream NumberMessage) returns (stream NumberMessage);", files["Stream.proto"]);
        Assert.Contains("rpc Seven (google.protobuf.Empty) returns (NumberMessage);", files["Stream.proto"]);
        Assert.Contains("import \"google/protobuf/empty.proto\";", files["Stream.proto"]);
        Assert.DoesNotContain("import \"google/protobuf/empty.proto\";", files["Api.proto"]);

        var single = await http.GetAsync("/proto/Api.proto");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        Assert.Equal(files["Api.proto"], await single.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/proto/Nope.proto")).StatusCode);
    }

    [Fact]
    public async Task Schema_EnumsAndSameNameTypes_AreUniqueAndPrefixed()
    {
        var http = _factory.CreateClient();
        var edge = await http.GetStringAsync("/proto/Edge.proto");
        Assert.Contains("PRIORITY_UNSPECIFIED = 0;", edge);
        Assert.Contains("DELTA_ZERO = 0;", edge);
        Assert.Contains("DELTA_MINUS = -1;", edge);
        Assert.Contains("message Alpha_Req", edge);
        Assert.Contains("message Beta_Req", edge);
        Assert.Contains("message Wrapper_List_Int32", edge);
        Assert.Contains("message Wrapper_List_String", edge);
        Assert.Contains("message Outer1_Inner", edge);
        Assert.Contains("message Outer2_Inner", edge);
        Assert.Contains("google.protobuf.Timestamp Date = 1;", edge);
        Assert.Contains("google.protobuf.Duration Time = 2;", edge);
    }

    [Fact]
    public async Task Schema_MatchesHandWrittenInteropProto()
    {
        var http = _factory.CreateClient();
        var edge = ProtoText.Parse(await http.GetStringAsync("/proto/Edge.proto"));
        var interopPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Interop", "interop.proto");
        var interop = ProtoText.Parse(await File.ReadAllTextAsync(interopPath));
        foreach (var (name, body) in interop.Blocks)
            Assert.Equal(body, edge.Blocks[name]);
    }
}

internal static class GrpcTestChannel
{
    static GrpcTestChannel() => AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    public static GrpcChannel Create<T>(WebApplicationFactory<T> factory) where T : class
    {
        var handler = factory.Server.CreateHandler();
        return GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = handler });
    }
}
