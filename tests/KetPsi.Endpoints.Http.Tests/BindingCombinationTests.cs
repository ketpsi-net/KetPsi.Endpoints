using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Additional binding combinations: content-types, empty query strings,
/// repeated calls, large query values, special characters in path/query.
/// </summary>
public class BindingCombinationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BindingCombinationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Query_SpecialCharacters()
    {
        var term = Uri.EscapeDataString("hello world & co");
        var response = await _client.GetAsync($"/search/term?q={term}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("term").GetString().Should().Be("hello world & co");
    }

    [Fact]
    public async Task Path_SlugWithDashesAndUnderscores()
    {
        var response = await _client.GetAsync("/slugs/my_product-slug.v2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("slug").GetString().Should().Be("my_product-slug.v2");
    }

    [Fact]
    public async Task EmptyQueryString_OnOptionalParams()
    {
        var response = await _client.GetAsync("/orders?");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExtraUnknownQueryParams_Ignored()
    {
        var response = await _client.GetAsync("/users/1?foo=bar&baz=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Post_Json_WithCharset()
    {
        var json = """{"email":"a@b.com","firstName":"A","lastName":"B"}""";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/users", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Put_Then_Get_SameId()
    {
        var payload = new { firstName = "X", lastName = "Y" };
        var put = await _client.PutAsJsonAsync("/users/77", payload);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await _client.GetAsync("/users/77");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FormUpload_MissingFile_StillBindsTitle()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("No File Title"), "title");

        var response = await _client.PostAsync("/documents", content);
        // file is optional (IFormFile?) – should not 500
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("title").GetString().Should().Be("No File Title");
        }
    }

    [Fact]
    public async Task Inventory_ExplicitFalse()
    {
        var response = await _client.GetAsync("/inventory?reserved=false");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var prop = body.GetProperty("includeReserved");
        if (prop.ValueKind != JsonValueKind.Null)
            prop.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Inventory_ExplicitTrue()
    {
        var response = await _client.GetAsync("/inventory?reserved=true&sku=X&wh=1&minQty=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("includeReserved").GetBoolean().Should().BeTrue();
        body.GetProperty("sku").GetString().Should().Be("X");
        body.GetProperty("warehouseId").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task OrdersFiltered_OnlyStatus()
    {
        var response = await _client.GetAsync("/orders/filtered?status=open&page=1&size=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("open");
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("size").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task CustomersSearch_AllFlattenedFields()
    {
        var response = await _client.GetAsync(
            "/customers/search?name=Ann&city=Munich&country=DE&zip=80331&active=false");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Ann");
        body.GetProperty("city").GetString().Should().Be("Munich");
        body.GetProperty("country").GetString().Should().Be("DE");
        body.GetProperty("zip").GetString().Should().Be("80331");
        body.GetProperty("active").GetBoolean().Should().BeFalse();
    }
}
