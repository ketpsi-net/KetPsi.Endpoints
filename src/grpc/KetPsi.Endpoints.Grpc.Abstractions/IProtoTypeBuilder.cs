using System.Linq.Expressions;

namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Fluent builder for mapping properties of <typeparamref name="T"/> to protobuf field numbers.
/// Binding sources (header/path) belong on <c>IGrpcEndpointConfiguration</c>, not here.
/// </summary>
public interface IProtoTypeBuilder<T>
{
    /// <summary>
    /// Maps a property to a protobuf field number (wire body).
    /// </summary>
    IProtoTypeBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, int order);

    /// <summary>
    /// Controls whether the deserializer should prefer a matching constructor when deserializing.
    /// </summary>
    IProtoTypeBuilder<T> UseConstrcutor(bool useConstructor);
}
