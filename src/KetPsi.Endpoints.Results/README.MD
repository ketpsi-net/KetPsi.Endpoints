# KetPsi.Endpoints.Results


[![NuGet](https://img.shields.io/nuget/v/KetPsi.Endpoints.Results.svg)](https://www.nuget.org/packages/KetPsi.Endpoints.Results/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)


API-layer results that sit **on top of** [KetPsi.Results](../KetPsi.Results). Domain/infrastructure code keeps using structured `Result` / `Result<T>`. The endpoint layer maps those outcomes into a single serializable shape with an **explicit HTTP status code**.

## Goal

| Layer | Type | Responsibility |
|-------|------|----------------|
| Application / infrastructure | `KetPsi.Results.Result` | Always-valid domain outcomes |
| API / endpoints | `ApiResult<T>` | HTTP status + data or errors for the client |

## What you get

- **`ApiResult<T>`** — `StatusCode`, `Value`, `Errors`
- **`ApiResultError`** — code, message, title, metadata, optional per-error status
- **HTTP factories** — `ApiResult.Ok`, `Request.NotFound`, `Server.ServiceUnavailable`, …
- **Builders from domain results** — `ApiResult.From(result).AsIs()` / `.As(map)` (+ async & page variants)

No Mapster dependency: mapping uses `Func<T, TResult>`.

## Performance notes

- `ApiResult.Ok()`, `Accepted()` and `NoContent()` return cached immutable instances (zero allocation).
- `ApiResult.From(...)` returns `readonly struct` builders, so `From(result).AsIs()` allocates only the `ApiResult<T>`.
- Async builders consume a `ValueTask` source exactly once (inside `From(...)`), so `AsIsAsync()` / `AsAsync()` may be
  called repeatedly without double-awaiting an `IValueTaskSource`. When the source has already completed, mapping runs
  synchronously (no async state machine).
- Error titles come from a switch over `ErrorType` (no `Enum.ToString()`), and metadata values are formatted with
  `CultureInfo.InvariantCulture`.
- `Request.BadRequest((ApiResultError[])null)` throws `ArgumentNullException`; a null element throws `ArgumentException`.

## Native AOT / source-generated JSON

The package is trimming/AOT compatible and ships `ApiResultJsonContext`, a source-generated `JsonSerializerContext`
for the payload-less shape (`ApiResult<object?>`, returned by `Ok()`, `NoContent()`, the error factories and
`From(Result)`) plus `ApiResultError`. Add it to the resolver chain and declare your typed results in your own context:

```csharp
[JsonSerializable(typeof(ApiResult<OrderDto>))]
internal partial class AppJsonContext : JsonSerializerContext;

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    o.SerializerOptions.TypeInfoResolverChain.Add(ApiResultJsonContext.Default);
});
```

Payload-less results always carry a null `Value`, so no polymorphic `object` metadata is needed. If you put a non-null
value into an `ApiResult<object?>` yourself, register its runtime type in a context too.

## Example

```csharp
var domain = await _orders.GetAsync(id, ct);

return ApiResult.From(domain)
    .As(o => o.ToDto());   // ApiResult<OrderDto>

// or without mapping
return ApiResult.From(domain).AsIs();
```

```csharp
// Direct HTTP-shaped construction
return ApiResult.Request.NotFound("resource.not_found", "Order was not found");
return ApiResult.Ok(dto);
return ApiResult.Accepted(trackingId);
```

## Status mapping (from KetPsi.Results)

| Domain `ErrorType` | HTTP |
|--------------------|------|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| RuleViolation | 422 |
| QuotaExceeded | 429 |
| Canceled | 499 |
| Failure | 500 |
| DependencyFailure | 503 |

Success → **200**, InProgress → **202**.

## Paging

Use domain `PagedData<T>` inside a normal API result:

```csharp
Result<PagedData<Order>> domain = ...;
return ApiResult.From(domain).AsIs();           // ApiResult<PagedData<Order>>
return ApiResult.From(domain).As(p => p.Items); // or map as needed
```
