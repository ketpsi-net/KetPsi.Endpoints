using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Metadata, routing, groups, obsolete, and concurrent access.
/// </summary>
public class MetadataAndRoutingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MetadataAndRoutingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ObsoleteEndpoint_StillReachable()
    {
        var response = await _client.GetAsync("/v1/users/1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetString().Should().Be("v1");
    }

    [Fact]
    public async Task GroupedAndNonGrouped_SameHandler_BothWork()
    {
        var r1 = await _client.GetAsync("/users/10");
        var r2 = await _client.GetAsync("/api/v1/users/10");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NestedAdminGroup_Delete()
    {
        var response = await _client.DeleteAsync("/api/v1/admin/users/5");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Concurrent_SameEndpoint_NoThrow()
    {
        var tasks = Enumerable.Range(1, 20)
            .Select(i => _client.GetAsync($"/users/{i}"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Concurrent_MixedEndpoints_NoThrow()
    {
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_client.GetAsync($"/users/{i}"));
            tasks.Add(_client.GetAsync("/orders?status=open"));
            tasks.Add(_client.GetAsync("/ping"));
            tasks.Add(_client.GetAsync("/inventory"));
        }

        var responses = await Task.WhenAll(tasks);
        responses.Should().OnlyContain(r =>
            r.StatusCode == HttpStatusCode.OK ||
            r.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MultipleClients_Independent()
    {
        var c1 = _factory.CreateClient();
        var c2 = _factory.CreateClient();

        var t1 = c1.GetAsync("/users/1");
        var t2 = c2.GetAsync("/users/2");

        var (r1, r2) = (await t1, await t2);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        var b1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var b2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        b1.GetProperty("id").GetInt32().Should().Be(1);
        b2.GetProperty("id").GetInt32().Should().Be(2);
    }
}
