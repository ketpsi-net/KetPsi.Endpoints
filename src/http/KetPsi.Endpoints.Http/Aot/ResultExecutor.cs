using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// Writes handler results without RequestDelegateFactory, with Minimal API semantics:
/// <list type="bullet">
/// <item><see cref="IResult"/> → <see cref="IResult.ExecuteAsync"/>; a null <see cref="IResult"/> throws like RDF.</item>
/// <item><see cref="string"/> → <c>text/plain; charset=utf-8</c>.</item>
/// <item>any other <c>T</c> → JSON through <see cref="JsonTypeInfo{T}"/> (a null class value is written as <c>null</c> with 200).</item>
/// <item><see cref="Task"/> / <see cref="ValueTask"/> / void → 200 with an empty body.</item>
/// </list>
/// Already-completed tasks are unwrapped synchronously (no async state machine on the hot path).
/// </summary>
public static class ResultExecutor
{
    private static readonly byte[] BadRequestUtf8 = "Bad Request"u8.ToArray();
    private const string PlainTextContentType = "text/plain; charset=utf-8";

    // ------------------------------------------------------------------
    // IResult
    // ------------------------------------------------------------------

    public static Task ExecuteResultAsync<T>(HttpContext httpContext, T result) where T : IResult?
    {
        if (result is null)
            throw new InvalidOperationException("The IResult returned by the Delegate must not be null.");
        return result.ExecuteAsync(httpContext);
    }

    public static Task ExecuteResultTaskAsync<T>(HttpContext httpContext, Task<T> task) where T : IResult?
    {
        if (task.IsCompletedSuccessfully)
            return ExecuteResultAsync(httpContext, task.Result);
        return Awaited(httpContext, task);

        static async Task Awaited(HttpContext httpContext, Task<T> task)
            => await ExecuteResultAsync(httpContext, await task.ConfigureAwait(false)).ConfigureAwait(false);
    }

