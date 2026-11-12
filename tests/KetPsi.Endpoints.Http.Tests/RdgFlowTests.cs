using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Broad flow coverage for the KetPsi RD path (bind → handler → result).
/// </summary>
public class RdgFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RdgFlowTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(999)]
    public async Task Get_User_Ids(int id)
    {
        var response = await _client.GetAsync($"/users/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_Create_User_With_Body()
    {
        var response = await _client.PostAsJsonAsync("/users", new { Name = "Sam", Email = "sam@ex.com" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_User_By_Id()
    {
        var response = await _client.DeleteAsync("/users/5");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Orders_Filtered_Status_Only()
    {
        var response = await _client.GetAsync("/orders/filtered?status=open");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("open");
    }

    [Fact]
    public async Task Orders_Filtered_With_Paging()
    {
        var response = await _client.GetAsync("/orders/filtered?status=x&page=2&size=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("size").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Search_Products_Query_Scalars()
    {
        var response = await _client.GetAsync("/products/search?category=books&min=1&max=50&inStock=true");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_Delete_Nested_Group()
    {
        var response = await _client.DeleteAsync("/api/v1/admin/users/1");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/ping")]
    [InlineData("/users/1")]
    public async Task Repeated_Gets_Are_Stable(string path)
    {
        for (var i = 0; i < 5; i++)
        {
            var response = await _client.GetAsync(path);
            response.IsSuccessStatusCode.Should().BeTrue($"iteration {i} path {path}");
        }
    }

    [Fact]
    public async Task Optional_Body_Empty_Is_Not_500()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/optional-body");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Bulk_Delete_Without_Body_Is_Not_500()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/users"));
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
