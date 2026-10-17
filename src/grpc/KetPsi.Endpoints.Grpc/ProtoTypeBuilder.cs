using System.Linq.Expressions;

using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Fluent surface for <see cref="IProtoTypeConfiguration{T}"/>.
/// The source generator reads call sites; runtime is a no-op.
/// </summary>
public sealed class ProtoTypeBuilder<T> : IProtoTypeBuilder<T>
{
    public IProtoTypeBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, int order)
        => this;

    public IProtoTypeBuilder<T> UseConstrcutor(bool useConstructor)
        => this;

    public IProtoTypeBuilder<T> InstantiateWith(string factoryMethodName)
        => this;

    public IProtoTypeBuilder<T> InstantiateWith(Type declaringType, string factoryMethodName)
        => this;

    public IProtoTypeBuilder<T> InstantiateWith(Delegate @delegate)
        => this;
}
