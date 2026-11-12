using FluentAssertions;
using KetPsi.Endpoints.Http.Aot;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace HttpEndpointGenerator.Tests.Aot;

public class ParameterBinderMoreTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("-5", -5)]
    [InlineData("2147483647", int.MaxValue)]
    public void Route_Int32_Matrix(string raw, int expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.RouteValues = new RouteValueDictionary { ["id"] = raw };
        ParameterBinder.TryBindRouteInt32(ctx, "id", out var v).Should().BeTrue();
        v.Should().Be(expected);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    public void Query_Bool_Matrix(string raw, bool expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["f"] = raw });
        ParameterBinder.TryBindQueryBool(ctx, "f", out var v).Should().BeTrue();
        v.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("1.25", 1.25)]
    [InlineData("100.00", 100.00)]
    public void Query_Decimal_Matrix(string raw, double expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["p"] = raw });
        ParameterBinder.TryBindQueryDecimal(ctx, "p", out var v).Should().BeTrue();
        ((double)v).Should().BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData("2020-01-01")]
    [InlineData("1999-12-31")]
    [InlineData("2030-06-15")]
    public void Query_DateOnly_Matrix(string raw)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["d"] = raw });
        ParameterBinder.TryBindQueryDateOnly(ctx, "d", out var v).Should().BeTrue();
        v.ToString("yyyy-MM-dd", null).Should().Be(raw);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("a b c")]
    public void Query_String_Matrix(string raw)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["q"] = raw });
        ParameterBinder.TryBindQueryString(ctx, "q", out var v).Should().BeTrue();
        v.Should().Be(raw);
    }

    [Fact]
    public void Route_String_Missing_False()
    {
        var ctx = new DefaultHttpContext();
        ParameterBinder.TryBindRouteString(ctx, "x", out _).Should().BeFalse();
    }

    [Fact]
    public void HttpContext_Passthrough()
    {
        var ctx = new DefaultHttpContext();
        ParameterBinder.HttpContext(ctx).Should().BeSameAs(ctx);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void HttpVerbs_ForMapMethod(string verb)
    {
        var map = "Map" + char.ToUpperInvariant(verb[0]) + verb[1..].ToLowerInvariant();
        if (verb == "GET") map = "MapGet";
        if (verb == "POST") map = "MapPost";
        if (verb == "PUT") map = "MapPut";
        if (verb == "DELETE") map = "MapDelete";
        if (verb == "PATCH") map = "MapPatch";
        HttpVerbs.ForMapMethod(map).Should().Contain(verb);
    }
}
