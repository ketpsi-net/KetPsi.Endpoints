namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Fluent mapping surface for a single meta-type (open or closed generic).
/// </summary>
public interface IOpenProtoMetaType
{
    IOpenProtoMetaType Property(string propertyName, int order);

    /// <summary>
    /// Controls whether the deserializer should use the type's constructor when deserializing.
    /// </summary>
    IOpenProtoMetaType UseConstrcutor(bool useConstructor);
}
