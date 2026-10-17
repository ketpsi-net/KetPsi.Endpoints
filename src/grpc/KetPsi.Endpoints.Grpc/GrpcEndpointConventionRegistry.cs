using System.Collections.Concurrent;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Stores endpoint conventions (e.g. <c>RequireAuthorization</c>) from <c>MapGrpc</c> call sites
/// and replays them when generated code calls <c>MapGrpcService</c> inside <c>MapGrpcEndpoints</c>.
/// </summary>
public static class GrpcEndpointConventionRegistry
{
    private static readonly ConcurrentDictionary<string, List<Action<EndpointBuilder>>> ByRoute =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a deferred convention builder keyed by the gRPC service route
    /// (e.g. <c>Api.Users</c> or the relative <c>MapGrpc</c> route string).
    /// </summary>
    /// <param name="serviceRoute">Route or service path used as the convention key.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> that records conventions until <see cref="Apply"/>.</returns>
    public static IEndpointConventionBuilder Register(string serviceRoute)
    {
        var list = ByRoute.GetOrAdd(Normalize(serviceRoute), _ => new List<Action<EndpointBuilder>>());
        return new DeferredGrpcEndpointConventionBuilder(list);
    }

    /// <summary>
    /// Applies all conventions previously recorded for <paramref name="serviceRoute"/> to
    /// the live endpoint builder returned by <c>MapGrpcService</c>.
    /// </summary>
    public static void Apply(string serviceRoute, IEndpointConventionBuilder target)
    {
        var normalized = Normalize(serviceRoute);
        if (!ByRoute.TryGetValue(normalized, out var list))
            return;
        lock (list)
        {
            foreach (var convention in list)
                target.Add(convention);
        }
    }

    private static string Normalize(string route)
        => (route ?? "").Replace('/', '.').Trim('.');

    private sealed class DeferredGrpcEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions;

        public DeferredGrpcEndpointConventionBuilder(List<Action<EndpointBuilder>> conventions)
            => _conventions = conventions;

        public void Add(Action<EndpointBuilder> convention)
        {
            lock (_conventions)
                _conventions.Add(convention);
        }
    }
}
