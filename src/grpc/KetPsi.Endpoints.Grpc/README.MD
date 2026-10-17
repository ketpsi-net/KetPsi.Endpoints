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
builder.Services.AddGrpcServices();   // generated
builder.Services.AddProtoEndpoint();  // optional options hook

var app = builder.Build();

app.MapGrpc<GetUserByIdHandler>("Users/GetById");
app.MapGrpcEndpoints();               // generated
app.UseProtoEndpoint();               // optional GET /proto

app.Run();
```

Variants: `MapGrpc`, `MapGrpcUnary`, `MapGrpcClientStream`, `MapGrpcServerStream`, `MapGrpcDuplexStream`.

---

## Routing

### Relative route on `MapGrpc`

The string argument is **service / method** (and optional path prefix segments). Analyzer **DG001** requires **at least two** non-parameter segments. There is **no** three-segment maximum. Segments like `{id}` are ignored when counting.

| `MapGrpc` route | Service | RPC |
|-----------------|---------|-----|
| `Users/GetById` | `Users` | `GetById` |
| `billing/Orders/Create` | `Orders` | `Create` |
| `Orders/{id}/Get` | `Orders` | `Get` (`{id}` skipped) |

### Package (proto `package`)

1. **MapGroup** path prefixes are prepended (supports locals: `var g = app.MapGroup(...); g.MapGrpc(...)`).
2. Remaining path segments **before** service/method become the package (joined with `.`).
3. If nothing is left for the package, default is `Api`.
4. **Version** comes **only** from the last **`WithGroupName("...")`** on the chain, appended as `.v1` (proto-style), not from treating path segments as versions.

```csharp
// package Api; service Users; rpc GetById
app.MapGrpc<GetUserByIdHandler>("Users/GetById");

// package api.tests; service Users; rpc GetById
app.MapGroup("/api/tests").MapGrpc<GetUserByIdHandler>("Users/GetById");

// package api.tests.v1; service test; rpc create
var group = app.MapGroup("/api/tests").WithGroupName("v1");
group.MapGrpc<CreateHandler>("test/create");
```

```protobuf
package api.tests.v1;

service test {
  rpc create (CreateRequest) returns (CreateResponse);
}
```

### `MapGroup` + `WithGroupName`

`MapGrpc` is defined on:

- `WebApplication` — top-level mapping
- `IEndpointConventionBuilder` — after `MapGroup` / `WithGroupName` (required because `WithGroupName` returns that interface)

Prefer fluent chains or assign the group to a local; both are resolved by the generator.

### Conventions (`RequireAuthorization`)

`MapGrpc` returns `IEndpointConventionBuilder`:

```csharp
app.MapGroup("/api/tests")
   .WithGroupName("v1")
   .MapGrpc<CreateOrderHandler>("orders/create")
   .RequireAuthorization()
   .RequireAuthorization("OrdersPolicy");
```

Conventions are stored by route and applied when `MapGrpcEndpoints` calls `MapGrpcService`.

---

## Authentication & authorization

gRPC on ASP.NET Core uses the same HTTP/2 pipeline as other endpoints.

### Server

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpc<SecureHandler>("Users/GetById")
   .RequireAuthorization();

app.MapGrpcEndpoints();
```

- Incoming `Authorization` is a normal request header; JWT bearer works without putting tokens on the protobuf contract.
- In handlers, resolve the user with `IHttpContextAccessor` or `ServerCallContext.GetHttpContext().User`.

```csharp
public sealed class SecureHandler : IEndpoint
{
    private readonly IHttpContextAccessor _http;
    public SecureHandler(IHttpContextAccessor http) => _http = http;

    public Task<GetUserResponse> ExecuteAsync(GetUserRequest request, CancellationToken ct)
    {
        var user = _http.HttpContext?.User;
        // ...
    }
}
```

Register `builder.Services.AddHttpContextAccessor();`.

### Propagating auth to downstream gRPC services

Copy the header onto outgoing metadata (or use a client interceptor):

```csharp
var auth = callContext.RequestHeaders.GetValue("authorization");
var headers = new Metadata();
if (!string.IsNullOrEmpty(auth))
    headers.Add("Authorization", auth);
```

Do **not** model bearer tokens as protobuf fields unless you intentionally want them in the contract.

---

## Header binding (`FromHeader`)

Bind request DTO properties from gRPC metadata (not from the wire message). Configure on **`IGrpcEndpointConfiguration<TEndpoint>`**, not on `IProtoTypeConfiguration`.

