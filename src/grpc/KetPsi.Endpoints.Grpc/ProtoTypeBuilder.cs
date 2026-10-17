using System.Linq.Expressions;

using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Runtime no-op builder for <see cref="IProtoTypeConfiguration{T}"/>.
/// The source generator reads <c>Property</c> / <c>InstantiateWith</c> call sites from source.
/// </summary>
/// <typeparam name="T">The CLR type being mapped to protobuf fields.</typeparam>
internal sealed class ProtoTypeBuilder<T> : IProtoTypeBuilder<T>
{
    /// <inheritdoc />
    public IProtoTypeBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, int order)
        => this;

    /// <inheritdoc />
    public IProtoTypeBuilder<T> UseConstrcutor(bool useConstructor)
        => this;

    /// <inheritdoc />
    public IProtoTypeBuilder<T> InstantiateWith(string factoryMethodName)
        => this;

    /// <inheritdoc />
    public IProtoTypeBuilder<T> InstantiateWith(Type declaringType, string factoryMethodName)
        => this;

    /// <inheritdoc />
    public IProtoTypeBuilder<T> InstantiateWith(Delegate @delegate)
        => this;
}
