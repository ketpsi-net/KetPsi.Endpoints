using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Call-site helpers to apply proto type configurations at runtime.
/// Field maps and factories are primarily consumed by the source generator from syntax;
/// these methods exist for tests and explicit invocation.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Applies an <see cref="IOpenProtoTypeConfiguration"/> using an internal open-generic builder.
    /// </summary>
    public static void ApplyOpenProtoConfiguration(this IOpenProtoTypeConfiguration configuration)
    {
        var builder = new OpenProtoTypeBuilder();
        configuration.Configure(builder);
    }

    /// <summary>
    /// Applies an <see cref="IProtoTypeConfiguration{T}"/> using an internal closed-type builder.
    /// </summary>
    public static void ApplyProtoConfiguration<T>(this IProtoTypeConfiguration<T> configuration)
    {
        configuration.Configure(new ProtoTypeBuilder<T>());
    }
}
