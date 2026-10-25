using System.Collections.Generic;

using Microsoft.CodeAnalysis;

namespace KetPsi.Endpoints.Http.Generator;

internal static class Diagnostics
{
    private const string Category = "KetPsi.Endpoints.Http";

    public static readonly DiagnosticDescriptor GenerationFailed = new(
        "KP001",
        "Source generation failed",
        "KetPsi endpoint source generation failed: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoEndpointToGenerate = new(
        "KP002",
        "No endpoint to generate",
        "No endpoint to generate",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CouldNotResolveMethodSymbol = new(
        "KP101",
        "Could not resolve method symbol",
        "Could not resolve the method symbol for '{0}'. Ensure the containing assembly is referenced and the required 'using' directives (e.g. 'using KetPsi.Endpoints.Http;') are present in the file.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MapMethodNotGeneric = new(
        "KP102",
        "Map method must be generic",
        "The endpoint mapping method '{0}' must be called with a generic type argument specifying the handler",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingExecuteAsyncMethod = new(
        "KP103",
        "Endpoint missing ExecuteAsync method",
        "Endpoint '{0}' implements 'IEndpoint' but does not define a public 'ExecuteAsync' method",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnresolvedMapCall = new(
        "KP104",
        "Map call could not be resolved",
        "The endpoint '{0}' could not be generated: {1}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HelperWithoutCallSites = new(
        "KP105",
        "Helper method has no call sites",
        "No call sites were found for '{0}'; the endpoint '{1}' is mapped on the application root",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonPortableExpression = new(
        "KP106",
        "Expression cannot be re-emitted",
        "The expression '{0}' cannot be re-emitted into the generated endpoint mapping because it references {1}. Use a constant or a static member instead.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedParameter = new(
        "KP107",
        "Parameter cannot be bound",
        "Parameter '{0}' of type '{1}' on '{2}' cannot be bound: {3}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KeyedServiceWithoutKey = new(
        "KP108",
        "Keyed service requires a key",
        "Parameter '{0}' on '{1}' uses [FromKeyedServices] without a service key",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleBodyParameters = new(
        "KP109",
        "Multiple body parameters",
        "'{0}' binds more than one parameter from the request body or form ({1})",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CallSiteCrashed = new(
        "KP110",
        "Endpoint generator crashed",
        "The endpoint generator failed while analyzing this call: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonConstantConfiguration = new(
        "KP111",
        "Configuration value must be constant",
        "The value '{0}' passed to '{1}' in an IHttpEndpointConfiguration must be a compile-time constant; it is ignored",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> All = new()
    {
        [GenerationFailed.Id] = GenerationFailed,
        [NoEndpointToGenerate.Id] = NoEndpointToGenerate,
        [CouldNotResolveMethodSymbol.Id] = CouldNotResolveMethodSymbol,
        [MapMethodNotGeneric.Id] = MapMethodNotGeneric,
        [MissingExecuteAsyncMethod.Id] = MissingExecuteAsyncMethod,
        [UnresolvedMapCall.Id] = UnresolvedMapCall,
        [HelperWithoutCallSites.Id] = HelperWithoutCallSites,
        [NonPortableExpression.Id] = NonPortableExpression,
        [UnsupportedParameter.Id] = UnsupportedParameter,
        [KeyedServiceWithoutKey.Id] = KeyedServiceWithoutKey,
        [MultipleBodyParameters.Id] = MultipleBodyParameters,
        [CallSiteCrashed.Id] = CallSiteCrashed,
        [NonConstantConfiguration.Id] = NonConstantConfiguration,
    };

    public static DiagnosticDescriptor ById(string id) => All.TryGetValue(id, out var d) ? d : GenerationFailed;
}
