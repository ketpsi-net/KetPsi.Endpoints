using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace KetPsi.Endpoints.Http.Benchmarks;

/// <summary>The request shapes every host maps identically (see <see cref="BenchHosts"/>).</summary>
public enum Scenario
{
    /// <summary>GET route int + two query ints; handler returns a cached <see cref="IResult"/> (empty 200).</summary>
    CachedResult,

    /// <summary>GET route int; handler returns <c>Results.Ok(dto)</c> (JSON via runtime type).</summary>
    JsonResultsOk,

    /// <summary>GET route int; handler returns <c>TypedResults.Ok(dto)</c>.</summary>
    JsonTypedOk,

    /// <summary>POST JSON body; handler echoes a DTO built from it.</summary>
    PostJsonBody,

    /// <summary>GET with a header-bound string.</summary>
    Header,

    /// <summary>GET with an [AsParameters] DTO bound from three query values.</summary>
    AsParameters,

    /// <summary>Same endpoint as <see cref="CachedResult"/> but a query int fails to parse → 400.</summary>
    BadRequest,

    /// <summary>GET route int; handler returns <c>Task&lt;UserDto&gt;</c> (framework serializes T).</summary>
    TaskOfDto,
}

/// <summary>A request template. Route values are what routing would produce for the endpoint-only benchmarks.</summary>
public sealed record BenchRequest(
    string Method,
    string Pattern,
    string Path,
    string Query,
    (string Name, string Value)[] RouteValues,
    (string Name, string Value)[] Headers,
    byte[]? Body)
{
    public IHeaderDictionary CreateHeaders()
    {
        var headers = new HeaderDictionary();
        foreach (var (name, value) in Headers)
            headers[name] = new StringValues(value);
        headers["Host"] = "localhost";
        if (Body is not null)
            headers.ContentLength = Body.Length;
        return headers;
    }

    public string PathAndQuery => Path + Query;
}

public static class Scenarios
{
    public const string ItemsPattern = "/items/{id:int}";
    public const string UsersPattern = "/users/{id:int}";
    public const string TypedUsersPattern = "/typed/users/{id:int}";
    public const string CreateUserPattern = "/users";
    public const string TenantPattern = "/tenant";
    public const string SearchPattern = "/search";
    public const string AsyncUsersPattern = "/async/users/{id:int}";

    private static readonly (string, string)[] NoValues = [];
    private static readonly (string, string)[] Id42 = [("id", "42")];

    public static BenchRequest Get(Scenario scenario) => scenario switch
    {
        Scenario.CachedResult => new("GET", ItemsPattern, "/items/42", "?a=1&b=2", Id42, NoValues, null),
        Scenario.JsonResultsOk => new("GET", UsersPattern, "/users/42", "", Id42, NoValues, null),
        Scenario.JsonTypedOk => new("GET", TypedUsersPattern, "/typed/users/42", "", Id42, NoValues, null),
        Scenario.PostJsonBody => new("POST", CreateUserPattern, "/users", "", NoValues,
            [("Content-Type", "application/json")],
            Encoding.UTF8.GetBytes("""{"name":"Ada","age":36}""")),
        Scenario.Header => new("GET", TenantPattern, "/tenant", "", NoValues, [("X-Tenant", "acme")], null),
        Scenario.AsParameters => new("GET", SearchPattern, "/search", "?page=2&pageSize=25&term=abc", NoValues, NoValues, null),
        Scenario.BadRequest => new("GET", ItemsPattern, "/items/42", "?a=oops&b=2", Id42, NoValues, null),
        Scenario.TaskOfDto => new("GET", AsyncUsersPattern, "/async/users/42", "", Id42, NoValues, null),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    /// <summary>
    /// Error responses are allowed to differ in body between frameworks (ASP.NET writes an empty 400 in Production,
    /// KetPsi writes a fixed UTF-8 message); status code must still match.
    /// </summary>
    public static bool CompareStatusOnly(Scenario scenario) => scenario == Scenario.BadRequest;
}
