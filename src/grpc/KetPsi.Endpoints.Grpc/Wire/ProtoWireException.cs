namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Thrown when protobuf wire data is malformed (truncated, invalid varint, wrong length,
/// recursion limit exceeded, invalid UTF-8, ...). Generated marshallers translate it into
/// <c>RpcException(StatusCode.InvalidArgument)</c>.
/// </summary>
public sealed class ProtoWireException : InvalidOperationException
{
    /// <summary>Creates the exception with a message.</summary>
    public ProtoWireException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public ProtoWireException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Limits enforced by <see cref="ProtoWireReader"/> and generated codecs.</summary>
public static class ProtoWireLimits
{
    /// <summary>
    /// Maximum nesting depth of messages / groups (same default as Google.Protobuf).
    /// Protects against stack overflow from maliciously deep or cyclic payloads and object graphs.
    /// </summary>
    public const int MaxDepth = 100;

    /// <summary>Throws <see cref="ProtoWireException"/> when <paramref name="depth"/> exceeds <see cref="MaxDepth"/>.</summary>
    public static void CheckDepth(int depth)
    {
        if (depth > MaxDepth)
            throw DepthExceeded();
    }

    internal static ProtoWireException DepthExceeded()
        => new($"Protobuf message nesting exceeds the maximum depth of {MaxDepth}.");
}
