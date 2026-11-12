using System.Globalization;

using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace HttpEndpointGenerator.Tests.Aot;

public class ParameterBinderTests
{
    private static DefaultHttpContext CreateContext(
        Action<DefaultHttpContext>? configure = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = new ServiceCollection()
            .AddSingleton<IProbeService, ProbeService>()
            .BuildServiceProvider();
        configure?.Invoke(ctx);
        return ctx;
    }

    [Fact]
    public void Cancellation_Returns_RequestAborted()
    {
        var ctx = CreateContext();
        using var cts = new CancellationTokenSource();
        ctx.RequestAborted = cts.Token;
        ParameterBinder.Cancellation(ctx).Should().Be(cts.Token);
    }

    [Fact]
    public void HttpContext_Returns_SameInstance()
    {
        var ctx = CreateContext();
        ParameterBinder.HttpContext(ctx).Should().BeSameAs(ctx);
    }

    [Fact]
    public void GetRequiredService_Resolves_From_RequestServices()
    {
        var ctx = CreateContext();
        var svc = ParameterBinder.GetRequiredService<IProbeService>(ctx);
        svc.Should().NotBeNull();
        svc.Name.Should().Be("probe");
    }

    [Fact]
    public void TryGetRouteValue_String_Stored_No_Extra_Alloc_Path()
    {
        var ctx = CreateContext(c => c.Request.RouteValues["id"] = "42");
        ParameterBinder.TryGetRouteValue(ctx, "id", out var value).Should().BeTrue();
        value.Should().Be("42");
    }

    [Fact]
    public void TryGetRouteValue_NonString_Converts()
    {
        var ctx = CreateContext(c => c.Request.RouteValues["id"] = 99);
        ParameterBinder.TryGetRouteValue(ctx, "id", out var value).Should().BeTrue();
        value.Should().Be("99");
    }

    [Fact]
    public void TryGetRouteValue_Missing_Returns_False()
    {
        var ctx = CreateContext();
        ParameterBinder.TryGetRouteValue(ctx, "missing", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Theory]
    [InlineData("42", 42, true)]
    [InlineData("not-int", 0, false)]
    [InlineData(null, 0, false)]
    public void TryBindRouteInt32(string? raw, int expected, bool ok)
    {
        var ctx = CreateContext();
        if (raw is not null)
            ctx.Request.RouteValues["n"] = raw;

        var success = ParameterBinder.TryBindRouteInt32(ctx, "n", out var value);
        success.Should().Be(ok);
        if (ok) value.Should().Be(expected);
    }

    [Theory]
    [InlineData("9223372036854775807", 9223372036854775807L, true)]
    [InlineData("bad", 0L, false)]
    public void TryBindRouteInt64(string raw, long expected, bool ok)
    {
        var ctx = CreateContext(c => c.Request.RouteValues["n"] = raw);
        var success = ParameterBinder.TryBindRouteInt64(ctx, "n", out var value);
        success.Should().Be(ok);
        if (ok) value.Should().Be(expected);
    }

    [Theory]
    [InlineData("3.14", 3.14, true)]
    [InlineData("not-double", 0d, false)]
    public void TryBindRouteDouble(string raw, double expected, bool ok)
    {
        var ctx = CreateContext(c => c.Request.RouteValues["n"] = raw);
        var success = ParameterBinder.TryBindRouteDouble(ctx, "n", out var value);
        success.Should().Be(ok);
        if (ok) value.Should().BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData("true", true, true)]
    [InlineData("false", false, true)]
    [InlineData("nope", false, false)]
    public void TryBindRouteBool(string raw, bool expected, bool ok)
    {
        var ctx = CreateContext(c => c.Request.RouteValues["f"] = raw);
        var success = ParameterBinder.TryBindRouteBool(ctx, "f", out var value);
        success.Should().Be(ok);
        if (ok) value.Should().Be(expected);
    }

    [Fact]
    public void TryBindRouteGuid_Valid()
    {
        var g = Guid.NewGuid();
        var ctx = CreateContext(c => c.Request.RouteValues["g"] = g.ToString());
        ParameterBinder.TryBindRouteGuid(ctx, "g", out var value).Should().BeTrue();
        value.Should().Be(g);
    }

    [Fact]
    public void TryBindRouteGuid_Invalid()
    {
        var ctx = CreateContext(c => c.Request.RouteValues["g"] = "not-a-guid");
        ParameterBinder.TryBindRouteGuid(ctx, "g", out _).Should().BeFalse();
    }

    [Fact]
    public void TryBindRouteString()
    {
        var ctx = CreateContext(c => c.Request.RouteValues["s"] = "hello");
        ParameterBinder.TryBindRouteString(ctx, "s", out var value).Should().BeTrue();
        value.Should().Be("hello");
    }

    [Theory]
    [InlineData("q", "hello", true)]
    [InlineData("missing", null, false)]
    public void TryBindQueryString(string name, string? expected, bool ok)
    {
        var ctx = CreateContext();
        if (ok)
            ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["q"] = "hello" });

