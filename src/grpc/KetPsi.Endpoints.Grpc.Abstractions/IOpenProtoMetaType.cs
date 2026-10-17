namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Fluent mapping surface for a single meta-type (open or closed generic).
/// </summary>
public interface IOpenProtoMetaType
{
    IOpenProtoMetaType Property(string propertyName, int order);

    /// <summary>
    /// Prefer a matching constructor when deserializing (legacy).
    /// </summary>
    IOpenProtoMetaType UseConstrcutor(bool useConstructor);

    /// <summary>
    /// Deserialize closed generics of this open type via a static factory method
    /// (e.g. <c>Create</c> on <c>ApiResult&lt;T&gt;</c>). Parameters must match
    /// configured properties in field-number order.
    /// </summary>
    IOpenProtoMetaType InstantiateWith(string factoryMethodName);

    /// <summary>
    /// Factory declared on another type (e.g. non-generic <c>ApiResult.Create&lt;T&gt;</c> helper).
    /// </summary>
    IOpenProtoMetaType InstantiateWith(Type declaringType, string factoryMethodName);

    /// <summary>
    /// Factory method group (same idea as Minimal APIs). Example:
    /// <c>.InstantiateWith(ResponseWrapper&lt;MyDto&gt;.Create)</c>
    /// </summary>
    IOpenProtoMetaType InstantiateWith(Delegate @delegate);
}

