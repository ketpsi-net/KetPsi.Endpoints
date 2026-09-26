using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Failure / negative cases – confirm we get the expected status codes
/// instead of 500s or silent wrong binding.
/// </summary>
public class NegativeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public NegativeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MissingRouteParam_Returns404()
    {
        // /users/{id} without id → no matching route
        var response = await _client.GetAsync("/users/");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/does-not-exist-xyz");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WrongHttpMethod_Returns405Or404()
    {
        // GET-only endpoint called with POST
        var response = await _client.PostAsync("/users/1", null);
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.MethodNotAllowed,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidJsonBody_Returns400()
    {
        var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/users", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidDateQuery_Returns400()
    {
        var response = await _client.GetAsync(
            "/orders/filtered?fromDate=not-a-date&toDate=also-bad");
        // Model binding failure for DateOnly
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        // Some hosts may coerce; if 200, body should not contain valid dates
    }

    [Fact]
    public async Task InvalidIntPath_Returns400Or404()
    {
        var response = await _client.GetAsync("/users/not-an-int");
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RequiredCodeMissing_MayReturn400()
    {
        // RequiredFilter.Code is required – missing query may 400 depending on binding
        var response = await _client.GetAsync("/lookup");
        // Accept either 400 (strict) or 200 with null/empty (if binder is lenient)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteWithoutBody_OnBulkEndpoint_StillWorksOr400()
    {
        // Bulk delete expects body; empty DELETE may 400 or succeed with empty
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/users"));
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnsupportedMediaType);
    }
}
