using System.Net.Sockets;

using KetPsi.Endpoints.Grpc.Internals;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

public class ParseGrpcEndpointTests
{
    [Theory]
    [InlineData("grpc://localhost:8020", "localhost", 8020, false, AddressFamily.Unspecified)]
    [InlineData("grpcs://localhost:8021", "localhost", 8021, true, AddressFamily.Unspecified)]
    [InlineData("grpc://+:8020", "+", 8020, false, AddressFamily.Unspecified)]
    [InlineData("grpc://*:8020", "*", 8020, false, AddressFamily.Unspecified)]
    [InlineData("grpc://0.0.0.0:8020", "0.0.0.0", 8020, false, AddressFamily.InterNetwork)]
    [InlineData("grpc://127.0.0.1:8020", "127.0.0.1", 8020, false, AddressFamily.InterNetwork)]
    [InlineData("grpcs://192.168.1.10:443", "192.168.1.10", 443, true, AddressFamily.InterNetwork)]
    [InlineData("grpc://[::]:8020", "::", 8020, false, AddressFamily.InterNetworkV6)]
    [InlineData("grpcs://[::1]:8021", "::1", 8021, true, AddressFamily.InterNetworkV6)]
    [InlineData("GRPC://LocalHost:9000", "LocalHost", 9000, false, AddressFamily.Unspecified)]
    [InlineData("GRPCS://[::]:443", "::", 443, true, AddressFamily.InterNetworkV6)]
    public void TryParse_HappyPath_ReturnsExpected(
        string input,
        string expectedHost,
        int expectedPort,
        bool expectedTls,
        AddressFamily expectedFamily)
    {
        var ok = ParseGrpcEndpoint.TryParseGrpcEndpoint(input, out var ep);

        Assert.True(ok);
        Assert.Equal(expectedHost, ep.Host);
        Assert.Equal(expectedPort, ep.Port);
        Assert.Equal(expectedTls, ep.UseTls);
        Assert.Equal(expectedFamily, ep.AddressFamily);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://localhost:8020")]
    [InlineData("https://localhost:8020")]
    [InlineData("grpc://")]
    [InlineData("grpcs://")]
    [InlineData("grpc://localhost")]          // missing port
    [InlineData("grpc://localhost:")]         // empty port
    [InlineData("grpc://localhost:abc")]      // non-numeric port
    [InlineData("grpc://localhost:0")]        // port out of range
    [InlineData("grpc://localhost:65536")]    // port out of range
    [InlineData("grpc://localhost:-1")]
    [InlineData("grpc://[::1")]               // unclosed bracket
    [InlineData("grpc://[::1]8020")]          // missing colon after bracket
    [InlineData("grpc://[::1]:")]             // empty port after bracket
    [InlineData("grpc://:8020")]              // empty host
    [InlineData("not-a-url")]
    public void TryParse_InvalidInput_ReturnsFalse(string input)
    {
        var ok = ParseGrpcEndpoint.TryParseGrpcEndpoint(input, out var ep);

        Assert.False(ok);
        Assert.Equal(default, ep);
    }

    [Fact]
    public void TryParse_PortBoundaries_AreAccepted()
    {
        Assert.True(ParseGrpcEndpoint.TryParseGrpcEndpoint("grpc://localhost:1", out var low));
        Assert.Equal(1, low.Port);

        Assert.True(ParseGrpcEndpoint.TryParseGrpcEndpoint("grpc://localhost:65535", out var high));
        Assert.Equal(65535, high.Port);
    }
}
