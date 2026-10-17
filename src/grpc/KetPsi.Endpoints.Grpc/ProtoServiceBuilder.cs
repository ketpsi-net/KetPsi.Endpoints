namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Optional fluent overrides for gRPC service package / service / method names.
/// Prefer route strings on <c>MapGrpc</c> and <c>MapGroup</c> / <c>WithGroupName</c> for package versioning.
/// </summary>
/// <typeparam name="TService">The service or handler type associated with the override.</typeparam>
public interface IProtoServiceConfigure<TService>
{
    /// <summary>Sets the logical service name segment.</summary>
    IProtoServiceConfigure<TService> HasServiceName(string serviceName);

    /// <summary>Overrides the default protobuf package namespace (e.g. <c>Api</c>).</summary>
    IProtoServiceConfigure<TService> WithNamespace(string grpcNamespace);

    /// <summary>Overrides the default RPC method name.</summary>
    IProtoServiceConfigure<TService> WithName(string name);
}

/// <summary>
/// Default implementation of <see cref="IProtoServiceConfigure{TService}"/> (runtime helper; not required for generator mapping).
/// </summary>
internal sealed class ProtoServiceBuilder<TService> : IProtoServiceConfigure<TService>
{
    /// <summary>Protobuf package namespace.</summary>
    public string GrpcNamespace { get; private set; } = "Api";

    /// <summary>Service name segment.</summary>
    public string ServiceName { get; private set; } = typeof(TService).Name.Replace("Handler", "Service");

    /// <summary>RPC method name.</summary>
    public string MethodName { get; private set; } = "Execute";

    /// <inheritdoc />
    public IProtoServiceConfigure<TService> WithNamespace(string grpcNamespace)
    {
        if (!string.IsNullOrWhiteSpace(grpcNamespace))
            GrpcNamespace = grpcNamespace;
        return this;
    }

    /// <inheritdoc />
    public IProtoServiceConfigure<TService> HasServiceName(string serviceName)
    {
        if (!string.IsNullOrWhiteSpace(serviceName))
            ServiceName = serviceName;
        return this;
    }

    /// <inheritdoc />
    public IProtoServiceConfigure<TService> WithName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            MethodName = name;
        return this;
    }
}
