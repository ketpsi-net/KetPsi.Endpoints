# KetPsi.Endpoints.Grpc

Code-first **gRPC** endpoints for the KetPsi stack. Define a handler with `ExecuteAsync` once; a Roslyn incremental generator emits protobuf-net.Grpc service contracts, proxies, DI registration, and mapping — **no `.proto` files to maintain**.

Same handler shape as [KetPsi.Endpoints.Http](https://www.nuget.org/packages/KetPsi.Endpoints.Http); protocol-specific packages handle HTTP vs gRPC.

---

## Install

```bash
dotnet add package KetPsi.Endpoints.Grpc
```

Also reference abstractions if you configure protobuf types in the Application layer:

```bash
dotnet add package KetPsi.Endpoints.Grpc.Abstractions
```

---

## Quick start

### 1. Handler

```csharp
public class GetUserByIdHandler
{
    public Task<GetUserResponse> ExecuteAsync(GetUserRequest request, CancellationToken ct)
        => Task.FromResult(new GetUserResponse { Id = request.Id, Name = "Ada" });
}
```

### 2. Map it

Route format (enforced by analyzer **DG001**):

- `Namespace/Service/Method` → contract `Namespace.Service`, method `Method`
- `Service/Method` → defaults namespace to `Api`

```csharp
app.MapGrpc<GetUserByIdHandler>("Api/Users/GetById");
// or
app.MapGrpcUnary<GetUserByIdHandler>("Users/GetById");
```

Variants: `MapGrpc`, `MapGrpcUnary`, `MapGrpcClientStream`, `MapGrpcServerStream`, `MapGrpcDuplexStream`.

### 3. Register generated endpoints

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddGrpc();                          // Code-first gRPC
builder.Services.AddGrpcServices();         // generated – registers handlers
// optional:
builder.Services.AddProtoEndpoint();

var app = builder.Build();

app.MapGrpc<GetUserByIdHandler>("Api/Users/GetById");
app.MapGrpcEndpoints();                     // generated – maps proxies

// optional – human-readable schema:
app.UseProtoEndpoint();                     // GET /proto

app.Run();
```

---

## Optional `/proto` endpoint

| Method | Side | Purpose |
|--------|------|---------|
| `AddProtoEndpoint()` | `IServiceCollection` | DI hook (reserved for options) |
| `UseProtoEndpoint()` | `WebApplication` | Maps `GET /proto` with aggregated protobuf schema |

Call `UseProtoEndpoint()` only when you want the endpoint. It is **not** registered by `MapGrpcEndpoints`.

---

## Protobuf type configuration

Use [KetPsi.Endpoints.Grpc.Abstractions](https://www.nuget.org/packages/KetPsi.Endpoints.Grpc.Abstractions) in Application projects:

```csharp
using KetPsi.Endpoints.Grpc.Abstractions;

public class UserResponseConfig : IProtoTypeConfiguration<UserResponse>
{
    public void Configure(IProtoTypeBuilder<UserResponse> builder)
    {
        builder.Property(x => x.Id, 1)
               .Property(x => x.Name, 2)
               .UseConstrcutor(false);
    }
}
```

Open generics:

```csharp
public class WrapperConfig : IOpenProtoTypeConfiguration
{
    public void Configure(IOpenProtoTypeBuilder builder)
    {
        builder.ConfigureGeneric(typeof(Wrapper<>), meta =>
        {
            meta.Property("Value", 1);
            meta.UseConstrcutor(false);
        });
    }
}
```

The generator applies closed generics discovered in handler signatures.

---

## What the generator emits

| Output | Description |
|--------|-------------|
| `*_GrpcService.g.cs` | `[ServiceContract]` interface + proxy per service route |
| `GeneratedGrpcRegistrationExtensions.g.cs` | `AddGrpcServices`, `MapGrpcEndpoints`, `UseProtoEndpoint` |

Route analyzer **DG001** fails the build on invalid routes (not 2 or 3 non-empty segments).

---

## Package relationships

```
KetPsi.Endpoints.Abstractions           ← shared IEndpoint (optional for gRPC handlers)
KetPsi.Endpoints.Grpc.Abstractions      ← IProtoTypeConfiguration / open-generic builders
        ▲
KetPsi.Endpoints.Grpc                  ← runtime + generatorerator (this package)
```

---

## Requirements

- .NET 8 / 9 / 10
- ASP.NET Core (shared framework)
- protobuf-net + protobuf-net.Grpc.AspNetCore
```

