using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
    // Polyfill so records / init-only setters compile for netstandard2.0.
    internal static class IsExternalInit;
}

namespace KetPsi.Endpoints.Http.Generator
{
    // ------------------------------------------------------------------
    // All pipeline models are records made only of strings, primitives, enums and EquatableArray,
    // so the incremental pipeline can compare them by value (no ISymbol / SyntaxNode / Compilation).
    // ------------------------------------------------------------------

    /// <summary>Source location stored by value (file path + span) for deferred diagnostics.</summary>
    internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

        public static LocationInfo? From(SyntaxNode? node) => From(node?.GetLocation());

        public static LocationInfo? From(Location? location)
        {
            if (location is null || location.SourceTree is null)
                return null;
            return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
        }
    }

    /// <summary>Diagnostic stored by value; materialized in the output step.</summary>
    internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Arguments)
    {
        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] args)
            => new(descriptor.Id, location, EquatableArray<string>.From(args));

        public Diagnostic ToDiagnostic()
            => Diagnostic.Create(Diagnostics.ById(Id), Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Arguments, static a => (object?)a)));
    }

    internal enum BindingSource
    {
        Inferred,
        Route,
        Query,
        Header,
        Form,
        Body,
        Services,
        KeyedServices,
        AsParameters,
    }

    internal enum SpecialKind
    {
        None,
        HttpContext,
        HttpRequest,
        HttpResponse,
        ClaimsPrincipal,
        CancellationToken,
        Stream,
        PipeReader,
        FormFile,
        FormFileCollection,
        FormCollection,
    }

    internal enum ParseKind
    {
        None,
        String,
        StringValues,
        Parsable,
        DateTime,
        DateTimeOffset,
        DateOnly,
        TimeOnly,
        Enum,
        Uri,
        StaticTryParseWithProvider,
        StaticTryParse,
    }

    internal enum CollectionKind
    {
        None,
        Array,
        List,
    }

    internal enum BindAsyncKind
    {
        None,
        /// <summary><c>static ValueTask&lt;T?&gt; BindAsync(HttpContext)</c></summary>
        Context,
        /// <summary><c>static ValueTask&lt;T?&gt; BindAsync(HttpContext, ParameterInfo)</c></summary>
        ContextAndParameter,
        /// <summary>Only through an explicit <c>IBindableFromHttpContext&lt;T&gt;</c> implementation.</summary>
        Interface,
    }

    internal enum ReturnShape
    {
        Void,
        Task,
        ValueTask,
        TaskOfT,
        ValueTaskOfT,
        Value,
    }

    internal enum ResultKind
    {
        None,
        Result,
        String,
        Object,
        Json,
    }

    /// <summary>Everything the emitters need to know about a parameter / member type.</summary>
    internal sealed record TypeModel(
        string Fqn,
        string ValueFqn,
        string DisplayName,
        bool IsValueType,
        bool IsNullableValueType,
        bool IsReferenceType,
        SpecialKind Special,
        ParseKind Parse,
        CollectionKind Collection,
        string? ElementFqn,
        string? ElementValueFqn,
        bool ElementIsNullable,
        BindAsyncKind BindAsync,
        bool BindAsyncReturnsNullableStruct,
        bool IsComplex,
        bool IsParameterMetadataProvider,
        bool IsMetadataProvider)
    {
        public bool IsParsableScalar => Parse != ParseKind.None && Parse != ParseKind.StringValues && Collection == CollectionKind.None;
        public bool IsParsableCollection => Collection != CollectionKind.None || Parse == ParseKind.StringValues;
    }

    /// <summary>Binding source + name (and key for keyed services) from an attribute or external configuration.</summary>
    internal sealed record BindingSpec(BindingSource Source, string? Name, string? KeyLiteral, bool AllowEmptyBody)
    {
        public static readonly BindingSpec Inferred = new(BindingSource.Inferred, null, null, false);
    }

    /// <summary>A bindable value: an ExecuteAsync parameter or an AsParameters member.</summary>
    internal sealed record ValueModel(
        string Name,
        TypeModel Type,
        BindingSpec Binding,
        bool IsOptional,
        string? DefaultLiteral,
        EquatableArray<string> ExtraAttributes,
        LocationInfo? Location);

    internal sealed record MemberModel(ValueModel Value, bool IsConstructorParameter, bool IsRequiredMember);

    /// <summary>Construction shape of a DTO used with AsParameters.</summary>
    internal sealed record DtoModel(
        string Fqn,
        string Accessibility,
        EquatableArray<MemberModel> ConstructorMembers,
        EquatableArray<MemberModel> PropertyMembers);

    internal sealed record ParameterModel(int Ordinal, ValueModel Value, DtoModel? Dto, bool UseSurrogate);

    internal sealed record ReturnModel(ReturnShape Shape, ResultKind Kind, string DeclaredFqn, string? ResultFqn, string? ResultTypeOf, bool ResultIsMetadataProvider);

    /// <summary>How the request-delegate path obtains the handler instance.</summary>
    internal enum HandlerActivation
    {
        /// <summary>Resolved from <c>HttpContext.RequestServices</c>.</summary>
        Services,
        /// <summary>Only a public parameterless constructor: <c>new THandler()</c> per request (no request scope needed).</summary>
        New,
        /// <summary>Parameterless and stateless (no instance fields, not disposable): one cached instance per endpoint.</summary>
        Shared,
    }

    internal enum ChainRootKind
    {
        App,
        HelperParameter,
        Unresolved,
    }

    /// <summary>
    /// Receiver of a Map* call (or helper argument): an optional helper-parameter root plus group steps.
    /// Step / template text may contain helper-parameter placeholders (see <see cref="Portable"/>).
    /// </summary>
    internal sealed record ChainModel(
        ChainRootKind Root,
        string? HelperKey,
        string? HelperName,
        int HelperOrdinal,
        string? HelperParameterType,
        EquatableArray<string> Steps);

    internal sealed record EndpointModel(
        string HandlerFqn,
        string HandlerShortName,
        bool IsStatic,
        string HttpMethod,
        string RouteTemplate,
        string ChainTemplate,
        ChainModel Receiver,
        string ExtensionClassName,
        EquatableArray<string> Usings,
        EquatableArray<ParameterModel> Parameters,
        ReturnModel Return,
        string? ObsoleteMessage,
        bool IsObsolete,
        string HandlerAccessibility,
        HandlerActivation Activation,
        LocationInfo? Location);

    internal sealed record MapCallResult(EndpointModel? Endpoint, bool Skipped, EquatableArray<DiagnosticInfo> Diagnostics);

    /// <summary>A call to a helper method (<c>app.MapUsers()</c>) whose body contains Map* calls on its parameters.</summary>
    internal sealed record HelperCallSite(
        string HelperKey,
        EquatableArray<HelperArgument> Arguments,
        LocationInfo? Location);

    /// <summary>The argument passed for one helper parameter (portable text and, for route builders, its chain).</summary>
    internal sealed record HelperArgument(int Ordinal, string? Portable, ChainModel? Chain, EquatableArray<DiagnosticInfo> Diagnostics);

    internal sealed record HelperScanResult(EquatableArray<HelperCallSite> CallSites);

    internal sealed record BuilderParameterModel(string TypeName, string Name, string Accessibility);

    internal sealed record HandlerBuilderModel(string HandlerFqn, string ShortName, string Accessibility, EquatableArray<BuilderParameterModel> Parameters);

    internal sealed record HandlerResult(HandlerBuilderModel? Handler, EquatableArray<DiagnosticInfo> Diagnostics);

    internal sealed record PropertyConfigModel(string PropertyName, BindingSpec Binding);

    internal sealed record ParameterConfigModel(string Key, bool AsParameters, BindingSpec? Direct, EquatableArray<PropertyConfigModel> Properties);

    internal sealed record ConfigModel(string HandlerFqn, EquatableArray<ParameterConfigModel> Parameters, EquatableArray<DiagnosticInfo> Diagnostics);

    internal sealed record GeneratorOptions(bool UseRequestDelegate, bool PublishAot, bool EnableRdg, string RawPublishAot, string RawEnableRdg);
}
