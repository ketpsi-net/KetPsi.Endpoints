namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Marks a class that configures protobuf field mapping for a concrete type <typeparamref name="T"/>.
/// </summary>
public interface IProtoTypeConfiguration<T>
{
    /// <summary>Configures field numbers / construction (read by the source generator at compile time; never executed).</summary>
    void Configure(IProtoTypeBuilder<T> builder);
}
