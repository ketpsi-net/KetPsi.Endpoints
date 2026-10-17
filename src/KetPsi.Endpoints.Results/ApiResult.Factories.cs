namespace KetPsi.Endpoints.Results;

/// <summary>
/// Entry point for creating HTTP-oriented API results and mapping from KetPsi.Results.
/// </summary>
public static partial class ApiResult
{
    private const string NullReferenceCode = "result.null_reference";
    private const string NullReferenceMessage = "Cannot create an API result from a null domain result.";

    // ───────────────────────────── 2xx ─────────────────────────────

    /// <summary>200 OK with no payload.</summary>
    public static ApiResult<object?> Ok()
        => new(200, null, null);

    /// <summary>200 OK with payload.</summary>
    public static ApiResult<T> Ok<T>(T data)
        => new(200, data, null);

    /// <summary>201 Created with payload.</summary>
    public static ApiResult<T> Created<T>(T data)
        => new(201, data, null);

    /// <summary>202 Accepted with no payload.</summary>
    public static ApiResult<object?> Accepted()
        => new(202, null, null);

    /// <summary>202 Accepted with payload.</summary>
    public static ApiResult<T> Accepted<T>(T data)
        => new(202, data, null);

    /// <summary>204 No Content.</summary>
    public static ApiResult<object?> NoContent()
        => new(204, null, null);

    // ───────────────────────────── Client errors (4xx) ─────────────────────────────

    /// <summary>Factory methods for client (4xx) failures.</summary>
    public static class Request
    {
        public static ApiResult<object?> BadRequest(string code, string message, IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(400, code, message, "BadRequest", metadata);

        public static ApiResult<object?> BadRequest(params ApiResultError[] errors)
            => new(400, null, Normalize(errors, 400));

        public static ApiResult<T> BadRequest<T>(string code, string message, IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(400, code, message, "BadRequest", metadata);

        public static ApiResult<T> BadRequest<T>(params ApiResultError[] errors)
            => new(400, default, Normalize(errors, 400));

        public static ApiResult<object?> Unauthorized(string code = "auth.unauthorized", string message = "Unauthorized.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(401, code, message, "Unauthorized", metadata);

        public static ApiResult<T> Unauthorized<T>(string code = "auth.unauthorized", string message = "Unauthorized.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(401, code, message, "Unauthorized", metadata);

        public static ApiResult<object?> Forbidden(string code = "permissions.forbidden", string message = "Forbidden.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(403, code, message, "Forbidden", metadata);

        public static ApiResult<T> Forbidden<T>(string code = "permissions.forbidden", string message = "Forbidden.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(403, code, message, "Forbidden", metadata);

        public static ApiResult<object?> NotFound(string code = "resource.not_found", string message = "Resource was not found.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(404, code, message, "NotFound", metadata);

        public static ApiResult<T> NotFound<T>(string code = "resource.not_found", string message = "Resource was not found.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(404, code, message, "NotFound", metadata);

        public static ApiResult<object?> Conflict(string code = "resource.already_exists", string message = "The request conflicts with the current state.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(409, code, message, "Conflict", metadata);

        public static ApiResult<T> Conflict<T>(string code = "resource.already_exists", string message = "The request conflicts with the current state.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(409, code, message, "Conflict", metadata);

        public static ApiResult<object?> UnprocessableEntity(string code = "business.rule_violation", string message = "The request could not be processed.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(422, code, message, "UnprocessableEntity", metadata);

        public static ApiResult<T> UnprocessableEntity<T>(string code = "business.rule_violation", string message = "The request could not be processed.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(422, code, message, "UnprocessableEntity", metadata);

        public static ApiResult<object?> TooManyRequests(string code = "business.quota_exceeded", string message = "Quota or rate limit exceeded.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(429, code, message, "TooManyRequests", metadata);

        public static ApiResult<T> TooManyRequests<T>(string code = "business.quota_exceeded", string message = "Quota or rate limit exceeded.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(429, code, message, "TooManyRequests", metadata);

        public static ApiResult<object?> Canceled(string code = "system.execution_canceled", string message = "The operation was canceled.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(499, code, message, "Canceled", metadata);

        public static ApiResult<T> Canceled<T>(string code = "system.execution_canceled", string message = "The operation was canceled.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(499, code, message, "Canceled", metadata);
    }

    // ───────────────────────────── Server errors (5xx) ─────────────────────────────

    /// <summary>Factory methods for server (5xx) failures.</summary>
    public static class Server
    {
        public static ApiResult<object?> InternalError(string code = "server.internal_error", string message = "An unexpected internal error occurred.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(500, code, message, "InternalError", metadata);

        public static ApiResult<T> InternalError<T>(string code = "server.internal_error", string message = "An unexpected internal error occurred.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(500, code, message, "InternalError", metadata);

        public static ApiResult<object?> BadGateway(string code = "integration.bad_gateway", string message = "An upstream service returned an invalid response.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(502, code, message, "BadGateway", metadata);

        public static ApiResult<T> BadGateway<T>(string code = "integration.bad_gateway", string message = "An upstream service returned an invalid response.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(502, code, message, "BadGateway", metadata);

        public static ApiResult<object?> ServiceUnavailable(string code = "system.service_unavailable", string message = "A required dependency is currently unavailable.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(503, code, message, "ServiceUnavailable", metadata);

        public static ApiResult<T> ServiceUnavailable<T>(string code = "system.service_unavailable", string message = "A required dependency is currently unavailable.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(503, code, message, "ServiceUnavailable", metadata);

        public static ApiResult<object?> GatewayTimeout(string code = "integration.gateway_timeout", string message = "An upstream service timed out.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail(504, code, message, "GatewayTimeout", metadata);

        public static ApiResult<T> GatewayTimeout<T>(string code = "integration.gateway_timeout", string message = "An upstream service timed out.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(504, code, message, "GatewayTimeout", metadata);
    }

    // ───────────────────────────── Internal helpers ─────────────────────────────
    private static Dictionary<string, string?>? GetMetadata(IReadOnlyDictionary<string, object?>? errors)
    {
        if (errors is null)
            return null;

        var result = new Dictionary<string, string?>();
        foreach (var keyValue in errors)
        {
            result[keyValue.Key] = keyValue.Value?.ToString();
        }

        return result;
    }
    private static ApiResult<object?> Fail(
        int statusCode,
        string code,
        string message,
        string title,
        IReadOnlyDictionary<string, object?>? metadata)
        => new(statusCode, null, [new ApiResultError(code, message, title, GetMetadata(metadata), statusCode)]);

    private static ApiResult<T> Fail<T>(
        int statusCode,
        string code,
        string message,
        string title,
        IReadOnlyDictionary<string, object?>? metadata)
        => new(statusCode, default, [new ApiResultError(code, message, title, GetMetadata(metadata), statusCode)]);

    private static IReadOnlyList<ApiResultError> Normalize(ApiResultError[] errors, int defaultStatus)
    {
        if (errors.Length == 0)
            return errors;

        var copy = new ApiResultError[errors.Length];
        for (var i = 0; i < errors.Length; i++)
        {
            var e = errors[i];
            copy[i] = e.StatusCode is null ? e with { StatusCode = defaultStatus } : e;
        }
        return copy;
    }

    internal static ApiResultError[] NullReferenceErrors()
        => [new ApiResultError(NullReferenceCode, NullReferenceMessage, "NullReference", null, 500)];
}
