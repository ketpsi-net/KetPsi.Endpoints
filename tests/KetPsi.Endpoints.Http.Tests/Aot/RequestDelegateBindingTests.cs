using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpEndpointGenerator.Tests.Aot;

/// <summary>
/// Binding semantics of the KetPsi request-delegate path (findings 7–11, 15, 16): every supported type,
/// nullable/required/default handling, names, route forms, special types, services and body inference.
/// </summary>
public class RequestDelegateBindingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RequestDelegateBindingTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private async Task<JsonElement> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task All_Supported_Query_Types_Bind()
    {
        var guid = Guid.NewGuid();
        var body = await GetJson(
            "/types?s16=-7&u8=200&u32=4000000000&u64=18000000000000000000&dec=12.5&f32=1.5&f64=2.25&flag=true" +
            $"&guid={guid}&dt=2025-01-02T03:04:05Z&dto=2025-01-02T03:04:05%2B02:00&date=2025-06-07&time=13:14:15" +
            "&span=01:02:03&color=gReEn&ch=x&uri=https%3A%2F%2Fexample.com%2Fa&point=3,4&ids=1&ids=2&ids=3&tags=a&tags=b&raw=r1&raw=r2");

        body.GetProperty("s16").GetString().Should().Be("-7");
        body.GetProperty("u8").GetString().Should().Be("200");
        body.GetProperty("u32").GetString().Should().Be("4000000000");
        body.GetProperty("u64").GetString().Should().Be("18000000000000000000");
        body.GetProperty("dec").GetString().Should().Be("12.5");
        body.GetProperty("f32").GetString().Should().Be("1.5");
        body.GetProperty("f64").GetString().Should().Be("2.25");
        body.GetProperty("flag").GetString().Should().Be("True");
        body.GetProperty("guid").GetString().Should().Be(guid.ToString());
        body.GetProperty("dt").GetString().Should().StartWith("2025-01-02T03:04:05");
        body.GetProperty("dto").GetString().Should().Contain("2025-01-02");
        body.GetProperty("date").GetString().Should().Be("2025-06-07");
        body.GetProperty("time").GetString().Should().StartWith("13:14:15");
        body.GetProperty("span").GetString().Should().Be("01:02:03");
        body.GetProperty("color").GetString().Should().Be("Green", "enums are parsed case-insensitively");
        body.GetProperty("ch").GetString().Should().Be("x");
        body.GetProperty("uri").GetString().Should().Be("https://example.com/a");
        body.GetProperty("point").GetString().Should().Be("3:4");
        body.GetProperty("ids").GetString().Should().Be("1|2|3");
        body.GetProperty("tags").GetString().Should().Be("a|b");
        body.GetProperty("raw").GetString().Should().Be("r1,r2");
    }

    [Fact]
    public async Task Missing_Nullable_Values_Are_Null_And_Collections_Empty()
    {
        var body = await GetJson("/types");
        foreach (var name in new[] { "s16", "u8", "dec", "flag", "guid", "dt", "date", "color", "uri", "point" })
            body.GetProperty(name).ValueKind.Should().Be(JsonValueKind.Null, name);
        body.GetProperty("ids").GetString().Should().BeEmpty();
        body.GetProperty("tags").GetString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("u8=300")]
    [InlineData("guid=nope")]
    [InlineData("color=purple")]
    [InlineData("date=2025-13-40")]
    [InlineData("point=1")]
    [InlineData("ids=1&ids=x")]
    [InlineData("flag=maybe")]
    public async Task Invalid_Values_Return_400(string query)
    {
        var response = await _client.GetAsync("/types?" + query);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Required_Defaults_And_Names()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/required/5?page=2&q=search");
        request.Headers.Add("X-Count", "7");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(5);
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("query").GetString().Should().Be("search", "[FromQuery(Name = \"q\")] is honored");
        body.GetProperty("count").GetInt32().Should().Be(7, "[FromHeader] int is parsed");
        body.GetProperty("format").GetString().Should().Be("json", "parameter default values are honored");
        body.GetProperty("size").GetInt32().Should().Be(10);
    }

    [Theory]
    [InlineData("/required/5?q=search", "7")]          // missing required int
    [InlineData("/required/5?page=2", "7")]            // missing required string (renamed)
    [InlineData("/required/5?page=2&q=s", null)]       // missing required header
    [InlineData("/required/5?page=abc&q=s", "7")]      // invalid int
    [InlineData("/required/5?page=1&q=s", "seven")]    // invalid header int
    public async Task Required_Or_Invalid_Returns_400(string url, string? header)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (header is not null)
            request.Headers.Add("X-Count", header);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Route_Constraint_Rejects_Non_Int()
    {
        var response = await _client.GetAsync("/required/abc?page=1&q=s");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Optional_Route_Parameter()
    {
        (await GetJson("/optional-route/9")).GetProperty("id").GetInt32().Should().Be(9);
        (await GetJson("/optional-route")).GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CatchAll_Route_Parameter()
        => (await GetJson("/files/a/b/c.txt")).GetProperty("path").GetString().Should().Be("a/b/c.txt");

    [Fact]
    public async Task Route_Bool_And_FromRoute_Name()
    {
        var body = await GetJson("/flags/true/xyz");
        body.GetProperty("flag").GetBoolean().Should().BeTrue();
        body.GetProperty("routeCode").GetString().Should().Be("xyz");
    }

    [Fact]
    public async Task Invalid_Route_Bool_Returns_400()
        => (await _client.GetAsync("/flags/notabool/xyz")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Group_Prefix_Route_Parameters_Bind_From_Route()
    {
        var body = await GetJson("/tenants/acme/items/3");
        body.GetProperty("tenantId").GetString().Should().Be("acme");
        body.GetProperty("id").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Special_Types_Services_Keyed_And_BindAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/specials");
        request.Headers.Add("X-Client-Name", "cli");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sameContext").GetBoolean().Should().BeTrue();
        body.GetProperty("hasUser").GetBoolean().Should().BeTrue();
        body.GetProperty("hasLogger").GetBoolean().Should().BeTrue("ILogger<T> is inferred as a service");
        body.GetProperty("docs").GetString().Should().Be("DocumentService", "registered interfaces are inferred as services");
        body.GetProperty("missingIsNull").GetBoolean().Should().BeTrue("optional [FromServices] T? uses GetService");
        body.GetProperty("keyed").GetString().Should().Be("Secondary");
        body.GetProperty("client").GetString().Should().Be("cli");
    }

    [Fact]
    public async Task BindAsync_Optional_Missing_Is_Null()
    {
        var body = await GetJson("/specials");
        body.GetProperty("client").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Stream_And_PipeReader_Bind_Request_Body()
    {
        var stream = await _client.PostAsync("/stream", new StringContent("raw-body", Encoding.UTF8, "text/plain"));
        stream.StatusCode.Should().Be(HttpStatusCode.OK);
        (await stream.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString().Should().Be("raw-body");

        var pipe = await _client.PostAsync("/pipe", new StringContent("12345", Encoding.UTF8, "text/plain"));
        pipe.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pipe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("length").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task Complex_Parameter_On_Post_Is_Inferred_As_Body()
    {
        var response = await _client.PostAsJsonAsync("/inferred-body", new { x = 3, y = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("x").GetInt32().Should().Be(3);
        body.GetProperty("y").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Body_With_Wrong_Content_Type_Returns_415()
    {
        var response = await _client.PostAsync("/inferred-body", new StringContent("{\"x\":1}", Encoding.UTF8, "text/plain"));
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Required_Body_Missing_Returns_400()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/inferred-body"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Struct_Body_Binds_And_Missing_Is_400()
    {
        var ok = await _client.PostAsJsonAsync("/struct-body", new { value = 42 });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetInt32().Should().Be(42);

        var missing = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/struct-body"));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Form_Fields_Typed_Nullable_And_Collections()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("3"), "count" },
            { new StringContent("a"), "tag" },
            { new StringContent("b"), "tag" },
            { new ByteArrayContent([1, 2, 3]), "upload", "f.bin" },
        };
        var response = await _client.PostAsync("/forms/fields", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("count").GetInt32().Should().Be(3, "[FromForm] int is parsed, not assigned a string");
        body.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null, "missing string? form field stays null");
        body.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).Should().Equal("a", "b");
        body.GetProperty("files").GetInt32().Should().Be(1, "IFormFileCollection binds all files");
    }

    [Fact]
    public async Task Form_With_Json_Content_Type_Returns_415()
    {
        var response = await _client.PostAsJsonAsync("/forms/fields", new { count = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Parameter_Names_Colliding_With_Generated_Locals_Work()
    {
        var body = await GetJson("/collide?httpContext=4&handler=h&__result=1&__handlerTask=2&__hasBody=b");
        body.GetProperty("httpContext").GetInt32().Should().Be(4);
        body.GetProperty("handler").GetString().Should().Be("h");
        body.GetProperty("__result").GetInt32().Should().Be(1);
        body.GetProperty("__handlerTask").GetInt32().Should().Be(2);
        body.GetProperty("__hasBody").GetString().Should().Be("b");
    }

    [Fact]
    public async Task Two_AsParameters_With_Same_Member_Names()
    {
        var body = await GetJson("/two-dtos?size=5&name=n");
        body.GetProperty("a").GetInt32().Should().Be(5);
        body.GetProperty("aName").GetString().Should().Be("n");
        body.GetProperty("b").GetInt32().Should().Be(5);
        body.GetProperty("sort").GetString().Should().Be("asc", "constructor defaults are honored");
    }
}
