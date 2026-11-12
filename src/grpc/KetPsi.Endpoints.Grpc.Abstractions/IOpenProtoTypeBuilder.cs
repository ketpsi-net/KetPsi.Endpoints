namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Registers configuration for open-generic types. The source generator later applies
/// that configuration to every closed generic found in endpoint signatures.
/// </summary>
public interface IOpenProtoTypeBuilder
{
    /// <summary>Configures an open generic type (e.g. <c>typeof(ApiResult&lt;&gt;)</c>); applied to every closed instantiation.</summary>
    IOpenProtoTypeBuilder ConfigureGeneric(Type openGenericType, Action<IOpenProtoMetaType> configure);
}
