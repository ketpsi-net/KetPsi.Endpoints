namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Marks a class that configures open-generic types for protobuf field mapping.
/// The configuration is applied later to each closed generic discovered by the source generator.
/// </summary>
public interface IOpenProtoTypeConfiguration
{
    void Configure(IOpenProtoTypeBuilder builder);
}
