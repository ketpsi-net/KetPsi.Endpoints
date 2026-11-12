using System.ComponentModel;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KetPsi.Endpoints.Grpc;

/// <summary>One generated <c>.proto</c> file (one per proto package).</summary>
/// <param name="FileName">File name, e.g. <c>Api.proto</c>.</param>
/// <param name="Package">Proto package.</param>
/// <param name="Content">proto3 source text.</param>
public sealed record GrpcProtoFile(string FileName, string Package, string Content);

/// <summary>
/// Marks an assembly that contains generated gRPC registration. The generator of a referencing
/// assembly calls <c>Ensure()</c> on the referenced type explicitly, so registration does not depend on
/// module initializers of assemblies that were never loaded.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GrpcGeneratedRegistrationAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public GrpcGeneratedRegistrationAttribute(Type registrationType) => RegistrationType = registrationType;

    /// <summary>The generated registration type (has a public static <c>Ensure()</c>).</summary>
    public Type RegistrationType { get; }
}

/// <summary>
/// Collects per-assembly generated registrations. Generated code registers each assembly once
/// (keyed by assembly name); the host's <c>AddGrpcServices</c> / <c>MapGrpcEndpoints</c> /
/// <c>UseProtoEndpoint</c> apply all of them. Only immutable code-level registrations are kept in
/// static state; per-application state lives in DI / <see cref="IApplicationBuilder.Properties"/>,
/// so several <see cref="WebApplication"/> instances in one process do not interfere and every call is idempotent.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrpcGeneratedRegistrationHub
{
    private const string MappedKey = "__KetPsi.Endpoints.Grpc.Mapped";
    private const string ProtoMappedKey = "__KetPsi.Endpoints.Grpc.ProtoMapped";

    private static readonly object Gate = new();
    private static readonly List<Registration> Registrations = new();

    private sealed record Registration(
        string Key,
        Action<IServiceCollection> AddServices,
        Action<IEndpointRouteBuilder> MapEndpoints,
        GrpcProtoFile[] ProtoFiles);

    private sealed class ServicesMarker { }

    /// <summary>Called by generated code. Re-registering the same <paramref name="assemblyKey"/> is ignored.</summary>
    public static void Register(
        string assemblyKey,
        Action<IServiceCollection> addServices,
        Action<IEndpointRouteBuilder> mapEndpoints,
        GrpcProtoFile[] protoFiles)
    {
        ArgumentNullException.ThrowIfNull(assemblyKey);
        ArgumentNullException.ThrowIfNull(addServices);
        ArgumentNullException.ThrowIfNull(mapEndpoints);
        lock (Gate)
        {
            foreach (var r in Registrations)
            {
                if (string.Equals(r.Key, assemblyKey, StringComparison.Ordinal))
                    return;
            }
            Registrations.Add(new Registration(assemblyKey, addServices, mapEndpoints, protoFiles ?? Array.Empty<GrpcProtoFile>()));
        }
    }

    /// <summary>Keys of registered assemblies (diagnostics / tests).</summary>
    public static IReadOnlyList<string> RegisteredAssemblies
    {
        get
        {
            lock (Gate)
                return Registrations.Select(r => r.Key).ToArray();
        }
    }

    /// <summary>Runs all registered service registrations once per <see cref="IServiceCollection"/>.</summary>
    public static IServiceCollection ApplyServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var d in services)
        {
            if (d.ServiceType == typeof(ServicesMarker))
                return services;
        }
        services.AddSingleton<ServicesMarker>();
        services.AddGrpc();
        foreach (var r in Snapshot())
            r.AddServices(services);
        return services;
    }

    /// <summary>Maps all registered gRPC services once per application.</summary>
    public static WebApplication ApplyMapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var props = ((IApplicationBuilder)app).Properties;
        if (props.ContainsKey(MappedKey))
            return app;
        props[MappedKey] = true;
        foreach (var r in Snapshot())
            r.MapEndpoints(app);
        return app;
    }

    /// <summary>
    /// Maps <c>GET /proto</c> (and <c>GET /proto/{file}</c>) once per application. With a single proto
    /// package <c>/proto</c> returns that file; with several, it returns all files, each preceded by a
    /// <c>// ===== file: name.proto =====</c> line, and every file is also served individually.
    /// </summary>
    public static WebApplication ApplyProtoEndpoint(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var props = ((IApplicationBuilder)app).Properties;
        if (props.ContainsKey(ProtoMappedKey))
            return app;
        props[ProtoMappedKey] = true;

        var files = new List<GrpcProtoFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Snapshot())
        {
            foreach (var f in r.ProtoFiles)
            {
                var name = f.FileName;
                for (var i = 2; !names.Add(name); i++)
                    name = Path.GetFileNameWithoutExtension(f.FileName) + "_" + i + ".proto";
                files.Add(f with { FileName = name });
            }
        }

        var bundle = BuildBundle(files);
        var bundleBytes = Encoding.UTF8.GetBytes(bundle);
        app.MapGet("/proto", (RequestDelegate)(ctx => WriteText(ctx, bundleBytes)));

        var byName = files.ToDictionary(f => f.FileName, f => Encoding.UTF8.GetBytes(f.Content), StringComparer.OrdinalIgnoreCase);
        app.MapGet("/proto/{file}", (RequestDelegate)(ctx =>
        {
            var file = ctx.Request.RouteValues["file"] as string;
            if (file is not null && !file.EndsWith(".proto", StringComparison.OrdinalIgnoreCase))
                file += ".proto";
            if (file is null || !byName.TryGetValue(file, out var bytes))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            return WriteText(ctx, bytes);
        }));
        return app;
    }

    internal static string BuildBundle(IReadOnlyList<GrpcProtoFile> files)
    {
        if (files.Count == 0)
            return "syntax = \"proto3\";\n";
        if (files.Count == 1)
            return files[0].Content;
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            sb.Append("// ===== file: ").Append(f.FileName).Append(" =====\n");
            sb.Append(f.Content);
            if (!f.Content.EndsWith('\n'))
                sb.Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static Task WriteText(HttpContext ctx, byte[] body)
    {
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength = body.Length;
        return ctx.Response.Body.WriteAsync(body, 0, body.Length);
    }

    private static Registration[] Snapshot()
    {
        lock (Gate)
            return Registrations.ToArray();
    }
}
