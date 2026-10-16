using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Build-time .proto text matching what SchemaGenerator would emit,
/// without any runtime reflection.
/// </summary>
internal static class ProtoSchemaEmitter
{
    /// <summary>
    /// Prefer field numbers from <see cref="TypeConfigModel"/> (IProtoTypeConfiguration.Property maps)
    /// over [ProtoMember] / sequential defaults when building /proto schema.
    /// </summary>
    public static System.Collections.Immutable.ImmutableArray<GrpcEndpointModel> ApplyTypeConfigFields(
        System.Collections.Immutable.ImmutableArray<GrpcEndpointModel> endpoints,
        System.Collections.Immutable.ImmutableArray<TypeConfigModel> typeConfigs)
    {
        if (typeConfigs.IsDefaultOrEmpty || endpoints.IsDefaultOrEmpty)
            return endpoints;

        var byTarget = new Dictionary<string, TypeConfigModel>(StringComparer.Ordinal);
        foreach (var c in typeConfigs)
        {
            if (c.TargetTypeName == "IOpenProtoTypeConfiguration" || c.Fields.IsDefaultOrEmpty)
                continue;
            // Key by simple name and full name
            byTarget[c.TargetTypeName] = c;
            var simple = c.TargetTypeName;
            var last = simple.LastIndexOf('.');
            if (last >= 0)
                simple = simple.Substring(last + 1);
            byTarget[simple] = c;
        }

        var list = new List<GrpcEndpointModel>();
        foreach (var ep in endpoints)
        {
            var reqFields = MergeFields(ep.RequestTypeName, ep.RequestMessageName, ep.RequestFields, byTarget);
            var resFields = MergeFields(ep.ResponseTypeName, ep.ResponseMessageName, ep.ResponseFields, byTarget);
            list.Add(new GrpcEndpointModel(
                ep.HandlerName,
                ep.HandlerFullyQualifiedName,
                ep.RequestTypeName,
                ep.ResponseTypeName,
                ep.GrpcRoute,
                ep.MethodName,
                ep.IsStream,
                ep.ClosedGenerics,
                ep.PackageName,
                ep.ServiceNameOnly,
                ep.RequestMessageName,
                ep.ResponseMessageName,
                reqFields,
                resFields,
                ep.NestedMessages,
                ep.Enums));
        }

        return list.ToImmutableArray();
    }

    private static System.Collections.Immutable.ImmutableArray<ProtoFieldModel> MergeFields(
        string typeName,
        string messageName,
        System.Collections.Immutable.ImmutableArray<ProtoFieldModel> existing,
        Dictionary<string, TypeConfigModel> byTarget)
    {
        TypeConfigModel? config = null;
        if (!string.IsNullOrEmpty(typeName) && byTarget.TryGetValue(typeName, out var c1))
            config = c1;
        else if (!string.IsNullOrEmpty(messageName) && byTarget.TryGetValue(messageName, out var c2))
            config = c2;
        else
        {
            var simple = typeName;
            var idx = simple.LastIndexOf('.');
            if (idx >= 0) simple = simple.Substring(idx + 1);
            // strip Task<>
            var lt = simple.IndexOf('<');
            if (lt >= 0) simple = simple.Substring(0, lt);
            if (byTarget.TryGetValue(simple, out var c3))
                config = c3;
        }

        if (config is null)
            return existing;

        // Strip FromMetadata properties from the wire/schema field list
        if (!config.MetadataBindings.IsDefaultOrEmpty)
        {
            var metaNames = new HashSet<string>(config.MetadataBindings.Select(m => m.Name), StringComparer.Ordinal);
            if (!existing.IsDefaultOrEmpty)
                existing = existing.Where(f => !metaNames.Contains(f.Name)).ToImmutableArray();
            if (config.Fields.IsDefaultOrEmpty)
                return existing;
        }

        if (config.Fields.IsDefaultOrEmpty)
            return existing;

        // Rebuild field list: keep proto type from existing props when names match
        var byName = new Dictionary<string, ProtoFieldModel>(StringComparer.Ordinal);
        if (!existing.IsDefaultOrEmpty)
        {
            foreach (var f in existing)
                byName[f.Name] = f;
        }

        var result = new List<ProtoFieldModel>();
        foreach (var (name, number) in config.Fields)
        {
            if (byName.TryGetValue(name, out var prev))
                result.Add(new ProtoFieldModel(name, number, prev.ProtoType, prev.ClrTypeHint, prev.IsNullable, prev.Kind, prev.ElementProtoType, prev.ElementClrHint, prev.ElementKind, prev.IsArray, prev.MapKeyProtoType, prev.MapKeyClrHint, prev.MapKeyKind));
            else
                result.Add(new ProtoFieldModel(name, number, "string", ""));
        }
        return result.ToImmutableArray();
    }

