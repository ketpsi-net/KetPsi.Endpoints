using System.Text;

using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HttpEndpointGenerator.Tests.Aot;

public class ResultExecutorExtendedTests
{
    [Fact]
    public async Task ExecuteAsync_IResult_Ok()
    {
        var http = NewContext();
        await ResultExecutor.ExecuteAsync(http, Results.Ok(new { A = 1 }));
        http.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ExecuteAsync_Null_Is_200_Json_Null()
    {
        var http = NewContext();
        await ResultExecutor.ExecuteAsync(http, (object?)null);
        http.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ExecuteHandlerTaskAsync_TaskOfIResult_Unwraps()
    {
        var http = NewContext();
        // Must use ExecuteHandlerTaskAsync — ExecuteAsync(object) would JSON-serialize the Task.
        await ResultExecutor.ExecuteHandlerTaskAsync(http, Task.FromResult(Results.Text("x")));
        http.Response.StatusCode.Should().Be(200);
        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body).ReadToEndAsync();
        text.Should().Be("x");
    }

    [Fact]
    public async Task WriteBadRequestAsync_Sets_400_And_Body()
    {
        var http = NewContext();
        await ResultExecutor.WriteBadRequestAsync(http);
        http.Response.StatusCode.Should().Be(400);
        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
        text.Should().Be("Bad Request");
    }

    [Fact]
    public void SetBadRequest_Only_Status()
    {
        var http = NewContext();
        ResultExecutor.SetBadRequest(http);
        http.Response.StatusCode.Should().Be(400);
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // IResult.ExecuteAsync requires ILoggerFactory
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        return http;
    }
}
