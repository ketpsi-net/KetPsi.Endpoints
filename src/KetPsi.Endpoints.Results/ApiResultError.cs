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
    public static ApiResultError From(ErrorResult error, int? statusCode = null)
        => new(
            error.Code,
            error.Message,
            error.Type.ToString(),
            GetMetadata(error.Metadata),
            statusCode ?? error.Type.ToStatusCode());

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
}
