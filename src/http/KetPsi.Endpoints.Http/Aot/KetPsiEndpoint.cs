using System.Reflection;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Http.Aot;

/// <summary>
/// Registration helpers for the generated KetPsi request-delegate endpoints.
/// <para>
/// Shape of a generated endpoint:
/// <c>Map(builder, pattern, verbs, metadata)</c> → user conventions → <c>Complete(builder, build)</c>.
/// The <c>metadata</c> convention runs first among the entry conventions (after group conventions), like
/// Minimal API inferred metadata, so later conventions such as <c>.DisableAntiforgery()</c> still win.
/// The <c>build</c> convention runs last: it sees every group and endpoint filter, composes them around the bound
/// arguments, assigns <see cref="EndpointBuilder.RequestDelegate"/> and clears the consumed filter factories so the
/// routing data source does not wrap the delegate a second time.
/// </para>
/// </summary>
public static class KetPsiEndpoint
{
    private static readonly RequestDelegate Pending = static _ =>
        throw new InvalidOperationException("The KetPsi endpoint request delegate was not built. The endpoint build convention did not run.");

    /// <summary>Maps the endpoint with a placeholder delegate and adds the metadata convention first.</summary>
    public static RouteHandlerBuilder Map(
        IEndpointRouteBuilder endpoints,
        string pattern,
        IReadOnlyList<string> httpMethods,
        Action<EndpointBuilder> metadata)
    {
        var inner = endpoints.MapMethods(pattern, httpMethods, Pending);
        inner.Add(metadata);
        return inner as RouteHandlerBuilder ?? new RouteHandlerBuilder([inner]);
    }

    /// <summary>Adds the final convention that builds the request delegate.</summary>
    public static void Complete(IEndpointConventionBuilder builder, Action<EndpointBuilder> build)
        => builder.Add(build);

    /// <summary>
    /// Composes the endpoint's filter factories (group filters outermost, then endpoint filters, then the handler)
    /// exactly like RDF and clears <see cref="EndpointBuilder.FilterFactories"/> so they are not applied twice.
    /// </summary>
    public static EndpointFilterDelegate BuildFilterPipeline(EndpointBuilder endpointBuilder, MethodInfo methodInfo, EndpointFilterDelegate handler)
    {
        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = methodInfo,
            ApplicationServices = endpointBuilder.ApplicationServices,
        };

        var pipeline = handler;
        var factories = endpointBuilder.FilterFactories;
        for (var i = factories.Count - 1; i >= 0; i--)
            pipeline = factories[i](factoryContext, pipeline);

        factories.Clear();
        return pipeline;
    }
}

/// <summary>Metadata helpers used by the generated metadata convention (mirrors RDF inferred metadata).</summary>
public static class KetPsiEndpointMetadata
{
    private static readonly string[] JsonContentTypes = ["application/json"];
    private static readonly string[] FormContentTypes = ["multipart/form-data", "application/x-www-form-urlencoded"];
    private static readonly string[] FormFileContentTypes = ["multipart/form-data"];
    private static readonly string[] PlainTextContentTypes = ["text/plain"];

    /// <summary>Antiforgery metadata added for form-bound endpoints (RDF adds the same).</summary>
    public static IAntiforgeryMetadata AntiforgeryRequired { get; } = new AntiforgeryRequiredMetadata();

    public static void AddJsonBody(EndpointBuilder builder, Type bodyType, bool isOptional)
        => builder.Metadata.Add(new AcceptsMetadata(JsonContentTypes, bodyType, isOptional));

    /// <summary>Form accepts metadata plus antiforgery-required metadata (added once per endpoint).</summary>
    public static void AddForm(EndpointBuilder builder, bool filesOnly)
    {
        builder.Metadata.Add(new AcceptsMetadata(filesOnly ? FormFileContentTypes : FormContentTypes, null, false));
        builder.Metadata.Add(AntiforgeryRequired);
    }

