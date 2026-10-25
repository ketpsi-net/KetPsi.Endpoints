using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// Low-allocation binding helpers used by the KetPsi request-delegate path.
/// Values are read from the existing request collections (no intermediate strings) and parsed in place.
/// </summary>
public static class ParameterBinder
{
    public static CancellationToken Cancellation(HttpContext httpContext) => httpContext.RequestAborted;

    public static HttpContext HttpContext(HttpContext httpContext) => httpContext;

    public static T GetRequiredService<T>(HttpContext httpContext) where T : notnull
        => httpContext.RequestServices.GetRequiredService<T>();

    public static T? GetService<T>(HttpContext httpContext)
        => httpContext.RequestServices.GetService<T>();

    public static object GetRequiredKeyedService(HttpContext httpContext, Type serviceType, object key)
        => httpContext.RequestServices.GetRequiredKeyedService(serviceType, key);

    public static T GetRequiredKeyedService<T>(HttpContext httpContext, object key) where T : notnull
        => httpContext.RequestServices.GetRequiredKeyedService<T>(key);

    // ------------------------------------------------------------------
    // Raw value access (no allocation for string route values / single-valued query & headers)
    // ------------------------------------------------------------------

    /// <summary>Route value as string, or null when absent.</summary>
    public static string? GetRouteValue(HttpContext httpContext, string name)
    {
        if (httpContext.Request.RouteValues.TryGetValue(name, out var raw) && raw is not null)
            return raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>Form field as string (comma-joined when multi-valued), or null when absent / no form.</summary>
    public static string? GetFormValue(IFormCollection? form, string name)
        => form is not null && form.TryGetValue(name, out var values) ? (string?)values : null;

    /// <summary>Form field values, or <see cref="StringValues.Empty"/> when absent / no form.</summary>
    public static StringValues GetFormValues(IFormCollection? form, string name)
        => form is not null && form.TryGetValue(name, out var values) ? values : StringValues.Empty;

    /// <summary>Uploaded file by name, or null when absent / no form.</summary>
    public static IFormFile? GetFormFile(IFormCollection? form, string name)
        => form?.Files.GetFile(name);

    // ------------------------------------------------------------------
    // Parsing (Minimal API / RDF compatible styles, invariant culture)
    // ------------------------------------------------------------------

    public static bool TryParse<T>(string value, [MaybeNullWhen(false)] out T result) where T : IParsable<T>
        => T.TryParse(value, CultureInfo.InvariantCulture, out result);

    public static bool TryParseEnum<T>(string value, out T result) where T : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out result);