    public static string Emit(IEnumerable<GrpcEndpointModel> endpoints)
    {
        var list = endpoints.ToList();
        if (list.Count == 0)
            return "syntax = \"proto3\";\n";

        // message name -> fields
        var messages = new Dictionary<string, List<ProtoFieldModel>>(StringComparer.Ordinal);
        var enums = new Dictionary<string, EnumDefinitionModel>(StringComparer.Ordinal);

        void AddMessage(string name, System.Collections.Immutable.ImmutableArray<ProtoFieldModel> fields)
        {
            if (string.IsNullOrEmpty(name) || fields.IsDefaultOrEmpty)
                return;
            if (!messages.ContainsKey(name))
                messages[name] = fields.ToList();
        }

        foreach (var ep in list)
        {
            AddMessage(ep.RequestMessageName, ep.RequestFields);
            AddMessage(ep.ResponseMessageName, ep.ResponseFields);
            if (!ep.NestedMessages.IsDefaultOrEmpty)
            {
                foreach (var n in ep.NestedMessages)
                    AddMessage(n.MessageName, n.Fields);
            }
            if (!ep.Enums.IsDefaultOrEmpty)
            {
                foreach (var e in ep.Enums)
                    enums[e.Name] = e;
            }
        }

        // Collect enum names referenced by fields (in case Enums list missed some)
        foreach (var fields in messages.Values)
        {
            foreach (var f in fields)
            {
                if (f.Kind == FieldKind.Enum && !string.IsNullOrEmpty(f.ClrTypeHint))
                {
                    var simple = f.ProtoType; // should be enum type name
                    // ensure proto type is enum name not int32
                }
            }
        }

        var packages = list.Select(e => e.PackageName).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal).ToList();
        var package = packages.Count == 1 ? packages[0] : "";

        var services = list
            .GroupBy(e => (e.PackageName, e.ServiceNameOnly), e => e)
            .OrderBy(g => g.Key.PackageName)
            .ThenBy(g => g.Key.ServiceNameOnly);

        var sb = new StringBuilder();
        sb.AppendLine("syntax = \"proto3\";");
        sb.AppendLine();

        // Imports for Well-Known Types (MS docs)
        var needsTimestamp = false;
        var needsDuration = false;
        var needsWrappers = false;
        var needsDecimal = false;
        void ScanFields(System.Collections.Generic.IEnumerable<ProtoFieldModel> fields)
        {
            foreach (var f in fields)
            {
                if (f.Kind == FieldKind.DateTime || f.ElementKind == FieldKind.DateTime) needsTimestamp = true;
                if (f.Kind == FieldKind.TimeSpan || f.ElementKind == FieldKind.TimeSpan) needsDuration = true;
                if (f.Kind == FieldKind.Decimal || f.ElementKind == FieldKind.Decimal) needsDecimal = true;
                if (f.IsNullable && f.Kind is FieldKind.Int32 or FieldKind.Int64 or FieldKind.UInt32
                    or FieldKind.UInt64 or FieldKind.Bool or FieldKind.Float or FieldKind.Double)
                    needsWrappers = true;
                if (f.Kind == FieldKind.Repeated)
                    ScanFields(new[] { new ProtoFieldModel(f.Name, f.Number, f.ElementProtoType, f.ElementClrHint, false, f.ElementKind) });
            }
        }
        foreach (var fields in messages.Values)
            ScanFields(fields);

