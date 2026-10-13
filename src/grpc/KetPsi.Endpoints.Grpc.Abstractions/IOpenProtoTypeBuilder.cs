namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Registers configuration for open-generic types. The source generator later applies
/// that configuration to every closed generic found in endpoint signatures.
/// </summary>
public interface IOpenProtoTypeBuilder
{
    IOpenProtoTypeBuilder ConfigureGeneric(Type openGenericType, Action<IOpenProtoMetaType> configure);
}
