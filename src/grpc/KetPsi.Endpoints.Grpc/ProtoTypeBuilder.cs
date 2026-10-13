using System.Linq.Expressions;

using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Fluent surface for <see cref="IProtoTypeConfiguration{T}"/>.
/// The source generator reads <c>Property(...)</c> call sites; runtime is a no-op.
/// </summary>
public sealed class ProtoTypeBuilder<T> : IProtoTypeBuilder<T>
{
    public IProtoTypeBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, int order)
        => this;

    public IProtoTypeBuilder<T> UseConstrcutor(bool useConstructor)
        => this;
}