        if (needsTimestamp) sb.AppendLine("import \"google/protobuf/timestamp.proto\";");
        if (needsDuration) sb.AppendLine("import \"google/protobuf/duration.proto\";");
        if (needsWrappers) sb.AppendLine("import \"google/protobuf/wrappers.proto\";");
        if (needsTimestamp || needsDuration || needsWrappers)
            sb.AppendLine();

        if (!string.IsNullOrEmpty(package))
        {
            sb.Append("package ").Append(SanitizeProtoIdent(package)).AppendLine(";");
            sb.AppendLine();
        }

        // Custom DecimalValue (MS docs — Protobuf has no native decimal)
        if (needsDecimal)
        {
            sb.AppendLine("// Per Microsoft gRPC protobuf guidance for .NET decimal");
            sb.AppendLine("message DecimalValue {");
            sb.AppendLine("   int64 units = 1;");
            sb.AppendLine("   sfixed32 nanos = 2;");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Enums first
        foreach (var e in enums.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            sb.Append("enum ").Append(SanitizeProtoIdent(e.Name)).AppendLine(" {");
            // proto3 requires first value to be 0
            var members = e.Members.OrderBy(m => m.Value).ToList();
            if (members.Count == 0)
                sb.Append("   ").Append(SanitizeProtoIdent(e.Name)).Append("_UNSPECIFIED = 0;").AppendLine();
            foreach (var (name, value) in members)
                sb.Append("   ").Append(SanitizeProtoIdent(name)).Append(" = ").Append(value).AppendLine(";");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var msg in messages.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            sb.Append("message ").Append(SanitizeProtoIdent(msg.Key)).AppendLine(" {");
            foreach (var field in msg.Value.OrderBy(f => f.Number))
            {
                var protoType = ToSchemaProtoType(field);
                // proto3 optional matches presence tracking for nullable CLR value types (raw scalar wire)
                var optional = field.IsNullable && field.Kind is FieldKind.Int32 or FieldKind.Int64 or FieldKind.UInt32
                    or FieldKind.UInt64 or FieldKind.Bool or FieldKind.Float or FieldKind.Double or FieldKind.Enum
                    ? "optional " : "";
                sb.Append("   ").Append(optional).Append(protoType).Append(' ').Append(SanitizeProtoIdent(field.Name))
                    .Append(" = ").Append(field.Number).AppendLine(";");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var svc in services)
        {
            var serviceName = string.IsNullOrEmpty(svc.Key.ServiceNameOnly) ? "Service" : svc.Key.ServiceNameOnly;
            sb.Append("service ").Append(SanitizeProtoIdent(serviceName)).AppendLine(" {");

            foreach (var method in svc
                         .GroupBy(m => m.MethodName, StringComparer.Ordinal)
                         .Select(g => g.First())
                         .OrderBy(m => m.MethodName, StringComparer.Ordinal))
            {
                var req = string.IsNullOrEmpty(method.RequestMessageName) ? "google.protobuf.Empty" : SanitizeProtoIdent(method.RequestMessageName);
                var res = string.IsNullOrEmpty(method.ResponseMessageName) ? "google.protobuf.Empty" : SanitizeProtoIdent(method.ResponseMessageName);
                sb.Append("   rpc ").Append(SanitizeProtoIdent(method.MethodName)).Append(" (").Append(req)
                    .Append(") returns (").Append(res).AppendLine(");");
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Proto schema type for a field (valid proto3 type names only).
    /// </summary>
    private static string ToSchemaProtoType(ProtoFieldModel field)
    {
        // Nullable scalars use proto3 "optional" (same raw wire type as our codecs).
        // Do NOT emit google.protobuf.*Value wrappers — those are length-delimited submessages.

        switch (field.Kind)
        {
            case FieldKind.Repeated:
            {
                var elem = string.IsNullOrEmpty(field.ElementProtoType) ? "string" : field.ElementProtoType;
                if (field.ElementKind == FieldKind.Enum || field.ElementKind == FieldKind.Message)
                    elem = SanitizeProtoIdent(SimpleName(field.ElementClrHint, elem));
                else if (field.ElementKind == FieldKind.DateTime)
                    elem = "google.protobuf.Timestamp";
                else if (field.ElementKind == FieldKind.TimeSpan)
                    elem = "google.protobuf.Duration";
                else if (field.ElementKind == FieldKind.Decimal)
                    elem = "DecimalValue";
                else if (field.ElementKind == FieldKind.Guid)
                    elem = "bytes";
                else
                    elem = NormalizeScalarProto(elem);
                return "repeated " + elem;
            }
            case FieldKind.Enum:
                return SanitizeProtoIdent(string.IsNullOrEmpty(field.ProtoType) ? SimpleName(field.ClrTypeHint, "Enum") : field.ProtoType);
            case FieldKind.Map:
                return string.IsNullOrEmpty(field.ProtoType) ? "map<string, string>" : field.ProtoType;
            case FieldKind.Message:
                if (field.ProtoType.StartsWith("map<", StringComparison.Ordinal))
                    return field.ProtoType;
                return SanitizeProtoIdent(string.IsNullOrEmpty(field.ProtoType) ? field.ClrTypeHint : field.ProtoType);
            case FieldKind.DateTime:
                return "google.protobuf.Timestamp";
            case FieldKind.TimeSpan:
                return "google.protobuf.Duration";
            case FieldKind.Decimal:
                return "DecimalValue";
            case FieldKind.Guid:
                return "bytes";
            case FieldKind.String:
                return "string";
            case FieldKind.Bytes:
                return "bytes";
            case FieldKind.Int32:
                return "int32";
            case FieldKind.Int64:
                return "int64";
            case FieldKind.UInt32:
                return "uint32";
            case FieldKind.UInt64:
                return "uint64";
            case FieldKind.Bool:
                return "bool";
            case FieldKind.Float:
                return "float";
            case FieldKind.Double:
                return "double";
            default:
                return NormalizeScalarProto(field.ProtoType);
        }
    }

    private static string NormalizeScalarProto(string proto)
    {
        if (string.IsNullOrEmpty(proto)) return "string";
        proto = proto.Trim();
        return proto switch
        {
            "int32" or "int64" or "uint32" or "uint64" or "bool" or "float" or "double" or "string" or "bytes" => proto,
            "sint32" or "sint64" or "fixed32" or "fixed64" or "sfixed32" or "sfixed64" => proto,
            _ when proto.StartsWith("repeated ", StringComparison.Ordinal) => proto,
            _ => SanitizeProtoIdent(proto) // message / enum name
        };
    }

    private static string SimpleName(string clr, string fallback)
    {
        if (string.IsNullOrEmpty(clr)) return fallback;
        var idx = clr.LastIndexOf('.');
        var name = idx >= 0 ? clr.Substring(idx + 1) : clr;
        var plus = name.LastIndexOf('+');
        if (plus >= 0) name = name.Substring(plus + 1);
        var gen = name.IndexOf('<');
        if (gen >= 0) name = name.Substring(0, gen);
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    private static string SanitizeProtoIdent(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        // proto identifiers: letters, digits, underscore; must not start with digit
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        if (chars.Length > 0 && char.IsDigit(chars[0]))
            return "_" + new string(chars);
        return new string(chars);
    }

    public static (System.Collections.Immutable.ImmutableArray<NestedMessageModel> Nested, System.Collections.Immutable.ImmutableArray<EnumDefinitionModel> Enums) CollectTypeGraph(
        ITypeSymbol? request, ITypeSymbol? response)
    {
        var messages = new List<NestedMessageModel>();
        var enums = new Dictionary<string, EnumDefinitionModel>(StringComparer.Ordinal);
        var seenMsg = new HashSet<string>(StringComparer.Ordinal);

        void AddEnum(ITypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Enum)
                return;
            var key = type.ToDisplayString();
            if (enums.ContainsKey(key))
                return;
            var members = new List<(string Name, int Value)>();
            foreach (var m in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!m.IsStatic || !m.HasConstantValue)
                    continue;
                var val = m.ConstantValue is int i ? i
                    : m.ConstantValue is byte b ? b
                    : m.ConstantValue is short s ? s
                    : m.ConstantValue is long l ? (int)l
                    : Convert.ToInt32(m.ConstantValue);
                members.Add((m.Name, val));
            }
            enums[key] = new EnumDefinitionModel(type.Name.Replace("+", "_"), key, members.ToImmutableArray());
        }

        void Walk(ITypeSymbol? type)
        {
            type = UnwrapTask(type);
            if (type is null)
                return;
            type = UnwrapNullable(type);

            if (type.TypeKind == TypeKind.Enum)
            {
                AddEnum(type);
                return;
            }

            if (IsMap(type, out var mapKey, out var mapVal))
            {
                Walk(mapKey);
                Walk(mapVal);
                return;
            }

            if (IsRepeated(type, out var elem))
            {
                Walk(elem);
                return;
            }

            // Never treat BCL collection types as nested messages
            if (type is INamedTypeSymbol ncol)
            {
                var def = ncol.OriginalDefinition.ToDisplayString();
                if (def.StartsWith("System.Collections.", StringComparison.Ordinal) ||
                    def.StartsWith("System.Collections.Generic.", StringComparison.Ordinal) ||
                    def.StartsWith("System.Collections.Concurrent.", StringComparison.Ordinal) ||
                    def.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal))
                    return;
            }

            if (type is IArrayTypeSymbol arr)
            {
                if (arr.ElementType.SpecialType != SpecialType.System_Byte)
                    Walk(arr.ElementType);
                return;
            }

            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                return;
            if (type.SpecialType != SpecialType.None)
                return;
            // skip framework primitives / non-DTO BCL types (never emit message codecs for these)
            var full = type.ToDisplayString();
            if (IsNonMessageBclType(type, full))
                return;

            var key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "");
            if (!seenMsg.Add(key))
                return;

            var fields = ExtractFields(type);
            if (fields.Fields.Count == 0)
                return;

            messages.Add(new NestedMessageModel(
                key,
                MessageName(type),
                fields.Fields.ToImmutableArray(),
                type.IsReferenceType));

            foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic || prop.GetMethod is null)
                    continue;
                var pt = UnwrapNullable(prop.Type);
                if (pt.TypeKind == TypeKind.Enum)
                    AddEnum(pt);
                else
                    Walk(pt);
            }
        }

