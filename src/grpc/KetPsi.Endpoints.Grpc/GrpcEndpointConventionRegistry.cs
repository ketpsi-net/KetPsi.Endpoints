using System.Collections.Concurrent;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Collects endpoint conventions (e.g. <c>RequireAuthorization</c>) applied to
/// <c>MapGrpc&lt;T&gt;(route)</c> and replays them onto the real
/// <see cref="Grpc.AspNetCore.Server.GrpcServiceEndpointConventionBuilder"/> when
/// <c>MapGrpcEndpoints</c> calls <c>MapGrpcService</c>.
/// </summary>
public static class GrpcEndpointConventionRegistry
{
    private static readonly ConcurrentDictionary<string, List<Action<EndpointBuilder>>> ByRoute =
        new(StringComparer.OrdinalIgnoreCase);

    public static IEndpointConventionBuilder Register(string route)
    {
        var normalized = Normalize(route);
        var list = ByRoute.GetOrAdd(normalized, _ => new List<Action<EndpointBuilder>>());
        return new DeferredGrpcEndpointConventionBuilder(list);
    }

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
