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
    /// The value task is consumed exactly once, when the builder is created.
    /// </summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<Result<T>> result)
        => new(result);

    // ───────────────────────────── From concrete non-generic ─────────────────────────────

    /// <summary>Maps a concrete <see cref="SuccessResult"/>.</summary>
    public static NonGenericResultBuilder From(SuccessResult result)
        => new(result);

    /// <summary>Maps a concrete <see cref="InProgressResult"/>.</summary>
    public static NonGenericResultBuilder From(InProgressResult result)
        => new(result);

    /// <summary>Maps a concrete <see cref="FailedResult"/> (non-generic failure).</summary>
    public static NonGenericResultBuilder From(FailedResult result)
        => new(result);

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
        => AsyncResultBuilder<T>.FromTask(result);

    /// <summary>
    /// Async map of <see cref="ValueTask{TResult}"/> of <see cref="FailedResult"/> into a typed builder.
    /// </summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<FailedResult> result)
        => result.IsCompletedSuccessfully
            ? new AsyncResultBuilder<T>(ToResult<T>(result.Result))
            : AsyncResultBuilder<T>.FromTask(result.AsTask());

    // ───────────────────────────── From concrete generic ─────────────────────────────

    /// <summary>Maps a concrete <see cref="SuccessResult{T}"/>.</summary>
    public static ResultBuilder<T> From<T>(SuccessResult<T> result)
        => new(result);

    /// <summary>Maps a concrete <see cref="InProgressResult{T}"/>.</summary>
    public static ResultBuilder<T> From<T>(InProgressResult<T> result)
        => new(result);

    /// <summary>Maps a concrete <see cref="FailedResult{T}"/>.</summary>
    public static ResultBuilder<T> From<T>(FailedResult<T> result)
        => new(result);

    /// <summary>Async map of <see cref="Task{TResult}"/> of <see cref="SuccessResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(Task<SuccessResult<T>> result)
        => AsyncResultBuilder<T>.FromTask(result);

    /// <summary>Async map of <see cref="Task{TResult}"/> of <see cref="InProgressResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(Task<InProgressResult<T>> result)
        => AsyncResultBuilder<T>.FromTask(result);

    /// <summary>Async map of <see cref="Task{TResult}"/> of <see cref="FailedResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(Task<FailedResult<T>> result)
        => AsyncResultBuilder<T>.FromTask(result);

    /// <summary>Async map of <see cref="ValueTask{TResult}"/> of <see cref="SuccessResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<SuccessResult<T>> result)
        => result.IsCompletedSuccessfully
            ? new AsyncResultBuilder<T>(result.Result)
            : AsyncResultBuilder<T>.FromTask(result.AsTask());

    /// <summary>Async map of <see cref="ValueTask{TResult}"/> of <see cref="InProgressResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<InProgressResult<T>> result)
        => result.IsCompletedSuccessfully
            ? new AsyncResultBuilder<T>(result.Result)
            : AsyncResultBuilder<T>.FromTask(result.AsTask());

    /// <summary>Async map of <see cref="ValueTask{TResult}"/> of <see cref="FailedResult{T}"/>.</summary>
    public static AsyncResultBuilder<T> From<T>(ValueTask<FailedResult<T>> result)
        => result.IsCompletedSuccessfully
            ? new AsyncResultBuilder<T>(result.Result)
            : AsyncResultBuilder<T>.FromTask(result.AsTask());

    // Null stays null (mapped to a 500 by the builders) instead of throwing inside the implicit conversion.
    private static Result<T>? ToResult<T>(FailedResult? failed)
        => failed is null ? null : (Result<T>)failed;

    // ───────────────────────────── Non-generic builder ─────────────────────────────

    /// <summary>
    /// Maps a non-generic domain result to <see cref="ApiResult{T}"/> with no payload.
    /// This is a <see langword="readonly"/> struct: creating it does not allocate.
    /// </summary>
    public readonly struct NonGenericResultBuilder
    {
        private readonly Result? _result;

        /// <summary>Creates a builder over <paramref name="result"/>.</summary>
        public NonGenericResultBuilder(Result result) => _result = result;

        /// <summary>
        /// Success → 200, InProgress → 202, Failed → status from first error type.
        /// A null (or default-constructed builder) result maps to 500.
        /// </summary>
        public ApiResult<object?> AsIs()
        {
            var result = _result;
            if (result is null)
                return new ApiResult<object?>(500, null, NullReferenceErrors());

            if (result.IsSuccessful())
                return Ok();

            if (result.IsInProgress())
                return Accepted();

            return MapFailed<object?>(result.GetErrors());
        }
    }

    // ───────────────────────────── Generic builder ─────────────────────────────

    /// <summary>
    /// Maps a domain <see cref="Result{T}"/> to API results.
    /// This is a <see langword="readonly"/> struct: creating it does not allocate.
    /// </summary>
    public readonly struct ResultBuilder<T>
    {
        private readonly Result<T>? _result;

        /// <summary>Creates a builder over <paramref name="result"/>.</summary>
        public ResultBuilder(Result<T> result) => _result = result;

        /// <summary>
        /// Success → 200 + data, InProgress → 202 + data, Failed → mapped errors.
        /// A null (or default-constructed builder) result maps to 500.
        /// </summary>
        public ApiResult<T> AsIs()
        {
            var result = _result;
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
        /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
        public ApiResult<TResult> As<TResult>(Func<T, TResult> map)
        {
            ArgumentNullException.ThrowIfNull(map);

            var result = _result;
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
        /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="whenNull"/> is null.</exception>
        public ApiResult<TResult> As<TResult>(Func<T, TResult> map, Func<TResult> whenNull)
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(whenNull);

            var result = _result;
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

    // ───────────────────────────── Async builder ─────────────────────────────

    /// <summary>
    /// Async wrapper around <see cref="ResultBuilder{T}"/> (Task or ValueTask).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="ValueTask{TResult}"/> source is consumed exactly once, in the constructor: if it already completed
    /// successfully its result is captured; otherwise it is converted to a <see cref="Task{TResult}"/>. The builder
    /// therefore never awaits a <see cref="ValueTask{TResult}"/> twice (which is undefined behavior for
    /// <c>IValueTaskSource</c>-backed tasks), and <see cref="AsIsAsync"/> / <see cref="AsAsync{TResult}"/> may be called
    /// any number of times.
    /// </para>
    /// <para>
    /// When the source has already completed, mapping runs synchronously and only the returned
    /// <see cref="Task{TResult}"/> is allocated (no async state machine).
    /// </para>
    /// </remarks>
    public readonly struct AsyncResultBuilder<T>
    {
        // Exactly one of these is meaningful:
        //  _task != null  → pending (or faulted/canceled) source; one of Task<Result<T>>, Task<SuccessResult<T>>,
        //                   Task<InProgressResult<T>>, Task<FailedResult<T>>, Task<FailedResult>.
        //  _task == null  → _completed holds the already-available domain result (null maps to 500).
        private readonly Task? _task;
        private readonly Result<T>? _completed;

        /// <summary>Creates a builder from a <see cref="Task{TResult}"/>.</summary>
        public AsyncResultBuilder(Task<Result<T>> resultTask)
        {
            if (resultTask is not null && resultTask.IsCompletedSuccessfully)
            {
                _completed = resultTask.Result;
                _task = null;
            }
            else
            {
                _completed = null;
                _task = resultTask;
            }
        }

        /// <summary>
        /// Creates a builder from a <see cref="ValueTask{TResult}"/>. The value task is consumed here, exactly once.
        /// </summary>
        public AsyncResultBuilder(ValueTask<Result<T>> resultTask)
        {
            if (resultTask.IsCompletedSuccessfully)
            {
                _completed = resultTask.Result;
                _task = null;
            }
            else
            {
                _completed = null;
                _task = resultTask.AsTask();
            }
        }

        internal AsyncResultBuilder(Result<T>? completed)
        {
            _completed = completed;
            _task = null;
        }

        private AsyncResultBuilder(Task task)
        {
            _completed = null;
            _task = task;
        }

        internal static AsyncResultBuilder<T> FromTask<TSource>(Task<TSource> task) where TSource : class
        {
            if (task is null)
                return default;

            return task.IsCompletedSuccessfully
                ? new AsyncResultBuilder<T>(Convert(task))
                : new AsyncResultBuilder<T>((Task)task);
        }

        /// <summary>Awaits the domain result and maps with <see cref="ResultBuilder{T}.AsIs"/>.</summary>
        /// <param name="cancellationToken">Stops waiting for a pending source (the source itself is not canceled).</param>
        public Task<ApiResult<T>> AsIsAsync(CancellationToken cancellationToken = default)
        {
            if (_task is null)
                return Task.FromResult(new ResultBuilder<T>(_completed!).AsIs());

            return AwaitAsIsAsync(_task, cancellationToken);
        }

        /// <summary>Awaits the domain result and maps the payload with <paramref name="map"/>.</summary>
        /// <param name="map">Payload mapper (ignored for failures).</param>
        /// <param name="cancellationToken">Stops waiting for a pending source (the source itself is not canceled).</param>
        /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
        public Task<ApiResult<TResult>> AsAsync<TResult>(
            Func<T, TResult> map,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(map);

            if (_task is null)
                return Task.FromResult(new ResultBuilder<T>(_completed!).As(map));

            return AwaitAsAsync(_task, map, cancellationToken);
        }

        private static async Task<ApiResult<T>> AwaitAsIsAsync(Task task, CancellationToken cancellationToken)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ResultBuilder<T>(Convert(task)!).AsIs();
        }

        private static async Task<ApiResult<TResult>> AwaitAsAsync<TResult>(
            Task task,
            Func<T, TResult> map,
            CancellationToken cancellationToken)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ResultBuilder<T>(Convert(task)!).As(map);
        }

        // Only called on a task that completed successfully.
        private static Result<T>? Convert(Task task) => task switch
        {
            Task<Result<T>> t => t.Result,
            Task<SuccessResult<T>> t => t.Result,
            Task<InProgressResult<T>> t => t.Result,
            Task<FailedResult<T>> t => t.Result,
            Task<FailedResult> t => ToResult<T>(t.Result),
            _ => throw new InvalidOperationException($"Unsupported task type '{task.GetType()}'."),
        };
    }

    // ───────────────────────────── Mapping helpers ─────────────────────────────

    private static ApiResult<T> MapFailed<T>(IReadOnlyList<ErrorResult> errors)
    {
        if (errors.Count == 0)
            return Server.InternalError<T>();

        var apiErrors = new ApiResultError[errors.Count];
        for (var i = 0; i < apiErrors.Length; i++)
            apiErrors[i] = ApiResultError.From(errors[i]);

        // ApiResultError.From defaults the per-error status to the mapped ErrorType status.
        return new ApiResult<T>(apiErrors[0].StatusCode!.Value, default, apiErrors);
    }
}
