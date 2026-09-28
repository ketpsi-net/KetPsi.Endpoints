# KetPsi.Endpoints

[![NuGet](https://img.shields.io/nuget/v/KetPsi.Endpoints.Abstractions.svg)](https://www.nuget.org/packages/KetPsi.Endpoints.Abstractions/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)


**One handler. Multiple protocols.**

Define your endpoint once as a plain class with `ExecuteAsync`. Source generators turn it into real endpoints — without maintaining parallel route maps, proto files, or a heavy runtime framework in the request path.

```csharp
public class GetOrderHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(OrderFilter filter, CancellationToken ct)
    {
        // business logic only
    }
}
```

The same handler shape is the foundation for HTTP today and gRPC next.

---

## Packages

| Package | Status | Description |
|---------|--------|-------------|
| **[KetPsi.Endpoints.Http](./src/http/KetPsi.Endpoints.Http)** | Available | Source-generated Minimal APIs from handlers |
| **KetPsi.Endpoints.Grpc** | Coming soon | Code-first gRPC from the same handlers (no `.proto` maintenance) |

---

## Design goals

- **Single source of truth** — the handler *is* the endpoint
- **Direct generation** — Minimal API lambdas call `ExecuteAsync` with minimal indirection
- **Clean handlers** — optional external configuration for binding (`AsParameters`, renamed query names, etc.)
- **Protocol symmetry** — same mental model for HTTP and gRPC

---

## HTTP

See **[README.md](./src/http/KetPsi.Endpoints.Http)** for usage, binding rules, `AsParameters` hints, examples, and project setup.

---

## gRPC

**Coming soon.**

Same handler-centric model, code-first contracts, no hand-maintained `.proto` files as the source of truth.

---

## License

MIT License
