namespace KetPsi.Endpoints.Results;

/// <summary>
/// Entry point for creating HTTP-oriented API results and mapping from KetPsi.Results.
/// </summary>
public static partial class ApiResult
{
    private const string NullReferenceCode = "result.null_reference";
    private const string NullReferenceMessage = "Cannot create an API result from a null domain result.";

    // Payload-less results are immutable, so a single instance per status is shared (zero allocation per call).
    private static readonly ApiResult<object?> s_ok = new(200, null, null);
    private static readonly ApiResult<object?> s_accepted = new(202, null, null);
    private static readonly ApiResult<object?> s_noContent = new(204, null, null);

    // ───────────────────────────── 2xx ─────────────────────────────

    /// <summary>200 OK with no payload. Returns a cached instance (no allocation).</summary>
    public static ApiResult<object?> Ok() => s_ok;

    /// <summary>200 OK with payload.</summary>
    public static ApiResult<T> Ok<T>(T data)
        => new(200, data, null);

    /// <summary>201 Created with payload.</summary>
    public static ApiResult<T> Created<T>(T data)
        => new(201, data, null);

    /// <summary>202 Accepted with no payload. Returns a cached instance (no allocation).</summary>
    public static ApiResult<object?> Accepted() => s_accepted;

    /// <summary>202 Accepted with payload.</summary>
    public static ApiResult<T> Accepted<T>(T data)
        => new(202, data, null);

    /// <summary>204 No Content. Returns a cached instance (no allocation).</summary>
    public static ApiResult<object?> NoContent() => s_noContent;

    // ───────────────────────────── Client errors (4xx) ─────────────────────────────

