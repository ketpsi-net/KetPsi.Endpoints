using KetPsi.Results;

namespace KetPsi.Endpoints.Results;

public static partial class ApiResult
{
    // ───────────────────────────── From abstract Result ─────────────────────────────

    /// <summary>
    /// Starts a builder that maps a non-generic domain <see cref="Result"/> to an API result.
    /// </summary>
    public static NonGenericResultBuilder From(Result result)
        => new(result);

    /// <summary>
    /// Starts a builder that maps a domain <see cref="Result{T}"/> to an API result.
    /// </summary>
    public static ResultBuilder<T> From<T>(Result<T> result)
        => new(result);

    /// <summary>
    /// Starts an async builder for <see cref="Task{TResult}"/> of <see cref="Result{T}"/>.
    /// </summary>
    public static AsyncResultBuilder<T> From<T>(Task<Result<T>> result)
        => new(result);

    /// <summary>
    /// Starts an async builder for <see cref="ValueTask{TResult}"/> of <see cref="Result{T}"/>.
    /// </summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<Result<T>> result)
        => new(result);

    // ───────────────────────────── From concrete non-generic ─────────────────────────────

    /// <summary>Maps a concrete <see cref="SuccessResult"/>.</summary>
    public static NonGenericResultBuilder From(SuccessResult result)
        => From((Result)result);

    /// <summary>Maps a concrete <see cref="InProgressResult"/>.</summary>
    public static NonGenericResultBuilder From(InProgressResult result)
        => From((Result)result);

    /// <summary>Maps a concrete <see cref="FailedResult"/> (non-generic failure).</summary>
    public static NonGenericResultBuilder From(FailedResult result)
        => From((Result)result);

    /// <summary>
    /// Maps a non-generic <see cref="FailedResult"/> into a typed API result builder.
    /// Uses the domain implicit conversion <c>FailedResult</c> → <c>Result&lt;T&gt;</c>.
    /// </summary>
    public static ResultBuilder<T> From<T>(FailedResult result)
        => new(result);

    /// <summary>
    /// Async map of <see cref="Task{TResult}"/> of <see cref="FailedResult"/> into a typed builder.
    /// </summary>
    public static AsyncResultBuilder<T> From<T>(Task<FailedResult> result)
        => new(AwaitFailedAsResult<T>(result));

    /// <summary>
    /// Async map of <see cref="ValueTask{TResult}"/> of <see cref="FailedResult"/> into a typed builder.
    /// </summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<FailedResult> result)
        => new(AwaitFailedAsResult<T>(result));

    // ───────────────────────────── From concrete generic ─────────────────────────────

    /// <summary>Maps a concrete <see cref="SuccessResult{T}"/>.</summary>
    public static ResultBuilder<T> From<T>(SuccessResult<T> result)
        => From((Result<T>)result);

    /// <summary>Maps a concrete <see cref="InProgressResult{T}"/>.</summary>
    public static ResultBuilder<T> From<T>(InProgressResult<T> result)
        => From((Result<T>)result);

    /// <summary>Maps a concrete <see cref="FailedResult{T}"/>.</summary>
    public static ResultBuilder<T> From<T>(FailedResult<T> result)
        => From((Result<T>)result);

    /// <summary>Async map of <see cref="Task{TResult}"/> of <see cref="SuccessResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(Task<SuccessResult<T>> result)
        => new(AwaitAsResult(result));

    /// <summary>Async map of <see cref="Task{TResult}"/> of <see cref="InProgressResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(Task<InProgressResult<T>> result)
        => new(AwaitAsResult(result));

    /// <summary>Async map of <see cref="Task{TResult}"/> of <see cref="FailedResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(Task<FailedResult<T>> result)
        => new(AwaitAsResult(result));

    /// <summary>Async map of <see cref="ValueTask{TResult}"/> of <see cref="SuccessResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<SuccessResult<T>> result)
        => new(AwaitAsResult(result));

    /// <summary>Async map of <see cref="ValueTask{TResult}"/> of <see cref="InProgressResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<InProgressResult<T>> result)
        => new(AwaitAsResult(result));

    /// <summary>Async map of <see cref="ValueTask{TResult}"/> of <see cref="FailedResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<FailedResult<T>> result)
        => new(AwaitAsResult(result));

    // ───────────────────────────── Await helpers (concrete → abstract) ─────────────────────────────

