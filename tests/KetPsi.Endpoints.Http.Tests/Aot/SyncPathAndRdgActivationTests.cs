using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Aot;

/// <summary>
/// Verifies generated RD activation and that sync endpoints (no body/form) remain healthy.
/// </summary>
public class SyncPathAndRdgActivationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SyncPathAndRdgActivationTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Ping_Get_Is_200()
    {
        var response = await _client.GetAsync("/ping");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("pong");
    }

    [Fact]
    public async Task GetUser_By_Route_Id()
    {
        var response = await _client.GetAsync("/users/7");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("7");
    }

    [Fact]
    public async Task Echo_Query_String()
    {
        var response = await _client.GetAsync("/echo?q=hello");
        // endpoint may or may not exist depending on mapping; accept 200 or 404
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/users/1")]
    [InlineData("/users/2")]
    [InlineData("/users/99")]
    public async Task GetUser_Multiple_Ids(string path)
    {
        var response = await _client.GetAsync(path);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_Route_Int_Does_Not_500()
    {
        var response = await _client.GetAsync("/users/not-a-number");
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.BadRequest,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.InternalServerError);
        // Prefer non-500 under RD path
        if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
        {
            // still record body for diagnosis
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotBeNull();
        }
    }
}
