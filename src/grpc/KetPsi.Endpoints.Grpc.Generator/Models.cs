using System.Collections.Immutable;

namespace KetPsi.Endpoints.Grpc.Generator;

internal sealed class TypeConfigModel
{
    public string ConfigClassName { get; }
    public string TargetTypeName { get; }
    /// <summary>Property name → field number from Configure(... Property(x => x.Foo, n)).</summary>
    public System.Collections.Immutable.ImmutableArray<(string Name, int Number)> Fields { get; }
    /// <summary>Property name → metadata key from Configure(... FromMetadata(x => x.Foo, "key")).</summary>
    public System.Collections.Immutable.ImmutableArray<(string Name, string Source, string Key, string ClrTypeHint)> MetadataBindings { get; }
    /// <summary>Fully-qualified interceptor type names from AddInterceptor&lt;T&gt;().</summary>
    public System.Collections.Immutable.ImmutableArray<string> Interceptors { get; }

    public TypeConfigModel(
        string configClassName,
        string targetTypeName,
        System.Collections.Immutable.ImmutableArray<(string Name, int Number)> fields = default,
        System.Collections.Immutable.ImmutableArray<(string Name, string Source, string Key, string ClrTypeHint)> metadataBindings = default,
        System.Collections.Immutable.ImmutableArray<string> interceptors = default)
    {
        ConfigClassName = configClassName;
        TargetTypeName = targetTypeName;
        Fields = fields.IsDefault ? System.Collections.Immutable.ImmutableArray<(string, int)>.Empty : fields;
        MetadataBindings = metadataBindings.IsDefault
            ? System.Collections.Immutable.ImmutableArray<(string, string, string, string)>.Empty
            : metadataBindings;
        Interceptors = interceptors.IsDefault
            ? System.Collections.Immutable.ImmutableArray<string>.Empty
            : interceptors;
    }
}

internal enum FieldKind
{
    Unknown = 0,
    Int32,
    Int64,
    UInt32,
    UInt64,
    Bool,
    Float,
    Double,
    String,
    Bytes,
    Guid,
    DateTime,
    TimeSpan,
    Decimal,
    Enum,
    Message,
    Repeated,
    Map
}

internal sealed class ProtoFieldModel
{
    public string Name { get; }
    public int Number { get; }
    public string ProtoType { get; }
    /// <summary>Full CLR type for codegen casts (e.g. byte, sbyte, char, int[], List&lt;int&gt;).</summary>
    public string ClrTypeHint { get; }
    public bool IsNullable { get; }
    public FieldKind Kind { get; }
    public string ElementProtoType { get; }
    public string ElementClrHint { get; }
    public FieldKind ElementKind { get; }
    /// <summary>True when the property is T[] (not List/IList).</summary>
    public bool IsArray { get; }
    public string MapKeyProtoType { get; }
    public string MapKeyClrHint { get; }
    public FieldKind MapKeyKind { get; }

    public ProtoFieldModel(
        string name,
        int number,
        string protoType,
        string clrTypeHint = "",
        bool isNullable = false,
        FieldKind kind = FieldKind.Unknown,
        string elementProtoType = "",
        string elementClrHint = "",
        FieldKind elementKind = FieldKind.Unknown,
        bool isArray = false,
        string mapKeyProtoType = "",
        string mapKeyClrHint = "",
        FieldKind mapKeyKind = FieldKind.Unknown)
    {
        Name = name;
        Number = number;
        ProtoType = protoType;
        ClrTypeHint = clrTypeHint ?? "";
        IsNullable = isNullable;
        Kind = kind;
        ElementProtoType = elementProtoType ?? "";
        ElementClrHint = elementClrHint ?? "";
        ElementKind = elementKind;
        IsArray = isArray;
        MapKeyProtoType = mapKeyProtoType ?? "";
        MapKeyClrHint = mapKeyClrHint ?? "";
        MapKeyKind = mapKeyKind;
    }
}

internal sealed class EnumDefinitionModel
{
    public string Name { get; }
    public string ClrTypeName { get; }
    public System.Collections.Immutable.ImmutableArray<(string Name, int Value)> Members { get; }

    public EnumDefinitionModel(string name, string clrTypeName, System.Collections.Immutable.ImmutableArray<(string Name, int Value)> members)
    {
        Name = name;
        ClrTypeName = clrTypeName;
        Members = members;
    }
}

internal sealed class NestedMessageModel
{
    public string ClrTypeName { get; }
    public string MessageName { get; }
    public System.Collections.Immutable.ImmutableArray<ProtoFieldModel> Fields { get; }
    public bool IsReferenceType { get; }

    public NestedMessageModel(string clrTypeName, string messageName, System.Collections.Immutable.ImmutableArray<ProtoFieldModel> fields, bool isReferenceType)
    {
        ClrTypeName = clrTypeName;
        MessageName = messageName;
        Fields = fields;
        IsReferenceType = isReferenceType;
    }
}

