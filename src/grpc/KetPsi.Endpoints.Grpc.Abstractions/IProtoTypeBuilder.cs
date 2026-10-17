using System.Linq.Expressions;

namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Fluent builder for mapping properties of <typeparamref name="T"/> to protobuf field numbers.
/// Binding sources (header/path) belong on endpoint configuration, not here.
/// </summary>
public interface IProtoTypeBuilder<T>
{
    /// <summary>
    /// Maps a property to a protobuf field number (wire body).
    /// </summary>
    IProtoTypeBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, int order);

    /// <summary>
    /// Prefer a matching constructor when deserializing (legacy; construction is analyzed automatically).
    /// </summary>
    IProtoTypeBuilder<T> UseConstrcutor(bool useConstructor);

    /// <summary>
    /// Deserialize by calling a static factory method on <typeparamref name="T"/> (or a declared type)
    /// whose parameters match configured properties in field-number order.
    /// Example: <c>builder.InstantiateWith(nameof(ApiResult&lt;OrderDto&gt;.Create));</c>
    /// or <c>builder.InstantiateWith(typeof(ApiResult), "Create");</c> for a non-generic helper.
    /// </summary>
    IProtoTypeBuilder<T> InstantiateWith(string factoryMethodName);

    /// <summary>
    /// Same as <see cref="InstantiateWith(string)"/> but resolves the method on <paramref name="declaringType"/>
    /// (e.g. a non-generic static helper that returns <typeparamref name="T"/>).
    /// </summary>
    IProtoTypeBuilder<T> InstantiateWith(Type declaringType, string factoryMethodName);

    /// <summary>
    /// Factory method group (same idea as Minimal APIs). Example:
    /// <c>.InstantiateWith(OrderDto.Create)</c>
    /// </summary>
    IProtoTypeBuilder<T> InstantiateWith(Delegate @delegate);
}