    /// <summary>Factory methods for client (4xx) failures.</summary>
    /// <remarks>
    /// Every factory taking <c>code</c>/<c>message</c> produces a single <see cref="ApiResultError"/> whose
    /// <see cref="ApiResultError.StatusCode"/> equals the result status. Metadata values are formatted with the
    /// invariant culture.
    /// </remarks>
    public static class Request
    {
        /// <summary>400 Bad Request with a single error.</summary>
        public static ApiResult<object?> BadRequest(string code, string message, IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(400, code, message, "BadRequest", metadata);

        /// <summary>400 Bad Request with the given errors. Errors without a status get 400.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="errors"/> contains a null element.</exception>
        public static ApiResult<object?> BadRequest(params ApiResultError[] errors)
            => new(400, null, Normalize(errors, 400));

        /// <summary>400 Bad Request with a single error.</summary>
        public static ApiResult<T> BadRequest<T>(string code, string message, IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(400, code, message, "BadRequest", metadata);

        /// <summary>400 Bad Request with the given errors. Errors without a status get 400.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="errors"/> contains a null element.</exception>
        public static ApiResult<T> BadRequest<T>(params ApiResultError[] errors)
            => new(400, default, Normalize(errors, 400));

        /// <summary>401 Unauthorized.</summary>
        public static ApiResult<object?> Unauthorized(string code = "auth.unauthorized", string message = "Unauthorized.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(401, code, message, "Unauthorized", metadata);

        /// <summary>401 Unauthorized.</summary>
        public static ApiResult<T> Unauthorized<T>(string code = "auth.unauthorized", string message = "Unauthorized.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(401, code, message, "Unauthorized", metadata);

        /// <summary>403 Forbidden.</summary>
        public static ApiResult<object?> Forbidden(string code = "permissions.forbidden", string message = "Forbidden.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(403, code, message, "Forbidden", metadata);

        /// <summary>403 Forbidden.</summary>
        public static ApiResult<T> Forbidden<T>(string code = "permissions.forbidden", string message = "Forbidden.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(403, code, message, "Forbidden", metadata);

        /// <summary>404 Not Found.</summary>
        public static ApiResult<object?> NotFound(string code = "resource.not_found", string message = "Resource was not found.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(404, code, message, "NotFound", metadata);

        /// <summary>404 Not Found.</summary>
        public static ApiResult<T> NotFound<T>(string code = "resource.not_found", string message = "Resource was not found.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(404, code, message, "NotFound", metadata);

        /// <summary>409 Conflict.</summary>
        public static ApiResult<object?> Conflict(string code = "resource.already_exists", string message = "The request conflicts with the current state.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(409, code, message, "Conflict", metadata);

        /// <summary>409 Conflict.</summary>
        public static ApiResult<T> Conflict<T>(string code = "resource.already_exists", string message = "The request conflicts with the current state.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(409, code, message, "Conflict", metadata);

        /// <summary>422 Unprocessable Entity.</summary>
        public static ApiResult<object?> UnprocessableEntity(string code = "business.rule_violation", string message = "The request could not be processed.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(422, code, message, "UnprocessableEntity", metadata);

        /// <summary>422 Unprocessable Entity.</summary>
        public static ApiResult<T> UnprocessableEntity<T>(string code = "business.rule_violation", string message = "The request could not be processed.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(422, code, message, "UnprocessableEntity", metadata);

        /// <summary>429 Too Many Requests.</summary>
        public static ApiResult<object?> TooManyRequests(string code = "business.quota_exceeded", string message = "Quota or rate limit exceeded.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(429, code, message, "TooManyRequests", metadata);

        /// <summary>429 Too Many Requests.</summary>
        public static ApiResult<T> TooManyRequests<T>(string code = "business.quota_exceeded", string message = "Quota or rate limit exceeded.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(429, code, message, "TooManyRequests", metadata);

        /// <summary>499 Client Closed Request (operation canceled).</summary>
        public static ApiResult<object?> Canceled(string code = "system.execution_canceled", string message = "The operation was canceled.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(499, code, message, "Canceled", metadata);

        /// <summary>499 Client Closed Request (operation canceled).</summary>
        public static ApiResult<T> Canceled<T>(string code = "system.execution_canceled", string message = "The operation was canceled.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(499, code, message, "Canceled", metadata);
    }

    // ───────────────────────────── Server errors (5xx) ─────────────────────────────

    /// <summary>Factory methods for server (5xx) failures.</summary>
    public static class Server
    {
        /// <summary>500 Internal Server Error.</summary>
        public static ApiResult<object?> InternalError(string code = "server.internal_error", string message = "An unexpected internal error occurred.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(500, code, message, "InternalError", metadata);

        /// <summary>500 Internal Server Error.</summary>
        public static ApiResult<T> InternalError<T>(string code = "server.internal_error", string message = "An unexpected internal error occurred.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(500, code, message, "InternalError", metadata);

        /// <summary>502 Bad Gateway.</summary>
        public static ApiResult<object?> BadGateway(string code = "integration.bad_gateway", string message = "An upstream service returned an invalid response.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(502, code, message, "BadGateway", metadata);

        /// <summary>502 Bad Gateway.</summary>
        public static ApiResult<T> BadGateway<T>(string code = "integration.bad_gateway", string message = "An upstream service returned an invalid response.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(502, code, message, "BadGateway", metadata);

        /// <summary>503 Service Unavailable.</summary>
        public static ApiResult<object?> ServiceUnavailable(string code = "system.service_unavailable", string message = "A required dependency is currently unavailable.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(503, code, message, "ServiceUnavailable", metadata);

        /// <summary>503 Service Unavailable.</summary>
        public static ApiResult<T> ServiceUnavailable<T>(string code = "system.service_unavailable", string message = "A required dependency is currently unavailable.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(503, code, message, "ServiceUnavailable", metadata);

        /// <summary>504 Gateway Timeout.</summary>
        public static ApiResult<object?> GatewayTimeout(string code = "integration.gateway_timeout", string message = "An upstream service timed out.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<object?>(504, code, message, "GatewayTimeout", metadata);

        /// <summary>504 Gateway Timeout.</summary>
        public static ApiResult<T> GatewayTimeout<T>(string code = "integration.gateway_timeout", string message = "An upstream service timed out.", IReadOnlyDictionary<string, object?>? metadata = null)
            => Fail<T>(504, code, message, "GatewayTimeout", metadata);
    }

    // ───────────────────────────── Internal helpers ─────────────────────────────

    private static ApiResult<T> Fail<T>(
        int statusCode,
        string code,
        string message,
        string title,
        IReadOnlyDictionary<string, object?>? metadata)
        => new(statusCode, default, new[]
        {
            new ApiResultError(code, message, title, ApiResultErrorHelpers.ToStringMetadata(metadata), statusCode),
        });

    private static IReadOnlyList<ApiResultError> Normalize(ApiResultError[] errors, int defaultStatus)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Length == 0)
            return Array.Empty<ApiResultError>();

        // Always copy: the caller owns the params array and could mutate it after the result is created.
        var copy = new ApiResultError[errors.Length];
        for (var i = 0; i < errors.Length; i++)
        {
            var e = errors[i] ?? throw new ArgumentException($"errors[{i}] is null.", nameof(errors));
            copy[i] = e.StatusCode is null ? e with { StatusCode = defaultStatus } : e;
        }
        return copy;
    }

    internal static ApiResultError[] NullReferenceErrors()
        => new[] { new ApiResultError(NullReferenceCode, NullReferenceMessage, "NullReference", null, 500) };
}
