using System.Collections.ObjectModel;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// Shared, read-only HTTP method lists so generated <c>MapMethods</c> calls do not allocate a
/// <c>string[]</c> per endpoint at registration time. The lists cannot be mutated by callers.
/// </summary>
public static class HttpVerbs
{
    public static IReadOnlyList<string> Get { get; } = new ReadOnlyCollection<string>(["GET"]);
    public static IReadOnlyList<string> Post { get; } = new ReadOnlyCollection<string>(["POST"]);
    public static IReadOnlyList<string> Put { get; } = new ReadOnlyCollection<string>(["PUT"]);
    public static IReadOnlyList<string> Delete { get; } = new ReadOnlyCollection<string>(["DELETE"]);
    public static IReadOnlyList<string> Patch { get; } = new ReadOnlyCollection<string>(["PATCH"]);

    public static IReadOnlyList<string> ForMapMethod(string mapMethodName) => mapMethodName switch
    {
        "MapGet" => Get,
        "MapPost" => Post,
        "MapPut" => Put,
        "MapDelete" => Delete,
        "MapPatch" => Patch,
        _ => Get
    };
}
