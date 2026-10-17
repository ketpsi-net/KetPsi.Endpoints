using KetPsi.Endpoints.Results;
using KetPsi.Results;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public class ApiResultErrorTests
{
    [Fact]
    public void From_ErrorResult_MapsAllFields()
    {
        var error = ErrorResult.Create(
            ErrorType.NotFound,
            "resource.not_found",
            "missing",
            new Dictionary<string, object?> { ["id"] = 9 });

        var api = ApiResultError.From(error);

        Assert.Equal("resource.not_found", api.Code);
        Assert.Equal("missing", api.Message);
        Assert.Equal("NotFound", api.Title);
        Assert.Equal("9", api.Metadata!["id"]);
        Assert.Equal(404, api.StatusCode);
    }

    [Fact]
    public void From_ErrorResult_ExplicitStatusOverride()
    {
        var error = ErrorResult.Create(ErrorType.Failure, "x", "y");
        var api = ApiResultError.From(error, statusCode: 502);
        Assert.Equal(502, api.StatusCode);
    }
}
