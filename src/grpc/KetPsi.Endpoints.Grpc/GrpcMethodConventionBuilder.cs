using System.ComponentModel;

using Grpc.AspNetCore.Server;

using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Applies endpoint conventions (e.g. <c>RequireAuthorization</c>) to a single gRPC method of a service
/// mapped with <c>MapGrpcService</c>. Generated code wraps the service's convention builder with one of
/// these per <c>MapGrpc</c> call site, so conventions chained on a call site affect only that RPC.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GrpcMethodConventionBuilder : IEndpointConventionBuilder
{
    private readonly IEndpointConventionBuilder _serviceBuilder;
    private readonly string _serviceName;
    private readonly string _methodName;

    /// <summary>Creates a filter over <paramref name="serviceBuilder"/> for <paramref name="serviceName"/>/<paramref name="methodName"/>.</summary>
    public GrpcMethodConventionBuilder(IEndpointConventionBuilder serviceBuilder, string serviceName, string methodName)
    {
        _serviceBuilder = serviceBuilder ?? throw new ArgumentNullException(nameof(serviceBuilder));
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _methodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
    }

    /// <inheritdoc />
    public void Add(Action<EndpointBuilder> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        _serviceBuilder.Add(b =>
        {
            if (Matches(b))
                convention(b);
        });
    }

    /// <inheritdoc />
    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        ArgumentNullException.ThrowIfNull(finallyConvention);
        _serviceBuilder.Finally(b =>
        {
            if (Matches(b))
                finallyConvention(b);
        });
    }

    private bool Matches(EndpointBuilder builder)
    {
        foreach (var item in builder.Metadata)
        {
            if (item is GrpcMethodMetadata m)
            {
                return string.Equals(m.Method.ServiceName, _serviceName, StringComparison.Ordinal)
                       && string.Equals(m.Method.Name, _methodName, StringComparison.Ordinal);
            }
        }
        return false;
    }
}

/// <summary>Convention builder returned by the inert <c>MapGrpc*</c> stubs; ignores everything.</summary>
internal sealed class NoopEndpointConventionBuilder : IEndpointConventionBuilder
{
    public static readonly NoopEndpointConventionBuilder Instance = new();

    private NoopEndpointConventionBuilder() { }

    public void Add(Action<EndpointBuilder> convention) { }

    public void Finally(Action<EndpointBuilder> finallyConvention) { }
}
