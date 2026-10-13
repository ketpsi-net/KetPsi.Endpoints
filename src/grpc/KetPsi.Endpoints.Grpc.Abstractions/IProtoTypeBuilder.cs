using System.Linq.Expressions;

namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Fluent builder for mapping properties of <typeparamref name="T"/> to protobuf field numbers.
/// </summary>
public interface IProtoTypeBuilder<T>
{
    IProtoTypeBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, int order);

    /// <summary>
    /// Controls whether the deserializer should use the type's constructor when deserializing.
    /// </summary>
    IProtoTypeBuilder<T> UseConstrcutor(bool useConstructor);
}
