using System.Text;
using System.Text.Json.Serialization.Metadata;

using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HttpEndpointGenerator.Tests.Aot;

/// <summary>Unit tests for the request-delegate runtime helpers (binding failures, parsing, body, results, environment).</summary>
public class RuntimeHelperTests
{
    private sealed record Dto(string Name);

    private abstract record Animal(string Name);

    private sealed record Dog(string Name, bool Barks) : Animal(Name);

    private static (EndpointEnvironment Env, IServiceProvider Services) CreateEnvironment(bool throwOnBadRequest = false, string pattern = "/items/{id}")
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = throwOnBadRequest)
            .Configure<JsonOptions>(_ => { })
            .AddSingleton<Dto>(new Dto("svc"))
            .BuildServiceProvider();
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0) { ApplicationServices = services };
        return (EndpointEnvironment.Create(builder), services);
    }

    private static DefaultHttpContext Context(IServiceProvider services, string? body = null, string contentType = "application/json")
    {
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Response.Body = new MemoryStream();
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.ContentType = contentType;
        }
        return ctx;
    }

    private static string ResponseText(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return new StreamReader(ctx.Response.Body).ReadToEnd();
    }

    // ------------------------------------------------------------------ environment

    [Fact]
    public void Environment_Resolves_Pattern_Options_And_Services_Once()
    {
        var (env, _) = CreateEnvironment(throwOnBadRequest: true);
        env.ThrowOnBadRequest.Should().BeTrue();
        env.HasRouteParameter("id").Should().BeTrue();
        env.HasRouteParameter("ID").Should().BeTrue();
        env.HasRouteParameter("page").Should().BeFalse();
        env.IsService(typeof(Dto)).Should().BeTrue();
        env.IsService(typeof(Animal)).Should().BeFalse();
        env.JsonOptions.Should().BeSameAs(env.JsonOptions);
        env.GetJsonTypeInfo<Dto>().Type.Should().Be(typeof(Dto));
        var act = () => env.EnsureRouteParameter("missing", "p");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Environment_Does_Not_Lock_Or_Replace_Host_Options()
    {
        var (env, services) = CreateEnvironment();
        var hostOptions = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        env.JsonOptions.Should().BeSameAs(hostOptions);
        JsonBodyReader.ResolveOptions(new DefaultHttpContext { RequestServices = services }).Should().BeSameAs(hostOptions);
    }

    [Fact]
    public void Body_Inference_Rules_Match_Minimal_Apis()
    {
        var get = () => EndpointEnvironment.EnsureBodyInference(1, 0, bodyAllowed: false, "x");
        get.Should().Throw<InvalidOperationException>().WithMessage("*Body was inferred*");
        var two = () => EndpointEnvironment.EnsureBodyInference(1, 1, bodyAllowed: true, "x");
        two.Should().Throw<InvalidOperationException>();
        var ok = () => EndpointEnvironment.EnsureBodyInference(1, 0, bodyAllowed: true, "x");
        ok.Should().NotThrow();
    }

    // ------------------------------------------------------------------ failures

    [Fact]
    public void BindingFailure_Sets_400_Without_Writing_Or_Throws_In_Development()
    {
        var (env, services) = CreateEnvironment();
        var ctx = Context(services);
        BindingFailure.Invalid(ctx, env, "int", "id", "abc");
        ctx.Response.StatusCode.Should().Be(400);
        ctx.Response.Body.Length.Should().Be(0);

        var (devEnv, devServices) = CreateEnvironment(throwOnBadRequest: true);
        var act = () => BindingFailure.Required(Context(devServices), devEnv, "int", "id", "route");
        act.Should().Throw<BadHttpRequestException>().Where(e => e.StatusCode == 400)
            .WithMessage("Required parameter \"int id\" was not provided from route.");
    }

    // ------------------------------------------------------------------ parsing

    private enum Shade { Light, Dark }

    [Fact]
    public void Parsing_Helpers_Use_Invariant_Culture_And_Minimal_Api_Styles()
    {
        ParameterBinder.TryParse<decimal>("1,234.5", out var dec).Should().BeTrue();
        dec.Should().Be(1234.5m);
        ParameterBinder.TryParse<int>("1.5", out _).Should().BeFalse();
        ParameterBinder.TryParseEnum<Shade>("dArK", out var shade).Should().BeTrue();
        shade.Should().Be(Shade.Dark);
        ParameterBinder.TryParseEnum<Shade>("nope", out _).Should().BeFalse();
        ParameterBinder.TryParseUri("/relative", out var uri).Should().BeTrue();
        uri!.IsAbsoluteUri.Should().BeFalse();
        ParameterBinder.TryParseDateOnly(" 2025-01-02 ", out var date).Should().BeTrue();
        date.Should().Be(new DateOnly(2025, 1, 2));
        ParameterBinder.TryParseDateTime("2025-01-02T03:00:00+02:00", out var dt).Should().BeTrue();
        dt.Kind.Should().Be(DateTimeKind.Utc);
        dt.Hour.Should().Be(1);
        ParameterBinder.TryParseTimeOnly("10:11", out var time).Should().BeTrue();
        time.Should().Be(new TimeOnly(10, 11));
    }

    [Fact]
    public void Raw_Value_Accessors_Handle_Missing_Values()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.RouteValues["id"] = 5;
        ParameterBinder.GetRouteValue(ctx, "id").Should().Be("5");
        ParameterBinder.GetRouteValue(ctx, "missing").Should().BeNull();
        ParameterBinder.GetFormValue(null, "x").Should().BeNull();
        ParameterBinder.GetFormValues(null, "x").Count.Should().Be(0);
        ParameterBinder.GetFormFile(null, "x").Should().BeNull();
    }

    // ------------------------------------------------------------------ body

    [Fact]
    public async Task TryReadAsync_Reads_Json()
    {
        var (env, services) = CreateEnvironment();
        var ctx = Context(services, """{"name":"Ada"}""");
        var result = await JsonBodyReader.TryReadAsync(ctx, env.GetJsonTypeInfo<Dto>(), env, required: true, "Dto", "dto");
        result.Success.Should().BeTrue();
        result.Value!.Name.Should().Be("Ada");
    }

    [Theory]
    [InlineData("{ bad", "application/json", 400)]
    [InlineData("""{"name":"x"}""", "text/plain", 415)]
    [InlineData(null, "application/json", 400)]
    public async Task TryReadAsync_Failures(string? body, string contentType, int status)
    {
        var (env, services) = CreateEnvironment();
        var ctx = Context(services, body, contentType);
        var result = await JsonBodyReader.TryReadAsync(ctx, env.GetJsonTypeInfo<Dto>(), env, required: true, "Dto", "dto");
        result.Success.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task TryReadAsync_Optional_Without_Body_Succeeds_With_Null()
    {
        var (env, services) = CreateEnvironment();
        var result = await JsonBodyReader.TryReadAsync(Context(services), env.GetJsonTypeInfo<Dto>(), env, required: false, "Dto", "dto");
        result.Success.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    // ------------------------------------------------------------------ results

    [Fact]
    public async Task WriteJsonAsync_Uses_Runtime_Type_For_Non_Polymorphic_Base()
    {
        var (env, services) = CreateEnvironment();
        var ctx = Context(services);
        await ResultExecutor.WriteJsonAsync<Animal>(ctx, new Dog("Rex", true), env.GetJsonTypeInfo<Animal>());
        ResponseText(ctx).Should().Contain("\"barks\":true");
    }

    [Fact]
    public async Task Task_Helpers_Complete_Synchronously_For_Completed_Tasks()
    {
        var (_, services) = CreateEnvironment();
        var ctx = Context(services);
        var task = ResultExecutor.ExecuteResultTaskAsync<IResult>(ctx, Task.FromResult<IResult>(Results.StatusCode(202)));
        task.IsCompletedSuccessfully.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(202);
        await task;

        var pending = new TaskCompletionSource<IResult>();
        var awaited = ResultExecutor.ExecuteResultTaskAsync<IResult>(ctx, pending.Task);
        awaited.IsCompleted.Should().BeFalse();
        pending.SetResult(Results.StatusCode(203));
        await awaited;
        ctx.Response.StatusCode.Should().Be(203);
    }

    [Fact]
    public async Task Null_IResult_Throws()
    {
        var (_, services) = CreateEnvironment();
        var act = () => ResultExecutor.ExecuteResultTaskAsync<IResult?>(Context(services), Task.FromResult<IResult?>(null));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task String_Results_Are_Plain_Text()
    {
        var (_, services) = CreateEnvironment();
        var ctx = Context(services);
        await ResultExecutor.WriteStringTaskAsync(ctx, Task.FromResult<string?>("hi"));
        ctx.Response.ContentType.Should().Be("text/plain; charset=utf-8");
        ResponseText(ctx).Should().Be("hi");
    }

    [Fact]
    public async Task Filter_Adapters_Box_Results_And_Map_Void_To_Empty()
    {
        (await ResultExecutor.FromTaskOf(Task.FromResult(5))).Should().Be(5);
        (await ResultExecutor.FromValueTaskOf(new ValueTask<string>("x"))).Should().Be("x");
        (await ResultExecutor.FromTask(Task.CompletedTask)).Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.EmptyHttpResult>();
        (await ResultExecutor.FromValue<int?>(null)).Should().BeNull();
    }

    // ------------------------------------------------------------------ filters

    [Fact]
    public async Task BuildFilterPipeline_Composes_In_Registration_Order_And_Clears_Factories()
    {
        var (_, services) = CreateEnvironment();
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0) { ApplicationServices = services };
        var trace = new List<string>();
        builder.FilterFactories.Add((_, next) => async ctx => { trace.Add("outer"); return await next(ctx); });
        builder.FilterFactories.Add((_, next) => async ctx => { trace.Add("inner"); return await next(ctx); });

        var method = typeof(RuntimeHelperTests).GetMethod(nameof(BuildFilterPipeline_Composes_In_Registration_Order_And_Clears_Factories))!;
        var pipeline = KetPsiEndpoint.BuildFilterPipeline(builder, method, ctx => { trace.Add("handler:" + ctx.GetArgument<int>(0)); return new ValueTask<object?>("done"); });

        builder.FilterFactories.Should().BeEmpty();
        var result = await pipeline(EndpointFilterInvocationContext.Create(new DefaultHttpContext(), 7));
        result.Should().Be("done");
        trace.Should().Equal("outer", "inner", "handler:7");
    }
}
