# KetPsi.Endpoints.Grpc.Abstractions

Abstractions for **protobuf-net type configuration** used by [KetPsi.Endpoints.Grpc](https://www.nuget.org/packages/KetPsi.Endpoints.Grpc).

Place configuration next to DTOs or use-cases in the **Application** layer without referencing ASP.NET Core or the full gRPC package.

---

## Why a separate package?

| Concern | Approach |
|---------|----------|
| Clean Application layer | Proto field maps live with domain/application types |
| No web framework leak | **No** `Microsoft.AspNetCore.*` dependency |
| Generator-friendly | `KetPsi.Endpoints.Grpc` discovers implementations at compile time |

---

## Install

```bash
dotnet add package KetPsi.Endpoints.Grpc.Abstractions
```

Host projects that map gRPC endpoints still need:

```bash
dotnet add package KetPsi.Endpoints.Grpc
```

---

## Usage

### Concrete type

```csharp
using KetPsi.Endpoints.Grpc.Abstractions;

public class GetUserResponseConfig : IProtoTypeConfiguration<GetUserResponse>
{
    public void Configure(IProtoTypeBuilder<GetUserResponse> builder)
    {
        builder.Property(x => x.Id, 1)
               .Property(x => x.Name, 2)
               .UseConstrcutor(false);
    }
}
```

### Open generic

```csharp
public class WrapperConfig : IOpenProtoTypeConfiguration
{
    public void Configure(IOpenProtoTypeBuilder builder)
    {
        builder.ConfigureGeneric(typeof(Wrapper<>), meta =>
        {
            meta.Property("Value", 1)
                .Property("Tag", 2)
                .UseConstrcutor(false);
        });
    }
}
```

The generatorerator in `KetPsi.Endpoints.Grpc` applies open-generic configuration to every closed generic found in handler `ExecuteAsync` signatures.

---

## Contracts

| Type | Role |
|------|------|
| `IProtoTypeConfiguration<T>` | Configure a concrete type |
| `IProtoTypeBuilder<T>` | Fluent `Property` / `UseConstrcutor` for `T` |
| `IOpenProtoTypeConfiguration` | Configure open generics |
| `IOpenProtoTypeBuilder` | Register `ConfigureGeneric(openType, action)` |
| `IOpenProtoMetaType` | Fluent mapping for a single meta-type |

---

## Package relationships

```
KetPsi.Endpoints.Grpc.Abstractions    ← this package (no AspNetCore)
        ▲
KetPsi.Endpoints.Grpc                ← runtime + generators
```

---

## Compatibility

- Target frameworks aligned with `KetPsi.Endpoints.Grpc` (`net8.0+`)
- Namespace: `KetPsi.Endpoints.Grpc.Abstractions`
```

