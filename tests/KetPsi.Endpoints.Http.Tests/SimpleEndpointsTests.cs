using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Tests;

public class SimpleEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SimpleEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetUserById_ReturnsOk()
    {
        var response = await _client.GetAsync("/users/42");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(42);
        body.GetProperty("name").GetString().Should().Be("User-42");
    }

    [Fact]
    public async Task SearchOrders_WithQueryAndHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/orders?status=shipped&page=2&size=10");
        request.Headers.Add("X-Tenant-ID", "acme-corp");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("shipped");
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("size").GetInt32().Should().Be(10);
        body.GetProperty("tenantId").GetString().Should().Be("acme-corp");
    }

    [Fact]
    public async Task CreateUser_PostJsonBody()
    {
        var payload = new
        {
            email = "jane@company.com",
            firstName = "Jane",
            lastName = "Doe",
            roles = new[] { "USER", "EDITOR" }
        };

        var response = await _client.PostAsJsonAsync("/users", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateUser_PutWithHeader()
    {
        var payload = new { firstName = "Jane", lastName = "Doe", email = "jane@company.com" };
        var request = new HttpRequestMessage(HttpMethod.Put, "/users/99")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("If-Match", "\"etag-abc\"");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(99);
        body.GetProperty("eTag").GetString().Should().Be("\"etag-abc\"");
    }

    [Fact]
    public async Task DeleteUser_ReturnsNoContent()
    {
        var response = await _client.DeleteAsync("/users/55");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BulkDeleteUsers_WithBody()
    {
        var payload = new { ids = new[] { 1, 2, 3 }, reason = "GDPR" };
        var request = new HttpRequestMessage(HttpMethod.Delete, "/users")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("deleted").GetInt32().Should().Be(3);
        body.GetProperty("reason").GetString().Should().Be("GDPR");
    }

    [Fact]
    public async Task ObsoleteEndpoint_StillWorks()
    {
        var response = await _client.GetAsync("/v1/users/7");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetString().Should().Be("v1");
    }
}
