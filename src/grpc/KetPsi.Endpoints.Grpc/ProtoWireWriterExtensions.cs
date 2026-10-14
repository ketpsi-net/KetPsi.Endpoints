using KetPsi.Endpoints.Grpc.Wire;

namespace KetPsi.Endpoints.Grpc
{
    public static class ProtoWireWriterExtensions
    {
        public static void WriteLengthDelimitedBytes(this ProtoWireWriter protoWireWriter, ReadOnlySpan<byte> data) => protoWireWriter.WriteBytes(data);
    }
}
