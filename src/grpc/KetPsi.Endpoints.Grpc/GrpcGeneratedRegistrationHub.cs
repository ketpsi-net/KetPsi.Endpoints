using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Collects per-assembly generated registration actions (via module initializers).
/// Host code calls the single <c>AddGrpcServices</c> / <c>MapGrpcEndpoints</c> /
/// <c>UseProtoEndpoint</c> extensions; those apply every registered assembly.
/// </summary>
public static class GrpcGeneratedRegistrationHub
{
    private static readonly object Gate = new();
    private static readonly List<Action<IServiceCollection>> ServiceActions = new();
    private static readonly List<Action<WebApplication>> MapActions = new();
    private static readonly List<Action<WebApplication>> ProtoActions = new();

    /// <summary>
    /// Called from generated module initializers in assemblies that contain <c>MapGrpc</c>.
    /// </summary>
    /// <param name="addServices">DI registration for handlers and generated services.</param>
    /// <param name="mapEndpoints">Maps gRPC services onto the application.</param>
    /// <param name="useProtoEndpoint">Optional <c>GET /proto</c> schema mapping.</param>
    public static void Register(
        Action<IServiceCollection>? addServices = null,
        Action<WebApplication>? mapEndpoints = null,
        Action<WebApplication>? useProtoEndpoint = null)
    {
        lock (Gate)
        {
            if (addServices is not null)
                ServiceActions.Add(addServices);
            if (mapEndpoints is not null)
                MapActions.Add(mapEndpoints);
            if (useProtoEndpoint is not null)
                ProtoActions.Add(useProtoEndpoint);
        }
    }

    /// <summary>Runs all registered service registration actions.</summary>
    public static IServiceCollection ApplyServices(IServiceCollection services)
    {
        foreach (var action in Snapshot(ServiceActions))
            action(services);
        return services;
    }

    /// <summary>Runs all registered endpoint mapping actions.</summary>
    public static WebApplication ApplyMapEndpoints(WebApplication app)
    {
        foreach (var action in Snapshot(MapActions))
            action(app);
        return app;
    }

    /// <summary>Runs all registered <c>/proto</c> mapping actions.</summary>
    public static WebApplication ApplyProtoEndpoint(WebApplication app)
    {
        foreach (var action in Snapshot(ProtoActions))
            action(app);
        return app;
    }

    private static Action<T>[] Snapshot<T>(List<Action<T>> list)
    {
        lock (Gate)
            return list.ToArray();
    }
}
