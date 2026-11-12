using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Grpc.Generator;

// All models are value-equatable records (strings, enums, nested records and EquatableArray only)
// so incremental generator steps cache across unrelated edits.

internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(SyntaxNode? node)
    {
        if (node is null)
            return null;
        var loc = node.GetLocation();
        if (loc.SourceTree is null)
            return null;
        return new LocationInfo(loc.SourceTree.FilePath, loc.SourceSpan, loc.GetLineSpan().Span);
    }
}

internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Args)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] args)
        => new(descriptor.Id, location, args.ToEquatableArray());

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Diagnostics.ById(Id), Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            Args.Select(a => (object)a).ToArray());
}

internal enum ValueKind
{
    None = 0,
    Bool,
    Int32,
    Int64,
    UInt32,
    UInt64,
    Float,
    Double,
    String,
    Bytes,
    Char,
    Enum,
    Guid,
    Decimal,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeSpan,
    TimeOnly,
    Message,
}

/// <summary>A single (non-collection) protobuf value.</summary>
/// <param name="Kind">Wire/value category.</param>
/// <param name="ClrType">Fully qualified CLR type (global::…, keywords for primitives), without nullable suffix.</param>
/// <param name="RefKey">Message / enum key (same as ClrType) when Kind is Message / Enum.</param>
/// <param name="IsValueType">True for structs (relevant for messages and nullability).</param>
internal sealed record ValueInfo(ValueKind Kind, string ClrType, string RefKey, bool IsValueType);

internal enum FieldShape
{
    Single,
    Repeated,
    Map,
}

internal enum CollectionKind
{
    None,
    Array,
    List,               // List<T>
    IndexedInterface,   // IList<T>, IReadOnlyList<T>  -> List<T>
    EnumerableInterface,// IEnumerable<T>, ICollection<T>, IReadOnlyCollection<T> -> List<T>
    HashSet,            // HashSet<T>
    SetInterface,       // ISet<T>, IReadOnlySet<T> -> HashSet<T>
    ImmutableArray,
    ImmutableList,
    Dictionary,         // Dictionary<K,V>
    DictionaryInterface,// IDictionary<K,V>, IReadOnlyDictionary<K,V> -> Dictionary<K,V>
}

internal enum SetterKind
{
    None,
    Set,
    Init,
}

internal sealed record FieldModel(
    string Name,
    int Order,
    int? AttributeNumber,
    FieldShape Shape,
    ValueInfo Value,
    ValueInfo? MapKey,
    bool IsNullable,
    CollectionKind Collection,
    string PropertyClrType,
    SetterKind Setter,
    bool IsRequired,
    string? UnsupportedReason);

internal sealed record CtorParamModel(string Name, string MatchedProperty, bool HasDefault);

internal sealed record CtorModel(EquatableArray<CtorParamModel> Parameters, bool SetsRequiredMembers);

internal sealed record MessageModel(
    string Key,
    string ClrType,
    string BaseName,
    string Namespace,
    string OpenGenericKey,
    bool IsValueType,
    bool IsReferenceType,
    string? UnsupportedReason,
    EquatableArray<FieldModel> Fields,
    EquatableArray<CtorModel> Constructors);

internal sealed record EnumMemberModel(string Name, string Value);

internal sealed record EnumModel(
    string Key,
    string ClrType,
    string BaseName,
    string Namespace,
    EquatableArray<EnumMemberModel> Members,
    string? OutOfRangeMember);

internal enum RpcKind
{
    Unary,
    ClientStreaming,
    ServerStreaming,
    DuplexStreaming,
}

internal enum HandlerReturnKind
{
    Task,           // Task<T>
    ValueTask,      // ValueTask<T>
    Sync,           // T
    TaskVoid,       // Task
    ValueTaskVoid,  // ValueTask
    Void,           // void
    AsyncEnumerable // IAsyncEnumerable<T>
}

internal enum BindingParseKind
{
    String,
    Int32,
    Int64,
    Int16,
    Byte,
    SByte,
    UInt16,
    UInt32,
    UInt64,
    Single,
    Double,
    Decimal,
    Bool,
    Guid,
    DateTime,
    DateTimeOffset,
    TimeSpan,
    DateOnly,
    TimeOnly,
    Char,
    Enum,
}

internal sealed record BindingModel(
    string RequestKey,
    string Property,
    string HeaderKey,
    BindingParseKind Parse,
    string ClrType);

internal sealed record ConventionModel(
    string Namespace,
    EquatableArray<string> RootUsings,
    EquatableArray<string> NamespaceUsings,
    string Chain,
    string UniqueId);

internal sealed record EndpointModel(
    string Package,
    string Service,
    string Method,
    RpcKind Kind,
    string HandlerType,
    string RequestType,
    string ResponseType,
    HandlerReturnKind ReturnKind,
    bool HasRequestParameter,
    bool PassesCancellationToken,
    EquatableArray<MessageModel> Messages,
    EquatableArray<EnumModel> Enums,
    EquatableArray<string> InlineInterceptors,
    EquatableArray<BindingModel> InlineBindings,
    string? ExplicitConfigType,
    ConventionModel? Conventions,
    LocationInfo? Location)
{
    public string ServiceFullName => Package + "." + Service;
}

internal sealed record CallSiteResult(EndpointModel? Endpoint, EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record ConfiguredField(string Name, int Number, LocationInfo? Location);

internal sealed record FactoryModel(string MethodName, string? DeclaringType, EquatableArray<string> ParameterNames);

internal sealed record ProtoTypeConfigModel(
    string ConfigType,
    string TargetKey,
    bool IsOpenGeneric,
    EquatableArray<ConfiguredField> Fields,
    FactoryModel? Factory,
    LocationInfo? Location);

internal sealed record EndpointConfigModel(
    string ConfigType,
    string HandlerKey,
    EquatableArray<string> Interceptors,
    EquatableArray<BindingModel> Bindings);

internal sealed record ConfigResult(
    EquatableArray<ProtoTypeConfigModel> ProtoConfigs,
    EquatableArray<EndpointConfigModel> EndpointConfigs,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record AssemblyInfo(string AssemblyName, bool ReferencesRuntime, EquatableArray<string> ReferencedRegistrations);
