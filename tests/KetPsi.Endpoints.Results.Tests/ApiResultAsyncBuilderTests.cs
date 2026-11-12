using System.Threading.Tasks.Sources;
using KetPsi.Results;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public class ApiResultAsyncBuilderTests
{
    private sealed record Order(int Id, string Name);

    /// <summary>
    /// A pooled-style <see cref="IValueTaskSource{TResult}"/>: after <see cref="Reset"/> the previous token is invalid,
    /// exactly like a recycled <c>IValueTaskSource</c> in Kestrel/sockets. Awaiting a ValueTask twice would observe the
    /// recycled state (or throw), so the builder must consume it only once.
    /// </summary>
    private sealed class ReusableSource<T> : IValueTaskSource<T>
    {
        private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };

        public int GetResultCalls { get; private set; }
        public ValueTask<T> Task => new(this, _core.Version);
        public void SetResult(T value) => _core.SetResult(value);
        public void SetException(Exception e) => _core.SetException(e);
        public void Reset() => _core.Reset();

        public T GetResult(short token)
        {
            GetResultCalls++;
            return _core.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }

    // ───────────────────────────── Double consumption ─────────────────────────────

    [Fact]
    public async Task PendingValueTaskSource_AwaitedTwice_ConsumesSourceOnce()
    {
        var source = new ReusableSource<Result<Order>>();
        var builder = ApiResult.From(source.Task);

        source.SetResult(Result.Success(new Order(1, "a")));

        var first = await builder.AsIsAsync();
        var mapped = await builder.AsAsync(o => o.Name);

        // Simulate the source being recycled for another operation.
        source.Reset();
        source.SetResult(Result.Success(new Order(2, "recycled")));

        var again = await builder.AsIsAsync();

        Assert.Equal(1, source.GetResultCalls);
        Assert.Equal(1, first.Value!.Id);
        Assert.Equal("a", mapped.Value);
        Assert.Equal(1, again.Value!.Id);
    }

    [Fact]
    public async Task CompletedValueTaskSource_ConsumedAtConstruction()
    {
        var source = new ReusableSource<Result<Order>>();
        var vt = source.Task;
        source.SetResult(Result.Success(new Order(5, "done")));

        var builder = ApiResult.From(vt);
        Assert.Equal(1, source.GetResultCalls);

        source.Reset(); // the original token is now invalid

        var a = await builder.AsIsAsync();
        var b = await builder.AsIsAsync();
        Assert.Equal(5, a.Value!.Id);
        Assert.Equal(5, b.Value!.Id);
        Assert.Equal(1, source.GetResultCalls);
    }

    [Fact]
    public async Task PendingConcreteValueTaskSource_AwaitedTwice_ConsumesSourceOnce()
    {
        var source = new ReusableSource<SuccessResult<string>>();
        var builder = ApiResult.From(source.Task);
        source.SetResult((SuccessResult<string>)Result.Success("x"));

        Assert.Equal("x", (await builder.AsIsAsync()).Value);
        Assert.Equal("x", (await builder.AsIsAsync()).Value);
        Assert.Equal(1, source.GetResultCalls);
    }

    [Fact]
    public async Task Task_AwaitedTwice_ReturnsSameOutcome()
    {
        var tcs = new TaskCompletionSource<Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = ApiResult.From(tcs.Task);
        var pending = builder.AsIsAsync();
        tcs.SetResult(Result.Success(3));

        Assert.Equal(3, (await pending).Value);
        Assert.Equal(3, (await builder.AsIsAsync()).Value);
        Assert.Equal(6, (await builder.AsAsync(v => v * 2)).Value);
    }

    // ───────────────────────────── Pending sources of every shape ─────────────────────────────

    public static TheoryData<string> PendingShapes =>
    [
        "Task<Result<T>>", "Task<SuccessResult<T>>", "Task<InProgressResult<T>>", "Task<FailedResult<T>>", "Task<FailedResult>",
        "ValueTask<Result<T>>", "ValueTask<SuccessResult<T>>", "ValueTask<InProgressResult<T>>", "ValueTask<FailedResult<T>>", "ValueTask<FailedResult>",
    ];

    [Theory]
    [MemberData(nameof(PendingShapes))]
    public async Task PendingSource_AllShapes_Map(string shape)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<TR> Later<TR>(TR value)
        {
            await gate.Task;
            return value;
        }

        var order = new Order(9, "n");
        ApiResult.AsyncResultBuilder<Order> builder = shape switch
        {
            "Task<Result<T>>" => ApiResult.From(Later<Result<Order>>(Result.Success(order))),
            "Task<SuccessResult<T>>" => ApiResult.From(Later((SuccessResult<Order>)Result.Success(order))),
            "Task<InProgressResult<T>>" => ApiResult.From(Later((InProgressResult<Order>)Result.InProgress(order))),
            "Task<FailedResult<T>>" => ApiResult.From(Later((FailedResult<Order>)Result.Failed<Order>(ErrorResult.Create(ErrorType.NotFound, "c", "m")))),
            "Task<FailedResult>" => ApiResult.From<Order>(Later(Result.Resource.NotFound("m"))),
            "ValueTask<Result<T>>" => ApiResult.From(new ValueTask<Result<Order>>(Later<Result<Order>>(Result.Success(order)))),
            "ValueTask<SuccessResult<T>>" => ApiResult.From(new ValueTask<SuccessResult<Order>>(Later((SuccessResult<Order>)Result.Success(order)))),
            "ValueTask<InProgressResult<T>>" => ApiResult.From(new ValueTask<InProgressResult<Order>>(Later((InProgressResult<Order>)Result.InProgress(order)))),
            "ValueTask<FailedResult<T>>" => ApiResult.From(new ValueTask<FailedResult<Order>>(Later((FailedResult<Order>)Result.Failed<Order>(ErrorResult.Create(ErrorType.NotFound, "c", "m"))))),
            "ValueTask<FailedResult>" => ApiResult.From<Order>(new ValueTask<FailedResult>(Later(Result.Resource.NotFound("m")))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var pending = builder.AsAsync(o => o.Id);
        Assert.False(pending.IsCompleted);
        gate.SetResult();
        var api = await pending;

        var expected = shape.Contains("Failed") ? 404 : shape.Contains("InProgress") ? 202 : 200;
        Assert.Equal(expected, api.StatusCode);
        if (expected == 404)
        {
            Assert.Equal(0, api.Value);
            Assert.Single(api.Errors!);
        }
        else
        {
            Assert.Equal(9, api.Value);
            Assert.Null(api.Errors);
        }
    }

    [Fact]
    public async Task CompletedConcreteTasks_MapSynchronously()
    {
        var success = ApiResult.From(Task.FromResult((SuccessResult<int>)Result.Success(1))).AsIsAsync();
        var inProgress = ApiResult.From(Task.FromResult((InProgressResult<int>)Result.InProgress(2))).AsIsAsync();
        var failed = ApiResult.From<int>(Task.FromResult(Result.Validation.InvalidInput("bad"))).AsIsAsync();
        var failedVt = ApiResult.From<int>(new ValueTask<FailedResult>(Result.Resource.NotFound("x"))).AsIsAsync();

        Assert.True(success.IsCompletedSuccessfully);
        Assert.True(inProgress.IsCompletedSuccessfully);
        Assert.True(failed.IsCompletedSuccessfully);
        Assert.True(failedVt.IsCompletedSuccessfully);
        Assert.Equal(200, (await success).StatusCode);
        Assert.Equal(202, (await inProgress).StatusCode);
        Assert.Equal(400, (await failed).StatusCode);
        Assert.Equal(404, (await failedVt).StatusCode);
    }

    // ───────────────────────────── Faults, cancellation, null ─────────────────────────────

    [Fact]
    public async Task FaultedTask_PropagatesException_EveryTime()
    {
        var builder = ApiResult.From(Task.FromException<Result<int>>(new InvalidOperationException("boom")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.AsIsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.AsAsync(v => v));
    }

    [Fact]
    public async Task FaultedValueTaskSource_PropagatesException()
    {
        var source = new ReusableSource<Result<int>>();
        var builder = ApiResult.From(source.Task);
        source.SetException(new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.AsIsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.AsIsAsync());
    }

    [Fact]
    public async Task CanceledToken_PendingSource_Throws_AndBuilderStaysUsable()
    {
        var tcs = new TaskCompletionSource<Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = ApiResult.From(tcs.Task);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.AsIsAsync(cts.Token));

        tcs.SetResult(Result.Success(4));
        Assert.Equal(4, (await builder.AsIsAsync()).Value);
    }

    [Fact]
    public async Task CanceledToken_CompletedSource_StillReturnsResult()
    {
        // Matches Task.WaitAsync semantics: a completed source wins over a canceled token.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var api = await ApiResult.From(Task.FromResult(Result.Success(1))).AsIsAsync(cts.Token);
        Assert.Equal(200, api.StatusCode);
    }

    [Fact]
    public async Task TokenCanceledWhileWaiting_Throws()
    {
        var tcs = new TaskCompletionSource<Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var pending = ApiResult.From(new ValueTask<Result<int>>(tcs.Task)).AsAsync(v => v, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task NullTask_MapsTo500()
    {
        var api = await ApiResult.From((Task<Result<int>>)null!).AsIsAsync();
        Assert.Equal(500, api.StatusCode);
        Assert.Equal("result.null_reference", api.Errors![0].Code);
    }

    [Fact]
    public async Task NullDomainResultInTask_MapsTo500()
    {
        var api = await ApiResult.From(Task.FromResult<Result<int>>(null!)).AsAsync(v => v);
        Assert.Equal(500, api.StatusCode);
        Assert.Equal("NullReference", api.Errors![0].Title);
    }

    [Fact]
    public async Task NullFailedResultInTask_MapsTo500()
    {
        var api = await ApiResult.From<int>(Task.FromResult<FailedResult>(null!)).AsIsAsync();
        Assert.Equal(500, api.StatusCode);
    }

    [Fact]
    public async Task DefaultBuilder_MapsTo500()
    {
        var api = await default(ApiResult.AsyncResultBuilder<int>).AsIsAsync();
        Assert.Equal(500, api.StatusCode);
    }

    [Fact]
    public void AsAsync_NullMap_Throws()
    {
        var builder = ApiResult.From(Task.FromResult(Result.Success(1)));
        Assert.Throws<ArgumentNullException>(() => { _ = builder.AsAsync<int>(null!); });
    }

    [Fact]
    public async Task AsAsync_FailureDoesNotInvokeMapper()
    {
        var called = false;
        var api = await ApiResult.From(Task.FromResult<Result<int>>(Result.Resource.NotFound("x")))
            .AsAsync(v => { called = true; return v; });
        Assert.False(called);
        Assert.Equal(404, api.StatusCode);
    }
}