    public static bool TryParseUri(string value, [NotNullWhen(true)] out Uri? result)
        => Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out result);

    public static bool TryParseDateTime(string value, out DateTime result)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out result);

    public static bool TryParseDateTimeOffset(string value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out result);

    public static bool TryParseDateOnly(string value, out DateOnly result)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result);

    public static bool TryParseTimeOnly(string value, out TimeOnly result)
        => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result);

    /// <summary><see cref="IBindableFromHttpContext{TSelf}"/> binding (also covers explicit interface implementations).</summary>
    public static ValueTask<T?> BindAsync<T>(HttpContext httpContext, ParameterInfo parameter) where T : class, IBindableFromHttpContext<T>
        => T.BindAsync(httpContext, parameter);

    // ------------------------------------------------------------------
    // Form
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads the request form once with Minimal API semantics: no body → <see cref="FormReadResult.Form"/> null;
    /// non-form content type → 415; failed antiforgery validation → 400; malformed form → 400.
    /// </summary>
    public static async ValueTask<FormReadResult> ReadFormAsync(HttpContext httpContext, EndpointEnvironment environment)
    {
        if (!RequestHasBody(httpContext))
            return new FormReadResult(true, null);

        // Must be checked before touching the form feature (it throws for unchecked invalid tokens).
        if (httpContext.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
        {
            BindingFailure.Status(httpContext, environment, StatusCodes.Status400BadRequest, "Invalid anti-forgery token found when reading the form.");
            return default;
        }

        if (!httpContext.Request.HasFormContentType)
        {
            BindingFailure.Status(httpContext, environment, StatusCodes.Status415UnsupportedMediaType,
                $"Expected a supported form media type but got \"{httpContext.Request.ContentType}\".");
            return default;
        }

        try
        {
            var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted).ConfigureAwait(false);
            return new FormReadResult(true, form);
        }
        catch (BadHttpRequestException ex)
        {
            if (environment.ThrowOnBadRequest)
                throw;
            httpContext.Response.StatusCode = ex.StatusCode;
            return default;
        }
        catch (InvalidDataException ex)
        {
            BindingFailure.Status(httpContext, environment, StatusCodes.Status400BadRequest, "Failed to read the request form: " + ex.Message);
            return default;
        }
        catch (IOException)
        {
            // Client disconnected while the form was read; nothing useful can be written.
            return default;
        }
    }

    /// <summary>True when the request can carry a body (Minimal API <c>IHttpRequestBodyDetectionFeature</c> semantics).</summary>
    public static bool RequestHasBody(HttpContext httpContext)
    {
        var detection = httpContext.Features.Get<IHttpRequestBodyDetectionFeature>();
        if (detection is not null)
            return detection.CanHaveBody;

        var request = httpContext.Request;
        return request.ContentLength is > 0
            || (request.ContentLength is null && request.Headers.ContainsKey("Transfer-Encoding"));
    }

    // ------------------------------------------------------------------
    // Typed helpers kept for direct use (route / query / header)
    // ------------------------------------------------------------------

    public static bool TryGetRouteValue(HttpContext httpContext, string name, [NotNullWhen(true)] out string? value)
    {
        value = GetRouteValue(httpContext, name);
        return value is not null;
    }

    public static bool TryBindRouteString(HttpContext httpContext, string name, [NotNullWhen(true)] out string? value)
        => TryGetRouteValue(httpContext, name, out value);

    public static bool TryBindQueryString(HttpContext httpContext, string name, out string? value)
    {
        if (httpContext.Request.Query.TryGetValue(name, out var values) && values.Count > 0)
        {
            value = values[0];
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryBindHeaderString(HttpContext httpContext, string name, out string? value)
    {
        if (httpContext.Request.Headers.TryGetValue(name, out var values) && values.Count > 0)
        {
            value = values[0];
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryBindRouteInt32(HttpContext httpContext, string name, out int value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindQueryInt32(HttpContext httpContext, string name, out int value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindRouteInt64(HttpContext httpContext, string name, out long value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindQueryInt64(HttpContext httpContext, string name, out long value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindRouteBool(HttpContext httpContext, string name, out bool value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && bool.TryParse(s, out value);
    }

    public static bool TryBindQueryBool(HttpContext httpContext, string name, out bool value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && bool.TryParse(s, out value);
    }

    public static bool TryBindRouteGuid(HttpContext httpContext, string name, out Guid value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && Guid.TryParse(s, out value);
    }

    public static bool TryBindQueryGuid(HttpContext httpContext, string name, out Guid value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && Guid.TryParse(s, out value);
    }

    public static bool TryBindRouteDouble(HttpContext httpContext, string name, out double value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindQueryDouble(HttpContext httpContext, string name, out double value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindRouteSingle(HttpContext httpContext, string name, out float value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindQuerySingle(HttpContext httpContext, string name, out float value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindRouteDecimal(HttpContext httpContext, string name, out decimal value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindQueryDecimal(HttpContext httpContext, string name, out decimal value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBindRouteDateOnly(HttpContext httpContext, string name, out DateOnly value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && TryParseDateOnly(s, out value);
    }

    public static bool TryBindQueryDateOnly(HttpContext httpContext, string name, out DateOnly value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && TryParseDateOnly(s, out value);
    }

    public static bool TryBindRouteDateTime(HttpContext httpContext, string name, out DateTime value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    public static bool TryBindQueryDateTime(HttpContext httpContext, string name, out DateTime value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    public static bool TryBindRouteDateTimeOffset(HttpContext httpContext, string name, out DateTimeOffset value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    public static bool TryBindQueryDateTimeOffset(HttpContext httpContext, string name, out DateTimeOffset value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    public static bool TryBindRouteTimeOnly(HttpContext httpContext, string name, out TimeOnly value)
    {
        value = default;
        return TryGetRouteValue(httpContext, name, out var s) && TryParseTimeOnly(s, out value);
    }

    public static bool TryBindQueryTimeOnly(HttpContext httpContext, string name, out TimeOnly value)
    {
        value = default;
        return TryBindQueryString(httpContext, name, out var s) && s is not null && TryParseTimeOnly(s, out value);
    }

    /// <summary>Prefer route when the name appears in the template; otherwise query.</summary>
    public static bool TryBindSimple(HttpContext httpContext, string name, bool preferRoute, out string? value)
    {
        if (preferRoute && TryGetRouteValue(httpContext, name, out value))
            return true;
        return TryBindQueryString(httpContext, name, out value);
    }

    /// <summary>Reads a form field as string. Returns null when not form content or the field is missing.</summary>
    public static async Task<string?> TryBindFormStringAsync(HttpContext httpContext, string name)
    {
        if (!httpContext.Request.HasFormContentType)
            return null;
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted).ConfigureAwait(false);
        return form.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;
    }

    /// <summary>Reads an uploaded file from the form. Returns null when missing or not form content.</summary>
    public static async Task<IFormFile?> TryBindFormFileAsync(HttpContext httpContext, string name)
    {
        if (!httpContext.Request.HasFormContentType)
            return null;
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted).ConfigureAwait(false);
        return form.Files.GetFile(name);
    }
}

/// <summary>Result of <see cref="ParameterBinder.ReadFormAsync"/>. When <see cref="Success"/> is false the response status is already set.</summary>
public readonly struct FormReadResult(bool success, IFormCollection? form)
{
    public bool Success { get; } = success;
    public IFormCollection? Form { get; } = form;
}
