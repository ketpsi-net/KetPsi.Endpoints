using KetPsi.Results;

namespace KetPsi.Endpoints.Results;

/// <summary>
/// A single API-facing error. Designed for JSON serialization at the HTTP boundary.
/// </summary>
/// <param name="Code">Stable machine-readable code (e.g. <c>resource.not_found</c>).</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="Title">Optional short title (often the <see cref="ErrorType"/> name).</param>
/// <param name="Metadata">Optional diagnostic bag (ids, provider codes, etc.).</param>
/// <param name="StatusCode">Optional per-error HTTP status hint.</param>
public sealed record ApiResultError(
    string Code,
    string Message,
    string? Title = null,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    int? StatusCode = null)
{
    /// <summary>
    /// Maps a <see cref="ErrorResult"/> from KetPsi.Results into an API error.
    /// </summary>
    /// <param name="error">The domain error.</param>
    /// <param name="statusCode">Explicit status; defaults to the status mapped from <see cref="ErrorResult.Type"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static ApiResultError From(ErrorResult error, int? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(
            error.Code,
            error.Message,
            ApiResultErrorHelpers.GetTitle(error.Type),
            ApiResultErrorHelpers.ToStringMetadata(error.Metadata),
            statusCode ?? error.Type.ToStatusCode());
    }
}