    private static async Task<Result<T>> AwaitAsResult<T>(Task<SuccessResult<T>> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitAsResult<T>(Task<InProgressResult<T>> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitAsResult<T>(Task<FailedResult<T>> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitAsResult<T>(ValueTask<SuccessResult<T>> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitAsResult<T>(ValueTask<InProgressResult<T>> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitAsResult<T>(ValueTask<FailedResult<T>> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitFailedAsResult<T>(Task<FailedResult> task)
        => await task.ConfigureAwait(false);

    private static async Task<Result<T>> AwaitFailedAsResult<T>(ValueTask<FailedResult> task)
        => await task.ConfigureAwait(false);

    // ───────────────────────────── Non-generic builder ─────────────────────────────

    /// <summary>Maps a non-generic domain result to <see cref="ApiResult{T}"/> with no payload.</summary>
    public sealed class NonGenericResultBuilder(Result result)
    {
        /// <summary>
        /// Success → 200, InProgress → 202, Failed → status from first error type.
        /// </summary>
        public ApiResult<object?> AsIs()
        {
            if (result is null)
                return new ApiResult<object?>(500, null, NullReferenceErrors());

            if (result.IsSuccessful())
                return Ok();

            if (result.IsInProgress())
                return Accepted();

            return MapFailed(result.GetErrors());
        }
    }

    /// <summary>Maps a domain <see cref="Result{T}"/> to API results.</summary>
    public sealed class ResultBuilder<T>(Result<T> result)
    {
        /// <summary>
        /// Success → 200 + data, InProgress → 202 + data, Failed → mapped errors.
        /// </summary>
        public ApiResult<T> AsIs()
        {
            if (result is null)
                return new ApiResult<T>(500, default, NullReferenceErrors());

            if (result.IsSuccessful())
            {
                result.TryGetResult(out var value);
                return Ok(value!);
            }

            if (result.IsInProgress())
            {
                result.TryGetResult(out var value);
                return Accepted(value!);
            }

            return MapFailed<T>(result.GetErrors());
        }

        /// <summary>
        /// Maps success/in-progress payload with a required mapper; failures ignore the mapper.
        /// </summary>
        public ApiResult<TResult> As<TResult>(Func<T, TResult> map)
        {
            ArgumentNullException.ThrowIfNull(map);

            if (result is null)
                return new ApiResult<TResult>(500, default, NullReferenceErrors());

            if (result.IsSuccessful())
            {
                result.TryGetResult(out var value);
                return Ok(map(value!));
            }

            if (result.IsInProgress())
            {
                result.TryGetResult(out var value);
                return Accepted(map(value!));
            }

            return MapFailed<TResult>(result.GetErrors());
        }

        /// <summary>
        /// Maps success/in-progress payload when present; if value is null, uses <paramref name="whenNull"/>.
        /// </summary>
        public ApiResult<TResult> As<TResult>(Func<T, TResult> map, Func<TResult> whenNull)
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(whenNull);

            if (result is null)
                return new ApiResult<TResult>(500, default, NullReferenceErrors());

            if (result.IsSuccessful())
            {
                result.TryGetResult(out var value);
                return Ok(value is null ? whenNull() : map(value));
            }

            if (result.IsInProgress())
            {
                result.TryGetResult(out var value);
                return Accepted(value is null ? whenNull() : map(value));
            }

            return MapFailed<TResult>(result.GetErrors());
        }
    }

    /// <summary>Async wrapper around <see cref="ResultBuilder{T}"/> (Task or ValueTask).</summary>
    public sealed class AsyncResultBuilder<T>
    {
        private readonly Task<Result<T>>? _task;
        private readonly ValueTask<Result<T>> _valueTask;
        private readonly bool _isValueTask;

        /// <summary>Creates a builder from a <see cref="Task{TResult}"/>.</summary>
        public AsyncResultBuilder(Task<Result<T>> resultTask)
        {
            _task = resultTask;
            _valueTask = default;
            _isValueTask = false;
        }

        /// <summary>Creates a builder from a <see cref="ValueTask{TResult}"/>.</summary>
        public AsyncResultBuilder(ValueTask<Result<T>> resultTask)
        {
            _task = null;
            _valueTask = resultTask;
            _isValueTask = true;
        }

        /// <summary>Awaits the domain result and maps with <see cref="ResultBuilder{T}.AsIs"/>.</summary>
        public async Task<ApiResult<T>> AsIsAsync(CancellationToken cancellationToken = default)
        {
            var result = await AwaitAsync(cancellationToken).ConfigureAwait(false);
            if (result is null)
                return new ApiResult<T>(500, default, NullReferenceErrors());
            return new ResultBuilder<T>(result).AsIs();
        }

        /// <summary>Awaits the domain result and maps the payload with <paramref name="map"/>.</summary>
        public async Task<ApiResult<TResult>> AsAsync<TResult>(
            Func<T, TResult> map,
            CancellationToken cancellationToken = default)
        {
            var result = await AwaitAsync(cancellationToken).ConfigureAwait(false);
            if (result is null)
                return new ApiResult<TResult>(500, default, NullReferenceErrors());
            return new ResultBuilder<T>(result).As(map);
        }

        private async Task<Result<T>?> AwaitAsync(CancellationToken cancellationToken)
        {
            if (_isValueTask)
            {
                if (cancellationToken.CanBeCanceled)
                    return await _valueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                return await _valueTask.ConfigureAwait(false);
            }

            if (_task is null)
                return null;

            return await _task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // ───────────────────────────── Mapping helpers ─────────────────────────────

    private static ApiResult<object?> MapFailed(IReadOnlyList<ErrorResult> errors)
    {
        if (errors.Count == 0)
            return Server.InternalError();

        var status = errors[0].Type.ToStatusCode();
        var apiErrors = new ApiResultError[errors.Count];
        for (var i = 0; i < errors.Count; i++)
            apiErrors[i] = ApiResultError.From(errors[i], errors[i].Type.ToStatusCode());

        return new ApiResult<object?>(status, null, apiErrors);
    }

    private static ApiResult<T> MapFailed<T>(IReadOnlyList<ErrorResult> errors)
    {
        if (errors.Count == 0)
            return Server.InternalError<T>();

        var status = errors[0].Type.ToStatusCode();
        var apiErrors = new ApiResultError[errors.Count];
        for (var i = 0; i < errors.Count; i++)
            apiErrors[i] = ApiResultError.From(errors[i], errors[i].Type.ToStatusCode());

        return new ApiResult<T>(status, default, apiErrors);
    }
}
