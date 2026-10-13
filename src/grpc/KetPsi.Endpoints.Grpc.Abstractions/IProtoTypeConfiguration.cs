namespace KetPsi.Endpoints.Grpc.Abstractions;

/// <summary>
/// Marks a class that configures protobuf field mapping for a concrete type <typeparamref name="T"/>.
/// </summary>
public interface IProtoTypeConfiguration<T>
{
    void Configure(IProtoTypeBuilder<T> builder);
}
