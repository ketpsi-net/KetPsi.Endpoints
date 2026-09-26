using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Tests;

public class GroupsAndFormTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public GroupsAndFormTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Grouped_GetUserById()
    {
        var response = await _client.GetAsync("/api/v1/users/100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Grouped_CreateUser()
    {
        var payload = new { email = "grouped@test.com", firstName = "Group", lastName = "User" };
        var response = await _client.PostAsJsonAsync("/api/v1/users", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task NestedGroup_AdminDelete()
    {
        var response = await _client.DeleteAsync("/api/v1/admin/users/200");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UploadDocument_MultipartForm()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Q3 Report"), "title");

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake pdf content"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "report.pdf");

        var response = await _client.PostAsync("/documents", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Q3 Report");
        body.GetProperty("fileName").GetString().Should().Be("report.pdf");
        body.GetProperty("service").GetString().Should().Be("DocumentService");
        body.GetProperty("storage").GetString().Should().Be("Primary");
    }
}
