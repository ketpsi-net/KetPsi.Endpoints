---
name: ketpsi-endpoints-grpc
description: How to build code-first gRPC services with KetPsi.Endpoints.Grpc (source-generated protobuf codecs, proto3 schema and Grpc.AspNetCore bindings from IEndpoint handler classes). Use when adding or mapping MapGrpc<T> handlers, streaming RPCs, field numbers (IProtoTypeConfiguration), metadata binding, per-RPC interceptors/authorization, the /proto endpoint, protobuf interop with non-.NET clients, or KPG0xx/DG001 diagnostics.
---

# KetPsi.Endpoints.Grpc

A handler is a plain class implementing `KetPsi.Endpoints.Abstractions.IEndpoint` with one `ExecuteAsync`. `app.MapGrpc<THandler>("Service/Method")` is an **inert compile-time marker**: it maps nothing at runtime, never runs `configure`, and doesn't allocate. A source generator reads it and emits:
- protobuf codecs, with no protobuf-net and no reflection;
- a proto3 schema;
- `IServiceMethodProvider` bindings for Grpc.AspNetCore;
- DI registration.

Clients in any language use the generated `.proto`.

## Setup

```bash
dotnet add package KetPsi.Endpoints.Grpc               # runtime + generator + DG001 analyzer
dotnet add package KetPsi.Endpoints.Grpc.Abstractions  # optional: proto type configuration for non-web layers
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpcServices();      // idempotent; calls AddGrpc(); registers handlers from every MapGrpc assembly
var app = builder.Build();

app.MapGrpc<GetUserByIdHandler>("Users/GetById");
app.MapGrpcEndpoints();                  // generated mapping, idempotent per app
app.UseProtoEndpoint();                  // optional: GET /proto and /proto/{package}.proto
app.Run();
```

`MapGrpc` calls inside referenced libraries are registered too; each assembly gets `KetPsiGrpcRegistration.Ensure()`, called explicitly.

## Handler shapes

| Signature | RPC kind | Map with |
|---|---|---|
| `Task<TRes>` / `ValueTask<TRes>` / `TRes ExecuteAsync(TReq, CancellationToken)` | unary | `MapGrpc` / `MapGrpcUnary` |
| `IAsyncEnumerable<TRes> ExecuteAsync(TReq, CancellationToken)` | server streaming | `MapGrpc` / `MapGrpcServerStream` |
| `Task<TRes> ExecuteAsync(IAsyncEnumerable<TReq>, CancellationToken)` | client streaming | `MapGrpc` / `MapGrpcClientStream` |
| `IAsyncEnumerable<TRes> ExecuteAsync(IAsyncEnumerable<TReq>, CancellationToken)` | duplex | `MapGrpc` / `MapGrpcDuplexStream` |
| no request parameter | — | request is `google.protobuf.Empty` (`GrpcEmpty`) |
| `Task` / `ValueTask` / `void` | unary | response is `google.protobuf.Empty` |

- **Inferred kind:** `MapGrpc` works out the kind from the handler signature. Using an explicit variant that doesn't match the handler is **KPG005**.
- **Classes only:** request and response types must be classes or records (**KPG023**). Structs are only allowed as nested messages.
- **Services:** inject through the constructor. `CancellationToken` is optional.

```csharp
public sealed class GetUserByIdHandler(IUserStore store) : IEndpoint
{
    public async Task<UserReply> ExecuteAsync(GetUserRequest request, CancellationToken ct)
        => await store.Get(request.Id, ct);
}
```

## Routing

The route is `[package segments/]Service/Method`. The server path is `/{package}.{Service}/{Method}`.

| Call | package | service | rpc |
|---|---|---|---|
| `MapGrpc<T>("Users/GetById")` | `Api` (default) | `Users` | `GetById` |
| `MapGrpc<T>("billing/Orders/Create")` | `billing` | `Orders` | `Create` |
| `MapGroup("/api/tests").MapGrpc<T>("Users/GetById")` | `api.tests` | `Users` | `GetById` |
| `MapGroup("api").WithGroupName("v1").MapGrpc<T>("Users/Get")` | `api.v1` | `Users` | `Get` |
| `MapGroup("Users").MapGrpc<T>("GetById")` | `Api` | `Users` | `GetById` |

