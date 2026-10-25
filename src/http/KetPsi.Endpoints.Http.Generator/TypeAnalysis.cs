using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KetPsi.Endpoints.Http.Generator;

/// <summary>Symbol analysis that turns parameters / members into value-equatable models.</summary>
internal static class TypeAnalysis
{
    public static TypeModel AnalyzeType(ITypeSymbol type, WellKnownTypes wk)
    {
        var fqn = type.ToFqn();
        var isNullableValueType = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var valueType = isNullableValueType ? ((INamedTypeSymbol)type).TypeArguments[0] : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var valueFqn = valueType.ToDisplayString(SymbolFormats.FqnNoAnnotations);
        var display = type.ToDisplayString(SymbolFormats.Short);

        var special = GetSpecial(valueType, wk);
        var parse = special == SpecialKind.None ? GetParseKind(valueType, wk) : ParseKind.None;

        var collection = CollectionKind.None;
        string? elementFqn = null, elementValueFqn = null;
        var elementIsNullable = false;
        if (special == SpecialKind.None && parse == ParseKind.None)
        {
            ITypeSymbol? element = null;
            if (valueType is IArrayTypeSymbol { Rank: 1 } array)
            {
                element = array.ElementType;
                collection = CollectionKind.Array;
            }
            else if (valueType is INamedTypeSymbol named && WellKnownTypes.Is(named, wk.ListOfT))
            {
                element = named.TypeArguments[0];
                collection = CollectionKind.List;
            }

            if (element is not null)
            {
                var elementIsNullableStruct = element is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
                var elementValue = elementIsNullableStruct ? ((INamedTypeSymbol)element).TypeArguments[0] : element.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                var elementParse = GetParseKind(elementValue, wk);
                if (elementParse is ParseKind.None or ParseKind.StringValues)
                {
                    collection = CollectionKind.None;
                }
                else
                {
                    parse = elementParse;
                    elementFqn = element.ToFqn();
                    elementValueFqn = elementValue.ToDisplayString(SymbolFormats.FqnNoAnnotations);
                    elementIsNullable = elementIsNullableStruct || element.NullableAnnotation == NullableAnnotation.Annotated;
                }
            }
        }

        var (bindAsync, bindAsyncNullableStruct) = special == SpecialKind.None ? GetBindAsync(valueType, wk) : (BindAsyncKind.None, false);

        var isComplex = special == SpecialKind.None && parse == ParseKind.None && collection == CollectionKind.None;

        return new TypeModel(
            fqn,
            valueFqn,
            display,
            type.IsValueType,
            isNullableValueType,
            type.IsReferenceType,
            special,
            parse,
            collection,
            elementFqn,
            elementValueFqn,
            elementIsNullable,
            bindAsync,
            bindAsyncNullableStruct,
            isComplex,
            WellKnownTypes.Implements(valueType, wk.ParameterMetadataProvider) && IsAccessibleConcreteType(valueType),
            WellKnownTypes.Implements(valueType, wk.MetadataProvider) && IsAccessibleConcreteType(valueType));
    }

    private static bool IsAccessibleConcreteType(ITypeSymbol type)
        => type.TypeKind is TypeKind.Class or TypeKind.Struct && !type.IsAbstract;

    private static SpecialKind GetSpecial(ITypeSymbol type, WellKnownTypes wk)
    {
        if (WellKnownTypes.Is(type, wk.HttpContext)) return SpecialKind.HttpContext;
        if (WellKnownTypes.Is(type, wk.HttpRequest)) return SpecialKind.HttpRequest;
        if (WellKnownTypes.Is(type, wk.HttpResponse)) return SpecialKind.HttpResponse;
        if (WellKnownTypes.Is(type, wk.ClaimsPrincipal)) return SpecialKind.ClaimsPrincipal;
        if (WellKnownTypes.Is(type, wk.CancellationToken)) return SpecialKind.CancellationToken;
        if (WellKnownTypes.Is(type, wk.Stream)) return SpecialKind.Stream;
        if (WellKnownTypes.Is(type, wk.PipeReader)) return SpecialKind.PipeReader;
        if (WellKnownTypes.Is(type, wk.FormFile)) return SpecialKind.FormFile;
        if (WellKnownTypes.Is(type, wk.FormFileCollection)) return SpecialKind.FormFileCollection;
        if (WellKnownTypes.Is(type, wk.FormCollection)) return SpecialKind.FormCollection;
        return SpecialKind.None;
    }

