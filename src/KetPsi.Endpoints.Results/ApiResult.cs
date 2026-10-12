namespace KetPsi.Endpoints.Results;

/// <summary>
/// Generic API outcome with an explicit HTTP status code and optional payload.
/// </summary>
/// <typeparam name="T">Payload type for success / accepted responses.</typeparam>
public sealed class ApiResult<T>
{
    /// <summary>HTTP status code for this response.</summary>
    public int StatusCode { get; }

    /// <summary>Payload when the outcome is success or accepted; otherwise default.</summary>
    public T? Value { get; }

    /// <summary>Error list when the outcome is a failure; otherwise null.</summary>
    public IReadOnlyList<ApiResultError>? Errors { get; }


    internal ApiResult(int statusCode, T? value, IReadOnlyList<ApiResultError>? errors)
    {
        StatusCode = statusCode;
        Value = value;
        Errors = errors;
    }
}
