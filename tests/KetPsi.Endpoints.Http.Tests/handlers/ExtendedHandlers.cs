using System.IO.Pipelines;
using System.Security.Claims;

using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace HttpEndpointGenerator.Tests.Handlers;

// =======================================================
// Supporting types
// =======================================================
public enum Color
{
    Red,
    Green,
    Blue,
}

/// <summary>Custom parsable type via static TryParse(string, out T).</summary>
public readonly record struct GridPoint(int X, int Y)
{
    public static bool TryParse(string? value, out GridPoint point)
    {
        point = default;
        var parts = value?.Split(',');
        if (parts is not { Length: 2 } || !int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y))
            return false;
        point = new GridPoint(x, y);
        return true;
    }
}

/// <summary>Custom type bound with static BindAsync(HttpContext).</summary>
public sealed class ClientInfo
{
    public string Name { get; init; } = "";

    public static ValueTask<ClientInfo?> BindAsync(HttpContext context)
        => ValueTask.FromResult(context.Request.Headers.TryGetValue("X-Client-Name", out var v)
            ? new ClientInfo { Name = v.ToString() }
            : null);
}

public sealed record PointBody(int X, int Y);

public readonly record struct StructBody(int Value);

public sealed class PageA
{
    public int? Size { get; set; }
    public string? Name { get; set; }
}

public sealed record PageB(int? Size, string? Sort = "asc");

// =======================================================
// Binding matrix (query, all optional)
// =======================================================
public class TypesHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        short? s16, byte? u8, uint? u32, ulong? u64, decimal? dec, float? f32, double? f64, bool? flag,
        Guid? guid, DateTime? dt, DateTimeOffset? dto, DateOnly? date, TimeOnly? time, TimeSpan? span,
        Color? color, char? ch, Uri? uri, GridPoint? point, int[] ids, List<string> tags, StringValues raw,
        CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new Dictionary<string, string?>
        {
            ["s16"] = s16?.ToString(),
            ["u8"] = u8?.ToString(),
            ["u32"] = u32?.ToString(),
            ["u64"] = u64?.ToString(),
            ["dec"] = dec?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["f32"] = f32?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["f64"] = f64?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["flag"] = flag?.ToString(),
            ["guid"] = guid?.ToString(),
            ["dt"] = dt?.ToString("O"),
            ["dto"] = dto?.ToString("O"),
            ["date"] = date?.ToString("O"),
            ["time"] = time?.ToString("O"),
            ["span"] = span?.ToString(),
            ["color"] = color?.ToString(),
            ["ch"] = ch?.ToString(),
            ["uri"] = uri?.ToString(),
            ["point"] = point is { } p ? $"{p.X}:{p.Y}" : null,
            ["ids"] = string.Join("|", ids),
            ["tags"] = string.Join("|", tags),
            ["raw"] = raw.ToString(),
        }));
    }
}

// =======================================================
// Required / defaults / names
// =======================================================
public class RequiredHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        int id,
        int page,
        [FromQuery(Name = "q")] string query,
        [FromHeader(Name = "X-Count")] int count,
        string format = "json",
        int size = 10,
        CancellationToken ct = default)
        => Task.FromResult(Results.Ok(new { id, page, query, count, format, size }));
}

public class OptionalRouteHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int? id) => Task.FromResult(Results.Ok(new { id }));
}

public class CatchAllHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(string path) => Task.FromResult(Results.Ok(new { path }));
}

public class RouteBoolHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(bool flag, [FromRoute(Name = "code")] string routeCode)
        => Task.FromResult(Results.Ok(new { flag, routeCode }));
}

public class GroupRouteParamHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(string tenantId, int id) => Task.FromResult(Results.Ok(new { tenantId, id }));
}

// =======================================================
// Special types, implicit services, optional services
// =======================================================
public class SpecialsHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        HttpContext context,
        HttpRequest request,
        HttpResponse response,
        ClaimsPrincipal user,
        ILogger<SpecialsHandler> logger,
        IDocumentService docs,
        [FromServices] IBlobStorage? missing,
        [FromKeyedServices("secondary")] IBlobStorage keyed,
        ClientInfo? client)
        => Task.FromResult(Results.Ok(new
        {
            SameContext = ReferenceEquals(context.Request, request) && ReferenceEquals(context.Response, response),
            HasUser = user is not null,
            HasLogger = logger is not null,
            Docs = docs.GetName(),
            MissingIsNull = missing is null,
            Keyed = keyed.Provider,
            Client = client?.Name,
        }));
}

public class StreamHandler : IEndpoint
{
    public async Task<IResult> ExecuteAsync(Stream body, CancellationToken ct)
    {
        using var reader = new StreamReader(body);
        return Results.Ok(new { text = await reader.ReadToEndAsync(ct) });
    }
}