- **Identifiers:** every segment must be a protobuf identifier `[A-Za-z_][A-Za-z0-9_]*`. `{param}` segments are ignored. Analyzer **DG001** checks this.
- **Versions:** come only from `WithGroupName`.
- **Constant routes:** the route must be a compile-time constant (**KPG001**), and `configure` must be an inline lambda (**KPG008**).
- **Duplicates:** mapping the same RPC to a different handler is **KPG003**. Identical duplicates are merged.

## Authorization and conventions are per RPC

```csharp
app.MapGrpc<ReadSecretHandler>("Vault/Read").RequireAuthorization("Admins"); // only Vault/Read is protected
app.MapGrpc<PingHandler>("Vault/Ping");                                       // stays anonymous
app.MapGroup("api").RequireAuthorization().MapGrpc<OrdersHandler>("Orders/Get");
```

- **Chain directly on the call:** conventions chained on the call, or on its `MapGroup` chain, are re-emitted by the generator and applied to that single method endpoint.
- **Arguments:** must be constants, static members or self-contained lambdas. A reference to a local or instance member is error **KPG006**, so a security convention is never silently dropped.
- **Not captured:**
  - `var ep = app.MapGrpc…; ep.RequireAuthorization();` is not captured (**KPG007**). Always chain.
  - Conventions added later to a group variable are not captured either.
- **Pipeline:** needs `UseAuthentication()` + `UseAuthorization()`. Anonymous calls fail with `StatusCode.Unauthenticated`.
- **Bearer tokens:** never model them as message fields. Read the user via `IHttpContextAccessor` or `context.GetHttpContext().User`.

## Field numbers: the wire contract

Field numbers are resolved in this order:
1. `IProtoTypeConfiguration<T>` (in `KetPsi.Endpoints.Grpc.Abstractions`, with no ASP.NET dependency).
2. `[ProtoMember(n)]`.
3. Declaration order, base-class properties first (**KPG015** info).

**Pin numbers for any type that will evolve.** With declaration order, reordering or inserting a property silently breaks deployed clients.

```csharp
public sealed class UserReplyProto : IProtoTypeConfiguration<UserReply>
{
    public void Configure(IProtoTypeBuilder<UserReply> b)
    {
        b.Property(x => x.Id, 1)
         .Property(x => x.Name, 2)
         .Property(x => x.Email, 3);
    }
}
```

- **Unnumbered properties:** once 1 or 2 is used for a type, unnumbered properties are **not serialized** (**KPG011**).
- **Number errors:** duplicate (**KPG012**), invalid or reserved (19000–19999) (**KPG013**), and non-constant (**KPG014**) numbers are reported.
- **Construction:** `InstantiateWith(...)` selects a factory. Otherwise the best constructor is used, plus `set`/`init` accessors for the remaining properties. **KPG016** means the type can't be created; **KPG017** means a property can't be set.
- **Generic types:** `IOpenProtoTypeConfiguration` configures open generics (e.g. `ApiResult<>`) for every closed instantiation.

## Type mapping (what non-.NET clients see)

| CLR | proto3 |
|---|---|
| integral / `float` / `double` / `bool` / `string` / `byte[]` | `int32`/`int64`/`uint32`/`uint64`/`float`/`double`/`bool`/`string`/`bytes` |
| `char` | `uint32` |
| enum | `enum`, values `ENUM_NAME_MEMBER`; a `ENUM_NAME_UNSPECIFIED = 0` is added when there's no zero; values must fit int32 (KPG019) |
| `Guid` / `decimal` | `string` ("D" format / lossless invariant) |
| `DateTime`, `DateTimeOffset`, `DateOnly` | `google.protobuf.Timestamp` (UTC; DateOnly = midnight) |
| `TimeSpan`, `TimeOnly` | `google.protobuf.Duration` |
| arrays, lists, sets, immutable collections | `repeated` (scalars are packed; both forms are read) |
| `Dictionary<K,V>` and its interfaces | `map<K,V>` (keys: integral, bool, string, char, Guid, decimal; otherwise KPG018) |
| class / record / struct | `message` (inherited properties included) |

