using System.Buffers;
using System.ComponentModel;

using Grpc.Core;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>Reads one message from a reader (generated <c>Read_X</c> methods).</summary>
public delegate T ProtoReadDelegate<out T>(ref ProtoWireReader reader);

/// <summary>Writes one message body to a writer (generated <c>Write_X</c> methods).</summary>
public delegate void ProtoWriteDelegate<in T>(ref ProtoWireWriter writer, T value);

/// <summary>Computes the encoded body size of a message (generated <c>Size_X</c> methods).</summary>
public delegate int ProtoSizeDelegate<in T>(T value, int depth);

/// <summary>
/// Builds gRPC <see cref="Marshaller{T}"/> instances on top of generated codecs.
/// Serialization sets the payload length and writes straight into the transport buffer;
/// deserialization reads single-segment payloads in place and copies multi-segment payloads into a pooled buffer.
/// Malformed payloads surface as <c>RpcException(StatusCode.InvalidArgument)</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ProtoMarshaller
{
    /// <summary>Creates a contextual marshaller from generated delegates.</summary>
    public static Marshaller<T> Create<T>(ProtoWriteDelegate<T> write, ProtoSizeDelegate<T> size, ProtoReadDelegate<T> read)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(read);
        return new Marshaller<T>(
            (value, ctx) =>
            {
                var length = size(value, 0);
                ctx.SetPayloadLength(length);
                var writer = new ProtoWireWriter(ctx.GetBufferWriter());
                write(ref writer, value);
                ctx.Complete();
            },
            ctx => Deserialize(ctx.PayloadAsReadOnlySequence(), read));
    }

    /// <summary>Deserializes a (possibly multi-segment) payload; malformed input throws InvalidArgument.</summary>
    public static T Deserialize<T>(ReadOnlySequence<byte> payload, ProtoReadDelegate<T> read)
    {
        try
        {
            if (payload.IsSingleSegment)
            {
                var reader = new ProtoWireReader(payload.FirstSpan);
                return read(ref reader);
            }

            var length = checked((int)payload.Length);
            var rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                payload.CopyTo(rented);
                var reader = new ProtoWireReader(rented.AsSpan(0, length));
                return read(ref reader);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch (Exception ex) when (ex is not RpcException and not OutOfMemoryException and not OperationCanceledException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid protobuf payload: " + ex.Message, ex));
        }
    }

    /// <summary>Convenience (not hot path): serializes to a new array.</summary>
    public static byte[] Serialize<T>(T value, ProtoWriteDelegate<T> write, ProtoSizeDelegate<T> size)
    {
        var buffer = new ArrayBufferWriter<byte>(Math.Max(1, size(value, 0)));
        var writer = new ProtoWireWriter(buffer);
        write(ref writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Convenience: deserializes from a span (throws <see cref="ProtoWireException"/> on malformed input).</summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> payload, ProtoReadDelegate<T> read)
    {
        var reader = new ProtoWireReader(payload);
        return read(ref reader);
    }
}