    public static Task ExecuteResultValueTaskAsync<T>(HttpContext httpContext, ValueTask<T> task) where T : IResult?
    {
        if (task.IsCompletedSuccessfully)
            return ExecuteResultAsync(httpContext, task.Result);
        return Awaited(httpContext, task);

        static async Task Awaited(HttpContext httpContext, ValueTask<T> task)
            => await ExecuteResultAsync(httpContext, await task.ConfigureAwait(false)).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // string
    // ------------------------------------------------------------------

    public static Task WriteStringAsync(HttpContext httpContext, string? text)
    {
        httpContext.Response.ContentType ??= PlainTextContentType;
        return string.IsNullOrEmpty(text) ? Task.CompletedTask : httpContext.Response.WriteAsync(text);
    }

    public static Task WriteStringTaskAsync(HttpContext httpContext, Task<string?> task)
    {
        if (task.IsCompletedSuccessfully)
            return WriteStringAsync(httpContext, task.Result);
        return Awaited(httpContext, task);

        static async Task Awaited(HttpContext httpContext, Task<string?> task)
            => await WriteStringAsync(httpContext, await task.ConfigureAwait(false)).ConfigureAwait(false);
    }

    public static Task WriteStringValueTaskAsync(HttpContext httpContext, ValueTask<string?> task)
    {
        if (task.IsCompletedSuccessfully)
            return WriteStringAsync(httpContext, task.Result);
        return Awaited(httpContext, task);

        static async Task Awaited(HttpContext httpContext, ValueTask<string?> task)
            => await WriteStringAsync(httpContext, await task.ConfigureAwait(false)).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // JSON (typed)
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="value"/> as JSON. When the runtime type differs from <typeparamref name="T"/> and the
    /// declared contract is not polymorphic, the runtime type's contract is used (same rule as RDF).
    /// </summary>
    public static Task WriteJsonAsync<T>(HttpContext httpContext, T value, JsonTypeInfo<T> typeInfo)
    {
        var runtimeType = value?.GetType();
        if (runtimeType is null || runtimeType == typeInfo.Type || typeInfo.PolymorphismOptions is not null)
            return httpContext.Response.WriteAsJsonAsync(value, typeInfo, contentType: null, default);

        return httpContext.Response.WriteAsJsonAsync(value, typeInfo.Options.GetTypeInfo(runtimeType), contentType: null, default);
    }

    public static Task WriteJsonTaskAsync<T>(HttpContext httpContext, Task<T> task, JsonTypeInfo<T> typeInfo)
    {
        if (task.IsCompletedSuccessfully)
            return WriteJsonAsync(httpContext, task.Result, typeInfo);
        return Awaited(httpContext, task, typeInfo);

        static async Task Awaited(HttpContext httpContext, Task<T> task, JsonTypeInfo<T> typeInfo)
            => await WriteJsonAsync(httpContext, await task.ConfigureAwait(false), typeInfo).ConfigureAwait(false);
    }

    public static Task WriteJsonValueTaskAsync<T>(HttpContext httpContext, ValueTask<T> task, JsonTypeInfo<T> typeInfo)
    {
        if (task.IsCompletedSuccessfully)
            return WriteJsonAsync(httpContext, task.Result, typeInfo);
        return Awaited(httpContext, task, typeInfo);

        static async Task Awaited(HttpContext httpContext, ValueTask<T> task, JsonTypeInfo<T> typeInfo)
            => await WriteJsonAsync(httpContext, await task.ConfigureAwait(false), typeInfo).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // object (runtime dispatch)
    // ------------------------------------------------------------------

    /// <summary>Runtime dispatch for <c>object</c>-typed results and filter results: IResult, string, else JSON of the runtime type.</summary>
    public static Task WriteObjectAsync(HttpContext httpContext, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case IResult result:
                return result.ExecuteAsync(httpContext);
            case string text:
                return WriteStringAsync(httpContext, text);
            case null:
                return httpContext.Response.WriteAsJsonAsync<object?>(null, (JsonTypeInfo<object?>)options.GetTypeInfo(typeof(object)), contentType: null, default);
            default:
                return httpContext.Response.WriteAsJsonAsync(value, options.GetTypeInfo(value.GetType()), contentType: null, default);
        }
    }

    public static Task WriteObjectTaskAsync(HttpContext httpContext, Task<object?> task, JsonSerializerOptions options)
    {
        if (task.IsCompletedSuccessfully)
            return WriteObjectAsync(httpContext, task.Result, options);
        return Awaited(httpContext, task, options);

        static async Task Awaited(HttpContext httpContext, Task<object?> task, JsonSerializerOptions options)
            => await WriteObjectAsync(httpContext, await task.ConfigureAwait(false), options).ConfigureAwait(false);
    }

    public static Task WriteObjectValueTaskAsync(HttpContext httpContext, ValueTask<object?> task, JsonSerializerOptions options)
    {
        if (task.IsCompletedSuccessfully)
            return WriteObjectAsync(httpContext, task.Result, options);
        return Awaited(httpContext, task, options);

        static async Task Awaited(HttpContext httpContext, ValueTask<object?> task, JsonSerializerOptions options)
            => await WriteObjectAsync(httpContext, await task.ConfigureAwait(false), options).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // void-like
    // ------------------------------------------------------------------

    /// <summary>Plain <see cref="ValueTask"/> handler: 200 with empty body once completed.</summary>
    public static Task FromValueTask(ValueTask task)
        => task.IsCompletedSuccessfully ? Task.CompletedTask : task.AsTask();

    // ------------------------------------------------------------------
    // Endpoint filter pipeline adapters (only used when filters are configured)
    // ------------------------------------------------------------------

    /// <summary>Result returned to filters when binding failed (status already ≥ 400) or the handler returns no value.</summary>
    public static ValueTask<object?> EmptyFilterResult => new(EmptyHttpResult.Instance);

    public static ValueTask<object?> FromValue<T>(T value) => new(value);

    public static async ValueTask<object?> FromTask(Task task)
    {
        await task.ConfigureAwait(false);
        return EmptyHttpResult.Instance;
    }

    public static async ValueTask<object?> FromValueTaskVoid(ValueTask task)
    {
        await task.ConfigureAwait(false);
        return EmptyHttpResult.Instance;
    }

    public static ValueTask<object?> FromTaskOf<T>(Task<T> task)
    {
        if (task.IsCompletedSuccessfully)
            return new ValueTask<object?>(task.Result);
        return Awaited(task);

        static async ValueTask<object?> Awaited(Task<T> task) => await task.ConfigureAwait(false);
    }

    public static ValueTask<object?> FromValueTaskOf<T>(ValueTask<T> task)
    {
        if (task.IsCompletedSuccessfully)
            return new ValueTask<object?>(task.Result);
        return Awaited(task);

        static async ValueTask<object?> Awaited(ValueTask<T> task) => await task.ConfigureAwait(false);
    }

    /// <summary>Writes the value produced by the filter pipeline (RDF <c>ExecuteValueTaskOfObject</c> semantics).</summary>
    public static Task ExecuteFilterResultAsync(HttpContext httpContext, object? result, EndpointEnvironment environment)
        => WriteObjectAsync(httpContext, result, environment.JsonOptions);

    // ------------------------------------------------------------------
    // Convenience API (not used by generated code)
    // ------------------------------------------------------------------

    /// <summary>Unwraps a handler task then writes the result.</summary>
    public static async Task ExecuteHandlerTaskAsync<T>(HttpContext httpContext, Task<T> handlerTask)
        => await ExecuteAsync(httpContext, await handlerTask.ConfigureAwait(false)).ConfigureAwait(false);

    public static async Task ExecuteHandlerTaskAsync<T>(HttpContext httpContext, ValueTask<T> handlerTask)
        => await ExecuteAsync(httpContext, await handlerTask.ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Runtime dispatch using the request's host JSON options.</summary>
    public static Task ExecuteAsync(HttpContext httpContext, object? result)
        => WriteObjectAsync(httpContext, result, JsonBodyReader.ResolveOptions(httpContext));

    /// <summary>Typed dispatch using the request's host JSON options (resolves the type info per call).</summary>
    public static Task ExecuteAsync<T>(HttpContext httpContext, T result)
    {
        if (result is IResult iresult)
            return iresult.ExecuteAsync(httpContext);
        if (typeof(IResult).IsAssignableFrom(typeof(T)))
            return ExecuteResultAsync<IResult?>(httpContext, null);
        if (typeof(T) == typeof(string))
            return WriteStringAsync(httpContext, (string?)(object?)result);

        var options = JsonBodyReader.ResolveOptions(httpContext);
        return WriteJsonAsync(httpContext, result, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
    }

    /// <summary>Sets 400 and writes a fixed UTF-8 body asynchronously.</summary>
    public static ValueTask WriteBadRequestAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        httpContext.Response.ContentType = PlainTextContentType;
        return httpContext.Response.Body.WriteAsync(BadRequestUtf8.AsMemory(), httpContext.RequestAborted);
    }

    /// <summary>Sets 400 only. Never performs synchronous I/O (Kestrel disallows it by default).</summary>
    public static void SetBadRequest(HttpContext httpContext)
        => httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

    [Obsolete("Use SetBadRequest(HttpContext) or WriteBadRequestAsync.")]
    public static void SetBadRequest(HttpContext httpContext, string message)
        => SetBadRequest(httpContext);
}
