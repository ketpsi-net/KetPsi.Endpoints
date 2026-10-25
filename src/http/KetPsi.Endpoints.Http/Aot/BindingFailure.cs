using Microsoft.AspNetCore.Http;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// Binding failure handling with Minimal API semantics: the response status code is set
/// (no body, no synchronous I/O, no allocation), or — when <see cref="EndpointEnvironment.ThrowOnBadRequest"/>
/// is on (Development by default) — a <see cref="BadHttpRequestException"/> with the same message RDF uses is thrown.
/// Messages are only formatted when throwing.
/// </summary>
public static class BindingFailure
{
    /// <summary>Required parameter missing from <paramref name="source"/> (e.g. "route", "query string", "header").</summary>
    public static void Required(HttpContext httpContext, EndpointEnvironment environment, string typeName, string parameterName, string source)
    {
        if (environment.ThrowOnBadRequest)
        {
            throw new BadHttpRequestException(
                $"Required parameter \"{typeName} {parameterName}\" was not provided from {source}.",
                StatusCodes.Status400BadRequest);
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
    }

    /// <summary>A value was supplied but could not be parsed.</summary>
    public static void Invalid(HttpContext httpContext, EndpointEnvironment environment, string typeName, string parameterName, string? rawValue)
    {
        if (environment.ThrowOnBadRequest)
        {
            throw new BadHttpRequestException(
                $"Failed to bind parameter \"{typeName} {parameterName}\" from \"{rawValue}\".",
                StatusCodes.Status400BadRequest);
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
    }

    /// <summary>Generic failure with a status code (415 wrong content type, 400 invalid JSON / antiforgery, …).</summary>
    public static void Status(HttpContext httpContext, EndpointEnvironment environment, int statusCode, string message)
    {
        if (environment.ThrowOnBadRequest)
            throw new BadHttpRequestException(message, statusCode);

        httpContext.Response.StatusCode = statusCode;
    }
}
