using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Tests;
using KetPsi.Endpoints.Grpc.Tests.Fixtures;
using KetPsi.Endpoints.Grpc.Tests.Scenarios;

using Microsoft.AspNetCore.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var arguments = args.Where(x => !x.StartsWith("--contentRoot")).ToList();
arguments.Add($"--contentRoot={AppContext.BaseDirectory}");
var builder = WebApplication.CreateBuilder(arguments.ToArray());

builder.AddGrpc();
builder.Services.AddGrpcServices();
builder.Services.AddGrpcServices(); // idempotent
builder.Services.AddProtoEndpoint();

builder.Services.AddScoped<GetUserByIdHandler>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<AllScalarsHandler>();
builder.Services.AddScoped<NullableScalarsHandler>();
builder.Services.AddScoped<CollectionsHandler>();
builder.Services.AddScoped<NestedRootHandler>();
builder.Services.AddScoped<PrimaryCtorOrderHandler>();
builder.Services.AddScoped<PrivateSetterOrderHandler>();
builder.Services.AddScoped<InitOnlyOrderHandler>();
builder.Services.AddScoped<MapOnlyHandler>();
builder.Services.AddScoped<EmptyRequestHandler>();

builder.Services.AddAuthentication(TestAuthHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, null);
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpc<GetUserByIdHandler>("Api/Users/GetById");
app.MapGrpc<CreateOrderHandler>("Orders/Create");
app.MapGrpc<AllScalarsHandler>("Codec/Scalars/Echo");
app.MapGrpc<NullableScalarsHandler>("Codec/Nullable/Echo");
app.MapGrpc<CollectionsHandler>("Codec/Collections/Echo");
app.MapGrpc<NestedRootHandler>("Codec/Nested/Echo");
app.MapGrpc<PrimaryCtorOrderHandler>("Codec/PrimaryCtor/Echo");
app.MapGrpc<PrivateSetterOrderHandler>("Codec/PrivateSet/Echo");
app.MapGrpc<InitOnlyOrderHandler>("Codec/InitOnly/Echo");
app.MapGrpc<MapOnlyHandler>("Codec/Map/Echo");
app.MapGrpc<EmptyRequestHandler>("Codec/Empty/Ping");

// Codec edge cases
app.MapGrpc<Echo<EdgeMessage>>("Edge/Kitchen/Echo");
app.MapGrpc<Echo<TemporalMessage>>("Edge/Temporal/Echo");
app.MapGrpc<Echo<EnumMessage>>("Edge/Enums/Echo");
app.MapGrpc<Echo<TreeNode>>("Edge/Tree/Echo");
app.MapGrpc<Echo<DerivedDto>>("Edge/Inherit/Echo");
app.MapGrpc<Echo<CtorInitRecord>>("Edge/CtorInit/Echo");
app.MapGrpc<Echo<SameNameRequest>>("Edge/SameName/Echo");
app.MapGrpc<Echo<StructHolder>>("Edge/StructHolder/Echo");

// Streaming + Empty
app.MapGrpcServerStream<CountdownHandler>("Stream/Numbers/Countdown");
app.MapGrpcClientStream<SumHandler>("Stream/Numbers/Sum");
app.MapGrpcDuplexStream<DoubleHandler>("Stream/Numbers/Double");
app.MapGrpc<SevenHandler>("Stream/Numbers/Seven");
app.MapGrpc<FireHandler>("Stream/Numbers/Fire");

// Authorization conventions are per RPC
app.MapGrpc<VaultReadHandler>("Secure/Vault/Read").RequireAuthorization();
app.MapGrpc<VaultPingHandler>("Secure/Vault/Ping");
app.MapGroup("grp").WithGroupName("v1").MapGrpc<VaultReadHandler>("Vault/Read").RequireAuthorization();
var group = app.MapGroup("grp").WithGroupName("v1");
group.MapGrpc<VaultPingHandler>("Vault/Ping");
var secureGroup = app.MapGroup("locked").RequireAuthorization();
secureGroup.MapGrpc<VaultPingHandler>("Vault/Ping");

// Per-endpoint interceptors
app.MapGrpc<InterceptedHandler>("Inter/Calls/Configured");
app.MapGrpc<InlineInterceptedHandler>("Inter/Calls/Inline", b => b.AddInterceptor<TagInterceptorA>());
app.MapGrpc<PlainHandler>("Inter/Calls/Plain");
app.MapGrpc<StreamWithConfigHandler, ExplicitConfig>("Inter/Calls/Stream");

// Metadata binding
app.MapGrpc<TenantEchoHandler>("Meta/Tenant/Echo");

app.MapGrpcEndpoints();
app.MapGrpcEndpoints(); // idempotent
app.UseProtoEndpoint();
app.UseProtoEndpoint(); // idempotent

app.Run();

public partial class Program;