```csharp
public sealed class CreateOrderConfig : IGrpcEndpointConfiguration<CreateOrderHandler>
{
    public void Configure(IGrpcEndpointBuilder<CreateOrderHandler> builder)
    {
        builder.Request<CreateOrderRequest>(req =>
        {
            req.Property(x => x.TenantId, p => p.FromHeader("x-tenant-id"));
            req.Property(x => x.TraceId, p => p.FromHeader()); // key = property name
        });
    }
}
```

Header-bound properties are omitted from the `/proto` schema and codecs. Applied after deserialize, before the handler runs.

---

## Per-handler interceptors

```csharp
public sealed class CreateOrderConfig : IGrpcEndpointConfiguration<CreateOrderHandler>
{
    public void Configure(IGrpcEndpointBuilder<CreateOrderHandler> builder)
    {
        builder.AddInterceptor<AuditInterceptor>();
        builder.AddInterceptor<TenantInterceptor>();
    }
}
```

- Discovered at compile time; registered in `AddGrpcServices`.
- Applied only on that handler’s methods inside the generated `IServiceMethodProvider` pipeline (not global `GrpcServiceOptions.Interceptors`).
- Resolved from `RequestServices` on the call’s `HttpContext`.

---

## Optional `/proto` endpoint

| Method | Purpose |
|--------|---------|
| `AddProtoEndpoint()` | DI hook (options reserved) |
| `UseProtoEndpoint()` | Maps `GET /proto` with compile-time schema |

Not registered by `MapGrpcEndpoints`; call `UseProtoEndpoint()` explicitly when needed.

Non-.NET teams generate clients from this schema with standard `protoc` tooling.

---

## Protobuf type configuration

Use **KetPsi.Endpoints.Grpc.Abstractions** in Application projects:

```csharp
public sealed class GetUserRequestConfig : IProtoTypeConfiguration<GetUserRequest>
{
    public void Configure(IProtoTypeBuilder<GetUserRequest> builder)
    {
        builder.Property(x => x.Id, 1)
               .Property(x => x.Name, 2);
    }
}
```

Field numbers drive schema emission and pure wire codecs at compile time.

### Custom factory (`InstantiateWith`)

Prefer a **public constructor** for simple DTOs. For domain types with **internal constructors** and static factories, use `InstantiateWith` so the generated reader does not call `new T()` + property setters.

**Preferred (method group / `Delegate`)** — same idea as Minimal APIs:

```csharp
public sealed class ApiResultConfiguration : IOpenProtoTypeConfiguration
{
    public void Configure(IOpenProtoTypeBuilder builder)
    {
        builder.ConfigureGeneric(typeof(ResponseWrapper<>), config => config
            .Property("Status", 1)
            .Property("Value", 2)
            .InstantiateWith(ResponseWrapper<object>.Create)); // method group
    }
}
```

```csharp
// Closed type
builder.Property(x => x.Id, 1)
       .Property(x => x.Name, 2)
       .InstantiateWith(OrderDto.Create);
```

**Also supported**

```csharp
.InstantiateWith("Create")
.InstantiateWith(nameof(ResponseWrapper<>.Create))
.InstantiateWith(typeof(Helper), "Create")
```

The deserializer calls the factory with property values in **field-number order**. The method must be **static** and accessible from generated code. Generated shape:

```csharp
return global::MyApp.ResponseWrapper<T>.Create(__p_Status, __p_Value);
```

---

## Package layout

| Package | Role |
|---------|------|
| `KetPsi.Endpoints.Abstractions` | `IEndpoint` marker |
| `KetPsi.Endpoints.Grpc.Abstractions` | Proto type configuration |
| `KetPsi.Endpoints.Grpc` | Runtime + extensions |
| `KetPsi.Endpoints.Grpc.Generator` | Source generator + `DG001` route analyzer |

Target frameworks: `net8.0`, `net9.0`, `net10.0`.

---

## Diagnostics

| Id | Meaning |
|----|---------|
| **DG001** | `MapGrpc` relative route has fewer than 2 non-parameter segments |

---

## Notes

- Prefer `WithGroupName` for **API version** in the proto package; keep `MapGroup` paths for hierarchical naming.
- Fluent `MapGroup(...).WithGroupName(...).MapGrpc(...)` or `var group = ...` are both supported for group discovery.
- Authentication is pipeline-based; header binding is for business metadata, not a replacement for `UseAuthentication`.