public class PipeReaderHandler : IEndpoint
{
    public async Task<IResult> ExecuteAsync(PipeReader reader)
    {
        var result = await reader.ReadAtLeastAsync(1);
        var length = result.Buffer.Length;
        reader.AdvanceTo(result.Buffer.End);
        return Results.Ok(new { length });
    }
}

// =======================================================
// Body inference and body edge cases
// =======================================================
public class InferredBodyHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(PointBody point) => Task.FromResult(Results.Ok(new { point.X, point.Y }));
}

public class StructBodyHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] StructBody body) => Task.FromResult(Results.Ok(new { body.Value }));
}

// =======================================================
// Result shapes
// =======================================================
public class PlainTaskHandler : IEndpoint
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.Headers["X-Plain"] = "task";
        return Task.CompletedTask;
    }
}

public class PlainValueTaskHandler : IEndpoint
{
    public ValueTask ExecuteAsync() => ValueTask.CompletedTask;
}

public class VoidHandler : IEndpoint
{
    public void ExecuteAsync(HttpResponse response) => response.Headers["X-Void"] = "1";
}

public class TextHandler : IEndpoint
{
    public Task<string> ExecuteAsync(string? name) => Task.FromResult($"hello {name}");
}

public class SyncTextHandler : IEndpoint
{
    public string ExecuteAsync() => "sync";
}

public record WeatherDto(string City, int Celsius);

public class JsonHandler : IEndpoint
{
    public Task<WeatherDto> ExecuteAsync(string city = "Berlin") => Task.FromResult(new WeatherDto(city, 21));
}

public class NullJsonHandler : IEndpoint
{
    public Task<WeatherDto?> ExecuteAsync() => Task.FromResult<WeatherDto?>(null);
}

public class ValueTaskJsonHandler : IEndpoint
{
    public ValueTask<WeatherDto> ExecuteAsync() => ValueTask.FromResult(new WeatherDto("Oslo", 3));
}

public class NullResultHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync() => Task.FromResult<IResult>(null!);
}

public class TypedResultHandler : IEndpoint
{
    public Task<Microsoft.AspNetCore.Http.HttpResults.Ok<WeatherDto>> ExecuteAsync()
        => Task.FromResult(TypedResults.Ok(new WeatherDto("Rome", 30)));
}

// =======================================================
// Forms
// =======================================================
public class FormFieldsHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromForm] int count,
        [FromForm] string? note,
        [FromForm(Name = "tag")] string[] tags,
        IFormCollection form,
        IFormFileCollection files)
        => Task.FromResult(Results.Ok(new { count, note, tags, keys = form.Keys.Count, files = files.Count }));
}

public class ProtectedFormHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromForm] string name) => Task.FromResult(Results.Ok(new { name }));
}

// =======================================================
// Filters
// =======================================================
public class FilterEchoHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, HttpContext context)
    {
        var trace = context.Items.TryGetValue("trace", out var t) ? (List<string>)t! : new List<string>();
        trace.Add("handler");
        return Task.FromResult(Results.Ok(new { id, trace }));
    }
}

public class FilterPlainTaskHandler : IEndpoint
{
    public Task ExecuteAsync(HttpContext context)
    {
        context.Response.Headers["X-Handler"] = "ran";
        return Task.CompletedTask;
    }
}

// =======================================================
// Name collisions / AsParameters edge cases / escaping / misc
// =======================================================
public class CollidingNamesHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int httpContext, string? handler, int? __result, int? __handlerTask, string? __hasBody)
        => Task.FromResult(Results.Ok(new { httpContext, handler, __result, __handlerTask, __hasBody }));
}

public class TwoDtosHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([AsParameters] PageA a, [AsParameters] PageB b)
        => Task.FromResult(Results.Ok(new { a = a.Size, aName = a.Name, b = b.Size, b.Sort }));
}

[Obsolete("Use \"v2\" \\ now")]
public class EscapeHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromHeader(Name = "X-\"Q\"")] string? q,
        [FromKeyedServices("we\"ird\\key")] IBlobStorage? storage)
        => Task.FromResult(Results.Ok(new { q, storage = storage?.Provider }));
}

public class DynamicRouteHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync() => Task.FromResult(Results.Ok(new { route = "dynamic" }));
}

public class HelperHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync() => Task.FromResult(Results.Ok(new { helper = true }));
}

public class GroupHelperHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id) => Task.FromResult(Results.Ok(new { id, helper = "group" }));
}

/// <summary>Static handler returning a cached result: used by the allocation test.</summary>
public class AllocationHandler : IEndpoint
{
    private sealed class NoContentResult : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }
    }

    private static readonly Task<IResult> Cached = Task.FromResult<IResult>(new NoContentResult());

    public static Task<IResult> ExecuteAsync(int id, int page) => Cached;
}
