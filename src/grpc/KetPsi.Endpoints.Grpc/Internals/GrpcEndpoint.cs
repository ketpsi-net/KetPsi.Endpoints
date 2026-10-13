using System.Net;
using System.Net.Sockets;

namespace KetPsi.Endpoints.Grpc.Internals;

internal readonly record struct GrpcEndpoint(
    string Host,
    int Port,
    AddressFamily AddressFamily,
    bool UseTls);

internal static class ParseGrpcEndpoint
{
    public static bool TryParseGrpcEndpoint(string value, out GrpcEndpoint grpcEndpoint)
    {
        grpcEndpoint = default;

        const string grpcScheme = "grpc://";
        const string grpcsScheme = "grpcs://";

        bool useTls;

        if (value.StartsWith(grpcsScheme, StringComparison.OrdinalIgnoreCase))
        {
            useTls = true;
            value = value[grpcsScheme.Length..];
        }
        else if (value.StartsWith(grpcScheme, StringComparison.OrdinalIgnoreCase))
        {
            useTls = false;
            value = value[grpcScheme.Length..];
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string host;
        string portText;

        // IPv6: grpc://[::]:8020  /  grpcs://[::1]:8021
        if (value.StartsWith('['))
        {
            var closingBracket = value.IndexOf(']');

            if (closingBracket < 0)
                return false;

            host = value[1..closingBracket];

            if (closingBracket + 1 >= value.Length || value[closingBracket + 1] != ':')
                return false;

            portText = value[(closingBracket + 2)..];
        }
        else
        {
            // IPv4, hostname, +, *
            var colonIndex = value.LastIndexOf(':');

            if (colonIndex <= 0 || colonIndex == value.Length - 1)
                return false;

            host = value[..colonIndex];
            portText = value[(colonIndex + 1)..];
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
            return false;

        var addressFamily = GetAddressFamily(host);

        grpcEndpoint = new GrpcEndpoint(
            Host: host,
            Port: port,
            AddressFamily: addressFamily,
            UseTls: useTls);

        return true;
    }

    private static AddressFamily GetAddressFamily(string host)
    {
        // Kestrel wildcard syntax.
        if (host is "+" or "*")
            return AddressFamily.Unspecified;

        if (!IPAddress.TryParse(host, out var address))
            return AddressFamily.Unspecified;

        return address.AddressFamily;
    }
}
