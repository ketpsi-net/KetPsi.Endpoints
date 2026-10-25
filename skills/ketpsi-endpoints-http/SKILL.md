---
name: ketpsi-endpoints-http
description: How to build ASP.NET Core HTTP endpoints with KetPsi.Endpoints.Http (source-generated Minimal APIs from IEndpoint handler classes). Use when adding, mapping, binding, configuring or debugging KetPsi HTTP handlers, MapGet<T>/MapPost<T> calls, IHttpEndpointConfiguration classes, AsParameters DTOs, Native AOT (PublishAot / EnableRequestDelegateGenerator) or KP1xx diagnostics.
---

# KetPsi.Endpoints.Http

A handler is a plain class implementing `KetPsi.Endpoints.Abstractions.IEndpoint` with one public `ExecuteAsync`. `app.MapGet<THandler>("route")` is an **inert compile-time marker**. A Roslyn source generator reads the call site and emits the real endpoint into the generated `MapHttpEndpoints()`, plus the DI registration in `AddHttpEndpoints()`.

## Setup

```bash
dotnet add package KetPsi.Endpoints.Http          # runtime + generator (analyzers) + build props
dotnet add package KetPsi.Endpoints.Http.Abstractions   # optional: configuration interfaces for non-web layers
```

```csharp
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddHttpEndpoints();      // generated: registers handlers (TryAddTransient)
var app = builder.Build();

app.MapGet<GetUserByIdHandler>("/users/{id}").WithName("GetUserById");
app.MapHttpEndpoints();                   // generated: maps every Map*<T> call site
app.Run();
```

Both `AddHttpEndpoints()` and `MapHttpEndpoints()` are required. Without `MapHttpEndpoints()` nothing is mapped, because the `Map*<T>` calls themselves do nothing at runtime.

## Writing a handler

```csharp
public sealed class GetUserByIdHandler(IUserStore store) : IEndpoint
{
    public async Task<IResult> ExecuteAsync(int id, CancellationToken ct)
        => await store.Find(id, ct) is { } user ? Results.Ok(user) : Results.NotFound();
}
```

- **Return types:** `Task<T>`, `ValueTask<T>`, `T`, `Task`, `ValueTask` and `void` are all supported.
  - `IResult` is executed, and a null `IResult` throws.
  - `string` is written as `text/plain`.
  - Any other `T` is written as JSON.
  - `Task`/`void` gives 200 with an empty body.
- **Services:** inject them through the constructor, not `ExecuteAsync`. `[FromServices]` on a parameter works but is discouraged.
- **`CancellationToken`:** binds to `RequestAborted`.
- **Statelessness:** keep handlers stateless. In Native AOT/RD mode a handler with only a public parameterless constructor is created without DI, and if it has no instance fields and isn't disposable it is **cached and reused** for every request.

## Mapping

Use `MapGet`, `MapPost`, `MapPut`, `MapDelete` and `MapPatch` with `<THandler>` where `THandler : class, IEndpoint`. They return a normal `RouteHandlerBuilder`, so chain any Minimal API convention (`WithName`, `WithTags`, `RequireAuthorization`, `AddEndpointFilter`, `DisableAntiforgery`, `Produces`, …).

```csharp
var api = app.MapGroup("/api/v1").WithTags("V1").RequireAuthorization();
api.MapPost<CreateUserHandler>("/users").WithName("CreateUser");

// Helpers work, including reduced extension calls and named/default arguments
public static void MapUsers(this RouteGroupBuilder g, string prefix = "/users")
    => g.MapGet<GetUserByIdHandler>(prefix + "/{id}");
app.MapGroup("/api").MapUsers();
```

Rules the generator enforces:
- **Constant arguments:** routes and convention arguments must be constants or static members, because the generator re-emits them fully qualified. A reference to a local, parameter or instance member is error **KP106**.
- **Skipping a call site:** `.SkipEndpointGeneration()` excludes it from generation.
- **`[Obsolete]`:** an `[Obsolete("…")]` on the handler becomes endpoint metadata.

## Parameter binding

Binding follows ASP.NET Minimal API rules. You can use inline attributes, or keep handlers clean with external configuration.

```csharp
public Task<IResult> ExecuteAsync(
    int id,                                            // route if the final pattern has {id}, else query
    [FromQuery(Name = "page_size")] int pageSize,
    [FromHeader(Name = "X-Tenant-ID")] string? tenant,
    [FromBody] CreateOrder body,                       // JSON; content type must be JSON (else 415)
    [FromForm] IFormFile? file,
    [FromKeyedServices("primary")] IBlobStorage blob,  // key is required (KP108)
    CancellationToken ct);
```

- **Simple types:** `string`, every numeric primitive, `decimal`, `bool`, `char`, `Guid`, `DateTime(Offset)`, `DateOnly`, `TimeOnly`, `TimeSpan`, enums (case-insensitive), `Uri`, and `T?` of all of these. Any `IParsable<T>` or static `TryParse` type also works. Arrays and `List<T>` of them, and `StringValues`, bind from repeated query or header values.
- **Special types:** `HttpContext`, `HttpRequest`, `HttpResponse`, `ClaimsPrincipal`, `Stream`, `PipeReader`, `IFormFile`, `IFormFileCollection`, `IFormCollection`, and types with `BindAsync`.
- **Complex types:** a type registered in DI is injected. Otherwise it is read as the JSON body on POST/PUT/PATCH. On GET/DELETE it throws at startup, so use `AsParameters`.
- **Required vs optional:**
  - A non-nullable parameter without a default is required: if missing, the response is **400**.
  - A nullable `T?` is null when missing.
  - Default values (`int page = 1`) are honoured.
  - A value that can't be parsed returns **400**.
