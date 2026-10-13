using KetPsi.Endpoints.Grpc.Abstractions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Optional fluent configuration for a gRPC service (namespace / name overrides).
/// </summary>
public interface IProtoServiceConfigure<TService>
{
    IProtoServiceConfigure<TService> HasServiceName(string serviceName);

    /// <summary>
    /// Overrides the default Protobuf package namespace (e.g., "Api").
    /// </summary>
    IProtoServiceConfigure<TService> WithNamespace(string grpcNamespace);

    /// <summary>
    /// Overrides the default method name. If empty, falls back to the default API name.
    /// </summary>
    IProtoServiceConfigure<TService> WithName(string name);
}

/// <summary>
/// Default implementation used when service-level overrides are supplied.
/// </summary>
public sealed class ProtoServiceBuilder<TService> : IProtoServiceConfigure<TService>
{
    public string GrpcNamespace { get; private set; } = "Api";
    public string ServiceName { get; private set; } = typeof(TService).Name.Replace("Handler", "Service");
    public string MethodName { get; private set; } = "Execute";

    public IProtoServiceConfigure<TService> WithNamespace(string grpcNamespace)
    {
        if (!string.IsNullOrWhiteSpace(grpcNamespace))
            GrpcNamespace = grpcNamespace;
        return this;
    }

    public IProtoServiceConfigure<TService> HasServiceName(string serviceName)
    {
        if (!string.IsNullOrWhiteSpace(serviceName))
            ServiceName = serviceName;
        return this;
    }

    public IProtoServiceConfigure<TService> WithName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            MethodName = name;
        return this;
    }
}
