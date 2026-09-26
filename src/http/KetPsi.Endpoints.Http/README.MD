# KetPsi.Endpoints.Http

Source-generated Minimal APIs from plain handler classes.

Define the endpoint **once** as a class with `ExecuteAsync`. A Roslyn incremental generator emits the Minimal API mapping, DI registration, and (when needed) `AsParameters` surrogate types. No runtime framework between Kestrel and your handler.

---

## Why

| Pain | This library |
|------|----------------|
| Duplicate route + handler definitions | One handler → generated `MapGet` / `MapPost` / … |
| Heavy framework pipeline | Generated lambda calls `ExecuteAsync` directly |
| Binding attributes cluttering business code | Optional **external configuration** |
| `AsParameters` + records / ctors are fragile | Generator builds surrogates and applies attributes |
| Same logic needed for HTTP and gRPC later | Same handler shape (gRPC story is separate) |

---

## Quick start

### 1. Handler

```csharp
using KetPsi.Endpoints.Abstractions;

public class GetUserByIdHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Id = id }));
    }
}
```

`CancellationToken` is always part of the contract. Return type is `Task<IResult>` (not `ValueTask`).

### 2. Map it

```csharp
app.MapGet<GetUserByIdHandler>("/users/{id}")
   .WithName("GetUserById");
```

### 3. Register generated endpoints

```csharp
// Program.cs
builder.Services.AddHttpEndpoints();   // generated – registers all handlers
// ...
app.MapHttpEndpoints();                // generated – maps all endpoints
```

Or call your own extension that contains the `Map*` calls; the generator still emits DI + mapping helpers per group.

---

## HTTP verbs

```csharp
app.MapGet<THandler>(pattern);
app.MapPost<THandler>(pattern);
app.MapPut<THandler>(pattern);
app.MapDelete<THandler>(pattern);
```

Chain normal Minimal API conventions:

```csharp
app.MapPost<CreateUserHandler>("/users")
   .WithName("CreateUser")
   .WithTags("Users")
   .RequireAuthorization();
```

---

## Parameter binding

### Inline attributes (on `ExecuteAsync` parameters)

```csharp
public Task<IResult> ExecuteAsync(
    int id,                                          // route (by name) or simple inference
    [FromQuery] string? status,
    [FromHeader(Name = "X-Tenant-ID")] string? tenantId,
    [FromBody] CreateUserRequest request,
    [FromForm] IFormFile? file,
    [FromServices] IMyService svc,
    [FromKeyedServices("primary")] IBlobStorage storage,
    CancellationToken ct);
```

### Path parameters

Route template `{id}` binds to a parameter named `id` (or use `[FromRoute]`).

### Body

```csharp
public Task<IResult> ExecuteAsync([FromBody] CreateUserRequest request, CancellationToken ct);
```

### Form + files

```csharp
app.MapPost<UploadDocumentHandler>("/documents")
   .DisableAntiforgery()
   .WithName("UploadDocument");
```

---

## AsParameters

Use a single DTO for many query (or route/header) values.

### Handler

```csharp
public record OrderFilter(
    string? Status,
    DateOnly? From,
    DateOnly? To,
    int Page = 1,
    int Size = 50);

public class GetOrdersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(OrderFilter filter, CancellationToken ct)
        => Task.FromResult(Results.Ok(filter));
}
```

### External configuration (recommended)

```csharp
public class GetOrdersConfig : IHttpEndpointConfiguration<GetOrdersHandler>
{
    public void Configure(HttpEndpointBuilder<GetOrdersHandler> builder)
    {
        builder.Filter()   // method name = parameter name in PascalCase
               .AsParameters()
               .Property(x => x.Status, p => p.FromQuery())
               .Property(x => x.From,   p => p.FromQuery(Name: "fromDate"))
               .Property(x => x.To,     p => p.FromQuery(Name: "toDate"))
               .Property(x => x.Page,   p => p.FromQuery())
               .Property(x => x.Size,   p => p.FromQuery());
    }
}
```

```csharp
app.MapGet<GetOrdersHandler>("/orders")
   .WithName("GetOrders");
```

The generator:

1. Detects `IHttpEndpointConfiguration<THandler>`
2. Builds an `[AsParameters]` surrogate struct when needed (records, ctors, property-only DTOs)
3. Applies `FromQuery` / renamed names / etc. on surrogate members
4. Emits mapping that converts surrogate → your DTO via `__ToModel()`

### Supported DTO shapes

| Shape | Support |
|-------|---------|
| Positional record / primary constructor | Yes |
| Class with public constructor | Yes (longest public ctor) |
| Property-only DTO (`get; set;` / `init`) | Yes |
| Nested complex type under AsParameters | **No** – flatten (see below) |

---

## Important binding rules (read these)

### 1. Nested complex types under AsParameters

ASP.NET Core does **not** bind nested objects under `[AsParameters]` as query fields; it often treats them as body and fails on GET.

**Wrong**

```csharp
public record AddressFilter(string? City, string? Country);
public record CustomerFilter(string? Name, AddressFilter? Address, bool? Active);
```

**Right – flatten**

```csharp
public record CustomerFilter(
    string? Name,
    string? City,
    string? Country,
    string? PostalCode = null,
    bool? Active = true);
```

### 2. Non-nullable value types are required

A missing query value for `bool IncludeReserved` yields **400**, even if the record has `= false`.

**Wrong**

```csharp
public record InventoryQuery(bool IncludeReserved = false, ...);
```

**Right**

```csharp
public record InventoryQuery(bool? IncludeReserved = false, ...);
```

Treat “optional with default” as **nullable** at the binding boundary; normalize null → default inside the handler if needed.

