# KetPsi.Endpoints.Http.Abstractions

[![NuGet](https://img.shields.io/nuget/v/KetPsi.Endpoints.Http.Abstractions.svg)](https://www.nuget.org/packages/KetPsi.Endpoints.Http.Abstractions/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)


Abstractions for **external HTTP endpoint configuration** used by [KetPsi.Endpoints.Http](https://www.nuget.org/packages/KetPsi.Endpoints.Http).

This package lets the Application layer declare binding rules (`AsParameters`, `FromQuery`, renamed names, headers, etc.) **without** referencing ASP.NET Core or the full HTTP endpoints package.

---

## Why a separate package?

| Concern | Approach |
|---------|----------|
| Clean Application layer | Configuration classes live next to handlers / use-cases |
| No web framework leak | This package has **no** `Microsoft.AspNetCore.*` dependency |
| Source-compatible | Types stay in namespace `KetPsi.Endpoints.Http` |

Handlers stay free of binding attributes. The source generator in `KetPsi.Endpoints.Http` discovers `IHttpEndpointConfiguration<THandler>` implementations and applies them at compile time.

---

## Install

```bash
dotnet add package KetPsi.Endpoints.Http.Abstractions
```

Use this package in **Application** (or domain-adjacent) projects.

Host / API projects still need:

```bash
dotnet add package KetPsi.Endpoints.Http
```

---

## Usage

```csharp
using KetPsi.Endpoints.Http; // namespace unchanged

public class GetOrdersConfig : IHttpEndpointConfiguration<GetOrdersHandler>
{
    public void Configure(HttpEndpointBuilder<GetOrdersHandler> builder)
    {
        builder.Filter()   // parameter name "filter" → Filter()
               .AsParameters()
               .Property(x => x.Status, p => p.FromQuery())
               .Property(x => x.From,   p => p.FromQuery(Name: "fromDate"))
               .Property(x => x.To,     p => p.FromQuery(Name: "toDate"))
               .Property(x => x.Page,   p => p.FromQuery())
               .Property(x => x.Size,   p => p.FromQuery());
    }
}
```

The handler itself only depends on `KetPsi.Endpoints.Abstractions` (`IEndpoint` + `ExecuteAsync`). Configuration depends only on **this** package.

---

## Design rules

- **One config type per handler** — `IHttpEndpointConfiguration<THandler>`
- **Parameter name → fluent method** — `ExecuteAsync(OrderFilter filter, ...)` maps to `builder.Filter()`
- **Attributes stay out of handlers** — binding lives in the config class
- **Generator owns the rest** — surrogates, `AsParameters`, and Minimal API mapping are emitted by `KetPsi.Endpoints.Http`

---

## Package relationships

```
KetPsi.Endpoints.Abstractions          ← IEndpoint, shared contracts
        ▲
KetPsi.Endpoints.Http.Abstractions    ← IHttpEndpointConfiguration<T>, builders  (this package)
        ▲
KetPsi.Endpoints.Http                 ← MapGet/MapPost, generators, AspNetCore
```

---

## Compatibility

- **Namespace:** `KetPsi.Endpoints.Http` (stable; not `…Http.Abstractions`)
- **Target frameworks:** aligned with `KetPsi.Endpoints.Http` (e.g. `net8.0+`)
- **Breaking changes:** none relative to configuration types previously shipped inside `KetPsi.Endpoints.Http`
