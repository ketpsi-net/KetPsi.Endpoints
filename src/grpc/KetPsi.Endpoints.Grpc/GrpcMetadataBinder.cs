using System.Globalization;

using Grpc.Core;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Runtime helpers to bind gRPC request headers (metadata) onto handler request DTOs
/// for properties configured with <c>FromHeader</c> on the endpoint configuration.
/// </summary>
public static class GrpcMetadataBinder
{
    public static string? GetString(ServerCallContext context, string metadataKey)
        => context is null ? null : GetString(context.RequestHeaders, metadataKey);

    public static string? GetString(Metadata headers, string metadataKey)
    {
        if (headers is null || string.IsNullOrEmpty(metadataKey))
            return null;
        var entry = headers.Get(metadataKey);
        return entry?.Value;
    }

    public static bool TryGetString(Metadata headers, string metadataKey, out string value)
    {
        var entry = headers?.Get(metadataKey);
        if (entry is null)
        {
            value = "";
            return false;
        }
        value = entry.Value ?? "";
        return true;
    }

    public static int? GetInt32(ServerCallContext context, string metadataKey)
        => GetInt32(context?.RequestHeaders, metadataKey);

    public static int? GetInt32(Metadata? headers, string metadataKey)
    {
        var s = GetString(headers!, metadataKey);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static long? GetInt64(ServerCallContext context, string metadataKey)
        => GetInt64(context?.RequestHeaders, metadataKey);

    public static long? GetInt64(Metadata? headers, string metadataKey)
    {
        var s = GetString(headers!, metadataKey);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static bool? GetBoolean(ServerCallContext context, string metadataKey)
        => GetBoolean(context?.RequestHeaders, metadataKey);

    public static bool? GetBoolean(Metadata? headers, string metadataKey)
    {
        var s = GetString(headers!, metadataKey);
        if (string.IsNullOrEmpty(s)) return null;
        if (bool.TryParse(s, out var b)) return b;
        if (s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    public static Guid? GetGuid(ServerCallContext context, string metadataKey)
        => GetGuid(context?.RequestHeaders, metadataKey);

    public static Guid? GetGuid(Metadata? headers, string metadataKey)
    {
        var s = GetString(headers!, metadataKey);
        return Guid.TryParse(s, out var g) ? g : null;
    }

    public static TEnum? GetEnum<TEnum>(ServerCallContext context, string metadataKey)
        where TEnum : struct, Enum
        => GetEnum<TEnum>(context?.RequestHeaders, metadataKey);

    public static TEnum? GetEnum<TEnum>(Metadata? headers, string metadataKey)
        where TEnum : struct, Enum
    {
        var s = GetString(headers!, metadataKey);
        if (string.IsNullOrEmpty(s)) return null;
        if (Enum.TryParse<TEnum>(s, ignoreCase: true, out var e)) return e;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) &&
            Enum.IsDefined(typeof(TEnum), i))
            return (TEnum)Enum.ToObject(typeof(TEnum), i);
        return null;
    }
}