        Walk(request);
        Walk(response);
        return (messages.ToImmutableArray(), enums.Values.ToImmutableArray());
    }

    public static ImmutableArrayAdapter ExtractFields(ITypeSymbol? type)
    {
        type = UnwrapTask(type);
        if (type is null || type.SpecialType == SpecialType.System_Void)
            return ImmutableArrayAdapter.Empty;

        // Enum as a "message" is not used — enums are fields
        if (type.TypeKind == TypeKind.Enum)
            return ImmutableArrayAdapter.Empty;

        var props = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.GetMethod is not null && p.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        var fields = new List<ProtoFieldModel>();
        foreach (var prop in props)
        {
            var number = TryGetProtoMemberNumber(prop);
            if (number is null)
                continue;
            fields.Add(CreateField(prop, number.Value));
        }

        if (fields.Count == 0)
        {
            var n = 1;
            foreach (var prop in props)
                fields.Add(CreateField(prop, n++));
        }

        return new ImmutableArrayAdapter(fields.OrderBy(f => f.Number).ToList());
    }

    private static ProtoFieldModel CreateField(IPropertySymbol prop, int number)
    {
        var type = prop.Type;
        var nullable = IsNullableProp(prop);
        var underlying = UnwrapNullable(type);

        if (IsMap(underlying, out var keyType, out var valType))
        {
            var (kProto, _, kKind) = Classify(keyType);
            var (vProto, _, vKind) = Classify(valType);
            var kClr = CleanClr(keyType.ToDisplayString());
            var vClr = CleanClr(valType.ToDisplayString());
            var collClr = CleanClr(underlying.ToDisplayString());
            return new ProtoFieldModel(
                prop.Name, number, $"map<{kProto}, {vProto}>", collClr,
                isNullable: true, kind: FieldKind.Map,
                elementProtoType: vProto, elementClrHint: vClr, elementKind: vKind,
                mapKeyProtoType: kProto, mapKeyClrHint: kClr, mapKeyKind: kKind);
        }

        if (IsRepeated(underlying, out var elem))
        {
            var (elemProto, _, elemKind) = Classify(elem);
            var elemClr = elem.ToDisplayString(); // e.g. int, byte, Nested
            if (elemClr.EndsWith("?", StringComparison.Ordinal))
                elemClr = elemClr.Substring(0, elemClr.Length - 1);
            if (elemClr.StartsWith("global::", StringComparison.Ordinal))
                elemClr = elemClr.Substring("global::".Length);
            var isArray = underlying is IArrayTypeSymbol;
            var collClr = underlying.ToDisplayString();
            if (collClr.StartsWith("global::", StringComparison.Ordinal))
                collClr = collClr.Substring("global::".Length);
            return new ProtoFieldModel(
                prop.Name, number, "repeated " + elemProto, collClr,
                isNullable: true, kind: FieldKind.Repeated,
                elementProtoType: elemProto, elementClrHint: elemClr, elementKind: elemKind,
                isArray: isArray);
        }

        var (proto, _, kind) = Classify(underlying);
        // Store real CLR type for casts (byte, sbyte, char, nint, ...) — no nullable '?' suffix
        var clr = underlying.ToDisplayString();
        if (clr.EndsWith("?", StringComparison.Ordinal))
            clr = clr.Substring(0, clr.Length - 1);
        if (clr.StartsWith("global::", StringComparison.Ordinal))
            clr = clr.Substring("global::".Length);
        return new ProtoFieldModel(prop.Name, number, proto, clr, nullable, kind);
    }

    private static (string Proto, string Hint, FieldKind Kind) Classify(ITypeSymbol type)
    {
        // Per https://learn.microsoft.com/aspnet/core/grpc/protobuf
        if (type.TypeKind == TypeKind.Enum)
            return (type.Name.Replace("+", "_"), type.ToDisplayString(), FieldKind.Enum);

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return ("bool", "Boolean", FieldKind.Bool);
            case SpecialType.System_String: return ("string", "String", FieldKind.String);
            case SpecialType.System_Char: return ("string", "Char", FieldKind.String);
            case SpecialType.System_Int32: return ("int32", "Int32", FieldKind.Int32);
            case SpecialType.System_Int64: return ("int64", "Int64", FieldKind.Int64);
            case SpecialType.System_UInt32: return ("uint32", "UInt32", FieldKind.UInt32);
            case SpecialType.System_UInt64: return ("uint64", "UInt64", FieldKind.UInt64);
            case SpecialType.System_Single: return ("float", "Single", FieldKind.Float);
            case SpecialType.System_Double: return ("double", "Double", FieldKind.Double);
            case SpecialType.System_Byte: return ("uint32", "Byte", FieldKind.UInt32);
            case SpecialType.System_SByte: return ("int32", "SByte", FieldKind.Int32);
            case SpecialType.System_Int16: return ("int32", "Int16", FieldKind.Int32);
            case SpecialType.System_UInt16: return ("uint32", "UInt16", FieldKind.UInt32);
            // DateTime / DateTimeOffset → google.protobuf.Timestamp
            case SpecialType.System_DateTime: return ("google.protobuf.Timestamp", "DateTime", FieldKind.DateTime);
            case SpecialType.System_Decimal: return ("DecimalValue", "Decimal", FieldKind.Decimal);
        }

        var name = type.Name;
        var full = type.ToDisplayString();

        if (name == "Guid" || full == "System.Guid")
            return ("bytes", "Guid", FieldKind.Guid); // 16-byte binary; not a scalar in the doc table

        if (name == "TimeSpan" || full == "System.TimeSpan")
            return ("google.protobuf.Duration", "TimeSpan", FieldKind.TimeSpan);

        if (name == "DateTimeOffset" || full == "System.DateTimeOffset")
            return ("google.protobuf.Timestamp", "DateTimeOffset", FieldKind.DateTime);

        if (name == "DateOnly" || full == "System.DateOnly")
            return ("google.protobuf.Timestamp", "DateOnly", FieldKind.DateTime);

        if (name == "TimeOnly" || full == "System.TimeOnly")
            return ("google.protobuf.Duration", "TimeOnly", FieldKind.TimeSpan);

        if (type is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte)
            return ("bytes", "Byte[]", FieldKind.Bytes);

        if (name is "IntPtr" || full == "System.IntPtr")
            return ("int64", "IntPtr", FieldKind.Int64);
        if (name is "UIntPtr" || full == "System.UIntPtr")
            return ("uint64", "UIntPtr", FieldKind.UInt64);

        // Dictionary / map
        if (type is INamedTypeSymbol mapNamed && mapNamed.IsGenericType && mapNamed.TypeArguments.Length == 2)
        {
            var def = mapNamed.OriginalDefinition.ToDisplayString();
            if (def is "System.Collections.Generic.Dictionary<TKey, TValue>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                var (kProto, _, _) = Classify(mapNamed.TypeArguments[0]);
                var (vProto, _, _) = Classify(mapNamed.TypeArguments[1]);
                return ($"map<{kProto}, {vProto}>", full, FieldKind.Map);
            }
        }

        // Never treat reflection / Type as a nested message
        if (IsNonMessageBclType(type))
            return ("string", type.ToDisplayString(), FieldKind.String);

        var msgName = name.Replace("+", "_");
        if (type is INamedTypeSymbol { IsGenericType: true } named)
            msgName = named.Name + "_" + string.Join("_", named.TypeArguments.Select(a => a.Name));
        return (msgName, type.ToDisplayString(), FieldKind.Message);
    }

    
    private static string CleanClr(string clr)
    {
        if (string.IsNullOrEmpty(clr)) return clr;
        if (clr.StartsWith("global::", StringComparison.Ordinal))
            clr = clr.Substring("global::".Length);
        if (clr.EndsWith("?", StringComparison.Ordinal))
            clr = clr.Substring(0, clr.Length - 1);
        return clr;
    }


    /// <summary>
    /// Types that must never become nested protobuf messages / codecs
    /// (e.g. System.Type → Assembly, TypeAttributes, …).
    /// </summary>
    private static bool IsNonMessageBclType(ITypeSymbol type, string? fullDisplay = null)
    {
        var full = fullDisplay ?? type.ToDisplayString();
        if (full.StartsWith("global::", StringComparison.Ordinal))
            full = full.Substring("global::".Length);

        if (full is "System.Guid" or "System.TimeSpan" or "System.DateTimeOffset"
            or "System.DateTime" or "System.Decimal" or "System.Object")
            return true;

        if (full is "System.Type" or "System.RuntimeType" or "System.Reflection.TypeInfo"
            or "System.Reflection.Assembly" or "System.Reflection.Module"
            or "System.Reflection.MemberInfo" or "System.Reflection.MethodInfo"
            or "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo"
            or "System.Reflection.ConstructorInfo" or "System.Reflection.ParameterInfo"
            or "System.Reflection.EventInfo" or "System.Delegate" or "System.MulticastDelegate"
            or "System.Enum" or "System.ValueType" or "System.Array")
            return true;

        if (full.StartsWith("System.Reflection.", StringComparison.Ordinal))
            return true;
        if (full.StartsWith("System.Runtime.Serialization.", StringComparison.Ordinal))
            return true;
        if (full.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal))
            return true;

        if (type.IsAbstract && type.SpecialType == SpecialType.None &&
            (full.StartsWith("System.", StringComparison.Ordinal) ||
             full.StartsWith("Microsoft.", StringComparison.Ordinal)))
            return true;

        return false;
    }

    private static bool IsMap(ITypeSymbol type, out ITypeSymbol key, out ITypeSymbol value)
    {
        key = type;
        value = type;
        if (type is not INamedTypeSymbol named || !named.IsGenericType || named.TypeArguments.Length != 2)
            return false;
        var def = named.OriginalDefinition.ToDisplayString();
        if (def is "System.Collections.Generic.Dictionary<TKey, TValue>"
            or "System.Collections.Generic.IDictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
            or "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>"
            or "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>")
        {
            key = named.TypeArguments[0];
            value = named.TypeArguments[1];
            return true;
        }
        return false;
    }

