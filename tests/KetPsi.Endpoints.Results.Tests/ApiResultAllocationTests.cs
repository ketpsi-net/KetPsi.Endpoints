using KetPsi.Results;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

/// <summary>
/// Allocation guarantees. Measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/>, so every test is synchronous
/// (no thread hops) and warms up first so JIT/tiering and static initialization are excluded.
/// </summary>
public class ApiResultAllocationTests
{
    private const int Iterations = 1_000;

    private static long AllocatedPerCall(Func<object?> action)
    {
        // Warm-up: JIT, static ctor, tiering.
        for (var i = 0; i < 100; i++)
            GC.KeepAlive(action());

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
            GC.KeepAlive(action());
        var after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / Iterations;
    }

    [Fact]
    public void Ok_NonGeneric_IsZeroAlloc()
        => Assert.Equal(0, AllocatedPerCall(static () => ApiResult.Ok()));

    [Fact]
    public void Accepted_NonGeneric_IsZeroAlloc()
        => Assert.Equal(0, AllocatedPerCall(static () => ApiResult.Accepted()));

    [Fact]
    public void NoContent_IsZeroAlloc()
        => Assert.Equal(0, AllocatedPerCall(static () => ApiResult.NoContent()));

    [Fact]
    public void PayloadlessFactories_ReturnCachedInstances()
    {
        Assert.Same(ApiResult.Ok(), ApiResult.Ok());
        Assert.Same(ApiResult.Accepted(), ApiResult.Accepted());
        Assert.Same(ApiResult.NoContent(), ApiResult.NoContent());
        Assert.NotSame(ApiResult.Ok(), ApiResult.NoContent());
    }

    [Fact]
    public void From_NonGenericSuccess_AsIs_IsZeroAlloc()
    {
        var success = Result.Success();
        Assert.Equal(0, AllocatedPerCall(() => ApiResult.From(success).AsIs()));
    }

    [Fact]
    public void From_NonGenericInProgress_AsIs_IsZeroAlloc()
    {
        var inProgress = Result.InProgress();
        Assert.Equal(0, AllocatedPerCall(() => ApiResult.From(inProgress).AsIs()));
    }

    [Fact]
    public void From_Generic_AsIs_AllocatesOnlyTheResult()
    {
        // The builder is a readonly struct: the only allocation is the ApiResult<T> itself.
        var success = Result.Success("payload");
        var perCall = AllocatedPerCall(() => ApiResult.From(success).AsIs());
        var resultOnly = AllocatedPerCall(static () => ApiResult.Ok("payload"));
        Assert.Equal(resultOnly, perCall);
    }

    [Fact]
    public void AsyncBuilder_CompletedSource_NoStateMachine()
    {
        // Completed source → mapping runs synchronously: Task<ApiResult<T>> + ApiResult<T>, nothing else.
        var completed = Task.FromResult(Result.Success("payload"));
        var perCall = AllocatedPerCall(() => ApiResult.From(completed).AsIsAsync());
        var baseline = AllocatedPerCall(static () => Task.FromResult(ApiResult.Ok("payload")));
        Assert.Equal(baseline, perCall);
    }

    [Fact]
    public void AsyncBuilder_CompletedValueTask_NoStateMachine()
    {
        var domain = Result.Success("payload");
        var perCall = AllocatedPerCall(() => ApiResult.From(new ValueTask<Result<string>>(domain)).AsIsAsync());
        var baseline = AllocatedPerCall(static () => Task.FromResult(ApiResult.Ok("payload")));
        Assert.Equal(baseline, perCall);
    }
}
