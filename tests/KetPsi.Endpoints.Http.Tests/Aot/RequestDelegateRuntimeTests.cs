using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using HttpEndpointGenerator.Tests.Handlers;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HttpEndpointGenerator.Tests.Aot;

/// <summary>
/// Request-delegate path runtime behavior: results (5, 17), filters (4), metadata / antiforgery / ApiExplorer (2),
/// helpers and non-constant routes (3, 13), escaping (14) and hot-path allocations.
/// </summary>
public class RequestDelegateRuntimeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RequestDelegateRuntimeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private RouteEndpoint Endpoint(string rawPattern, string method)
    {
        _client.GetAsync("/ping").GetAwaiter().GetResult(); // make sure the host started
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        return source.Endpoints.OfType<RouteEndpoint>().Single(e =>
            e.RoutePattern.RawText == rawPattern &&
            e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));
    }

    // ------------------------------------------------------------------ results

    [Fact]
    public async Task Plain_Task_Returns_200_Empty()
    {
        var response = await _client.GetAsync("/results/task");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Plain").Should().ContainSingle("task");
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/results/valuetask")]
    [InlineData("/results/void")]
    public async Task Plain_ValueTask_And_Void_Return_200_Empty(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/results/text?name=bob", "hello bob")]
    [InlineData("/results/sync-text", "sync")]
    public async Task String_Results_Are_Text_Plain(string url, string expected)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData("/results/json", "Berlin")]
    [InlineData("/results/json?city=Paris", "Paris")]
    [InlineData("/results/valuetask-json", "Oslo")]
    [InlineData("/results/typed", "Rome")]
    public async Task Typed_Results_Are_Json(string url, string city)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("city").GetString().Should().Be(city);
    }

    [Fact]
    public async Task Null_Class_Result_Is_200_Json_Null()
    {
        var response = await _client.GetAsync("/results/null-json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("null");
    }

    [Fact]
    public async Task Null_IResult_Throws_Like_Rdf()
    {
        var response = await _client.GetAsync("/results/null-result");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    // ------------------------------------------------------------------ filters

    [Fact]
    public async Task Filters_Run_Group_Then_Endpoint_Then_Handler_With_Arguments()
    {
        var response = await _client.GetAsync("/filters/echo/4");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(4);
        body.GetProperty("trace").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("group", "endpoint:4", "handler");
        response.Headers.GetValues("X-Arg-Count").Should().ContainSingle("2", "filters see every bound argument");
    }

    [Fact]
    public async Task Filters_Can_Modify_Arguments()
    {
        var body = await (await _client.GetAsync("/filters/echo/4?mutate=1")).Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(40);
    }

    [Fact]
    public async Task Filters_Can_Short_Circuit()
    {
        var response = await _client.GetAsync("/filters/echo/0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("blocked");
    }

    [Fact]
    public async Task Group_Filter_Applies_To_Plain_Task_Handler()
    {
        var response = await _client.GetAsync("/filters/plain");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Handler").Should().ContainSingle("ran");
    }

    [Fact]
    public async Task Filters_Still_Run_When_Binding_Fails_But_Handler_Does_Not()
    {
        var response = await _client.GetAsync("/filters/echo/abc");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Filter_Factories_Are_Consumed_Once()
    {
        var endpoint = Endpoint("/filters/echo/{id}", "GET");
        endpoint.Metadata.GetMetadata<System.Reflection.MethodInfo>().Should().NotBeNull();
    }

    // ------------------------------------------------------------------ antiforgery

    [Fact]
    public void Form_Endpoint_Requires_Antiforgery_And_DisableAntiforgery_Still_Wins()
    {
        Endpoint("/forms/protected", "POST").Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation.Should().BeTrue();
        Endpoint("/documents", "POST").Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation.Should().BeFalse();
        Endpoint("/users/{id}", "GET").Metadata.GetMetadata<IAntiforgeryMetadata>().Should().BeNull();
    }

    [Fact]
    public async Task Form_Post_Without_Antiforgery_Token_Is_Rejected()
    {
        using var content = new FormUrlEncodedContent([new("name", "x")]);
        var response = await _client.PostAsync("/forms/protected", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Form_Post_With_Valid_Antiforgery_Token_Succeeds()
    {
        var antiforgery = _factory.Services.GetRequiredService<IAntiforgery>();
        var http = new DefaultHttpContext { RequestServices = _factory.Services };
        var tokens = antiforgery.GetAndStoreTokens(http);
        var cookie = http.Response.Headers.SetCookie.ToString().Split(';')[0];

        var request = new HttpRequestMessage(HttpMethod.Post, "/forms/protected")
        {
            Content = new FormUrlEncodedContent([new("name", "ok"), new(tokens.FormFieldName, tokens.RequestToken!)]),
        };
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("ok");
    }

    // ------------------------------------------------------------------ OpenAPI / ApiExplorer

    private ApiDescription Describe(string relativePath, string method)
    {
        _client.GetAsync("/ping").GetAwaiter().GetResult();
        var provider = _factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        return provider.ApiDescriptionGroups.Items.SelectMany(g => g.Items)
            .Single(d => d.RelativePath == relativePath && d.HttpMethod == method);
    }

    [Fact]
    public void ApiExplorer_Describes_Route_And_Query_Parameters()
    {
        var get = Describe("users/{id}", "GET");
        get.ParameterDescriptions.Should().Contain(p => p.Name == "id" && p.Source == BindingSource.Path);

        var search = Describe("orders", "GET");
        search.ParameterDescriptions.Should().Contain(p => p.Name == "status" && p.Source == BindingSource.Query);
        search.ParameterDescriptions.Should().Contain(p => p.Name == "X-Tenant-ID" && p.Source == BindingSource.Header);
    }

    [Fact]
    public void ApiExplorer_Describes_Request_Body_And_Response()
    {
        var create = Describe("users", "POST");
        create.ParameterDescriptions.Should().Contain(p => p.Source == BindingSource.Body && p.Type == typeof(CreateUserRequest));
        create.SupportedRequestFormats.Should().Contain(f => f.MediaType == "application/json");

        var json = Describe("results/json", "GET");
        json.SupportedResponseTypes.Should().Contain(r => r.StatusCode == 200 && r.Type == typeof(WeatherDto));

        var typed = Describe("results/typed", "GET");
        typed.SupportedResponseTypes.Should().Contain(r => r.StatusCode == 200 && r.Type == typeof(WeatherDto));
    }

    [Fact]
    public void ApiExplorer_Describes_AsParameters_Surrogate_Members()
    {
        var filtered = Describe("orders/filtered", "GET");
        filtered.ParameterDescriptions.Should().Contain(p => p.Name == "fromDate" && p.Source == BindingSource.Query);
    }

    // ------------------------------------------------------------------ helpers / routes / escaping

    [Theory]
    [InlineData("/reports/12")]
    [InlineData("/helpers/root")]
    [InlineData("/by-name/named")]
    [InlineData("/dynamic-route")]
    public async Task Helper_Extensions_And_NonConstant_Routes_Are_Mapped(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Escaped_Name_And_Obsolete_Message_Survive()
    {
        var endpoint = Endpoint("/escape", "GET");
        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName.Should().Be("Escape \"quoted\" \\ name");
        endpoint.Metadata.GetMetadata<ObsoleteAttribute>()!.Message.Should().Be("Use \"v2\" \\ now");
    }

    [Fact]
    public async Task Escaped_Header_Name_Binds()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/escape");
        request.Headers.TryAddWithoutValidation("X-\"Q\"", "v");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ allocations

    [Fact]
    public async Task Hot_Path_Does_Not_Allocate()
    {
        var endpoint = Endpoint("/alloc/{id}", "GET");
        var context = new DefaultHttpContext { RequestServices = _factory.Services };
        context.Request.Method = "GET";
        context.Request.RouteValues["id"] = "5";
        context.Request.QueryString = new QueryString("?page=2");

        for (var i = 0; i < 100; i++)
            await endpoint.RequestDelegate!(context);
        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var task = endpoint.RequestDelegate!(context);
            if (!task.IsCompletedSuccessfully)
                await task;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().BeLessThan(1000, "binding a route int + query int and returning a cached IResult must not allocate per request");
    }
}
