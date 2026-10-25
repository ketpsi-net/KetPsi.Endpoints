using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HttpEndpointGenerator.Tests.Aot;

public class JsonBodyReaderTests
{
    private sealed class SampleDto
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [Fact]
    public async Task ReadAsync_Deserializes_With_TypeInfoResolver()
    {
        var ctx = CreateContext("""{"name":"Ada","age":36}""");
        var dto = await JsonBodyReader.ReadAsync<SampleDto>(ctx);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Ada");
        dto.Age.Should().Be(36);
    }

    [Fact]
    public async Task ReadAsync_EmptyObject_Returns_Defaults()
    {
        var ctx = CreateContext("{}");
        var dto = await JsonBodyReader.ReadAsync<SampleDto>(ctx);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("");
        dto.Age.Should().Be(0);
    }

    [Fact]
    public void ResolveOptions_Uses_Registered_JsonOptions()
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        var sp = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = sp };
        var opts = JsonBodyReader.ResolveOptions(http);
        opts.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    private static DefaultHttpContext CreateContext(string json)
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(_ => { });
        var sp = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = sp };
        var bytes = Encoding.UTF8.GetBytes(json);
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = bytes.Length;
        return http;
    }
}