- **Presence:** non-nullable scalars use implicit presence, so defaults aren't written. `T?` and `string?` become `optional`. An absent nested message reads as `null`.
- **Unsupported types:** `object`, nested collections and `List<int?>` are **KPG010**.
- **Unknown fields:** skipped, not preserved.
- **Name collisions:** types with the same simple name get disambiguated message names (`Ns_Type`, `Outer_Inner`, `Wrapper_List_Int32`).

## Metadata binding and interceptors

Configure both per handler with `IGrpcEndpointConfiguration<THandler>`, inline on `MapGrpc<T>(route, b => …)`, or with `MapGrpc<THandler, TConfig>(route)`:

```csharp
public sealed class CreateOrderConfig : IGrpcEndpointConfiguration<CreateOrderHandler>
{
    public void Configure(IGrpcEndpointBuilder<CreateOrderHandler> builder)
    {
        builder.Request<CreateOrderRequest>(r => r.Property(x => x.TenantId, p => p.FromMetadata("x-tenant-id")));
        builder.AddInterceptor<AuditInterceptor>();          // runs only for this handler's RPCs, in order
    }
}
```

- **Metadata-bound properties:** these are removed from the message and the schema.
  - They are parsed with the invariant culture.
  - A value that can't be parsed leaves the property unchanged.
  - They need a non-`init` setter (**KPG020**).
  - Metadata binding applies to unary and server-streaming RPCs only.
- **Interceptors:** per-endpoint interceptors are registered with `TryAddScoped`. For global interceptors use `services.AddGrpcInterceptor<T>()`.

## Robustness, AOT and performance

- **Malformed input:** returns `RpcException(StatusCode.InvalidArgument)`, with `ProtoWireException` inside. Nesting is limited to 100 levels (`ProtoWireLimits.MaxDepth`), so cyclic graphs fail instead of overflowing the stack.
- **AOT and trimming:** the runtime is trim- and Native AOT-safe for net8.0, net9.0 and net10.0.
- **Allocations:**
  - Messages are size-first encoded straight into the transport buffer, with no intermediate buffers.
  - Multi-segment requests are read through a pooled buffer.
  - RPCs without interceptors call the handler directly.
  - The remaining per-call costs are DI resolution of the handler and `AsTask()` for `ValueTask` handlers.

## Diagnostics

| Id | Severity | Meaning |
|---|---|---|
| DG001 | Error | Invalid route (segment count or identifiers, MapGroup-aware) |
| KPG001 | Warning | Route is not constant, so it's not mapped |
| KPG003 | Error | Same RPC mapped to a different handler or signature |
| KPG004 | Error | Unsupported handler signature |
| KPG005 | Error | Handler shape doesn't match the `MapGrpc*` variant |
| KPG006 | Error | Chained convention references local state |
| KPG007 | Warning | `MapGrpc` result stored; later conventions are ignored |
| KPG008 | Warning | `configure` is not an inline lambda |
| KPG010 | Error | Unsupported property type |
| KPG011 | Warning | Property has no field number, so it's not serialized |
| KPG012 / KPG013 / KPG014 | Error / Error / Warning | Duplicate / invalid / non-constant field number |
| KPG015 | Info | Declaration-order numbering in use |
| KPG016 / KPG017 | Error / Warning | Type can't be constructed / property can't be set |
| KPG018 | Error | Invalid map key type |
| KPG019 | Error | Enum value outside int32 |
| KPG020 | Error | Invalid `FromMetadata` binding |
| KPG022 | Warning | Configured property not found |
| KPG023 | Error | Type can't be a message (e.g. a struct request) |

## Debugging checklist

- **UNIMPLEMENTED from a client:** compare the client's package/service/method with `/proto`. Check that `MapGrpcEndpoints()` is called and that there's no KPG001.
- **A field arrives empty in another language:** check field numbers and types in `/proto`. For Guid and decimal, remember they are strings.
- **Generated code:** set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and look under `obj/**/generated/KetPsi.Endpoints.Grpc.Generator/`.
- **Tests:** use `WebApplicationFactory<Program>` with a `GrpcChannel` on `factory.Server.CreateHandler()`. For interop, compile the `/proto` output with Grpc.Tools and round-trip messages through Google.Protobuf.