    public static void AddJsonResponse(EndpointBuilder builder, Type responseType)
        => builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, responseType, JsonContentTypes));

    public static void AddTextResponse(EndpointBuilder builder)
        => builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(string), PlainTextContentTypes));

    /// <summary>Calls <see cref="IEndpointMetadataProvider.PopulateMetadata"/> for a parameter or return type.</summary>
    public static void Populate<T>(MethodInfo method, EndpointBuilder builder) where T : IEndpointMetadataProvider
        => T.PopulateMetadata(method, builder);

    /// <summary>Calls <see cref="IEndpointParameterMetadataProvider.PopulateMetadata"/> for a parameter type.</summary>
    public static void PopulateParameter<T>(ParameterInfo parameter, EndpointBuilder builder) where T : IEndpointParameterMetadataProvider
        => T.PopulateMetadata(parameter, builder);

    /// <summary>
    /// Adds <c>IParameterBindingMetadata</c> (.NET 9+) for a handler parameter so ApiExplorer / OpenAPI describe it
    /// without reflection-based inference (RDF/RDG add the same metadata). No-op on .NET 8 (ApiExplorer reads the
    /// <see cref="MethodInfo"/> metadata instead).
    /// </summary>
    public static void AddParameter(EndpointBuilder builder, ParameterInfo parameter, bool hasTryParse, bool hasBindAsync, bool isOptional)
    {
#if NET9_0_OR_GREATER
        builder.Metadata.Add(new ParameterBindingMetadata(parameter.Name ?? string.Empty, parameter, hasTryParse, hasBindAsync, isOptional));
#endif
    }

    /// <summary>
    /// Adds parameter binding metadata for one member of an <c>[AsParameters]</c> type (the property is looked up by a
    /// constant name on a statically known type, which the trimmer understands).
    /// </summary>
    public static void AddAsParametersMember(EndpointBuilder builder, MethodInfo method, PropertyInfo? property, bool hasTryParse, bool hasBindAsync, bool isOptional)
    {
#if NET9_0_OR_GREATER
        if (property is null)
            return;
        var parameter = new PropertyParameterInfo(property, method);
        builder.Metadata.Add(new ParameterBindingMetadata(property.Name, parameter, hasTryParse, hasBindAsync, isOptional));
#endif
    }

#if NET9_0_OR_GREATER
    private sealed class ParameterBindingMetadata(string name, ParameterInfo parameterInfo, bool hasTryParse, bool hasBindAsync, bool isOptional)
        : IParameterBindingMetadata
    {
        public string Name { get; } = name;
        public bool HasTryParse { get; } = hasTryParse;
        public bool HasBindAsync { get; } = hasBindAsync;
        public ParameterInfo ParameterInfo { get; } = parameterInfo;
        public bool IsOptional { get; } = isOptional;
    }

    /// <summary>Exposes an <c>[AsParameters]</c> property as a parameter (name, type and attributes of the property).</summary>
    private sealed class PropertyParameterInfo : ParameterInfo
    {
        private readonly PropertyInfo _property;

        public PropertyParameterInfo(PropertyInfo property, MemberInfo member)
        {
            _property = property;
            NameImpl = property.Name;
            ClassImpl = property.PropertyType;
            MemberImpl = member;
            PositionImpl = -1;
            AttrsImpl = ParameterAttributes.None;
        }

        public override bool HasDefaultValue => false;
        public override object? DefaultValue => null;
        public override object[] GetCustomAttributes(bool inherit) => _property.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => _property.GetCustomAttributes(attributeType, inherit);
        public override bool IsDefined(Type attributeType, bool inherit) => _property.IsDefined(attributeType, inherit);
        public override IList<CustomAttributeData> GetCustomAttributesData() => _property.GetCustomAttributesData();
    }
#endif

    private sealed class AntiforgeryRequiredMetadata : IAntiforgeryMetadata
    {
        public bool RequiresValidation => true;
    }
}
