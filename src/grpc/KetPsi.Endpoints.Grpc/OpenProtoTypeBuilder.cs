using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Runtime no-op builder for <see cref="IOpenProtoTypeConfiguration"/>.
/// The source generator reads configuration from syntax; this type exists for call-site helpers and tests.
/// </summary>
internal sealed class OpenProtoTypeBuilder : IOpenProtoTypeBuilder
{
    /// <inheritdoc />
    public IOpenProtoTypeBuilder ConfigureGeneric(Type openGenericType, Action<IOpenProtoMetaType> configure)
    {
        configure(new OpenProtoMetaTypeStub());
        return this;
    }

    /// <summary>No-op; closed generics are applied by the generator when seen on handlers.</summary>
    public void ApplyClosedGeneric(Type closedGenericType)
    {
    }
}

/// <summary>No-op meta-type builder for open-generic configuration lambdas.</summary>
internal sealed class OpenProtoMetaTypeStub : IOpenProtoMetaType
{
    /// <inheritdoc />
    public IOpenProtoMetaType Property(string propertyName, int order) => this;

    /// <inheritdoc />
    public IOpenProtoMetaType UseConstrcutor(bool useConstructor) => this;

    /// <inheritdoc />
    public IOpenProtoMetaType InstantiateWith(string factoryMethodName) => this;

    /// <inheritdoc />
    public IOpenProtoMetaType InstantiateWith(Type declaringType, string factoryMethodName) => this;

    /// <inheritdoc />
    public IOpenProtoMetaType InstantiateWith(Delegate @delegate) => this;
}
