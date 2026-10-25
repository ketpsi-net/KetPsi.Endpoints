using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Tests;

public class BindingMatrixTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public BindingMatrixTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(100)]
    public async Task Route_Int_Matrix(int id)
    {
        var r = await _client.GetAsync($"/users/{id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("pending")]
    [InlineData("shipped")]
    [InlineData("cancelled")]
    public async Task Query_Status_Matrix(string status)
    {
        var r = await _client.GetAsync($"/orders/filtered?status={status}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be(status);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 25)]
    [InlineData(3, 50)]
    [InlineData(5, 100)]
    public async Task Query_Paging_Matrix(int page, int size)
    {
        var r = await _client.GetAsync($"/orders/filtered?status=x&page={page}&size={size}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("page").GetInt32().Should().Be(page);
        body.GetProperty("size").GetInt32().Should().Be(size);
    }

    [Theory]
    [InlineData("books", "1", "10", "true")]
    [InlineData("toys", "0", "99", "false")]
    [InlineData("home", "5.5", "20.25", "true")]
    public async Task Product_Search_Matrix(string cat, string min, string max, string stock)
    {
        var r = await _client.GetAsync($"/products/search?category={cat}&min={min}&max={max}&inStock={stock}");
        r.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_Gets()
    {
        var tasks = Enumerable.Range(1, 20).Select(i => _client.GetAsync($"/users/{i}"));
        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Head_Or_Get_Ping()
    {
        var r = await _client.GetAsync("/ping");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
