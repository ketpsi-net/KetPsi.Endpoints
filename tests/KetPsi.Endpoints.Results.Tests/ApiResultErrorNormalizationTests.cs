using System.Globalization;
using KetPsi.Results;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public class ApiResultErrorNormalizationTests
{
    // ───────────────────────────── BadRequest(params ApiResultError[]) ─────────────────────────────

    [Fact]
    public void BadRequest_NullArray_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => ApiResult.Request.BadRequest((ApiResultError[])null!));
        Assert.Throws<ArgumentNullException>(() => ApiResult.Request.BadRequest<int>((ApiResultError[])null!));
    }

    [Fact]
    public void BadRequest_NullElement_ThrowsArgument()
    {
        var ex = Assert.Throws<ArgumentException>(() => ApiResult.Request.BadRequest(new ApiResultError("a", "b"), null!));
        Assert.Equal("errors", ex.ParamName);
    }

    [Fact]
    public void BadRequest_NoErrors_IsEmptyList()
    {
        var r = ApiResult.Request.BadRequest();
        Assert.Equal(400, r.StatusCode);
        Assert.NotNull(r.Errors);
        Assert.Empty(r.Errors);

        var typed = ApiResult.Request.BadRequest<string>(Array.Empty<ApiResultError>());
        Assert.Empty(typed.Errors!);
        Assert.Null(typed.Value);
    }

    [Fact]
    public void BadRequest_PreservesExplicitStatus_DefaultsMissingTo400()
    {
        var r = ApiResult.Request.BadRequest<int>(
            new ApiResultError("a", "b", StatusCode: 422),
            new ApiResultError("c", "d"));

        Assert.Equal(422, r.Errors![0].StatusCode);
        Assert.Equal(400, r.Errors[1].StatusCode);
        Assert.Equal(0, r.Value);
    }

    [Fact]
    public void BadRequest_CopiesCallerArray()
    {
        var errors = new[] { new ApiResultError("a", "b", StatusCode: 400) };
        var r = ApiResult.Request.BadRequest(errors);

        errors[0] = new ApiResultError("mutated", "x");

        Assert.Equal("a", r.Errors![0].Code);
    }

    // ───────────────────────────── Titles ─────────────────────────────

    [Fact]
    public void From_ErrorResult_TitleMatchesEnumName_ForEveryErrorType()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            var api = ApiResultError.From(ErrorResult.Create(type, "c", "m"));
            Assert.Equal(type.ToString(), api.Title);
            Assert.Equal(type.ToStatusCode(), api.StatusCode);
        }
    }

    [Fact]
    public void From_ErrorResult_UndefinedErrorType_TitleIsNumber()
    {
        var api = ApiResultError.From(ErrorResult.Create((ErrorType)123, "c", "m"), statusCode: 500);
        Assert.Equal("123", api.Title);
    }

    [Fact]
    public void From_ErrorResult_TitleIsInterned_NoPerCallAllocation()
    {
        var a = ApiResultError.From(ErrorResult.Create(ErrorType.NotFound, "c", "m"));
        var b = ApiResultError.From(ErrorResult.Create(ErrorType.NotFound, "c", "m"));
        Assert.Same(a.Title, b.Title);
    }

    // ───────────────────────────── Metadata ─────────────────────────────

    [Fact]
    public void Metadata_IsFormattedWithInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var metadata = new Dictionary<string, object?>
            {
                ["double"] = 1.5,
                ["decimal"] = 1234.5m,
                ["date"] = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                ["int"] = 1000,
                ["string"] = "text",
                ["null"] = null,
                ["guid"] = Guid.Empty,
                ["other"] = new Uri("https://x.test/a"),
            };

            var fromFactory = ApiResult.Request.NotFound(metadata: metadata).Errors![0].Metadata!;
            var fromDomain = ApiResultError.From(ErrorResult.Create(ErrorType.NotFound, "c", "m", metadata)).Metadata!;

            foreach (var m in new[] { fromFactory, fromDomain })
            {
                Assert.Equal("1.5", m["double"]);
                Assert.Equal("1234.5", m["decimal"]);
                Assert.Equal("01/02/2026 03:04:05", m["date"]);
                Assert.Equal("1000", m["int"]);
                Assert.Equal("text", m["string"]);
                Assert.Null(m["null"]);
                Assert.Equal(Guid.Empty.ToString(), m["guid"]);
                Assert.Equal("https://x.test/a", m["other"]);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Metadata_PreservesSourceDictionaryComparer()
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Id"] = 7 };
        var m = ApiResult.Request.Conflict(metadata: metadata).Errors![0].Metadata!;
        Assert.Equal("7", m["id"]);
    }

    [Fact]
    public void Metadata_NullStaysNull_EmptyIsEmpty()
    {
        Assert.Null(ApiResult.Request.NotFound().Errors![0].Metadata);

        var empty = ApiResult.Request.NotFound(metadata: new Dictionary<string, object?>()).Errors![0].Metadata;
        Assert.NotNull(empty);
        Assert.Empty(empty);
    }

    [Fact]
    public void Metadata_IsNotAliasedToSource()
    {
        var metadata = new Dictionary<string, object?> { ["k"] = 1 };
        var m = ApiResult.Server.BadGateway(metadata: metadata).Errors![0].Metadata!;
        metadata["k"] = 2;
        Assert.Equal("1", m["k"]);
    }

    [Fact]
    public void From_ErrorResult_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => ApiResultError.From(null!));
}