    private static ParseKind GetParseKind(ITypeSymbol type, WellKnownTypes wk)
    {
        if (type.SpecialType == SpecialType.System_String) return ParseKind.String;
        if (WellKnownTypes.Is(type, wk.StringValues)) return ParseKind.StringValues;
        if (type.TypeKind == TypeKind.Enum) return ParseKind.Enum;
        if (WellKnownTypes.Is(type, wk.DateTime)) return ParseKind.DateTime;
        if (WellKnownTypes.Is(type, wk.DateTimeOffset)) return ParseKind.DateTimeOffset;
        if (WellKnownTypes.Is(type, wk.DateOnly)) return ParseKind.DateOnly;
        if (WellKnownTypes.Is(type, wk.TimeOnly)) return ParseKind.TimeOnly;
        if (WellKnownTypes.Is(type, wk.Uri)) return ParseKind.Uri;
        if (type is not INamedTypeSymbol named || type.TypeKind is TypeKind.Interface or TypeKind.Error)
            return ParseKind.None;

        if (wk.Parsable is not null && named.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, wk.Parsable) &&
                SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], named)))
        {
            return ParseKind.Parsable;
        }

        foreach (var method in GetStaticMethods(named, "TryParse"))
        {
            if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
                continue;
            var p = method.Parameters;
            if (p.Length == 3 && p[0].Type.SpecialType == SpecialType.System_String &&
                WellKnownTypes.Is(p[1].Type, wk.FormatProvider) &&
                p[2].RefKind == RefKind.Out && SymbolEqualityComparer.Default.Equals(p[2].Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
            {
                return ParseKind.StaticTryParseWithProvider;
            }
        }

        foreach (var method in GetStaticMethods(named, "TryParse"))
        {
            if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
                continue;
            var p = method.Parameters;
            if (p.Length == 2 && p[0].Type.SpecialType == SpecialType.System_String &&
                p[1].RefKind == RefKind.Out && SymbolEqualityComparer.Default.Equals(p[1].Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
            {
                return ParseKind.StaticTryParse;
            }
        }

        return ParseKind.None;
    }

    private static IEnumerable<IMethodSymbol> GetStaticMethods(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public, IsGenericMethod: false } m)
                    yield return m;
            }
        }
    }

    private static (BindAsyncKind Kind, bool ReturnsNullableStruct) GetBindAsync(ITypeSymbol type, WellKnownTypes wk)
    {
        if (type is not INamedTypeSymbol named || type.TypeKind is TypeKind.Interface or TypeKind.Error)
            return (BindAsyncKind.None, false);

        BindAsyncKind found = BindAsyncKind.None;
        var nullableStruct = false;
        foreach (var method in GetStaticMethods(named, "BindAsync"))
        {
            if (method.ReturnType is not INamedTypeSymbol ret || !WellKnownTypes.Is(ret, wk.ValueTaskOfT))
                continue;
            var result = ret.TypeArguments[0];
            var isNullableStruct = result is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n &&
                                   SymbolEqualityComparer.Default.Equals(n.TypeArguments[0], named);
            if (!isNullableStruct && !SymbolEqualityComparer.Default.Equals(result.WithNullableAnnotation(NullableAnnotation.NotAnnotated), named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
                continue;

            var p = method.Parameters;
            if (p.Length == 2 && WellKnownTypes.Is(p[0].Type, wk.HttpContext) && WellKnownTypes.Is(p[1].Type, wk.ParameterInfo))
            {
                found = BindAsyncKind.ContextAndParameter;
                nullableStruct = isNullableStruct;
                break;
            }

            if (p.Length == 1 && WellKnownTypes.Is(p[0].Type, wk.HttpContext) && found == BindAsyncKind.None)
            {
                found = BindAsyncKind.Context;
                nullableStruct = isNullableStruct;
            }
        }

        if (found == BindAsyncKind.None && wk.BindableFromHttpContext is not null && named.IsReferenceType &&
            named.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, wk.BindableFromHttpContext)))
        {
            found = BindAsyncKind.Interface;
        }

        return (found, nullableStruct);
    }

    // ------------------------------------------------------------------
    // Attributes → BindingSpec
    // ------------------------------------------------------------------

    public static BindingSpec GetBindingSpec(ImmutableAttributes attributes, WellKnownTypes wk, out bool keyMissing)
    {
        keyMissing = false;
        foreach (var attr in attributes.Items)
        {
            var cls = attr.AttributeClass;
            if (cls is null)
                continue;

            if (WellKnownTypes.Is(cls, wk.AsParameters))
                return new BindingSpec(BindingSource.AsParameters, null, null, false);

            if (WellKnownTypes.Is(cls, wk.FromKeyedServices))
            {
                if (attr.ConstructorArguments.Length > 0 && !attr.ConstructorArguments[0].IsNull)
                {
                    var arg = attr.ConstructorArguments[0];
                    return new BindingSpec(BindingSource.KeyedServices, null, Literals.Constant(arg.Value, arg.Type), false);
                }

                keyMissing = true;
                return new BindingSpec(BindingSource.KeyedServices, null, null, false);
            }

            var name = GetNamedString(attr, "Name");
            if (WellKnownTypes.Implements(cls, wk.FromRouteMetadata)) return new BindingSpec(BindingSource.Route, name, null, false);
            if (WellKnownTypes.Implements(cls, wk.FromQueryMetadata)) return new BindingSpec(BindingSource.Query, name, null, false);
            if (WellKnownTypes.Implements(cls, wk.FromHeaderMetadata)) return new BindingSpec(BindingSource.Header, name, null, false);
            if (WellKnownTypes.Implements(cls, wk.FromFormMetadata)) return new BindingSpec(BindingSource.Form, name, null, false);
            if (WellKnownTypes.Implements(cls, wk.FromServiceMetadata)) return new BindingSpec(BindingSource.Services, null, null, false);
            if (WellKnownTypes.Implements(cls, wk.FromBodyMetadata))
            {
                // [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] → optional body.
                var allowEmpty = attr.NamedArguments.Any(a => a.Key == "EmptyBodyBehavior" && a.Value.Value is int v && v == 1)
                                 || attr.NamedArguments.Any(a => a.Key == "AllowEmpty" && a.Value.Value is true);
                return new BindingSpec(BindingSource.Body, null, null, allowEmpty);
            }
        }

        return BindingSpec.Inferred;
    }

    public static bool IsBindingAttribute(AttributeData attr, WellKnownTypes wk)
    {
        var cls = attr.AttributeClass;
        if (cls is null)
            return false;
        return WellKnownTypes.Is(cls, wk.AsParameters)
            || WellKnownTypes.Is(cls, wk.FromKeyedServices)
            || WellKnownTypes.Implements(cls, wk.FromRouteMetadata)
            || WellKnownTypes.Implements(cls, wk.FromQueryMetadata)
            || WellKnownTypes.Implements(cls, wk.FromHeaderMetadata)
            || WellKnownTypes.Implements(cls, wk.FromFormMetadata)
            || WellKnownTypes.Implements(cls, wk.FromServiceMetadata)
            || WellKnownTypes.Implements(cls, wk.FromBodyMetadata);
    }

    private static string? GetNamedString(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is string s && s.Length > 0)
                return s;
        }
        return null;
    }

    /// <summary>
    /// Re-creates non-binding attributes (validation, metadata, …) with fully qualified names from symbol data,
    /// so they compile in the generated file independently of the original usings.
    /// </summary>
    public static EquatableArray<string> GetExtraAttributes(ImmutableAttributes attributes, WellKnownTypes wk)
    {
        List<string>? result = null;
        foreach (var attr in attributes.Items)
        {
            var cls = attr.AttributeClass;
            if (cls is null || cls.TypeKind == TypeKind.Error || IsBindingAttribute(attr, wk))
                continue;
            var ns = cls.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns.StartsWith("System.Runtime.CompilerServices", StringComparison.Ordinal) ||
                ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal) ||
                ns.StartsWith("System.Diagnostics.CodeAnalysis", StringComparison.Ordinal) ||
                cls.Name == "ParamArrayAttribute")
            {
                continue;
            }
            if (!wk.Compilation.IsSymbolAccessibleWithin(cls, wk.Compilation.Assembly))
                continue;

            var args = new List<string>();
            foreach (var ctorArg in attr.ConstructorArguments)
                args.Add(ctorArg.ToCSharpString());
            foreach (var named in attr.NamedArguments)
                args.Add(named.Key + " = " + named.Value.ToCSharpString());

            var text = cls.ToDisplayString(SymbolFormats.FqnNoAnnotations) + (args.Count > 0 ? "(" + string.Join(", ", args) + ")" : string.Empty);
            (result ??= new List<string>()).Add(text);
        }

        return EquatableArray<string>.From(result);
    }

    // ------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------

    public static ValueModel AnalyzeParameter(IParameterSymbol p, WellKnownTypes wk, out bool keyMissing)
    {
        var attrs = new ImmutableAttributes(p.GetAttributes());
        var type = AnalyzeType(p.Type, wk);
        var binding = GetBindingSpec(attrs, wk, out keyMissing);
        string? defaultLiteral = p.HasExplicitDefaultValue ? Literals.Constant(p.ExplicitDefaultValue, p.Type) : null;
        var optional = defaultLiteral is not null || IsNullable(p.Type) || (binding.Source == BindingSource.Body && binding.AllowEmptyBody);
        return new ValueModel(p.Name, type, binding, optional, defaultLiteral, GetExtraAttributes(attrs, wk), LocationInfo.From(p.Locations.FirstOrDefault()));
    }

    public static bool IsNullable(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
           || (type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated);

    /// <summary>
    /// AsParameters construction shape (RDF rules): a public parameterless constructor (or struct) binds settable
    /// properties; otherwise the public constructor with the most parameters binds its parameters (matched to
    /// properties by name for attributes) plus any remaining settable properties.
    /// </summary>
    public static DtoModel? AnalyzeDto(ITypeSymbol type, WellKnownTypes wk)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n)
            type = n.TypeArguments[0];
        if (type is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Class or TypeKind.Struct) || named.IsAbstract || named.SpecialType != SpecialType.None)
            return null;

        var properties = new List<IPropertySymbol>();
        for (INamedTypeSymbol? current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var prop in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.DeclaredAccessibility != Accessibility.Public || prop.IsStatic || prop.IsIndexer || prop.GetMethod is null)
                    continue;
                if (properties.Any(x => x.Name == prop.Name))
                    continue;
                if (prop.Name == "EqualityContract")
                    continue;
                properties.Add(prop);
            }
        }

        var publicCtors = named.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, named)))
            .ToList();
        var parameterless = named.IsValueType || publicCtors.Any(c => c.Parameters.Length == 0);
        var ctor = parameterless ? null : publicCtors.OrderByDescending(c => c.Parameters.Length).FirstOrDefault();
        if (!parameterless && ctor is null)
            return null;

        var ctorMembers = new List<MemberModel>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ctor is not null)
        {
            foreach (var cp in ctor.Parameters)
            {
                var prop = properties.FirstOrDefault(x => string.Equals(x.Name, cp.Name, StringComparison.OrdinalIgnoreCase));
                var allAttrs = cp.GetAttributes().AddRange(prop?.GetAttributes() ?? System.Collections.Immutable.ImmutableArray<AttributeData>.Empty);
                var attrs = new ImmutableAttributes(allAttrs);
                var t = AnalyzeType(cp.Type, wk);
                var binding = GetBindingSpec(attrs, wk, out _);
                string? defaultLiteral = cp.HasExplicitDefaultValue ? Literals.Constant(cp.ExplicitDefaultValue, cp.Type) : null;
                var name = prop?.Name ?? cp.Name;
                used.Add(name);
                var value = new ValueModel(
                    name,
                    t,
                    binding,
                    defaultLiteral is not null || IsNullable(cp.Type),
                    defaultLiteral,
                    GetExtraAttributes(attrs, wk),
                    LocationInfo.From(cp.Locations.FirstOrDefault()));
                ctorMembers.Add(new MemberModel(value, true, false));
            }
        }

        var propMembers = new List<MemberModel>();
        foreach (var prop in properties)
        {
            if (used.Contains(prop.Name))
                continue;
            if (prop.SetMethod is null || prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
                continue;
            var attrs = new ImmutableAttributes(prop.GetAttributes());
            var t = AnalyzeType(prop.Type, wk);
            var binding = GetBindingSpec(attrs, wk, out _);
            var value = new ValueModel(
                prop.Name,
                t,
                binding,
                IsNullable(prop.Type),
                null,
                GetExtraAttributes(attrs, wk),
                LocationInfo.From(prop.Locations.FirstOrDefault()));
            propMembers.Add(new MemberModel(value, false, prop.IsRequired));
        }

        if (ctorMembers.Count == 0 && propMembers.Count == 0)
            return null;

        var accessibility = GetAccessibility(named);
        var memberTypes = (ctor?.Parameters.Select(p => p.Type) ?? Enumerable.Empty<ITypeSymbol>())
            .Concat(properties.Where(p => !used.Contains(p.Name)).Select(p => p.Type));
        if (memberTypes.Any(t => GetAccessibility(t) == "internal"))
            accessibility = "internal";

        return new DtoModel(named.ToDisplayString(SymbolFormats.FqnNoAnnotations), accessibility, ctorMembers.ToEquatableArray(), propMembers.ToEquatableArray());
    }

    /// <summary>"public" when the type (and all type arguments / containing types) is public; otherwise "internal".</summary>
    public static string GetAccessibility(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null || typeSymbol is IErrorTypeSymbol)
            return "internal";
        if (typeSymbol is IArrayTypeSymbol arrayType)
            return GetAccessibility(arrayType.ElementType);
        if (typeSymbol is ITypeParameterSymbol)
            return "internal";
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                if (GetAccessibility(typeArg) == "internal")
                    return "internal";
            }
        }

        for (ITypeSymbol? current = typeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public)
                return "internal";
        }

        return "public";
    }
}

/// <summary>Small wrapper so attribute lists can be passed around without re-querying symbols.</summary>
internal readonly struct ImmutableAttributes(System.Collections.Immutable.ImmutableArray<AttributeData> items)
{
    public System.Collections.Immutable.ImmutableArray<AttributeData> Items { get; } = items.IsDefault ? System.Collections.Immutable.ImmutableArray<AttributeData>.Empty : items;
}
