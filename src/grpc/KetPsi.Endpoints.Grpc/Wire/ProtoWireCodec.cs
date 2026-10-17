using System.Buffers;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Helpers for whole-message buffers. Callers must drive <see cref="ProtoWireWriter"/> /
/// <see cref="ProtoWireReader"/> themselves — those are ref structs and cannot be Func/Action type args.
/// </summary>
public static class ProtoWireCodec
{
    /// <summary>Allocates a buffer writer suitable for building a length-delimited message.</summary>
    public static ArrayBufferWriter<byte> CreateBuffer(int initialCapacity = 128)
        => new ArrayBufferWriter<byte>(initialCapacity);

    /// <summary>Disposes the writer and returns the written payload as a new array.</summary>
    public static byte[] ToArrayAndDispose(ref ProtoWireWriter writer, ArrayBufferWriter<byte> buffer)
    {
        writer.Dispose();
        return buffer.WrittenSpan.ToArray();
    }
}
