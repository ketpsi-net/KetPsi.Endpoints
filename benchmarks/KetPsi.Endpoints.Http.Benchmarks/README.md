# KetPsi.Endpoints.Http.Benchmarks

Measures the **server-side** cost of answering a request: parameter binding, the handler and result writing, and
optionally routing. It compares three hosts that map the same scenarios:

| Host | How endpoints are produced |
|------|----------------------------|
| `KetPsi` | `app.MapGet<THandler>(…)`. The KetPsi generator emits its own `RequestDelegate` (`EnableRequestDelegateGenerator=true`, the same path `PublishAot` uses). |
| `AspNetRdg` | Hand-written Minimal API lambdas, intercepted at compile time by Microsoft's Request Delegate Generator. This is the baseline. |
| `AspNetRdf` | The same lambdas passed as `Delegate`. RDG cannot intercept them, so `RequestDelegateFactory` builds them at runtime (the classic non-AOT path). |

All hosts use `WebApplication.CreateSlimBuilder` in the Production environment, with logging disabled and a
source-generated `JsonSerializerContext` first in the resolver chain, as a Native AOT app would be configured.

## Why the old numbers were wrong

The previous version sent requests through `HttpClient` and `TestServer`. Most of the reported time and the roughly
13 KB per request came from the client, `HttpRequestMessage`/`HttpResponseMessage`, TestServer's stream pipes and
hosting, not from the endpoint. Those costs are identical for every framework, so they hid the differences.

## How it works now (in-process, no sockets, no HttpClient)

- Each host is **built but never started**.
- `Harness/InMemoryServerFeatures` stands in for Kestrel's per-connection object. Like Kestrel's `HttpProtocol`, it
  implements the request, response, response-body (a reusable `PipeWriter` and `Stream` over a growable buffer),
  request-body `PipeReader`, lifetime, identifier, route-values and endpoint features, and resets them in place.
  Features that Kestrel does not implement (query collection, items, request services, …) are still created lazily
  by `DefaultHttpContext`, exactly as in production. They are therefore counted, and they are cleared per request.
- `Harness/InMemoryRequestRunner` reuses one `DefaultHttpContext` (`Initialize`/`Uninitialize`, which is what
  Kestrel's context pooling does) with the app's `IServiceScopeFactory`. `RequestServices` therefore creates a real
  scope, and that scope is disposed by the response's `OnCompleted` callbacks.
- The reset happens **inside** the measured method. It costs 0 B (see the `Harness only` row), so it is not done in
  `[IterationSetup]`, which would distort the timings.
- Two categories:
  - **`EndpointBenchmarks`** invokes `RouteEndpoint.RequestDelegate` directly, with route values pre-set as routing
    would leave them. This measures binding, the handler and result writing.
  - **`PipelineBenchmarks`** runs `UseRouting()` → `UseEndpoints()`, adding route matching, route-value allocation
    and HTTP method matching.
- **Equivalence check.** `[GlobalSetup]` and program start run each scenario once per host and compare the status,
  content type and body (`BadRequest` compares the status only, because ASP.NET writes an empty 400 and KetPsi
  writes a short text). A scenario whose hosts disagree throws in `[GlobalSetup]`, so BenchmarkDotNet reports it as
  failed instead of comparing different work.

### Scenarios (`[ParamsAllValues] Scenario`)

| Scenario | Request | Handler result |
|----------|---------|----------------|
| `CachedResult` | `GET /items/42?a=1&b=2` (route int + 2 query ints) | cached `IResult` (empty 200) |
| `JsonResultsOk` | `GET /users/42` | `Results.Ok(new UserDto(...))` |
| `JsonTypedOk` | `GET /typed/users/42` | `TypedResults.Ok(new UserDto(...))` |
| `PostJsonBody` | `POST /users` with `{"name":"Ada","age":36}` | `Results.Ok(dto)` |
| `Header` | `GET /tenant` with `X-Tenant: acme` | `TypedResults.Ok(new TenantDto(...))` |
| `AsParameters` | `GET /search?page=2&pageSize=25&term=abc` | `TypedResults.Ok(new SearchResultDto(...))` |
| `BadRequest` | `GET /items/42?a=oops&b=2` | 400 from binding |
| `TaskOfDto` | `GET /async/users/42` | `Task<UserDto>` (the framework serializes T) |

## Running

```bash
# equivalence check only (exit code 1 on mismatch)
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --verify

# everything (BenchmarkSwitcher: any BenchmarkDotNet CLI option works)
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --filter "*" --job short

# one category
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --filter "*EndpointBenchmarks*"
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --filter "*PipelineBenchmarks*"

# quick "does it run" pass (timings meaningless, allocations already correct)
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --filter "*" --job dry
```

### Reading the results

- **`Allocated`** is the managed allocation per request on the server, for the work listed above. `Harness only`
  must be `-` (0 B). If it is not, the harness is leaking cost into every row.
- The ratio columns compare against `ASP.NET RDG` within the same scenario and category.
- The `Pipeline` minus `Endpoint` difference is the cost of routing.
- Not included: Kestrel's HTTP parsing and socket I/O, hosting diagnostics (`HostingApplication`, activity and
  logging scope), and the client. Use the throughput mode for those.

