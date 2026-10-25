namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Marks a class that configures open-generic types for protobuf field mapping.
/// The configuration is applied later to each closed generic discovered by the source generator.
/// </summary>
public interface IOpenProtoTypeConfiguration
{
    /// <summary>Configures open generic types (read by the source generator at compile time; never executed).</summary>
    void Configure(IOpenProtoTypeBuilder builder);
}
