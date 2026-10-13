using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Open-generic configuration surface for the generator / call-site API.
/// Closed generic codecs are emitted when those types appear on handlers.
/// </summary>
public sealed class OpenProtoTypeBuilder : IOpenProtoTypeBuilder
{
    public IOpenProtoTypeBuilder ConfigureGeneric(Type openGenericType, Action<IOpenProtoMetaType> configure)
    {
        configure(new OpenProtoMetaTypeStub());
        return this;
    }

    public void ApplyClosedGeneric(Type closedGenericType)
    {
    }
}

internal sealed class OpenProtoMetaTypeStub : IOpenProtoMetaType
{
    public IOpenProtoMetaType Property(string propertyName, int order) => this;
    public IOpenProtoMetaType UseConstrcutor(bool useConstructor) => this;
}
