#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using KetPsi.Endpoints.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
// Alias: inside KetPsi.Endpoints.* "Results" would bind to the KetPsi.Endpoints.Results namespace when that assembly is referenced.
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace KetPsi.Endpoints.Http.Benchmarks;

// ───────────────────────────── DTOs + source-generated JSON (AOT representative) ─────────────────────────────

public sealed record UserDto(int Id, string Name);

public sealed record CreateUserRequest(string Name, int Age);

public sealed record TenantDto(string Tenant);

// Term is non-nullable on purpose: RDG (10.0) emits typeof(string?) for nullable ctor params of [AsParameters] records (CS8639).
public sealed record SearchQuery(int Page, int PageSize, string Term);

public sealed record SearchResultDto(int Page, int PageSize, string Term, int Total);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(TenantDto))]
[JsonSerializable(typeof(SearchQuery))]
[JsonSerializable(typeof(SearchResultDto))]
internal sealed partial class BenchJsonContext : JsonSerializerContext;

/// <summary>Values shared by every host so they all do exactly the same work.</summary>
public static class Shared
{
    public const string UserName = "Ada";

    /// <summary>The cached result returned by <see cref="Scenario.CachedResult"/>.</summary>
    public static readonly IResult CachedOk = HttpResults.Ok();

    public static readonly Task<IResult> CachedOkTask = Task.FromResult(CachedOk);
}

// ───────────────────────────── KetPsi handlers (mapped via MapGet<THandler>/MapPost<THandler>) ─────────────────────────────

public sealed class CachedResultHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, [FromQuery] int a, [FromQuery] int b, CancellationToken ct)
        => Shared.CachedOkTask;
}

public sealed class JsonResultsOkHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, CancellationToken ct)
        => Task.FromResult(HttpResults.Ok(new UserDto(id, Shared.UserName)));
}

public sealed class JsonTypedOkHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, CancellationToken ct)
        => Task.FromResult<IResult>(TypedResults.Ok(new UserDto(id, Shared.UserName)));
}

public sealed class PostJsonBodyHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] CreateUserRequest request, CancellationToken ct)
        => Task.FromResult(HttpResults.Ok(new UserDto(request.Age, request.Name)));
}

public sealed class HeaderHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromHeader(Name = "X-Tenant")] string tenant, CancellationToken ct)
        => Task.FromResult<IResult>(TypedResults.Ok(new TenantDto(tenant)));
}

public sealed class AsParametersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(SearchQuery query, CancellationToken ct)
        => Task.FromResult<IResult>(TypedResults.Ok(new SearchResultDto(query.Page, query.PageSize, query.Term, 42)));
}

/// <summary>External binding configuration for <see cref="AsParametersHandler"/> (read by the generator, never executed).</summary>
public sealed class AsParametersHandlerConfig : IHttpEndpointConfiguration<AsParametersHandler>
{
    public void Configure(IHttpEndpointBuilder<AsParametersHandler> builder)
    {
        builder.Query()
               .AsParameters()
               .Property(x => x.Page, p => p.FromQuery())
               .Property(x => x.PageSize, p => p.FromQuery())
               .Property(x => x.Term, p => p.FromQuery());
    }
}

public sealed class TaskOfDtoHandler : IEndpoint
{
    public Task<UserDto> ExecuteAsync(int id, CancellationToken ct)
        => Task.FromResult(new UserDto(id, Shared.UserName));
}
