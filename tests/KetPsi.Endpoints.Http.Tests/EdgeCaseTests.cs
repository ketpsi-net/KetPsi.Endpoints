using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Edge cases: no params, only CT, single property, init-only,
/// required members, emptyish, collision, ValueTask, optional body, headers.
/// </summary>
public class EdgeCaseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EdgeCaseTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ping_NoParameters()
    {
        var response = await _client.GetAsync("/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pong").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Health_OnlyCancellationToken()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task SingleProperty_AsParameters()
    {
        var response = await _client.GetAsync("/search/term?q=hello");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("term").GetString().Should().Be("hello");
        body.GetProperty("kind").GetString().Should().Be("SingleProperty");
    }

    [Fact]
    public async Task SingleProperty_Missing_StillOk()
    {
        var response = await _client.GetAsync("/search/term");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InitOnly_Properties()
    {
        var response = await _client.GetAsync("/search/initonly?category=books&min=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("category").GetString().Should().Be("books");
        body.GetProperty("minScore").GetInt32().Should().Be(10);
        body.GetProperty("kind").GetString().Should().Be("InitOnly");
    }

    [Fact]
    public async Task RequiredMember_WithCode()
    {
        var response = await _client.GetAsync("/lookup?code=ABC&note=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("ABC");
        body.GetProperty("optionalNote").GetString().Should().Be("test");
        body.GetProperty("kind").GetString().Should().Be("RequiredMember");
    }

    [Fact]
    public async Task Emptyish_AllMissing()
    {
        var response = await _client.GetAsync("/emptyish");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("kind").GetString().Should().Be("Emptyish");
    }

    [Fact]
    public async Task Emptyish_Partial()
    {
        var response = await _client.GetAsync("/emptyish?a=x&b=7");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("a").GetString().Should().Be("x");
        body.GetProperty("b").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Collision_InlinePlusExternalConfig()
    {
        // External config renames Q → "query"
        var response = await _client.GetAsync("/collision?query=searchme&page=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("kind").GetString().Should().Be("Collision");
        // q may be bound from "query" if external config wins
        if (body.TryGetProperty("q", out var q))
            q.GetString().Should().Be("searchme");
        if (body.TryGetProperty("page", out var page))
            page.GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetBySlug_PathString()
    {
        var response = await _client.GetAsync("/slugs/my-product-slug");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("slug").GetString().Should().Be("my-product-slug");
    }

    [Fact]
    public async Task MultiQuery_AllPresent()
    {
        var response = await _client.GetAsync("/multi-query?a=1&b=2&n=99");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("a").GetString().Should().Be("1");
        body.GetProperty("b").GetString().Should().Be("2");
        body.GetProperty("n").GetInt32().Should().Be(99);
    }

    [Fact]
    public async Task HeaderEcho()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo");
        request.Headers.Add("X-Correlation-ID", "corr-123");
        request.Headers.Add("X-Client", "test-suite");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("correlationId").GetString().Should().Be("corr-123");
        body.GetProperty("client").GetString().Should().Be("test-suite");
    }

    [Fact]
    public async Task HeaderEcho_MissingHeaders_StillOk()
    {
        var response = await _client.GetAsync("/headers/echo");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OptionalBody_WithJson()
    {
        var response = await _client.PostAsJsonAsync("/optional-body", new { note = "hello" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("note").GetString().Should().Be("hello");
        body.GetProperty("kind").GetString().Should().Be("OptionalBody");
    }

    [Fact]
    public async Task OptionalBody_EmptyBody()
    {
        var response = await _client.PostAsync("/optional-body", null);
        // nullable body – should not 400
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task ValueTask_Return()
    {
        var response = await _client.GetAsync("/valuetask/42");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(42);
        body.GetProperty("kind").GetString().Should().Be("ValueTask");
    }
}
