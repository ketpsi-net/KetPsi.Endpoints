using Grpc.Core;

using KetPsi.Endpoints.Grpc.Wire;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// CLR counterpart of <c>google.protobuf.Empty</c>. Used for handlers without a request parameter
/// (<c>ExecuteAsync(CancellationToken)</c>) or without a result (<c>Task ExecuteAsync(...)</c>).
/// </summary>
public sealed class GrpcEmpty
{
    private GrpcEmpty() { }

    /// <summary>The singleton instance.</summary>
    public static GrpcEmpty Instance { get; } = new();

    /// <summary>Marshaller: writes nothing, validates and skips any received fields.</summary>
    public static Marshaller<GrpcEmpty> Marshaller { get; } = ProtoMarshaller.Create<GrpcEmpty>(
        static (ref ProtoWireWriter _, GrpcEmpty _) => { },
        static (_, _) => 0,
        static (ref ProtoWireReader reader) =>
        {
            while (reader.TryReadTag(out _, out var wireType))
                reader.SkipField(wireType);
            return Instance;
        });
}
