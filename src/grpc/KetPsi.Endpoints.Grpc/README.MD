# KetPsi.Endpoints.Grpc

Code-first **gRPC** endpoints for the KetPsi stack. Define a handler with `ExecuteAsync` once; a Roslyn incremental generator emits:

- Compile-time **proto3** schema (`GET /proto`)
- Pure wire codecs (no protobuf-net runtime model)
- `IServiceMethodProvider` binding for Grpc.AspNetCore
- DI registration (`AddGrpcServices`) and `MapGrpcEndpoints`

Cross-language clients use the standard `.proto` — not .NET-specific proxies.

Same handler marker (`IEndpoint`) as [KetPsi.Endpoints.Http](https://www.nuget.org/packages/KetPsi.Endpoints.Http).

---

## Install

```bash
dotnet add package KetPsi.Endpoints.Grpc
```

Configure types in the Application layer without ASP.NET references:

```bash
dotnet add package KetPsi.Endpoints.Grpc.Abstractions
```

---

## Quick start

### 1. Handler

```csharp
public sealed class GetUserByIdHandler : IEndpoint
{
    public Task<GetUserResponse> ExecuteAsync(GetUserRequest request, CancellationToken ct)
        => Task.FromResult(new GetUserResponse { Id = request.Id, Name = "Ada" });
}
```

### 2. Map and register

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddGrpc();
builder.Services.AddGrpcServices();   // aggregates all MapGrpc assemblies via hub
builder.Services.AddProtoEndpoint();  // optional options hook

var app = builder.Build();

app.MapGrpc<GetUserByIdHandler>("Users/GetById");
app.MapGrpcEndpoints();               // generated
app.UseProtoEndpoint();               // optional GET /proto


app.Run();
```

Variants: `MapGrpc` (method kind inferred from the handler signature), `MapGrpcUnary`, `MapGrpcClientStream`, `MapGrpcServerStream`, `MapGrpcDuplexStream`, `MapGrpc<THandler, TConfig>`.

The `MapGrpc*` methods are **inert markers**: they map nothing at runtime, never invoke `configure`, keep no state and do not allocate. The generator reads the call site at compile time, so the route must be a compile-time constant (KPG001) and `configure` must be an inline lambda (KPG008).

---

## Handler shapes and streaming

| Handler signature | RPC kind | proto |
|---|---|---|
| `Task<TRes>` / `ValueTask<TRes>` / `TRes ExecuteAsync(TReq, CancellationToken)` | unary | `rpc M (TReq) returns (TRes)` |
| `IAsyncEnumerable<TRes> ExecuteAsync(TReq, CancellationToken)` | server streaming | `returns (stream TRes)` |
| `Task<TRes> ExecuteAsync(IAsyncEnumerable<TReq>, CancellationToken)` | client streaming | `(stream TReq)` |
| `IAsyncEnumerable<TRes> ExecuteAsync(IAsyncEnumerable<TReq>, CancellationToken)` | duplex | `(stream TReq) returns (stream TRes)` |
| `ExecuteAsync(CancellationToken)` (no request) | any | request is `google.protobuf.Empty` (`GrpcEmpty`) |
| `Task` / `ValueTask` / `void` return | unary | response is `google.protobuf.Empty` (`GrpcEmpty`) |

The `CancellationToken` parameter is optional. Request/response messages must be classes or records (structs are fine as nested messages, KPG023). Using `MapGrpcServerStream` etc. with a handler of a different shape is an error (KPG005).

---

## Routing

The relative route is **service / method** (plus optional package segments). With `MapGroup` prefixes the relative route may be just the method (`app.MapGroup("Users").MapGrpc<T>("GetById")`). Every segment must be a protobuf identifier (`[A-Za-z_][A-Za-z0-9_]*`); `{param}` segments are ignored. Analyzer **DG001** enforces this (including `MapGroup` / `WithGroupName` values).

| Call | Package | Service | RPC |
|---|---|---|---|
| `MapGrpc<T>("Users/GetById")` | `Api` | `Users` | `GetById` |
| `MapGrpc<T>("billing/Orders/Create")` | `billing` | `Orders` | `Create` |
| `MapGroup("/api/tests").MapGrpc<T>("Users/GetById")` | `api.tests` | `Users` | `GetById` |
| `MapGroup("api").WithGroupName("v1").MapGrpc<T>("Users/Get")` | `api.v1` | `Users` | `Get` |

The server route is `/{package}.{Service}/{Method}`. Group locals, fields and properties (also in other files) are resolved. Mapping the same RPC to a *different* handler/signature is an error (KPG003); identical duplicate mappings are merged.

### Conventions (`RequireAuthorization`, ...) are per RPC

```csharp
app.MapGrpc<ReadSecretHandler>("Vault/Read").RequireAuthorization("Admins"); // only Vault/Read
app.MapGrpc<PingHandler>("Vault/Ping");                                       // stays anonymous
app.MapGroup("api").RequireAuthorization().MapGrpc<OrdersHandler>("Orders/Get"); // group chain conventions apply too
```

Conventions chained directly on the call (and on the `MapGroup` receiver chain, including the initializer of a group local) are re-emitted by the generator and applied to that single gRPC method endpoint. Arguments must be constants, static members or self-contained lambdas: references to locals, parameters or instance members are an **error** (KPG006) instead of silently dropping a security convention; `const` locals are inlined. Conventions applied to a stored result (`var ep = app.MapGrpc...; ep.RequireAuthorization();`) or added later to a group local are **not** captured (warning KPG007 for the former).

---

## Authentication & authorization

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpc<SecureHandler>("Users/GetById").RequireAuthorization();
app.MapGrpcEndpoints();
```

Anonymous calls to a protected RPC fail with `StatusCode.Unauthenticated`. In handlers use `IHttpContextAccessor` (or `ServerCallContext.GetHttpContext().User` in interceptors). Do not model bearer tokens as protobuf fields.

---

## Metadata binding (`FromMetadata`)

```csharp
public sealed class CreateOrderConfig : IGrpcEndpointConfiguration<CreateOrderHandler>
{
    public void Configure(IGrpcEndpointBuilder<CreateOrderHandler> builder)
        => builder.Request<CreateOrderRequest>(req =>
        {
            req.Property(x => x.TenantId, p => p.FromMetadata("x-tenant-id"));
            req.Property(x => x.TraceId, p => p.FromMetadata()); // key = property name
        });
}
```

- Header-bound properties are removed from the request message (codec and `/proto`).
- Values are parsed by generated, typed, reflection-free code using `CultureInfo.InvariantCulture` (integral types, `float`/`double`/`decimal`, `bool`, `Guid`, `DateTime(Offset)`, `DateOnly`, `TimeOnly`, `TimeSpan`, `char`, enums via `Enum.TryParse<T>` case-insensitive). Unparseable values leave the property unchanged.
- The property needs an accessible, non-`init` setter (KPG020). Bindings apply to unary and server-streaming RPCs.

---

## Per-endpoint interceptors

```csharp
public sealed class CreateOrderConfig : IGrpcEndpointConfiguration<CreateOrderHandler>
{
    public void Configure(IGrpcEndpointBuilder<CreateOrderHandler> builder)
    {
        builder.AddInterceptor<AuditInterceptor>();
        builder.AddInterceptor(typeof(TenantInterceptor));
    }
}

// or inline, or with an explicit configuration type
app.MapGrpc<PingHandler>("Svc/Ping", b => b.AddInterceptor<AuditInterceptor>());
app.MapGrpc<StreamHandler, StreamConfig>("Svc/Stream");
```

Interceptors run (in declaration order) only for the RPCs of that handler / call site, not via global `GrpcServiceOptions.Interceptors`. They are registered with `TryAddScoped` and resolved from the call's request services. The chain is built from cached static delegates (no per-call closures); RPCs without interceptors call the handler directly. Global interceptors: `services.AddGrpcInterceptor<T>()`.

---

## `/proto` endpoint

`UseProtoEndpoint()` maps `GET /proto` and `GET /proto/{package}.proto`. One self-contained proto3 file is generated per package (messages and enums used by several packages are repeated in each file, which is wire-compatible), so the `package` always matches the server route. With a single package `/proto` returns that file; with several it returns all files, each preceded by a `// ===== file: name.proto =====` line.

---

## Protobuf mapping (wire format)

| CLR | proto3 | Notes |
|---|---|---|
| `bool`, `int`/`short`/`sbyte`, `long`/`nint`, `uint`/`ushort`/`byte`, `ulong`/`nuint`, `float`, `double` | `bool`, `int32`, `int64`, `uint32`, `uint64`, `float`, `double` | negative `int32` is a 10-byte varint |
| `string`, `byte[]` | `string`, `bytes` | strings must be valid UTF-8 |
| `char` | `uint32` | UTF-16 code unit |
| enum | `enum` | values `ENUM_NAME_MEMBER`; a synthetic `ENUM_NAME_UNSPECIFIED = 0` is added when the CLR enum has no 0; values must fit int32 (KPG019) |
| `Guid` | `string` | canonical `"D"` format (`""` = `Guid.Empty`) |
| `decimal` | `string` | lossless invariant-culture text (exponent accepted when reading) |
| `DateTime`, `DateTimeOffset` | `google.protobuf.Timestamp` | UTC; `Unspecified` treated as UTC |
| `DateOnly` | `google.protobuf.Timestamp` | midnight UTC |
| `TimeSpan` | `google.protobuf.Duration` | tick precision |
| `TimeOnly` | `google.protobuf.Duration` | since midnight |
| class / record / struct | `message` | inherited properties included |
| `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`, `IEnumerable<T>`, `ICollection<T>`, `HashSet<T>`, `ISet<T>`, `ImmutableArray<T>`, `ImmutableList<T>` | `repeated T` | scalars are written **packed**; packed and unpacked are both accepted |
| `Dictionary<K,V>`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>` | `map<K, V>` | keys: integral, `bool`, `string`, `char`, `Guid`, `decimal` (KPG018); a missing key/value reads as the default (`""`, empty message, ...) |

Presence: non-nullable scalars use proto3 implicit presence (defaults are not written; missing fields read as defaults). Nullable value types and nullable-annotated strings are `optional` (explicit presence: `null` differs from the default). Absent nested message fields read as `null`.

Unsupported property types (e.g. `object`, nested collections, `List<int?>`) are compile errors (KPG010). Unknown fields are skipped (not preserved).

### Field numbers

1. `IProtoTypeConfiguration<T>` / `IOpenProtoTypeConfiguration` (`Property(x => x.Name, 1)`; constants and simple counters such as `n++` are evaluated),
2. otherwise `[ProtoMember(n)]`,
3. otherwise declaration order (base-class properties first). This is reported as info KPG015 because reordering properties then breaks the wire contract.

When 1 or 2 is used, properties without a number are **not** serialized and produce warning KPG011. Duplicate (KPG012) and invalid or reserved (KPG013) numbers are errors.

### Construction when deserializing

`InstantiateWith(...)` factories win (arguments matched by parameter name, otherwise in field-number order). Otherwise the generator picks the accessible constructor that covers the most serialized properties and sets the remaining ones through `set` / `init` accessors, so `record R(int A) { public string B { get; init; } }` restores both. Properties that cannot be set produce KPG017; types that cannot be created produce KPG016.

### Robustness and limits

- Malformed input (truncated data, invalid varints including the 10th byte, field number 0, invalid wire types, length prefixes beyond the buffer, invalid UTF-8, mismatched groups) throws `ProtoWireException`. A known field with the wrong wire type is skipped. Generated marshallers turn every deserialization failure into `RpcException(StatusCode.InvalidArgument)`.
- Message nesting is limited to 100 levels (`ProtoWireLimits.MaxDepth`) when reading and writing, so cyclic object graphs and hostile payloads fail instead of overflowing the stack.
- Serialization computes sizes up front and writes straight into the transport buffer (`SetPayloadLength` + `GetBufferWriter`, no intermediate buffers); multi-segment requests are read through a pooled buffer.

---

## Registration (single host API)

```csharp
services.AddGrpcServices();  // idempotent; also calls AddGrpc()
app.MapGrpcEndpoints();      // idempotent per application
app.UseProtoEndpoint();      // optional, idempotent per application
```

Each assembly containing `MapGrpc` gets a generated `KetPsi.Endpoints.Grpc.Generated.{Assembly}.KetPsiGrpcRegistration` (marked with `[assembly: GrpcGeneratedRegistration]`). Every assembly compiled with the generator explicitly calls `Ensure()` on the registrations of the assemblies it references, so library registrations are deterministic even when the library was never touched before `AddGrpcServices()` (no reliance on module initializers of unloaded assemblies; AOT-safe). Per-application state lives in DI and `IApplicationBuilder.Properties`, so several `WebApplication` instances in one process (for example `WebApplicationFactory` tests) do not interfere.

---

## Package layout

| Package | Role |
|---------|------|
| `KetPsi.Endpoints.Abstractions` | `IEndpoint` marker |
| `KetPsi.Endpoints.Grpc.Abstractions` | Proto type configuration |
| `KetPsi.Endpoints.Grpc` | Runtime and extensions; ships the generator and analyzer in `analyzers/dotnet/cs` |

Target frameworks: `net8.0`, `net9.0`, `net10.0`. The runtime is trimming and Native AOT compatible (no reflection on the request path).

---

## Diagnostics

| Id | Severity | Meaning |
|----|----------|---------|
| DG001 | Error | Invalid route (segments, identifiers, MapGroup-aware) |
| KPG001 | Warning | Route is not a compile-time constant (not mapped) |
| KPG003 | Error | Same RPC mapped to a different handler or signature |
| KPG004 | Error | Unsupported handler signature |
| KPG005 | Error | Handler shape does not match the `MapGrpc*` variant |
| KPG006 | Error | Chained convention references local state |
| KPG007 | Warning | `MapGrpc` result stored; later conventions are not applied |
| KPG008 | Warning | `configure` is not an inline lambda |
| KPG010 | Error | Unsupported property type |
| KPG011 | Warning | Property not serialized (no field number) |
| KPG012 / KPG013 | Error | Duplicate / invalid field number |
| KPG014 | Warning | Field number is not a compile-time value |
| KPG015 | Info | Field numbers derived from declaration order |
| KPG016 | Error | Message type cannot be constructed |
| KPG017 | Warning | Serialized property cannot be set when deserializing |
| KPG018 | Error | Invalid map key type |
| KPG019 | Error | Enum value outside int32 |
| KPG020 | Error | Invalid `FromMetadata` binding |
| KPG022 | Warning | Configured property not found |
| KPG023 | Error | Type cannot be a message (for example a struct request) |

---

## Breaking changes in this version

Wire and schema (existing clients must regenerate from `/proto`):

- `Guid`: 16 raw bytes (.NET mixed-endian) became `string` (canonical "D").
- `decimal`: `DecimalValue { int64 units; sfixed32 nanos }` became `string` (lossless); `DecimalValue` is no longer emitted.
- `char`: one-character `string` became `uint32`.
- `DateOnly` / `TimeOnly`: now `Timestamp` (midnight UTC) / `Duration` (previously serialized as broken sub-messages).
- Repeated scalars are written packed; default scalar values are no longer written (implicit presence).
- Enum value names are `UPPER_SNAKE` prefixed with the enum name; `X_UNSPECIFIED = 0` is added when needed.
- Inherited properties are now serialized (with declaration-order numbering, base properties come first, which shifts numbers).
- Message names are disambiguated (`Outer_Inner`, `Ns_Type`, `Wrapper_List_Int32`) when simple names collide.
- `/proto` emits one file per package (bundle format when there are several); service paths always match the server routes.

API:

- Removed: the `System.ServiceModel` contracts and `*_Proxy` classes (and the `System.ServiceModel.Primitives` dependency), `GrpcEndpointConventionRegistry`, `GrpcEndpointInterceptorRegistry`, `ProtoWireCodec`, `ProtoWireWriterExtensions`, `ProtoWireReader.ReadPacked(int, Action)` / `BeginPacked()`.
- `GeneratedProtoCodecs` and the generated service types are now `internal`; codecs expose `Size_X`, `Write_X`, `Read_X`, `Serialize_X`, `Deserialize_X(ReadOnlySpan<byte>)` and `Marshaller_X`.
- `GrpcGeneratedRegistrationHub.Register` signature changed (generated code only).
- Reader errors are `ProtoWireException` (derives from `InvalidOperationException`).
- `MapGrpcServerStream` / `MapGrpcClientStream` / `MapGrpcDuplexStream` now require matching handler shapes.

## Coverage / ReportGenerator

The test project references **both** `coverlet.msbuild` and `coverlet.collector`. Using both can produce empty or conflicting coverage files. Prefer **one**:

```bash
# Recommended: collector only
dotnet test tests/KetPsi.Endpoints.Grpc.Tests \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults

reportgenerator \
  -reports:./TestResults/**/coverage.cobertura.xml \
  -targetdir:./CoverageReport \
  -reporttypes:Html
```

Common ReportGenerator issues with this solution:

| Issue | Cause / fix |
|-------|-------------|
| Empty report | Do not combine `coverlet.msbuild` (`CollectCoverage=true`) with the XPlat collector in the same run |
| 0% on Grpc package | Add `--settings` or `/p:Include="[KetPsi.Endpoints.Grpc]*"` — by default generated and test assemblies dominate |
| No cobertura file | `OutputType=Exe` on the test host can confuse some runners; use `--collect:"XPlat Code Coverage"` and check `TestResults/**/coverage.cobertura.xml` |
| Missing branch data | Use `coverlet.collector` 6+ and `coverage.cobertura.xml`, not `coverage.json` alone |

Remove `coverlet.msbuild` from the test `.csproj` if you standardize on the collector + ReportGenerator pipeline.