- **Route detection:** uses the final pattern, including `MapGroup` prefixes, `{id?}`, `{*path}` and constraints.
- **Body and form:** only one body or form source is allowed per handler (KP109).

### External configuration and AsParameters

The configuration method name is the **PascalCase form of the `ExecuteAsync` parameter name** (`filter` → `builder.Filter()`). The generator generates these fluent stubs per handler. Configuration classes are **read at compile time and never executed**.

```csharp
public record OrderFilter(string? Status, DateOnly? From, DateOnly? To, int? Page = 1, int? Size = 50);

public sealed class GetOrdersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(OrderFilter filter, CancellationToken ct) => Task.FromResult(Results.Ok(filter));
}

public sealed class GetOrdersConfig : IHttpEndpointConfiguration<GetOrdersHandler>
{
    public void Configure(IHttpEndpointBuilder<GetOrdersHandler> builder)
    {
        builder.Filter()
               .AsParameters()
               .Property(x => x.Status, p => p.FromQuery())
               .Property(x => x.From,   p => p.FromQuery(name: "fromDate"))
               .Property(x => x.To,     p => p.FromQuery(name: "toDate"));
    }
}
```

Parameter builder methods: `FromRoute/FromQuery/FromHeader/FromForm(name?)`, `FromBody()`, `FromServices()`, `FromKeyedServices(key)`, `AsParameters().Property(selector, configure)`.

AsParameters rules:
- **DTO shapes:** positional records, classes with a constructor, and property-only DTOs all work.
- **Flatten DTOs:** nested complex types under AsParameters are **not** bound.
- **Optional values must be nullable:** use `int?` or `bool?`. A non-nullable value type without a default is required and returns 400 when missing.
- **No attribute on the parameter itself:** never put `[FromQuery]` on the complex parameter, because that means "parse the whole object from one string". Use `.AsParameters()` instead.

## Native AOT (request-delegate mode)

Set `<PublishAot>true</PublishAot>` (or `<EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>`). The generator then emits `MapMethods` with a pre-built `RequestDelegate` that uses no reflection and no RequestDelegateFactory. This is needed because Microsoft's RDG does not see endpoints that a third-party generator emits.

- **Build props:** the NuGet package's `build/` props make these flags visible to the generator. If you consume the library through a **project reference**, add the following yourself:
  ```xml
  <ItemGroup>
    <CompilerVisibleProperty Include="PublishAot" />
    <CompilerVisibleProperty Include="EnableRequestDelegateGenerator" />
  </ItemGroup>
  ```
- **JSON types:** register every request and response type in a `JsonSerializerContext` placed first in the resolver chain. A missing type fails when the endpoint is built, not per request.
  ```csharp
  builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
  ```
- **Checking the mode:** `obj/**/generated/**/KetPsiRdgActivation.g.cs` records which mode the generator resolved.
- **Same behaviour as Minimal APIs:** 400/415 rules, endpoint filters (group, then endpoint, then handler, with the bound arguments in `context.Arguments`), ApiExplorer/OpenAPI metadata, and antiforgery. Form endpoints require a valid antiforgery token unless `.DisableAntiforgery()` is set, and need `AddAntiforgery()` + `UseAntiforgery()`.
- **Allocations:** the hot path (scalar binding → handler → cached `IResult`) allocates nothing. Keep it that way by returning cached `IResult`s or `TypedResults`, avoiding closures in handlers, and preferring a sync `T` or a completed `ValueTask<T>` when no I/O happens.
- **Known limitations:** complex `[FromForm]` DTOs are a compile error in RD mode, and a .NET 10 validation filter added after the KetPsi metadata convention is not applied.

## Diagnostics

| Id | Meaning | Fix |
|---|---|---|
| KP101 | Map method symbol could not be resolved | Add `using Microsoft.AspNetCore.Builder;` and check references |
| KP102 | `Map*` called without a generic handler | Use `MapGet<THandler>(…)` |
| KP103 | `IEndpoint` without a public `ExecuteAsync` | Add the method |
| KP104 | Map call or group could not be resolved; no endpoint generated | Simplify the call chain; use constants/static members |
| KP105 | Helper has no call sites; mapped on the app root | Call the helper, or ignore |
| KP106 | Route or convention argument references a local, parameter or instance member | Use a constant or static member |
| KP107 | Parameter type can't be bound in RD mode | Change the type, use `[FromServices]`/`[FromBody]`, or add `TryParse`/`BindAsync` |
| KP108 | `[FromKeyedServices]` without a key | Pass the key |
| KP109 | More than one body/form source | Merge into one DTO |
| KP110 / KP001 | The generator crashed (call site / output) | Report a bug with the call site |

## Debugging

- **Generated code:** set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and inspect `obj/<cfg>/<tfm>/generated/KetPsi.Endpoints.Http.Generator/`.
  - `HttpEndpoints.g.cs` holds the master `MapHttpEndpoints`/`AddHttpEndpoints`.
  - `*HttpEndpoints.g.cs` holds the mappings for each group.
  - `*HttpEndpointBuilder.g.cs` holds the configuration stubs.
- **Endpoint missing:** check that `MapHttpEndpoints()` is called, the call isn't `SkipEndpointGeneration()`, and there's no KP104 warning.
- **400 on an "optional" value:** make it nullable or give it a default.
- **Testing:** use `WebApplicationFactory<Program>` integration tests, plus `CSharpGeneratorDriver` tests for compile-only cases.
