using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Heavy focus on AsParameters scenarios that ASP.NET Core itself does not fully support / test:
/// - Positional records
/// - Classes with explicit constructors
/// - Property-only DTOs
/// - Default values + nullable value types
/// - Mixed AsParameters + regular parameters
/// - Nested records (limited)
/// </summary>
public class AsParametersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AsParametersTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // -------------------------------------------------
    // Record (positional / primary constructor)
    // -------------------------------------------------
    [Fact]
    public async Task OrderFilter_Record_AllParameters()
    {
        var response = await _client.GetAsync(
            "/orders/filtered?status=shipped&fromDate=2025-01-01&toDate=2025-12-31&page=3&size=25");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetString().Should().Be("shipped");
        body.GetProperty("from").GetString().Should().Be("2025-01-01");
        body.GetProperty("to").GetString().Should().Be("2025-12-31");
        body.GetProperty("page").GetInt32().Should().Be(3);
        body.GetProperty("size").GetInt32().Should().Be(25);
        body.GetProperty("kind").GetString().Should().Be("Record");
    }

    [Fact]
    public async Task OrderFilter_Record_Defaults()
    {
        var response = await _client.GetAsync("/orders/filtered?status=pending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var page = body.GetProperty("page");
        (page.ValueKind == JsonValueKind.Null || page.GetBoolean() == false).Should().BeTrue();

        var size = body.GetProperty("size");
        (size.ValueKind == JsonValueKind.Null || size.GetBoolean() == false).Should().BeTrue();
    }

    // -------------------------------------------------
    // Class with explicit constructor
    // -------------------------------------------------
    [Fact]
    public async Task ProductSearch_ClassWithCtor()
    {
        var response = await _client.GetAsync(
            "/products/search?category=electronics&min=10.5&max=999.99&inStock=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("category").GetString().Should().Be("electronics");
        body.GetProperty("minPrice").GetDecimal().Should().Be(10.5m);
        body.GetProperty("maxPrice").GetDecimal().Should().Be(999.99m);
        body.GetProperty("inStockOnly").GetBoolean().Should().BeTrue();
        body.GetProperty("kind").GetString().Should().Be("ClassWithCtor");
    }

    [Fact]
    public async Task ProductSearch_ClassWithCtor_Partial()
    {
        var response = await _client.GetAsync("/products/search?category=books");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("category").GetString().Should().Be("books");
        var prop = body.GetProperty("inStockOnly");
        (prop.ValueKind == JsonValueKind.Null || prop.GetBoolean() == false).Should().BeTrue();
    }

    // -------------------------------------------------
    // Property-only DTO
    // -------------------------------------------------
    [Fact]
    public async Task PaginationAndSort_PropertyOnly()
    {
        var response = await _client.GetAsync("/customers?page=4&size=15&sort=lastName&desc=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("page").GetInt32().Should().Be(4);
        body.GetProperty("size").GetInt32().Should().Be(15);
        body.GetProperty("sortBy").GetString().Should().Be("lastName");
        body.GetProperty("descending").GetBoolean().Should().BeTrue();
        body.GetProperty("kind").GetString().Should().Be("PropertyOnly");
    }

    // -------------------------------------------------
    // Mixed: AsParameters + regular query/header
    // -------------------------------------------------
    [Fact]
    public async Task SalesReport_Mixed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "/reports/sales?from=2025-01-01&to=2025-03-31&region=EU");
        request.Headers.Add("X-Report-Format", "csv");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("from").GetString().Should().Be("2025-01-01");
        body.GetProperty("to").GetString().Should().Be("2025-03-31");
        body.GetProperty("region").GetString().Should().Be("EU");
        body.GetProperty("format").GetString().Should().Be("csv");
        body.GetProperty("kind").GetString().Should().Be("Mixed");
    }

    // -------------------------------------------------
    // Defaults + nullable value types
    // -------------------------------------------------
    [Fact]
    public async Task Inventory_DefaultsAndNullables()
    {
        var response = await _client.GetAsync("/inventory?sku=ABC-123&wh=7&reserved=true&minQty=5.5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sku").GetString().Should().Be("ABC-123");
        body.GetProperty("warehouseId").GetInt32().Should().Be(7);
        body.GetProperty("includeReserved").GetBoolean().Should().BeTrue();
        body.GetProperty("minQuantity").GetDecimal().Should().Be(5.5m);
        body.GetProperty("kind").GetString().Should().Be("DefaultsAndNullables");
    }

    [Fact]
    public async Task Inventory_OnlyDefaults()
    {
        var response = await _client.GetAsync("/inventory");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // bool? – missing query value is null (or false if default was applied)
        var prop = body.GetProperty("includeReserved");
        (prop.ValueKind == JsonValueKind.Null || prop.GetBoolean() == false).Should().BeTrue();
    }

    // -------------------------------------------------
    // Nested record (limited support test)
    // -------------------------------------------------
    [Fact]
    public async Task CustomerFilter_Flattened_AllProperties()
    {
        var response = await _client.GetAsync(
            "/customers/search?name=John&city=Berlin&country=DE&zip=10115&active=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("John");
        body.GetProperty("city").GetString().Should().Be("Berlin");
        body.GetProperty("country").GetString().Should().Be("DE");
        body.GetProperty("zip").GetString().Should().Be("10115");
        body.GetProperty("active").GetBoolean().Should().BeTrue();
        body.GetProperty("kind").GetString().Should().Be("FlattenedComplex");
    }
}