        ParameterBinder.TryBindQueryString(ctx, name, out var value).Should().Be(ok);
        value.Should().Be(expected);
    }

    [Fact]
    public void TryBindQueryInt32_Parses()
    {
        var ctx = CreateContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["page"] = "7" });
        ParameterBinder.TryBindQueryInt32(ctx, "page", out var value).Should().BeTrue();
        value.Should().Be(7);
    }

    [Fact]
    public void TryBindQueryInt64_Parses()
    {
        var ctx = CreateContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["n"] = "100" });
        ParameterBinder.TryBindQueryInt64(ctx, "n", out var value).Should().BeTrue();
        value.Should().Be(100L);
    }

    [Fact]
    public void TryBindQueryBool_Parses()
    {
        var ctx = CreateContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["f"] = "true" });
        ParameterBinder.TryBindQueryBool(ctx, "f", out var value).Should().BeTrue();
        value.Should().BeTrue();
    }

    [Fact]
    public void TryBindQueryGuid_Parses()
    {
        var g = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ctx = CreateContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["g"] = g.ToString() });
        ParameterBinder.TryBindQueryGuid(ctx, "g", out var value).Should().BeTrue();
        value.Should().Be(g);
    }

    [Fact]
    public void TryBindQueryDouble_Parses()
    {
        var ctx = CreateContext();
        ctx.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "2.5" });
        ParameterBinder.TryBindQueryDouble(ctx, "x", out var value).Should().BeTrue();
        value.Should().BeApproximately(2.5, 0.0001);
    }

    [Fact]
    public void TryBindHeaderString()
    {
        var ctx = CreateContext();
        ctx.Request.Headers["X-Tenant"] = "acme";
        ParameterBinder.TryBindHeaderString(ctx, "X-Tenant", out var value).Should().BeTrue();
        value.Should().Be("acme");
    }

    [Fact]
    public void TryBindHeaderString_Missing()
    {
        var ctx = CreateContext();
        ParameterBinder.TryBindHeaderString(ctx, "X-Missing", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryBindSimple_Prefers_Route_When_Asked()
    {
        var ctx = CreateContext(c =>
        {
            c.Request.RouteValues["id"] = "from-route";
            c.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["id"] = "from-query" });
        });
        ParameterBinder.TryBindSimple(ctx, "id", preferRoute: true, out var value).Should().BeTrue();
        value.Should().Be("from-route");
    }

    [Fact]
    public void TryBindSimple_Falls_Back_To_Query()
    {
        var ctx = CreateContext(c =>
        {
            c.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { ["id"] = "from-query" });
        });
        ParameterBinder.TryBindSimple(ctx, "id", preferRoute: true, out var value).Should().BeTrue();
        value.Should().Be("from-query");
    }

    private interface IProbeService
    {
        string Name { get; }
    }

    private sealed class ProbeService : IProbeService
    {
        public string Name => "probe";
    }
}
