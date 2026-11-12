using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace KetPsi.Endpoints.Results.Tests;

public sealed record JsonOrderDto(int Id, string Name);

/// <summary>An application-style source-generated context: only declares its own typed results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiResult<JsonOrderDto>))]
[JsonSerializable(typeof(ApiResult<int>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>
/// JSON round-trips through source-generated metadata only (no reflection resolver in the chain), i.e. the Native AOT
/// configuration.
/// </summary>
public class ApiResultJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(AppJsonContext.Default, ApiResultJsonContext.Default),
    };

    [Fact]
    public void PackageContext_CoversPayloadlessShapes()
    {
        Assert.NotNull(ApiResultJsonContext.Default.GetTypeInfo(typeof(ApiResult<object>)));
        Assert.NotNull(ApiResultJsonContext.Default.GetTypeInfo(typeof(ApiResultError)));
        Assert.NotNull(ApiResultJsonContext.Default.GetTypeInfo(typeof(IReadOnlyList<ApiResultError>)));
    }

    [Fact]
    public void Ok_Serializes_WithPackageContextOnly()
    {
        var json = JsonSerializer.Serialize(ApiResult.Ok(), ApiResultJsonContext.Default.ApiResultObject);
        Assert.Equal("""{"statusCode":200,"value":null,"errors":null}""", json);
    }

    [Fact]
    public void NoContent_Serializes_ThroughCombinedResolver()
    {
        var json = JsonSerializer.Serialize<ApiResult<object?>>(ApiResult.NoContent(), Options);
        Assert.Equal("""{"statusCode":204,"value":null,"errors":null}""", json);
    }

    [Fact]
    public void Error_RoundTrips()
    {
        var original = ApiResult.Request.NotFound(
            "order.not_found",
            "Order 9 was not found",
            new Dictionary<string, object?> { ["id"] = 9, ["ratio"] = 0.5 });

        var json = JsonSerializer.Serialize<ApiResult<object?>>(original, Options);
        var back = JsonSerializer.Deserialize<ApiResult<object?>>(json, Options)!;

        Assert.Equal(404, back.StatusCode);
        Assert.Null(back.Value);
        var error = Assert.Single(back.Errors!);
        Assert.Equal("order.not_found", error.Code);
        Assert.Equal("Order 9 was not found", error.Message);
        Assert.Equal("NotFound", error.Title);
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("9", error.Metadata!["id"]);
        Assert.Equal("0.5", error.Metadata["ratio"]);
        Assert.Contains("\"statusCode\":404", json);
        Assert.Contains("\"metadata\":{\"id\":\"9\"", json);
    }

    [Fact]
    public void TypedResult_RoundTrips_ThroughAppContext()
    {
        var original = ApiResult.Created(new JsonOrderDto(3, "pen"));
        var json = JsonSerializer.Serialize(original, AppJsonContext.Default.ApiResultJsonOrderDto);
        Assert.Equal("""{"statusCode":201,"value":{"id":3,"name":"pen"},"errors":null}""", json);

        var back = JsonSerializer.Deserialize(json, AppJsonContext.Default.ApiResultJsonOrderDto)!;
        Assert.Equal(201, back.StatusCode);
        Assert.Equal(new JsonOrderDto(3, "pen"), back.Value);
        Assert.Null(back.Errors);
    }

    [Fact]
    public void TypedFailure_RoundTrips()
    {
        var original = ApiResult.Request.BadRequest<int>(new ApiResultError("v.a", "bad a"), new ApiResultError("v.b", "bad b", StatusCode: 422));
        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<ApiResult<int>>(json, Options)!;

        Assert.Equal(400, back.StatusCode);
        Assert.Equal(2, back.Errors!.Count);
        Assert.Equal(400, back.Errors[0].StatusCode);
        Assert.Equal(422, back.Errors[1].StatusCode);
        Assert.Null(back.Errors[0].Metadata);
    }

    [Fact]
    public void ErrorRecord_RoundTrips_WithPackageContext()
    {
        var error = new ApiResultError("c", "m", "T", new Dictionary<string, string?> { ["k"] = null }, 409);
        var json = JsonSerializer.Serialize(error, ApiResultJsonContext.Default.ApiResultError);
        var back = JsonSerializer.Deserialize(json, ApiResultJsonContext.Default.ApiResultError)!;
        Assert.Equal(error.Code, back.Code);
        Assert.Equal(error.Title, back.Title);
        Assert.Equal(409, back.StatusCode);
        Assert.Null(back.Metadata!["k"]);
    }
}