## Throughput mode (real Kestrel, load from a separate process)

```bash
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --throughput \
    [--duration 5] [--warmup 2] [--connections 32] [--scenario CachedResult,PostJsonBody] [--host KetPsi,AspNetRdg]
```

For each scenario and host, the server (this process) starts that host on Kestrel (loopback, dynamic port) and
starts **this same executable as a separate load-client process** (`--load-client`). The client warms up, prints
`WARM`, runs for `--duration` and prints `DONE ok/err`. The server samples **only its own** counters between those
two markers:

- `RPS`: requests the server handled, counted by an allocation-free middleware, divided by the elapsed time.
- `B/req`: `GC.GetTotalAllocatedBytes(precise: true)` delta divided by the request count. This includes Kestrel's
  parsing and I/O, hosting and routing. HttpClient is excluded because it runs in the other process.
- `CPU us/req`: server process CPU time divided by the request count.
- `Gen0/1/2`: server GC collections in the window.

The server runs with Server GC (the Web SDK default for this project). Client and server share the machine, so RPS depends on the hardware. Compare hosts relative to each other. The
allocation and CPU per request are much more stable than RPS.

### With an external load tool

```bash
dotnet run -c Release --project benchmarks/KetPsi.Endpoints.Http.Benchmarks -- --serve --host KetPsi --port 5005
```

The server prints `bombardier` command lines for every scenario, and every 5 s it prints its own stats:
`rps`, `alloc/req`, `cpu/req` and GC counts. For example:

```bash
bombardier -c 64 -d 10s -l "http://127.0.0.1:5005/items/42?a=1&b=2"
bombardier -c 64 -d 10s -l -H "Content-Type: application/json" -m POST -b '{"name":"Ada","age":36}' "http://127.0.0.1:5005/users"
wrk -t4 -c64 -d10s "http://127.0.0.1:5005/users/42"
```

## Native AOT smoke (KetPsi host)

`benchmarks/KetPsi.Endpoints.Http.AotSmoke` maps the same handlers, plus two `ApiResult` endpoints from
`KetPsi.Endpoints.Results`, with `PublishAot=true`. Trimming and AOT warnings are treated as errors.

```bash
dotnet publish benchmarks/KetPsi.Endpoints.Http.AotSmoke -c Release -r win-x64   # or linux-x64 (needs clang)
benchmarks/KetPsi.Endpoints.Http.AotSmoke/bin/Release/net10.0/win-x64/publish/KetPsi.Endpoints.Http.AotSmoke --smoke
```
