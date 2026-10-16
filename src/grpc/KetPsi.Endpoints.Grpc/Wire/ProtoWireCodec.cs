using System.Buffers;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Helpers for whole-message buffers. Callers must drive <see cref="ProtoWireWriter"/> /
/// <see cref="ProtoWireReader"/> themselves — those are ref structs and cannot be Func/Action type args.
/// </summary>
public static class ProtoWireCodec
{
    public static ArrayBufferWriter<byte> CreateBuffer(int initialCapacity = 128)
        => new ArrayBufferWriter<byte>(initialCapacity);

    public static byte[] ToArrayAndDispose(ref ProtoWireWriter writer, ArrayBufferWriter<byte> buffer)
    {
        writer.Dispose();
        return buffer.WrittenSpan.ToArray();
    }
}