### 3. Do not put `[FromQuery]` on a complex AsParameters parameter

```csharp
// Throws at startup: type must have TryParse...
ExecuteAsync([FromQuery] OrderFilter filter, CancellationToken ct)
```

Configure with `.AsParameters()` in external config (or attributes on the DTO / surrogate). `[FromQuery]` on the **complex parameter itself** means “bind entire object from one string.”

### 4. Parameter name → config method name

External config uses PascalCase of the parameter name:

```csharp
// ExecuteAsync(OrderFilter filter, ...)
builder.Filter()   // "filter" → Filter()
```

Generated builder extensions expose one method per parameter for fluent config.

---

## Groups

```csharp
var api = app.MapGroup("/api/v1")
             .WithTags("V1");

api.MapGet<GetUserByIdHandler>("/users/{id}")
   .WithName("GetUserById_Grouped");

var admin = api.MapGroup("/admin")
               .WithTags("Admin");

admin.MapDelete<DeleteUserHandler>("/users/{id}")
     .WithName("AdminDeleteUser");
```

The generator tracks `MapGroup`  chains and emits mapping under the correct group variable.

---

## Metadata helpers

| Call | Effect |
|------|--------|
| `.WithName("...")` | Endpoint name (OpenAPI / link generation) |
| `.SkipEndpointGeneration()` | Generator ignores this map (no emitted endpoint) |
| `[Obsolete("...")]` on handler | Emitted as endpoint metadata |

```csharp
app.MapGet<InternalDebugHandler>("/internal/debug")
   .SkipEndpointGeneration()
   .WithName("InternalDebug");
```

---

## Examples

### GET + query + header

```csharp
public class SearchOrdersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromHeader(Name = "X-Tenant-ID")] string? tenantId,
        CancellationToken ct)
        => Task.FromResult(Results.Ok(new { status, page, size, tenantId }));
}

app.MapGet<SearchOrdersHandler>("/orders")
   .WithName("SearchOrders");
```

### POST + JSON body

```csharp
public record CreateUserRequest(string Email, string FirstName, string LastName);

public class CreateUserHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] CreateUserRequest request, CancellationToken ct)
        => Task.FromResult(Results.Created($"/users/{Guid.NewGuid():N}", request));
}

app.MapPost<CreateUserHandler>("/users")
   .WithName("CreateUser");
```

### PUT + path + body + header

```csharp
public class UpdateUserHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        int id,
        [FromBody] UpdateUserRequest request,
        [FromHeader(Name = "If-Match")] string? etag,
        CancellationToken ct)
        => Task.FromResult(Results.Ok(new { id, request, etag }));
}

app.MapPut<UpdateUserHandler>("/users/{id}")
   .WithName("UpdateUser");
```

### DELETE + optional body (bulk)

```csharp
public record BulkDeleteRequest(int[] Ids, string? Reason);

public class BulkDeleteUsersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] BulkDeleteRequest request, CancellationToken ct)
        => Task.FromResult(Results.Ok(new { Deleted = request.Ids.Length }));
}

app.MapDelete<BulkDeleteUsersHandler>("/users")
   .WithName("BulkDeleteUsers");
```

### Mixed AsParameters + regular parameters

```csharp
public record DateRange(DateOnly From, DateOnly To);

public class GetSalesReportHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        DateRange range,
        string? region,
        string format,
        CancellationToken ct)
        => Task.FromResult(Results.Ok(new { range, region, format }));
}

public class GetSalesReportConfig : IHttpEndpointConfiguration<GetSalesReportHandler>
{
    public void Configure(HttpEndpointBuilder<GetSalesReportHandler> builder)
    {
        builder.Range().AsParameters()
                       .Property(x => x.From, p => p.FromQuery(Name: "from"))
                       .Property(x => x.To,   p => p.FromQuery(Name: "to"))
                .Region().FromQuery()
                .Format().FromHeader(name: "X-Report-Format")
               ;
    }
}
```

---

## What the generator emits

- `*HttpEndpointBuilder.g.cs` – fluent stubs per handler parameter (compile-time config API)
- `*HttpEndpoints.g.cs` – `Map*Endpoints`, `Add*HttpEndpoints`, surrogates
- `HttpEndpoints.g.cs` – master `MapHttpEndpoints` / `AddHttpEndpoints`

Inspect `obj/.../generated` after build if you need to debug output.

---

## Testing recommendations

1. **Integration tests** – `WebApplicationFactory` against real routes (happy path + negatives).
2. **Snapshot tests** – `CSharpGeneratorDriver` + Verify on generated sources (separate project).
3. Cover at least: record / class+ctor / property-only AsParameters, renamed query names, nullable defaults, flattened filters, groups, obsolete, skip.

---

## FAQ

**Does the handler need a base interface?**  
Only if your product defines one (e.g. `IEndpoint`). The generator keys off `ExecuteAsync` and the generic `Map*<THandler>` call.

**Can I still use normal Minimal API APIs?**  
Yes. Generated endpoints are standard endpoint metadata; filters, auth, OpenAPI, groups all apply.

**Why is my GET returning 400 on an optional bool?**  
Non-nullable value type ⇒ required by model binding. Use `bool?`.

**Why startup exception about `TryParse`?**  
`[FromQuery]` (or similar) was applied to a **complex** parameter that is also used as `AsParameters`. Remove the attribute from the complex parameter.

**Nested DTO under AsParameters?**  
Flatten properties into one type. Nested complex binding is not reliable for query under `[AsParameters]`.

---

## License / package

*(Add your license and package IDs here.)*

---

## See also

- gRPC code-first generation from the same handler model — *coming / separate package*
```