private static bool IsRepeated(ITypeSymbol type, out ITypeSymbol element)
    {
        element = type;
        if (type is IArrayTypeSymbol arr)
        {
            if (arr.ElementType.SpecialType == SpecialType.System_Byte)
                return false; // bytes
            element = arr.ElementType;
            return true;
        }
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.OriginalDefinition.ToDisplayString();
            if (def is "System.Collections.Generic.List<T>"
                or "System.Collections.Generic.IList<T>"
                or "System.Collections.Generic.IReadOnlyList<T>"
                or "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>"
                or "System.Collections.Generic.HashSet<T>"
                or "System.Collections.Generic.ISet<T>"
                or "System.Collections.Generic.IReadOnlySet<T>"
                or "System.Collections.Immutable.ImmutableArray<T>"
                or "System.Collections.Immutable.ImmutableList<T>")
            {
                element = named.TypeArguments[0];
                return true;
            }
        }
        return false;
    }


    private static bool IsNullableProp(IPropertySymbol prop)
    {
        if (prop.NullableAnnotation == NullableAnnotation.Annotated)
            return true;
        var ty = prop.Type;
        if (ty is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return true;
        // Non-nullable value types
        if (ty.IsValueType && !(ty is INamedTypeSymbol n2 && n2.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
            return false;
        // Reference types: only null-check when annotated (or oblivious)
        if (!ty.IsValueType && prop.NullableAnnotation == NullableAnnotation.NotAnnotated)
            return false;
        if (!ty.IsValueType)
            return true;
        return false;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
            return named.TypeArguments[0];
        return type;
    }

    public static string MessageName(ITypeSymbol? type)
    {
        type = UnwrapTask(type);
        if (type is null)
            return "";
        var name = type.Name;
        if (type is INamedTypeSymbol { IsGenericType: true } named)
            name = named.Name + "_" + string.Join("_", named.TypeArguments.Select(a => a.Name));
        return name.Replace("+", "_");
    }

    private static int? TryGetProtoMemberNumber(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName is not ("ProtoMemberAttribute" or "ProtoMember"))
                continue;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int i)
                return i;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is not null)
                return Convert.ToInt32(attr.ConstructorArguments[0].Value);
        }
        return null;
    }

    private static ITypeSymbol? UnwrapTask(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
            return type;
        var full = named.OriginalDefinition.ToDisplayString();
        if (full is "System.Threading.Tasks.Task<TResult>"
            or "System.Threading.Tasks.ValueTask<TResult>")
            return named.TypeArguments.Length == 1 ? named.TypeArguments[0] : null;
        if (full is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            return null;
        return type;
    }

    private static string ToProtoType(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        return Classify(type).Proto;
    }

    internal readonly struct ImmutableArrayAdapter
    {
        public static ImmutableArrayAdapter Empty { get; } = new(new List<ProtoFieldModel>());
        public List<ProtoFieldModel> Fields { get; }
        public ImmutableArrayAdapter(List<ProtoFieldModel> fields) => Fields = fields;
    }

    private sealed class ImmutableFieldList
    {
        public List<ProtoFieldModel> Fields { get; }
        public ImmutableFieldList(System.Collections.Immutable.ImmutableArray<ProtoFieldModel> fields)
            => Fields = fields.IsDefaultOrEmpty ? new List<ProtoFieldModel>() : fields.ToList();
    }
}
