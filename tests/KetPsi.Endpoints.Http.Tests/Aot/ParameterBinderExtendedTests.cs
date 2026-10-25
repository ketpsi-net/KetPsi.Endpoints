using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace HttpEndpointGenerator.Tests.Aot;

public class ParameterBinderExtendedTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("-1", -1)]
    public void TryBindQueryInt32_Parses(string raw, int expected)
    {
        var ctx = Ctx(query: ("id", raw));
        ParameterBinder.TryBindQueryInt32(ctx, "id", out var v).Should().BeTrue();
        v.Should().Be(expected);
    }

    [Fact]
    public void TryBindQueryInt32_Missing_Returns_False()
    {
        var ctx = Ctx();
        ParameterBinder.TryBindQueryInt32(ctx, "id", out _).Should().BeFalse();
    }

    [Fact]
    public void TryBindRouteGuid_Parses()
    {
        var g = Guid.NewGuid();
        var ctx = Ctx(route: ("gid", g.ToString()));
        ParameterBinder.TryBindRouteGuid(ctx, "gid", out var v).Should().BeTrue();
        v.Should().Be(g);
    }

    [Fact]
    public void TryBindQueryDecimal_Parses()
    {
        var ctx = Ctx(query: ("price", "12.50"));
        ParameterBinder.TryBindQueryDecimal(ctx, "price", out var v).Should().BeTrue();
        v.Should().Be(12.50m);
    }

    [Fact]
    public void TryBindQueryDateOnly_Parses()
    {
        var ctx = Ctx(query: ("d", "2024-06-15"));
        ParameterBinder.TryBindQueryDateOnly(ctx, "d", out var v).Should().BeTrue();
        v.Should().Be(new DateOnly(2024, 6, 15));
    }

    [Fact]
    public void TryBindQueryBool_Parses_True()
    {
        var ctx = Ctx(query: ("flag", "true"));
        ParameterBinder.TryBindQueryBool(ctx, "flag", out var v).Should().BeTrue();
        v.Should().BeTrue();
    }

    [Fact]
    public void TryBindHeaderString_Reads_Header()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Trace"] = "abc";
        ParameterBinder.TryBindHeaderString(ctx, "X-Trace", out var v).Should().BeTrue();
        v.Should().Be("abc");
    }

    [Fact]
    public void Cancellation_Is_RequestAborted()
    {
        var ctx = new DefaultHttpContext();
        using var cts = new CancellationTokenSource();
        ctx.RequestAborted = cts.Token;
        ParameterBinder.Cancellation(ctx).Should().Be(cts.Token);
    }

    [Fact]
    public void GetRequiredService_Resolves()
    {
        var services = new ServiceCollection();
        services.AddSingleton("hello");
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ParameterBinder.GetRequiredService<string>(ctx).Should().Be("hello");
    }

    [Fact]
    public async Task TryBindFormStringAsync_Reads_Form_Field()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("title=Report"));
        var value = await ParameterBinder.TryBindFormStringAsync(ctx, "title");
        value.Should().Be("Report");
    }

    [Fact]
    public async Task TryBindFormFileAsync_NoForm_Returns_Null()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/json";
        var file = await ParameterBinder.TryBindFormFileAsync(ctx, "file");
        file.Should().BeNull();
    }

    [Theory]
    [InlineData("3.14", 3.14)]
    public void TryBindQueryDouble_Parses(string raw, double expected)
    {
        var ctx = Ctx(query: ("n", raw));
        ParameterBinder.TryBindQueryDouble(ctx, "n", out var v).Should().BeTrue();
        v.Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void TryBindRouteInt64_Parses()
    {
        var ctx = Ctx(route: ("id", "9223372036854775807"));
        ParameterBinder.TryBindRouteInt64(ctx, "id", out var v).Should().BeTrue();
        v.Should().Be(long.MaxValue);
    }

    private static DefaultHttpContext Ctx((string, string)? query = null, (string, string)? route = null)
    {
        var ctx = new DefaultHttpContext();
        if (query is { } q)
            ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { [q.Item1] = q.Item2 });
        if (route is { } r)
            ctx.Request.RouteValues = new RouteValueDictionary { [r.Item1] = r.Item2 };
        return ctx;
    }
}
