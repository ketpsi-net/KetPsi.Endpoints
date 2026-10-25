using System.Globalization;

using Grpc.Core;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Reads gRPC request metadata (HTTP/2 headers) for properties configured with
/// <c>FromMetadata</c> on <see cref="IGrpcEndpointConfiguration{TEndpoint}"/>.
/// Used by generated binders after the request message is deserialized.
/// </summary>
public static class GrpcMetadataBinder
{
    /// <summary>Gets a metadata entry as a string from the call context request headers.</summary>
    public static string? GetString(ServerCallContext context, string metadataKey)
        => context is null ? null : GetString(context.RequestHeaders, metadataKey);

    /// <summary>Gets a metadata entry as a string from a header collection (case-insensitive key).</summary>
    public static string? GetString(Metadata headers, string metadataKey)
    {
        if (headers is null || string.IsNullOrEmpty(metadataKey))
            return null;
        return TryGetString(headers, metadataKey, out var value) ? value : null;
    }

    /// <summary>Tries to read a string metadata entry.</summary>
    public static bool TryGetString(Metadata? headers, string metadataKey, out string value)
    {
        value = "";
        if (headers is null || string.IsNullOrEmpty(metadataKey))
            return false;
        for (var i = 0; i < headers.Count; i++)
        {
            var entry = headers[i];
            if (entry.Key.Equals(metadataKey, StringComparison.OrdinalIgnoreCase) && !entry.IsBinary)
            {
                value = entry.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>Parses a metadata entry as <see cref="int"/>.</summary>
    public static int? GetInt32(ServerCallContext context, string metadataKey)
        => GetInt32(context?.RequestHeaders, metadataKey);

    /// <summary>Parses a metadata entry as <see cref="int"/>.</summary>
    public static int? GetInt32(Metadata? headers, string metadataKey)
    {
        if (headers is null) return null;
        var s = GetString(headers, metadataKey);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Parses a metadata entry as <see cref="long"/>.</summary>
    public static long? GetInt64(ServerCallContext context, string metadataKey)
        => GetInt64(context?.RequestHeaders, metadataKey);

    /// <summary>Parses a metadata entry as <see cref="long"/>.</summary>
    public static long? GetInt64(Metadata? headers, string metadataKey)
    {
        if (headers is null) return null;
        var s = GetString(headers, metadataKey);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Parses a metadata entry as <see cref="bool"/>.</summary>
    public static bool? GetBoolean(ServerCallContext context, string metadataKey)
        => GetBoolean(context?.RequestHeaders, metadataKey);

    /// <summary>Parses a metadata entry as <see cref="bool"/>.</summary>
    public static bool? GetBoolean(Metadata? headers, string metadataKey)
    {
        if (headers is null) return null;
        var s = GetString(headers, metadataKey);
        return bool.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Parses a metadata entry as <see cref="Guid"/>.</summary>
    public static Guid? GetGuid(ServerCallContext context, string metadataKey)
        => GetGuid(context?.RequestHeaders, metadataKey);

    /// <summary>Parses a metadata entry as <see cref="Guid"/>.</summary>
    public static Guid? GetGuid(Metadata? headers, string metadataKey)
    {
        if (headers is null) return null;
        var s = GetString(headers, metadataKey);
        return Guid.TryParse(s, out var v) ? v : null;
    }
}
