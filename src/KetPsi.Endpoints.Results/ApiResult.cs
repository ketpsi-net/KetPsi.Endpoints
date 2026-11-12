using System.Text.Json.Serialization;

namespace KetPsi.Endpoints.Results;

/// <summary>
/// Generic API outcome with an explicit HTTP status code and optional payload.
/// </summary>
/// <remarks>
/// Instances are immutable. Payload-less factories (<see cref="ApiResult.Ok()"/>,
/// <see cref="ApiResult.Accepted()"/>, <see cref="ApiResult.NoContent()"/>) return cached singletons,
/// so do not rely on reference identity to distinguish calls.
/// </remarks>
/// <typeparam name="T">Payload type for success / accepted responses.</typeparam>
public sealed class ApiResult<T>
{
    /// <summary>HTTP status code for this response.</summary>
    public int StatusCode { get; }

    /// <summary>Payload when the outcome is success or accepted; otherwise default.</summary>
    public T? Value { get; }

    /// <summary>Error list when the outcome is a failure; otherwise null.</summary>
    public IReadOnlyList<ApiResultError>? Errors { get; }

    /// <summary>
    /// Creates a new <see cref="ApiResult{T}"/>. Prefer the <see cref="ApiResult"/> factories in application code;
    /// this constructor exists so source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>s
    /// (Native AOT) can deserialize the type.
    /// </summary>
    /// <param name="statusCode">Http status code for the response.</param>
    /// <param name="value">Optional payload for success or accepted responses.</param>
    /// <param name="errors">Optional list of errors for failure responses.</param>
    [JsonConstructor]
    public ApiResult(int statusCode, T? value, IReadOnlyList<ApiResultError>? errors)
    {
        StatusCode = statusCode;
        Value = value;
        Errors = errors;
    }

    /// <summary>
    /// Creates a new <see cref="ApiResult{T}"/> instance with the specified status code, optional payload, and optional error list.
    /// </summary>
    /// <param name="statusCode">Http status code for the response.</param>
    /// <param name="value">Optional payload for success or accepted responses.</param>
    /// <param name="errors">Optional list of errors for failure responses.</param>
    /// <returns>A new <see cref="ApiResult{T}"/> instance.</returns>
    public static ApiResult<T> Create(int statusCode, T? value, IReadOnlyList<ApiResultError>? errors)
        => new(statusCode, value, errors);
}