internal sealed class GrpcEndpointModel
{
    public string HandlerName { get; }
    public string HandlerFullyQualifiedName { get; }
    public string RequestTypeName { get; }
    public string ResponseTypeName { get; }
    public string GrpcRoute { get; }
    public string MethodName { get; }
    public bool IsStream { get; }
    public bool AcceptsCancellationToken { get; }
    public ImmutableArray<string> ClosedGenerics { get; }

    public string PackageName { get; }
    public string ServiceNameOnly { get; }
    public string RequestMessageName { get; }
    public string ResponseMessageName { get; }
    public ImmutableArray<ProtoFieldModel> RequestFields { get; }
    public ImmutableArray<ProtoFieldModel> ResponseFields { get; }
    public ImmutableArray<NestedMessageModel> NestedMessages { get; }
    public ImmutableArray<EnumDefinitionModel> Enums { get; }

    public string FinalGrpcRoute => GrpcRoute;

    /// <summary>
    /// Used by the proxy/contract generator.
    /// </summary>
    public GrpcEndpointModel(
        string handlerName,
        string handlerFullyQualifiedName,
        string requestTypeName,
        string responseTypeName,
        string grpcRoute,
        string methodName,
        bool acceptsCancellationToken)
    {
        HandlerName = handlerName;
        HandlerFullyQualifiedName = handlerFullyQualifiedName;
        RequestTypeName = requestTypeName;
        ResponseTypeName = responseTypeName;
        GrpcRoute = grpcRoute;
        MethodName = methodName;
        AcceptsCancellationToken = acceptsCancellationToken;
        IsStream = false;
        ClosedGenerics = ImmutableArray<string>.Empty;
        PackageName = "";
        ServiceNameOnly = "";
        RequestMessageName = SimpleTypeName(requestTypeName);
        ResponseMessageName = SimpleTypeName(UnwrapTask(responseTypeName));
        RequestFields = ImmutableArray<ProtoFieldModel>.Empty;
        ResponseFields = ImmutableArray<ProtoFieldModel>.Empty;
        NestedMessages = ImmutableArray<NestedMessageModel>.Empty;
        Enums = ImmutableArray<EnumDefinitionModel>.Empty;
    }

    /// <summary>
    /// Used by the registration generator (includes schema field data).
    /// </summary>
    public GrpcEndpointModel(
        string handlerName,
        string handlerFullyQualifiedName,
        string requestTypeName,
        string responseTypeName,
        string grpcRoute,
        string methodName,
        bool isStream,
        ImmutableArray<string> closedGenerics,
        string packageName,
        string serviceNameOnly,
        string requestMessageName,
        string responseMessageName,
        ImmutableArray<ProtoFieldModel> requestFields,
        ImmutableArray<ProtoFieldModel> responseFields,
        ImmutableArray<NestedMessageModel> nestedMessages = default,
        ImmutableArray<EnumDefinitionModel> enums = default)
    {
        HandlerName = handlerName;
        HandlerFullyQualifiedName = handlerFullyQualifiedName;
        RequestTypeName = requestTypeName;
        ResponseTypeName = responseTypeName;
        GrpcRoute = grpcRoute;
        MethodName = methodName;
        IsStream = isStream;
        ClosedGenerics = closedGenerics;
        AcceptsCancellationToken = false;
        PackageName = packageName;
        ServiceNameOnly = serviceNameOnly;
        RequestMessageName = requestMessageName;
        ResponseMessageName = responseMessageName;
        RequestFields = requestFields;
        ResponseFields = responseFields;
        NestedMessages = nestedMessages.IsDefault ? ImmutableArray<NestedMessageModel>.Empty : nestedMessages;
        Enums = enums.IsDefault ? ImmutableArray<EnumDefinitionModel>.Empty : enums;
    }

    private static string UnwrapTask(string typeName)
    {
        // Task<T> / ValueTask<T> / global::System.Threading.Tasks.Task<T>
        var s = typeName;
        const string taskPrefix = "System.Threading.Tasks.Task<";
        const string valueTaskPrefix = "System.Threading.Tasks.ValueTask<";
        const string globalTask = "global::System.Threading.Tasks.Task<";
        const string globalValueTask = "global::System.Threading.Tasks.ValueTask<";

        foreach (var prefix in new[] { globalTask, globalValueTask, taskPrefix, valueTaskPrefix })
        {
            var idx = s.IndexOf(prefix, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = idx + prefix.Length;
                var end = s.LastIndexOf('>');
                if (end > start)
                    return s.Substring(start, end - start).Trim();
            }
        }

        return s;
    }

    private static string SimpleTypeName(string fullyQualified)
    {
        var s = UnwrapTask(fullyQualified);
        var lastDot = s.LastIndexOf('.');
        var name = lastDot >= 0 ? s.Substring(lastDot + 1) : s;
        var generic = name.IndexOf('<');
        if (generic >= 0)
            name = name.Substring(0, generic);
        return name.Replace("+", "_");
    }
}
