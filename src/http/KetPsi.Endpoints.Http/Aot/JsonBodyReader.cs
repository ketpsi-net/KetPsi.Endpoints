using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// AOT-safe JSON body reading through a pre-resolved <see cref="JsonTypeInfo{T}"/>
/// (resolved once per endpoint by <see cref="EndpointEnvironment.GetJsonTypeInfo{T}"/>).
/// </summary>
public static class JsonBodyReader
{
    /// <summary>
    /// Reads the body with Minimal API semantics:
    /// no body → value absent (400 when <paramref name="required"/>); non-JSON content type → 415;
    /// malformed JSON → 400. When <see cref="JsonBodyResult{T}.Success"/> is false the status code is already set
    /// (or a <see cref="BadHttpRequestException"/> was thrown when <see cref="EndpointEnvironment.ThrowOnBadRequest"/> is on).
    /// Completes without allocating when the body is already buffered.
    /// </summary>
    public static async ValueTask<JsonBodyResult<T>> TryReadAsync<T>(
        HttpContext httpContext,
        JsonTypeInfo<T> typeInfo,
        EndpointEnvironment environment,
        bool required,
        string typeName,
        string parameterName)
    {
        T? value = default;
        var hasValue = false;

        if (ParameterBinder.RequestHasBody(httpContext))
        {
            if (!httpContext.Request.HasJsonContentType())
            {
                BindingFailure.Status(httpContext, environment, StatusCodes.Status415UnsupportedMediaType,
                    $"Expected a supported JSON media type but got \"{httpContext.Request.ContentType}\".");
                return default;
            }

            try
            {
                // ReadFromJsonAsync handles charset transcoding and (on .NET 10) reads through the PipeReader.
                value = await httpContext.Request.ReadFromJsonAsync(typeInfo).ConfigureAwait(false);
                hasValue = value is not null;
            }
            catch (JsonException)
            {
                BindingFailure.Status(httpContext, environment, StatusCodes.Status400BadRequest,
                    $"Failed to read parameter \"{typeName} {parameterName}\" from the request body as JSON.");
                return default;
            }
            catch (BadHttpRequestException ex)
            {
                if (environment.ThrowOnBadRequest)
                    throw;
                httpContext.Response.StatusCode = ex.StatusCode;
                return default;
            }
            catch (IOException)
            {
                // Client disconnected while the body was read.
                return default;
            }
        }

        if (!hasValue && required)
        {
            BindingFailure.Required(httpContext, environment, typeName, parameterName, "body");
            return default;
        }

        return new JsonBodyResult<T>(true, value);
    }

    /// <summary>Reads <typeparamref name="T"/> with a caller-supplied type info (no per-call option resolution).</summary>
    public static ValueTask<T?> ReadAsync<T>(HttpContext httpContext, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => httpContext.Request.ReadFromJsonAsync(typeInfo, cancellationToken);

    /// <summary>
    /// Convenience overload that resolves the type info from the request's host options.
    /// Generated endpoints never use it (they cache the type info per endpoint).
    /// </summary>
    public static ValueTask<T?> ReadAsync<T>(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var typeInfo = (JsonTypeInfo<T>)ResolveOptions(httpContext).GetTypeInfo(typeof(T));
        return httpContext.Request.ReadFromJsonAsync(typeInfo, cancellationToken);
    }

    /// <summary>Host <c>JsonOptions.SerializerOptions</c> (a shared Web-defaults instance when none is registered). Never mutated.</summary>
    public static JsonSerializerOptions ResolveOptions(HttpContext httpContext)
        => EndpointEnvironment.ResolveJsonOptions(httpContext.RequestServices);
}

/// <summary>Result of <see cref="JsonBodyReader.TryReadAsync{T}"/>.</summary>
public readonly struct JsonBodyResult<T>(bool success, T? value)
{
    /// <summary>False when binding failed and the response status was already set.</summary>
    public bool Success { get; } = success;

    /// <summary>The deserialized value (default when no body was sent and the parameter is optional).</summary>
    public T? Value { get; } = value;
}
