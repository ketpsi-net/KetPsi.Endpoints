using KetPsi.Results;

using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public class ApiResultFromBuilderTests
{
    private sealed record Order(int Id, string Name);
    private sealed record OrderDto(int Id, string Name);

    // ───────────────────────────── Concrete factory returns ─────────────────────────────

    [Fact]
    public void From_ConcreteFailedResult_NonGeneric_AsIs()
    {
        FailedResult domain = Result.Resource.NotFound("gone");
        var api = ApiResult.From(domain).AsIs();
        Assert.Equal(404, api.StatusCode);
        Assert.Equal("gone", api.Errors[0]!.Message);
    }

    [Fact]
    public void From_ConcreteFailedResult_Typed_AsIs()
    {
        FailedResult domain = Result.Resource.NotFound("gone");
        var api = ApiResult.From<Order>(domain).AsIs();
        Assert.Equal(404, api.StatusCode);
        Assert.Null(api.Value);
        Assert.Equal("gone", api.Errors[0]!.Message);
    }

    [Fact]
    public void From_ConcreteSuccessResult_AsIs()
    {
        SuccessResult<Order> domain = (SuccessResult<Order>)Result.Success(new Order(1, "a"));
        var api = ApiResult.From(domain).AsIs();
        Assert.Equal(200, api.StatusCode);
        Assert.Equal(1, api.Value!.Id);
    }

    [Fact]
    public async Task From_TaskOfFailedResult_Typed_AsIsAsync()
    {
        Task<FailedResult> task = Task.FromResult(Result.Validation.InvalidInput("bad"));
        var api = await ApiResult.From<Order>(task).AsIsAsync();
        Assert.Equal(400, api.StatusCode);
        Assert.Null(api.Value);
    }

    // ───────────────────────────── From Result (non-generic) ─────────────────────────────

    [Fact]
    public void From_NonGeneric_Success_Is200()
    {
        var api = ApiResult.From(Result.Success()).AsIs();
        Assert.Equal(200, api.StatusCode);
        Assert.Null(api.Errors);
    }

    [Fact]
    public void From_NonGeneric_InProgress_Is202()
    {
        var api = ApiResult.From(Result.InProgress()).AsIs();
        Assert.Equal(202, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_NotFound_Is404()
    {
        var api = ApiResult.From(Result.Resource.NotFound("gone")).AsIs();
        Assert.Equal(404, api.StatusCode);
        Assert.Equal("resource.not_found", api.Errors[0]!.Code);
        Assert.Equal("gone", api.Errors[0].Message);
        Assert.Equal("NotFound", api.Errors[0].Title);
    }

    [Fact]
    public void From_NonGeneric_Failed_Validation_Is400()
    {
        var api = ApiResult.From(Result.Validation.InvalidInput("bad")).AsIs();
        Assert.Equal(400, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_Unauthorized_Is401()
    {
        var api = ApiResult.From(Result.Authentication.Unauthorized()).AsIs();
        Assert.Equal(401, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_Forbidden_Is403()
    {
        var api = ApiResult.From(Result.Permissions.Forbidden()).AsIs();
        Assert.Equal(403, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_Conflict_Is409()
    {
        var api = ApiResult.From(Result.Resource.AlreadyExists()).AsIs();
        Assert.Equal(409, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_RuleViolation_Is422()
    {
        var api = ApiResult.From(Result.Business.RuleViolation("nope")).AsIs();
        Assert.Equal(422, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_Quota_Is429()
    {
        var api = ApiResult.From(Result.Business.QuotaExceeded()).AsIs();
        Assert.Equal(429, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_Dependency_Is503()
    {
        var api = ApiResult.From(Result.Service.Unavailable("keycloak")).AsIs();
        Assert.Equal(503, api.StatusCode);
        Assert.StartsWith("integration.keycloak", api.Errors[0]!.Code);
    }

    [Fact]
    public void From_NonGeneric_Failed_Internal_Is500()
    {
        var api = ApiResult.From(Result.System.InternalError()).AsIs();
        Assert.Equal(500, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_Failed_Canceled_Is499()
    {
        var api = ApiResult.From(Result.System.ExecutionCanceled()).AsIs();
        Assert.Equal(499, api.StatusCode);
    }

    [Fact]
    public void From_NonGeneric_MultipleErrors_UsesFirstForStatus()
    {
        var domain = Result.Failed(
            ErrorResult.Create(ErrorType.Validation, "v.1", "a"),
            ErrorResult.Create(ErrorType.NotFound, "n.1", "b"));

        var api = ApiResult.From(domain).AsIs();
        Assert.Equal(400, api.StatusCode);
        Assert.Equal(2, api.Errors!.Count);
        Assert.Equal("v.1", api.Errors[0].Code);
        Assert.Equal("n.1", api.Errors[1].Code);
    }

    // ───────────────────────────── From Result&lt;T&gt; ─────────────────────────────

    [Fact]
    public void From_Generic_Success_AsIs()
    {
        var api = ApiResult.From(Result.Success(new Order(1, "a"))).AsIs();
        Assert.Equal(200, api.StatusCode);
        Assert.Equal(1, api.Value!.Id);
        Assert.Equal("a", api.Value.Name);
    }

    [Fact]
    public void From_Generic_InProgress_AsIs()
    {
        var api = ApiResult.From(Result.InProgress("track-1")).AsIs();
        Assert.Equal(202, api.StatusCode);
        Assert.Equal("track-1", api.Value);
    }

    [Fact]
    public void From_Generic_Failed_AsIs_NoData()
    {
        Result<Order> domain = Result.Resource.NotFound("missing");
        var api = ApiResult.From(domain).AsIs();
        Assert.Equal(404, api.StatusCode);
        Assert.Null(api.Value);
        Assert.Equal("missing", api.Errors[0]!.Message);
    }

    [Fact]
    public void From_Generic_As_MapsPayload()
    {
        var api = ApiResult.From(Result.Success(new Order(7, "x")))
            .As(o => new OrderDto(o.Id, o.Name.ToUpperInvariant()));

        Assert.Equal(200, api.StatusCode);
        Assert.Equal(7, api.Value!.Id);
        Assert.Equal("X", api.Value.Name);
    }

    [Fact]
    public void From_Generic_As_OnFailure_DoesNotCallMapper()
    {
        var mapperCalled = false;
        Result<Order> domain = Result.Validation.InvalidInput("bad");

        var api = ApiResult.From(domain).As(o =>
        {
            mapperCalled = true;
            return new OrderDto(o.Id, o.Name);
        });

        Assert.False(mapperCalled);
        Assert.Equal(400, api.StatusCode);
        Assert.Null(api.Value);
    }

    [Fact]
    public void From_Generic_As_NullMapper_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ApiResult.From(Result.Success(1)).As<string>(null!));
    }

    [Fact]
    public void From_Generic_As_WhenNull_UsesFallback()
    {
        var api = ApiResult.From(Result.Success<string?>(null))
            .As(s => s!.Length, whenNull: () => -1);

        Assert.Equal(200, api.StatusCode);
        Assert.Equal(-1, api.Value);
    }

    [Fact]
    public void From_Generic_PreservesMetadata()
    {
        var error = ErrorResult.Create(
            ErrorType.Failure,
            "integration.keycloak",
            "failed",
            new Dictionary<string, object?> { ["status_code"] = 502 });

        Result<string> domain = Result.Failed(error);
        var api = ApiResult.From(domain).AsIs();

        Assert.Equal(500, api.StatusCode);
        Assert.Equal(502, api.Errors[0]!.Metadata!["status_code"]);
    }

    // ───────────────────────────── Async ─────────────────────────────

    [Fact]
    public async Task From_Async_Success_AsIsAsync()
    {
        Task<SuccessResult<int>> task = Task.FromResult(Result.Success(99));
        var api = await ApiResult.From(task).AsIsAsync();
        Assert.Equal(200, api.StatusCode);
        Assert.Equal(99, api.Value);
    }

    [Fact]
    public async Task From_Async_Failed_AsAsync()
    {
        Task<Result<Order>> task = Task.FromResult<Result<Order>>(Result.Resource.NotFound());
        var api = await ApiResult.From(task).AsAsync(o => new OrderDto(o.Id, o.Name));
        Assert.Equal(404, api.StatusCode);
        Assert.Null(api.Value);
    }

    [Fact]
    public async Task From_ValueTask_Success_AsIsAsync()
    {
        ValueTask<Result<int>> vt = new(Result.Success(42));
        var api = await ApiResult.From(vt).AsIsAsync();
        Assert.Equal(200, api.StatusCode);
        Assert.Equal(42, api.Value);
    }

    [Fact]
    public async Task From_ValueTask_Failed_AsAsync()
    {
        Result<Order> domain = Result.Resource.NotFound("gone");
        ValueTask<Result<Order>> vt = new(domain);
        var api = await ApiResult.From(vt).AsAsync(o => new OrderDto(o.Id, o.Name));
        Assert.Equal(404, api.StatusCode);
        Assert.Null(api.Value);
        Assert.Equal("gone", api.Errors[0]!.Message);
    }

    [Fact]
    public async Task From_ValueTask_FromTask_AsIsAsync()
    {
        ValueTask<SuccessResult<string>> vt = new(Task.FromResult(Result.Success("ok")));
        var api = await ApiResult.From(vt).AsIsAsync();
        Assert.Equal(200, api.StatusCode);
        Assert.Equal("ok", api.Value);
    }

    [Fact]
    public async Task From_Async_CanceledToken_Throws()
    {
        var tcs = new TaskCompletionSource<Result<int>>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ApiResult.From(tcs.Task).AsIsAsync(cts.Token));
    }

    // ───────────────────────────── ErrorType

    // ───────────────────────────── ErrorType → status matrix ─────────────────────────────

    [Theory]
    [InlineData(ErrorType.Failure, 500)]
    [InlineData(ErrorType.DependencyFailure, 503)]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.RuleViolation, 422)]
    [InlineData(ErrorType.QuotaExceeded, 429)]
    [InlineData(ErrorType.Canceled, 499)]
    public void From_Failed_MapsErrorTypeToHttpStatus(ErrorType type, int expectedStatus)
    {
        var domain = Result.Failed(ErrorResult.Create(type, "c", "m"));
        var api = ApiResult.From(domain).AsIs();
        Assert.Equal(expectedStatus, api.StatusCode);
        Assert.Equal(expectedStatus, api.Errors[0]!.StatusCode);
    }
}
