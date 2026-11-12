using System.Text;
using System.Text.Json.Serialization;
using KetPsi.Endpoints.Abstractions;
using KetPsi.Endpoints.Http;
using KetPsi.Endpoints.Http.Benchmarks;
using KetPsi.Endpoints.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KetPsi.Endpoints.Http.AotSmoke;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var smoke = args.Contains("--smoke");

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        if (smoke)
            builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, SmokeJsonContext.Default);
            o.SerializerOptions.TypeInfoResolverChain.Insert(1, BenchJsonContext.Default);
            o.SerializerOptions.TypeInfoResolverChain.Add(ApiResultJsonContext.Default);
        });
        builder.Services.AddHttpEndpoints();

        var app = builder.Build();
        app.MapGet<CachedResultHandler>("/items/{id:int}");
        app.MapGet<JsonResultsOkHandler>("/users/{id:int}");
        app.MapGet<JsonTypedOkHandler>("/typed/users/{id:int}");
        app.MapPost<PostJsonBodyHandler>("/users");
        app.MapGet<HeaderHandler>("/tenant");
        app.MapGet<AsParametersHandler>("/search");
        app.MapGet<TaskOfDtoHandler>("/async/users/{id:int}");
        app.MapGet<ApiResultHandler>("/api-result/{id:int}");
        app.MapGet<ApiResultEmptyHandler>("/api-result/empty");
        app.MapHttpEndpoints();

        if (!smoke)
        {
            await app.RunAsync();
            return 0;
        }

        await app.StartAsync();
        try
        {
            return await RunSmokeAsync(new Uri(app.Urls.First()));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<int> RunSmokeAsync(Uri baseUri)
    {
        using var http = new HttpClient { BaseAddress = baseUri };
        var failures = 0;

        async Task Check(HttpRequestMessage request, int status, string? body)
        {
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var ok = (int)response.StatusCode == status && (body is null || text == body);
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {request.Method} {request.RequestUri} -> {(int)response.StatusCode} {text}");
            if (!ok)
                failures++;
        }

        await Check(new(HttpMethod.Get, "/items/42?a=1&b=2"), 200, "");
        await Check(new(HttpMethod.Get, "/users/42"), 200, """{"id":42,"name":"Ada"}""");
        await Check(new(HttpMethod.Get, "/typed/users/42"), 200, """{"id":42,"name":"Ada"}""");
        await Check(new(HttpMethod.Post, "/users")
        {
            Content = new StringContent("""{"name":"Ada","age":36}""", Encoding.UTF8, "application/json"),
        }, 200, """{"id":36,"name":"Ada"}""");
        var tenant = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        tenant.Headers.Add("X-Tenant", "acme");
        await Check(tenant, 200, """{"tenant":"acme"}""");
        await Check(new(HttpMethod.Get, "/search?page=2&pageSize=25&term=abc"), 200, """{"page":2,"pageSize":25,"term":"abc","total":42}""");
        await Check(new(HttpMethod.Get, "/async/users/42"), 200, """{"id":42,"name":"Ada"}""");
        await Check(new(HttpMethod.Get, "/api-result/7"), 200, """{"statusCode":200,"value":{"id":7,"name":"Ada"},"errors":null}""");
        await Check(new(HttpMethod.Get, "/api-result/empty"), 200, """{"statusCode":204,"value":null,"errors":null}""");

        Console.WriteLine(failures == 0 ? "AOT smoke: all checks passed." : $"AOT smoke: {failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}

public sealed class ApiResultHandler : IEndpoint
{
    public Task<ApiResult<UserDto>> ExecuteAsync(int id, CancellationToken ct)
        => Task.FromResult(ApiResult.Ok(new UserDto(id, Shared.UserName)));
}

public sealed class ApiResultEmptyHandler : IEndpoint
{
    public Task<ApiResult<object?>> ExecuteAsync(CancellationToken ct)
        => Task.FromResult(ApiResult.NoContent());
}

[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiResult<UserDto>))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;
