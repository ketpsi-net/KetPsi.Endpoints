using HttpEndpointGenerator.Tests.Handlers;

using KetPsi.Endpoints.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HttpEndpointGenerator.Tests.Mapping;

public static class Routes
{
    // Not a compile-time constant: the generator must re-emit the expression, not "".
    public static readonly string Dynamic = "/dynamic-" + "route";
}

public static class FilterTrace
{
    public static void Add(EndpointFilterInvocationContext context, string entry)
    {
        if (!context.HttpContext.Items.TryGetValue("trace", out var list))
            context.HttpContext.Items["trace"] = list = new List<string>();
        ((List<string>)list!).Add(entry);
    }
}

/// <summary>Typed endpoint filter: records the bound argument count.</summary>
public sealed class ArgumentCountFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers["X-Arg-Count"] = context.Arguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return next(context);
    }
}

/// <summary>
/// Extended scenarios: helper extensions called in reduced form, groups with route parameters,
/// filters, non-constant routes, escaping and the full binding matrix.
/// </summary>
public static class ExtendedMappings
{
    public static void MapExtendedEndpoints(this WebApplication app)
    {
        app.MapGet<TypesHandler>("/types");
        app.MapGet<RequiredHandler>("/required/{id:int}");
        app.MapGet<OptionalRouteHandler>("/optional-route/{id?}");
        app.MapGet<CatchAllHandler>("/files/{*path}");
        app.MapGet<RouteBoolHandler>("/flags/{flag}/{code}");
        app.MapGet<SpecialsHandler>("/specials");
        app.MapPost<StreamHandler>("/stream").DisableAntiforgery();
        app.MapPost<PipeReaderHandler>("/pipe");
        app.MapPost<InferredBodyHandler>("/inferred-body");
        app.MapPost<StructBodyHandler>("/struct-body");

        app.MapGet<PlainTaskHandler>("/results/task");
        app.MapGet<PlainValueTaskHandler>("/results/valuetask");
        app.MapGet<VoidHandler>("/results/void");
        app.MapGet<TextHandler>("/results/text");
        app.MapGet<SyncTextHandler>("/results/sync-text");
        app.MapGet<JsonHandler>("/results/json").WithName("JsonResult");
        app.MapGet<NullJsonHandler>("/results/null-json");
        app.MapGet<ValueTaskJsonHandler>("/results/valuetask-json");
        app.MapGet<NullResultHandler>("/results/null-result");
        app.MapGet<TypedResultHandler>("/results/typed");

        app.MapPost<FormFieldsHandler>("/forms/fields").DisableAntiforgery();
        app.MapPost<ProtectedFormHandler>("/forms/protected").WithName("ProtectedForm");

        app.MapGet<CollidingNamesHandler>("/collide");
        app.MapGet<TwoDtosHandler>("/two-dtos");
        app.MapGet<EscapeHandler>("/escape").WithName("Escape \"quoted\" \\ name");
        app.MapGet<DynamicRouteHandler>(Routes.Dynamic);
        app.MapGet<AllocationHandler>("/alloc/{id}");

        // Group with a route parameter in the prefix.
        app.MapGroup("/tenants/{tenantId}").MapGet<GroupRouteParamHandler>("/items/{id}");

        // Filters: group filter (outer) → endpoint filters → handler.
        var filtered = app.MapGroup("/filters")
            .AddEndpointFilter(async (context, next) =>
            {
                FilterTrace.Add(context, "group");
                return await next(context);
            });

        filtered.MapGet<FilterEchoHandler>("/echo/{id}")
            .AddEndpointFilter(async (context, next) =>
            {
                FilterTrace.Add(context, "endpoint:" + context.GetArgument<int>(0).ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (context.GetArgument<int>(0) == 0)
                    return Results.Content("blocked");
                if (context.HttpContext.Request.Query.ContainsKey("mutate"))
                    context.Arguments[0] = context.GetArgument<int>(0) * 10;
                return await next(context);
            })
            .AddEndpointFilter<ArgumentCountFilter>();

        filtered.MapGet<FilterPlainTaskHandler>("/plain");

        // Helpers in reduced (extension) form.
        app.MapGroup("/reports").MapGroupHelpers();
        app.MapRootHelpers();
        Helpers.MapNamedArgument(prefix: "/named", endpoints: app.MapGroup("/by-name"));
    }

    public static void MapGroupHelpers(this RouteGroupBuilder group)
    {
        group.MapGet<GroupHelperHandler>("/{id}");
    }

    public static void MapRootHelpers(this IEndpointRouteBuilder endpoints, string prefix = "/helpers")
    {
        endpoints.MapGet<HelperHandler>(prefix + "/root");
    }
}

public static class Helpers
{
    public static void MapNamedArgument(IEndpointRouteBuilder endpoints, string prefix)
    {
        endpoints.MapGet<HelperHandler>(prefix);
    }
}
