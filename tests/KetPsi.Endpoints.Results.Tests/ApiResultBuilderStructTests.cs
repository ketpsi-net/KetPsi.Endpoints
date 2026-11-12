using KetPsi.Results;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public class ApiResultBuilderStructTests
{
    [Fact]
    public void Builders_AreReadOnlyValueTypes()
    {
        foreach (var type in new[]
                 {
                     typeof(ApiResult.NonGenericResultBuilder),
                     typeof(ApiResult.ResultBuilder<int>),
                     typeof(ApiResult.AsyncResultBuilder<int>),
                 })
        {
            Assert.True(type.IsValueType, type.Name);
            Assert.Contains(type.CustomAttributes, a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        }
    }

    [Fact]
    public void DefaultNonGenericBuilder_MapsTo500()
    {
        var api = default(ApiResult.NonGenericResultBuilder).AsIs();
        Assert.Equal(500, api.StatusCode);
        Assert.Equal("result.null_reference", api.Errors![0].Code);
        Assert.Equal(500, api.Errors[0].StatusCode);
    }

    [Fact]
    public void DefaultGenericBuilder_MapsTo500_ForEveryMethod()
    {
        var builder = default(ApiResult.ResultBuilder<string>);
        Assert.Equal(500, builder.AsIs().StatusCode);
        Assert.Equal(500, builder.As(s => s.Length).StatusCode);
        Assert.Equal(500, builder.As(s => s.Length, () => -1).StatusCode);
    }

    [Fact]
    public void NullDomainResult_MapsTo500()
    {
        Assert.Equal(500, ApiResult.From((Result)null!).AsIs().StatusCode);
        Assert.Equal(500, ApiResult.From((Result<int>)null!).AsIs().StatusCode);
    }

    [Fact]
    public void Builder_CanBeReusedAndCopied()
    {
        var builder = ApiResult.From(Result.Success(2));
        var copy = builder;
        Assert.Equal(2, builder.AsIs().Value);
        Assert.Equal(4, copy.As(v => v * 2).Value);
        Assert.Equal(2, builder.AsIs().Value);
    }

    [Fact]
    public void As_NullMappers_Throw()
    {
        var builder = ApiResult.From(Result.Success(1));
        Assert.Throws<ArgumentNullException>(() => builder.As<int>(null!));
        Assert.Throws<ArgumentNullException>(() => builder.As(v => v, null!));
        Assert.Throws<ArgumentNullException>(() => builder.As<int>(null!, () => 0));
    }

    [Fact]
    public void As_WhenNull_UsedForNullPayload_InProgressToo()
    {
        var success = ApiResult.From(Result.Success<string?>(null)).As(s => s!.Length, () => -1);
        Assert.Equal(200, success.StatusCode);
        Assert.Equal(-1, success.Value);

        var inProgress = ApiResult.From(Result.InProgress<string?>(null)).As(s => s!.Length, () => -2);
        Assert.Equal(202, inProgress.StatusCode);
        Assert.Equal(-2, inProgress.Value);

        var present = ApiResult.From(Result.InProgress<string?>("abc")).As(s => s!.Length, () => -2);
        Assert.Equal(3, present.Value);
    }

    [Fact]
    public void As_WhenNull_FailureIgnoresBothDelegates()
    {
        Result<string> domain = Result.Business.QuotaExceeded();
        var api = ApiResult.From(domain).As<int>(_ => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Equal(429, api.StatusCode);
    }

    [Fact]
    public void ConcreteNonGeneric_SuccessAndInProgress()
    {
        Assert.Equal(200, ApiResult.From((SuccessResult)Result.Success()).AsIs().StatusCode);
        Assert.Equal(202, ApiResult.From((InProgressResult)Result.InProgress()).AsIs().StatusCode);
        Assert.Same(ApiResult.Ok(), ApiResult.From(Result.Success()).AsIs());
        Assert.Same(ApiResult.Accepted(), ApiResult.From(Result.InProgress()).AsIs());
    }

    [Fact]
    public void ConcreteGeneric_InProgressAndFailed()
    {
        var inProgress = ApiResult.From((InProgressResult<int>)Result.InProgress(5)).AsIs();
        Assert.Equal(202, inProgress.StatusCode);
        Assert.Equal(5, inProgress.Value);

        var failed = ApiResult.From((FailedResult<int>)Result.Failed<int>(ErrorResult.Create(ErrorType.Conflict, "c", "m"))).AsIs();
        Assert.Equal(409, failed.StatusCode);
        Assert.Equal("Conflict", failed.Errors![0].Title);
    }

    [Fact]
    public void FailedWithoutErrors_MapsTo500()
    {
        var api = ApiResult.From(Result.System.InternalError()).AsIs();
        Assert.Equal(500, api.StatusCode);
        Assert.Equal("server.internal_error", api.Errors![0].Code);
    }

    [Fact]
    public void MappedErrors_CarryPerErrorStatusAndMetadata()
    {
        var domain = Result.Failed(
            ErrorResult.Create(ErrorType.RuleViolation, "r", "m", new Dictionary<string, object?> { ["n"] = 2.5 }),
            ErrorResult.Create(ErrorType.Forbidden, "f", "m"));

        var api = ApiResult.From(domain).AsIs();
        Assert.Equal(422, api.StatusCode);
        Assert.Equal(422, api.Errors![0].StatusCode);
        Assert.Equal(403, api.Errors[1].StatusCode);
        Assert.Equal("2.5", api.Errors[0].Metadata!["n"]);
        Assert.Equal("RuleViolation", api.Errors[0].Title);
    }

    [Fact]
    public void Create_And_PublicConstructor_SetAllFields()
    {
        var errors = new[] { new ApiResultError("c", "m") };
        var a = ApiResult<int>.Create(418, 7, errors);
        var b = new ApiResult<int>(418, 7, errors);
        Assert.Equal(418, a.StatusCode);
        Assert.Equal(7, a.Value);
        Assert.Same(errors, a.Errors);
        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.Equal(a.Value, b.Value);
    }
}
