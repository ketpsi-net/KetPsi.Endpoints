using KetPsi.Endpoints.Results;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public class ApiResultFactoryTests
{
    // ───────────────────────────── 2xx ─────────────────────────────

    [Fact]
    public void Ok_NonGeneric_Is200()
    {
        var r = ApiResult.Ok();
        Assert.Equal(200, r.StatusCode);
        Assert.Null(r.Errors);
    }

    [Fact]
    public void Ok_Generic_CarriesData()
    {
        var r = ApiResult.Ok(42);
        Assert.Equal(200, r.StatusCode);
        Assert.Equal(42, r.Value);
        Assert.Null(r.Errors);
    }

    [Fact]
    public void Created_Is201()
    {
        var r = ApiResult.Created("new-id");
        Assert.Equal(201, r.StatusCode);
        Assert.Equal("new-id", r.Value);
    }

    [Fact]
    public void Accepted_Is202()
    {
        var r = ApiResult.Accepted();
        Assert.Equal(202, r.StatusCode);
    }

    [Fact]
    public void Accepted_Generic_CarriesData()
    {
        var r = ApiResult.Accepted("job-1");
        Assert.Equal(202, r.StatusCode);
        Assert.Equal("job-1", r.Value);
    }

    [Fact]
    public void NoContent_Is204()
    {
        var r = ApiResult.NoContent();
        Assert.Equal(204, r.StatusCode);
    }

    // ───────────────────────────── 4xx Request ─────────────────────────────

    [Fact]
    public void Request_BadRequest_Is400()
    {
        var r = ApiResult.Request.BadRequest("validation.email", "Email required");
        Assert.Equal(400, r.StatusCode);
        Assert.Equal("validation.email", r.Errors![0].Code);
        Assert.Equal("Email required", r.Errors[0].Message);
        Assert.Equal(400, r.Errors[0].StatusCode);
    }

    [Fact]
    public void Request_BadRequest_MultipleErrors()
    {
        var r = ApiResult.Request.BadRequest(
            new ApiResultError("v.email", "bad email"),
            new ApiResultError("v.age", "too young"));

        Assert.Equal(400, r.StatusCode);
        Assert.Equal(2, r.Errors!.Count);
        Assert.All(r.Errors, e => Assert.Equal(400, e.StatusCode));
    }

    [Fact]
    public void Request_Unauthorized_Is401()
    {
        var r = ApiResult.Request.Unauthorized();
        Assert.Equal(401, r.StatusCode);
        Assert.Equal("auth.unauthorized", r.Errors![0].Code);
    }

    [Fact]
    public void Request_Forbidden_Is403()
    {
        var r = ApiResult.Request.Forbidden();
        Assert.Equal(403, r.StatusCode);
    }

    [Fact]
    public void Request_NotFound_Is404()
    {
        var r = ApiResult.Request.NotFound();
        Assert.Equal(404, r.StatusCode);
        Assert.Equal("resource.not_found", r.Errors![0].Code);
    }

    [Fact]
    public void Request_NotFound_Generic_NoData()
    {
        var r = ApiResult.Request.NotFound<string>("resource.not_found", "gone");
        Assert.Equal(404, r.StatusCode);
        Assert.Null(r.Value);
        Assert.Equal("gone", r.Errors![0].Message);
    }

    [Fact]
    public void Request_Conflict_Is409()
    {
        var r = ApiResult.Request.Conflict();
        Assert.Equal(409, r.StatusCode);
    }

    [Fact]
    public void Request_UnprocessableEntity_Is422()
    {
        var r = ApiResult.Request.UnprocessableEntity();
        Assert.Equal(422, r.StatusCode);
    }

    [Fact]
    public void Request_TooManyRequests_Is429()
    {
        var r = ApiResult.Request.TooManyRequests();
        Assert.Equal(429, r.StatusCode);
    }

    [Fact]
    public void Request_Canceled_Is499()
    {
        var r = ApiResult.Request.Canceled();
        Assert.Equal(499, r.StatusCode);
    }

    [Fact]
    public void Request_WithMetadata()
    {
        var meta = new Dictionary<string, object?> { ["field"] = "email" };
        var r = ApiResult.Request.BadRequest("v.1", "bad", meta);
        Assert.Equal("email", r.Errors![0].Metadata!["field"]);
    }

    // ───────────────────────────── 5xx Server ─────────────────────────────

    [Fact]
    public void Server_InternalError_Is500()
    {
        var r = ApiResult.Server.InternalError();
        Assert.Equal(500, r.StatusCode);
        Assert.Equal("server.internal_error", r.Errors![0].Code);
    }

    [Fact]
    public void Server_BadGateway_Is502()
    {
        var r = ApiResult.Server.BadGateway();
        Assert.Equal(502, r.StatusCode);
    }

    [Fact]
    public void Server_ServiceUnavailable_Is503()
    {
        var r = ApiResult.Server.ServiceUnavailable();
        Assert.Equal(503, r.StatusCode);
    }

    [Fact]
    public void Server_GatewayTimeout_Is504()
    {
        var r = ApiResult.Server.GatewayTimeout();
        Assert.Equal(504, r.StatusCode);
    }

    [Fact]
    public void Server_Generic_NoData()
    {
        var r = ApiResult.Server.InternalError<int>();
        Assert.Equal(500, r.StatusCode);
        Assert.Equal(0, r.Value);
        Assert.NotNull(r.Errors);
    }

}
