using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// Per-endpoint state resolved once when the endpoint is built (never per request):
/// <see cref="RouteHandlerOptions.ThrowOnBadRequest"/>, the host <see cref="JsonSerializerOptions"/>,
/// the final route pattern (including group prefixes) and DI service inference.
/// </summary>
public sealed class EndpointEnvironment
{
    private static readonly JsonSerializerOptions FallbackJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _services;
    private JsonSerializerOptions? _jsonOptions;

    private EndpointEnvironment(IServiceProvider services, RoutePattern? routePattern, bool throwOnBadRequest)
    {
        _services = services;
        RoutePattern = routePattern;
        ThrowOnBadRequest = throwOnBadRequest;
    }

    /// <summary>Mirrors <see cref="RouteHandlerOptions.ThrowOnBadRequest"/> (true in Development by default).</summary>
    public bool ThrowOnBadRequest { get; }

    /// <summary>Final route pattern of the endpoint, including <c>MapGroup</c> prefixes.</summary>
    public RoutePattern? RoutePattern { get; }

    public IServiceProvider ApplicationServices => _services;

    /// <summary>Host <c>JsonOptions.SerializerOptions</c>, resolved once per endpoint.</summary>
    public JsonSerializerOptions JsonOptions => _jsonOptions ??= ResolveJsonOptions(_services);

    public static EndpointEnvironment Create(EndpointBuilder endpointBuilder)
    {
        ArgumentNullException.ThrowIfNull(endpointBuilder);
        var services = endpointBuilder.ApplicationServices;
        var throwOnBadRequest = services.GetService<IOptions<RouteHandlerOptions>>()?.Value.ThrowOnBadRequest ?? false;
        var pattern = (endpointBuilder as RouteEndpointBuilder)?.RoutePattern;
        return new EndpointEnvironment(services, pattern, throwOnBadRequest);
    }

    internal static JsonSerializerOptions ResolveJsonOptions(IServiceProvider? services)
        => services?.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? FallbackJsonOptions;

    /// <summary>True when <paramref name="name"/> is a parameter of the final route pattern (case-insensitive).</summary>
    public bool HasRouteParameter(string name) => RoutePattern?.GetParameter(name) is not null;

    /// <summary>
    /// Throws like Minimal APIs when an explicit <c>[FromRoute]</c> parameter is not part of the route.
    /// </summary>
    public void EnsureRouteParameter(string name, string parameterName)
    {
        if (RoutePattern is not null && RoutePattern.GetParameter(name) is null)
        {
            throw new InvalidOperationException(
                $"'{name}' is not a route parameter. Parameter '{parameterName}' is bound with [FromRoute] but the route pattern '{RoutePattern.RawText}' does not contain it.");
        }
    }

    /// <summary>Minimal API service inference (<see cref="IServiceProviderIsService"/>).</summary>
    public bool IsService(Type serviceType)
        => _services.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false;

    /// <summary>
    /// Resolves the <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/> from the host options once per endpoint.
    /// Under Native AOT the type must be registered on a <c>JsonSerializerContext</c> (same contract as RDG);
    /// a missing registration fails when the endpoint is built rather than on every request.
    /// </summary>
    public JsonTypeInfo<T> GetJsonTypeInfo<T>()
    {
        var info = JsonOptions.GetTypeInfo(typeof(T));
        return info as JsonTypeInfo<T>
            ?? throw new InvalidOperationException($"The JsonTypeInfo resolved for '{typeof(T)}' is not a JsonTypeInfo<{typeof(T).Name}>.");
    }

    /// <summary>
    /// Throws (when the endpoint is built) like Minimal APIs when a body would be inferred for a verb that does not allow it,
    /// or when more than one body source is present.
    /// </summary>
    public static void EnsureBodyInference(int inferredBodyCount, int explicitBodyCount, bool bodyAllowed, string displayName)
    {
        if (inferredBodyCount > 0 && !bodyAllowed)
        {
            throw new InvalidOperationException(
                $"Body was inferred but the method does not allow inferred body parameters ({displayName}). " +
                "Did you mean to register the parameter type as a service or apply the [FromServices], [FromQuery] or [FromBody] attribute?");
        }

        if (inferredBodyCount + explicitBodyCount > 1)
        {
            throw new InvalidOperationException(
                $"Failure to infer one or more parameters for {displayName}: more than one parameter is bound from the request body or form.");
        }
    }
}
