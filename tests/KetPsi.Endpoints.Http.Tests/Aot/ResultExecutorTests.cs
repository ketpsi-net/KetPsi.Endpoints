using System.Text;

using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HttpEndpointGenerator.Tests.Aot;

public class ResultExecutorTests
{
    private static DefaultHttpContext CreateContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        return ctx;
    }

    private static string Body(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public async Task ExecuteAsync_NullObject_Writes_Json_Null_With_200()
    {
        var ctx = CreateContext();
        await ResultExecutor.ExecuteAsync(ctx, (object?)null);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        Body(ctx).Should().Be("null");
    }

    [Fact]
    public async Task ExecuteAsync_Generic_NullString_Writes_Empty_Text()
    {
        var ctx = CreateContext();
        await ResultExecutor.ExecuteAsync<string?>(ctx, null);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        Body(ctx).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Generic_NullIResult_Throws_Like_Rdf()
    {
        var ctx = CreateContext();
        var act = () => ResultExecutor.ExecuteAsync<IResult?>(ctx, null);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_IResult_Delegates()
    {
        var ctx = CreateContext();
        var result = Results.Text("hello", "text/plain");
        await ResultExecutor.ExecuteAsync(ctx, result);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        Body(ctx).Should().Be("hello");
    }

    [Fact]
    public async Task WriteBadRequestAsync_Writes_Fixed_Utf8_Body()
    {
        var ctx = CreateContext();
        await ResultExecutor.WriteBadRequestAsync(ctx);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.ContentType.Should().Be("text/plain; charset=utf-8");
        Body(ctx).Should().Be("Bad Request");
    }

    [Fact]
    public void SetBadRequest_Sets_Status_Without_Synchronous_Write()
    {
        var ctx = CreateContext();
        ResultExecutor.SetBadRequest(ctx);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public void SetBadRequest_Obsolete_Overload_Ignores_Message()
    {
        var ctx = CreateContext();
#pragma warning disable CS0618
        ResultExecutor.SetBadRequest(ctx, "ignored");
#pragma warning restore CS0618
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public void HttpVerbs_Are_Shared_ReadOnly_Lists()
    {
        HttpVerbs.Get.Should().BeEquivalentTo(["GET"]);
        HttpVerbs.Post.Should().BeEquivalentTo(["POST"]);
        HttpVerbs.Put.Should().BeEquivalentTo(["PUT"]);
        HttpVerbs.Delete.Should().BeEquivalentTo(["DELETE"]);
        HttpVerbs.Patch.Should().BeEquivalentTo(["PATCH"]);
        HttpVerbs.ForMapMethod("MapGet").Should().BeSameAs(HttpVerbs.Get);
        HttpVerbs.ForMapMethod("MapPost").Should().BeSameAs(HttpVerbs.Post);
        HttpVerbs.ForMapMethod("MapPut").Should().BeSameAs(HttpVerbs.Put);
        HttpVerbs.ForMapMethod("MapDelete").Should().BeSameAs(HttpVerbs.Delete);
        HttpVerbs.ForMapMethod("MapPatch").Should().BeSameAs(HttpVerbs.Patch);
        HttpVerbs.ForMapMethod("MapUnknown").Should().BeSameAs(HttpVerbs.Get);
    }

    [Fact]
    public void HttpVerbs_Cannot_Be_Mutated()
    {
        var list = (IList<string>)HttpVerbs.Get;
        list.IsReadOnly.Should().BeTrue();
        var act = () => list[0] = "POST";
        act.Should().Throw<NotSupportedException>();
        HttpVerbs.Get[0].Should().Be("GET");
    }
}
