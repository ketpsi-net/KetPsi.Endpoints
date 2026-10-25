using System.Globalization;

using Microsoft.CodeAnalysis;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Symbol → model conversion for message / enum graphs. Runs inside incremental transforms so the
/// output stage only deals with value-equatable data.
/// </summary>
internal sealed class TypeGraphBuilder
{
    private readonly Compilation _compilation;
    private readonly Dictionary<string, MessageModel?> _messages = new(StringComparer.Ordinal);
    private readonly List<string> _messageOrder = new();
    private readonly Dictionary<string, EnumModel> _enums = new(StringComparer.Ordinal);

    public TypeGraphBuilder(Compilation compilation) => _compilation = compilation;

    public IEnumerable<MessageModel> Messages => _messageOrder.Select(k => _messages[k]).OfType<MessageModel>();
    public IEnumerable<EnumModel> Enums => _enums.Values.OrderBy(e => e.Key, StringComparer.Ordinal);

    public static string Fqn(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>Adds a request/response root type. Returns an error message when it cannot be a message.</summary>
    public string? AddRoot(ITypeSymbol type, out string key)
    {
        key = Fqn(type);
        var error = MessageTypeError(type);
        if (error is not null)
            return error;
        key = AddMessage((INamedTypeSymbol)type);
        return null;
    }

    public static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

    public static bool IsNullableValueType(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private bool IsAccessible(ISymbol symbol) => _compilation.IsSymbolAccessibleWithin(symbol, _compilation.Assembly);

    public static string? MessageTypeError(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return "only classes, records and structs can be messages";
        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return "only classes, records and structs can be messages";
        if (named.IsAbstract || named.IsStatic)
            return "abstract and static types cannot be messages";
        if (named.IsTupleType || named.IsAnonymousType)
            return "tuples and anonymous types are not supported";
        if (named.IsUnboundGenericType || ContainsTypeParameter(named))
            return "open generic types are not supported";
        if (named.SpecialType != SpecialType.None)
            return "primitive and framework types cannot be messages (wrap them in a DTO)";
        var ns = named.ContainingNamespace?.ToDisplayString() ?? "";
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
            || ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal))
            return "framework types cannot be messages (wrap them in a DTO)";
        return null;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;
            case IArrayTypeSymbol a:
                return ContainsTypeParameter(a.ElementType);
            case INamedTypeSymbol n:
                foreach (var arg in n.TypeArguments)
                {
                    if (ContainsTypeParameter(arg))
                        return true;
                }
                return n.ContainingType is not null && ContainsTypeParameter(n.ContainingType);
            default:
                return false;
        }
    }

    public static string BaseName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol a:
                return BaseName(a.ElementType) + "Array";
            case INamedTypeSymbol n:
            {
                var prefix = n.ContainingType is not null ? BaseName(n.ContainingType) + "_" : "";
                var name = n.Name;
                if (n.TypeArguments.Length > 0)
                    name += "_" + string.Join("_", n.TypeArguments.Select(BaseName));
                return prefix + name;
            }
            default:
                return type.Name;
        }
    }

    public static string NamespaceOf(ITypeSymbol type)
        => type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";

    private string AddMessage(INamedTypeSymbol type)
    {
        var key = Fqn(type);
        if (_messages.ContainsKey(key))
            return key;
        _messages[key] = null; // recursion guard
        _messageOrder.Add(key);

        var props = CollectProperties(type);
        var fields = new List<FieldModel>();
        for (var i = 0; i < props.Count; i++)
            fields.Add(CreateField(props[i], i));

        var ctors = new List<CtorModel>();
        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.IsStatic || !IsAccessible(ctor))
                continue;
            if (ctor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, type))
                continue; // copy constructor
            var ps = new List<CtorParamModel>();
            foreach (var p in ctor.Parameters)
            {
                var match = props.FirstOrDefault(pp =>
                    string.Equals(pp.Name, p.Name, StringComparison.OrdinalIgnoreCase)
                    && SymbolEqualityComparer.Default.Equals(pp.Type, p.Type));
                ps.Add(new CtorParamModel(p.Name, match?.Name ?? "", p.HasExplicitDefaultValue || p.IsParams));
            }
            var setsRequired = ctor.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
            ctors.Add(new CtorModel(ps.ToEquatableArray(), setsRequired));
        }

        var openKey = type.IsGenericType ? Fqn(type.OriginalDefinition) : "";
        _messages[key] = new MessageModel(
            key,
            key,
            BaseName(type),
            NamespaceOf(type),
            openKey,
            type.IsValueType,
            type.IsReferenceType,
            null,
            fields.ToEquatableArray(),
            ctors.ToEquatableArray());
        return key;
    }

    private List<IPropertySymbol> CollectProperties(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        var ordered = new List<IPropertySymbol>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in chain)
        {
            foreach (var p in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (p.IsStatic || p.IsIndexer || p.DeclaredAccessibility != Accessibility.Public
                    || p.GetMethod is null || !IsAccessible(p.GetMethod))
                    continue;
                if (index.TryGetValue(p.Name, out var i))
                    ordered[i] = p; // override / new: most-derived wins, base position kept
                else
                {
                    index[p.Name] = ordered.Count;
                    ordered.Add(p);
                }
            }
        }
        return ordered;
    }

    private FieldModel CreateField(IPropertySymbol prop, int order)
    {
        var declared = prop.Type;
        var setter = SetterKind.None;
        if (prop.SetMethod is { } set && IsAccessible(set))
            setter = set.IsInitOnly ? SetterKind.Init : SetterKind.Set;
        var attrNumber = TryGetProtoMemberNumber(prop);
        var propClr = Fqn(declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        var nullable = IsNullableValueType(declared)
                       || (!declared.IsValueType && prop.NullableAnnotation == NullableAnnotation.Annotated);
        var underlying = UnwrapNullable(declared);

        FieldModel Unsupported(string reason)
            => new(prop.Name, order, attrNumber, FieldShape.Single, new ValueInfo(ValueKind.None, Fqn(underlying), "", underlying.IsValueType),
                null, nullable, CollectionKind.None, propClr, setter, prop.IsRequired, reason);

        if (underlying.SpecialType == SpecialType.System_String || IsByteArray(underlying))
        {
            var v = Classify(underlying, out var err)!;
            return new FieldModel(prop.Name, order, attrNumber, FieldShape.Single, v, null, nullable,
                CollectionKind.None, propClr, setter, prop.IsRequired, err);
        }

        if (TryGetMap(underlying, out var keyType, out var valueType, out var mapKind, out var mapError))
        {
            if (mapError is not null)
                return Unsupported(mapError);
            if (IsNullableValueType(valueType))
                return Unsupported("map values cannot be Nullable<T> (protobuf has no null map values)");
            if (IsCollection(valueType) || IsCollection(keyType))
                return Unsupported("nested collections are not supported in protobuf maps");
            var k = Classify(keyType, out var kErr);
            if (k is null)
                return Unsupported("map key: " + kErr);
            var v = Classify(valueType, out var vErr);
            if (v is null)
                return Unsupported("map value: " + vErr);
            return new FieldModel(prop.Name, order, attrNumber, FieldShape.Map, v, k, true, mapKind, propClr,
                setter, prop.IsRequired, null);
        }

        if (TryGetRepeated(underlying, out var elemType, out var collKind, out var collError))
        {
            if (collError is not null)
                return Unsupported(collError);
            if (IsNullableValueType(elemType))
                return Unsupported("repeated Nullable<T> elements are not supported (protobuf has no null elements)");
            if (IsCollection(elemType))
                return Unsupported("nested collections are not supported (wrap the inner collection in a message)");
            var e = Classify(elemType, out var eErr);
            if (e is null)
                return Unsupported(eErr!);
            return new FieldModel(prop.Name, order, attrNumber, FieldShape.Repeated, e, null, true, collKind, propClr,
                setter, prop.IsRequired, null);
        }

        var value = Classify(underlying, out var error);
        if (value is null)
            return Unsupported(error!);
        return new FieldModel(prop.Name, order, attrNumber, FieldShape.Single, value, null, nullable,
            CollectionKind.None, propClr, setter, prop.IsRequired, null);
    }

    private static bool IsByteArray(ITypeSymbol t)
        => t is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };

    private static bool IsCollection(ITypeSymbol t)
        => !IsByteArray(t) && t.SpecialType != SpecialType.System_String
           && (t is IArrayTypeSymbol || TryGetMap(UnwrapNullable(t), out _, out _, out _, out _) || TryGetRepeated(UnwrapNullable(t), out _, out _, out _));

    private static string DefName(INamedTypeSymbol n) => n.OriginalDefinition.ToDisplayString();

    private static bool TryGetMap(ITypeSymbol type, out ITypeSymbol key, out ITypeSymbol value, out CollectionKind kind, out string? error)
    {
        key = value = type;
        kind = CollectionKind.None;
        error = null;
        if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 2 } n)
            return false;
        switch (DefName(n))
        {
            case "System.Collections.Generic.Dictionary<TKey, TValue>":
                kind = CollectionKind.Dictionary;
                break;
            case "System.Collections.Generic.IDictionary<TKey, TValue>":
            case "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                kind = CollectionKind.DictionaryInterface;
                break;
            case "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>":
            case "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>":
            case "System.Collections.Immutable.IImmutableDictionary<TKey, TValue>":
            case "System.Collections.Generic.SortedDictionary<TKey, TValue>":
            case "System.Collections.Frozen.FrozenDictionary<TKey, TValue>":
                error = $"dictionary type '{n.ToDisplayString()}' is not supported; use Dictionary<,>, IDictionary<,> or IReadOnlyDictionary<,>";
                break;
            default:
                return false;
        }
        key = n.TypeArguments[0];
        value = n.TypeArguments[1];
        return true;
    }

    private static bool TryGetRepeated(ITypeSymbol type, out ITypeSymbol element, out CollectionKind kind, out string? error)
    {
        element = type;
        kind = CollectionKind.None;
        error = null;
        if (type is IArrayTypeSymbol arr)
        {
            if (arr.Rank != 1)
            {
                error = "multi-dimensional arrays are not supported";
                return true;
            }
            if (arr.ElementType.SpecialType == SpecialType.System_Byte)
                return false;
            element = arr.ElementType;
            kind = CollectionKind.Array;
            return true;
        }
        if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } n)
            return false;
        switch (DefName(n))
        {
            case "System.Collections.Generic.List<T>":
                kind = CollectionKind.List;
                break;
            case "System.Collections.Generic.IList<T>":
            case "System.Collections.Generic.IReadOnlyList<T>":
                kind = CollectionKind.IndexedInterface;
                break;
            case "System.Collections.Generic.IEnumerable<T>":
            case "System.Collections.Generic.ICollection<T>":
            case "System.Collections.Generic.IReadOnlyCollection<T>":
                kind = CollectionKind.EnumerableInterface;
                break;
            case "System.Collections.Generic.HashSet<T>":
                kind = CollectionKind.HashSet;
                break;
            case "System.Collections.Generic.ISet<T>":
            case "System.Collections.Generic.IReadOnlySet<T>":
                kind = CollectionKind.SetInterface;
                break;
            case "System.Collections.Immutable.ImmutableArray<T>":
                kind = CollectionKind.ImmutableArray;
                break;
            case "System.Collections.Immutable.ImmutableList<T>":
                kind = CollectionKind.ImmutableList;
                break;
            default:
                if (n.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"))
                {
                    error = $"collection type '{n.ToDisplayString()}' is not supported; use T[], List<T>, IList<T>, IReadOnlyList<T>, IEnumerable<T>, HashSet<T> or ImmutableArray<T>";
                    return true;
                }
                return false;
        }
        element = n.TypeArguments[0];
        return true;
    }

    /// <summary>Classifies a single value type; returns null with an error for unsupported types.</summary>
    private ValueInfo? Classify(ITypeSymbol type, out string? error)
    {
        error = null;
        var clr = Fqn(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        if (type is ITypeParameterSymbol)
        {
            error = "generic type parameters are not supported";
            return null;
        }
        if (type.TypeKind == TypeKind.Enum)
        {
            AddEnum((INamedTypeSymbol)type);
            return new ValueInfo(ValueKind.Enum, clr, clr, true);
        }
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return new ValueInfo(ValueKind.Bool, clr, "", true);
            case SpecialType.System_Char: return new ValueInfo(ValueKind.Char, clr, "", true);
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_Int32: return new ValueInfo(ValueKind.Int32, clr, "", true);
            case SpecialType.System_Byte:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32: return new ValueInfo(ValueKind.UInt32, clr, "", true);
            case SpecialType.System_Int64:
            case SpecialType.System_IntPtr: return new ValueInfo(ValueKind.Int64, clr, "", true);
            case SpecialType.System_UInt64:
            case SpecialType.System_UIntPtr: return new ValueInfo(ValueKind.UInt64, clr, "", true);
            case SpecialType.System_Single: return new ValueInfo(ValueKind.Float, clr, "", true);
            case SpecialType.System_Double: return new ValueInfo(ValueKind.Double, clr, "", true);
            case SpecialType.System_Decimal: return new ValueInfo(ValueKind.Decimal, clr, "", true);
            case SpecialType.System_String: return new ValueInfo(ValueKind.String, clr, "", false);
            case SpecialType.System_DateTime: return new ValueInfo(ValueKind.DateTime, clr, "", true);
            case SpecialType.System_Object:
                error = "'object' is not supported";
                return null;
        }
        if (IsByteArray(type))
            return new ValueInfo(ValueKind.Bytes, "byte[]", "", false);

        switch (type.ToDisplayString())
        {
            case "System.Guid": return new ValueInfo(ValueKind.Guid, clr, "", true);
            case "System.DateTimeOffset": return new ValueInfo(ValueKind.DateTimeOffset, clr, "", true);
            case "System.TimeSpan": return new ValueInfo(ValueKind.TimeSpan, clr, "", true);
            case "System.DateOnly": return new ValueInfo(ValueKind.DateOnly, clr, "", true);
            case "System.TimeOnly": return new ValueInfo(ValueKind.TimeOnly, clr, "", true);
        }

        var msgError = MessageTypeError(type);
        if (msgError is not null)
        {
            error = $"type '{type.ToDisplayString()}' is not supported ({msgError})";
            return null;
        }
        var key = AddMessage((INamedTypeSymbol)type);
        return new ValueInfo(ValueKind.Message, key, key, type.IsValueType);
    }

    private void AddEnum(INamedTypeSymbol type)
    {
        var key = Fqn(type);
        if (_enums.ContainsKey(key))
            return;
        var members = new List<EnumMemberModel>();
        string? outOfRange = null;
        foreach (var f in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!f.IsStatic || !f.HasConstantValue || f.ConstantValue is null)
                continue;
            long value;
            switch (f.ConstantValue)
            {
                case ulong ul:
                    if (ul > int.MaxValue)
                    {
                        outOfRange ??= f.Name;
                        continue;
                    }
                    value = (long)ul;
                    break;
                case uint ui: value = ui; break;
                case long l: value = l; break;
                case int i: value = i; break;
                case short s: value = s; break;
                case ushort us: value = us; break;
                case byte b: value = b; break;
                case sbyte sb: value = sb; break;
                default: continue;
            }
            if (value is > int.MaxValue or < int.MinValue)
            {
                outOfRange ??= f.Name;
                continue;
            }
            members.Add(new EnumMemberModel(f.Name, value.ToString(CultureInfo.InvariantCulture)));
        }
        _enums[key] = new EnumModel(key, key, BaseName(type), NamespaceOf(type), members.ToEquatableArray(), outOfRange);
    }

    private static int? TryGetProtoMemberNumber(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("ProtoMemberAttribute" or "ProtoMember"))
                continue;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is { } v)
            {
                try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
                catch (OverflowException) { return -1; }
            }
        }
        return null;
    }
}
