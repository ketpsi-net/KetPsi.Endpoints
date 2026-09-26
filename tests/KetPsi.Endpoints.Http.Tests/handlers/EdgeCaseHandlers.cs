using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HttpEndpointGenerator.Tests.Handlers;

// -------------------------------------------------------
// No parameters at all
// -------------------------------------------------------
public class PingHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync()
    {
        return Task.FromResult(Results.Ok(new { Pong = true }));
    }
}

// -------------------------------------------------------
// Only CancellationToken
// -------------------------------------------------------
public class HealthHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Status = "Healthy" }));
    }
}

// -------------------------------------------------------
// Single-property AsParameters
// -------------------------------------------------------
public record SingleFilter(string? Term);

public class SearchByTermHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(SingleFilter filter, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { filter.Term, Kind = "SingleProperty" }));
    }
}

// -------------------------------------------------------
// Init-only properties DTO
// -------------------------------------------------------
public class InitOnlyCriteria
{
    public string? Category { get; init; }
    public int? MinScore { get; init; }
}

public class SearchInitOnlyHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(InitOnlyCriteria criteria, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            criteria.Category,
            criteria.MinScore,
            Kind = "InitOnly"
        }));
    }
}

// -------------------------------------------------------
// Required members (C# 11+)
// -------------------------------------------------------
public class RequiredFilter
{
    public required string Code { get; set; }
    public string? OptionalNote { get; set; }
}

public class LookupByCodeHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(RequiredFilter filter, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            filter.Code,
            filter.OptionalNote,
            Kind = "RequiredMember"
        }));
    }
}

// -------------------------------------------------------
// Empty-ish record (all optional)
// -------------------------------------------------------
public record EmptyishFilter(string? A = null, int? B = null);

public class EmptyishHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(EmptyishFilter filter, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { filter.A, filter.B, Kind = "Emptyish" }));
    }
}

// -------------------------------------------------------
// Attribute collision: inline [FromQuery] on parameter + external config
// -------------------------------------------------------
public record CollisionFilter(string? Q, int? Page = 1);

public class CollisionHandler : IEndpoint
{
    // Inline attribute on the complex parameter itself
    public Task<IResult> ExecuteAsync(
        CollisionFilter filter,
        CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            filter.Q,
            filter.Page,
            Kind = "Collision"
        }));
    }
}

// -------------------------------------------------------
// Non-nullable string path param (required by route)
// -------------------------------------------------------
public class GetBySlugHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(string slug, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Slug = slug }));
    }
}

// -------------------------------------------------------
// Multiple simple query params without AsParameters
// -------------------------------------------------------
public class MultiQueryHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromQuery] string? a,
        [FromQuery] string? b,
        [FromQuery] int? n,
        CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { a, b, n }));
    }
}

// -------------------------------------------------------
// Header-only endpoint
// -------------------------------------------------------
public class HeaderEchoHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromHeader(Name = "X-Correlation-ID")] string? correlationId,
        [FromHeader(Name = "X-Client")] string? client,
        CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { CorrelationId = correlationId, Client = client }));
    }
}

// -------------------------------------------------------
// POST with empty body allowed (nullable body)
// -------------------------------------------------------
public record OptionalBody(string? Note);

public class OptionalBodyHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] OptionalBody? body, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Note = body?.Note, Kind = "OptionalBody" }));
    }
}

// -------------------------------------------------------
// ValueTask return
// -------------------------------------------------------
public class ValueTaskHandler : IEndpoint
{
    public ValueTask<IResult> ExecuteAsync(int id, CancellationToken ct)
    {
        return ValueTask.FromResult(Results.Ok(new { Id = id, Kind = "ValueTask" }));
    }
}
