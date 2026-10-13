using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

public static class Extensions
{
    /// <summary>
    /// Applies an <see cref="IOpenProtoTypeConfiguration"/> (call-site helper; generator already reads maps).
    /// </summary>
    public static void ApplyOpenProtoConfiguration(this IOpenProtoTypeConfiguration configuration)
    {
        var builder = new OpenProtoTypeBuilder();
        configuration.Configure(builder);
    }

    /// <summary>
    /// Applies an <see cref="IProtoTypeConfiguration{T}"/> (call-site helper; generator already reads maps).
    /// </summary>
    public static void ApplyProtoConfiguration<T>(this IProtoTypeConfiguration<T> configuration)
    {
        configuration.Configure(new ProtoTypeBuilder<T>());
    }
}
