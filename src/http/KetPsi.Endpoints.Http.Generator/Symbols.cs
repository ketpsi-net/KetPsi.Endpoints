using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KetPsi.Endpoints.Http.Generator;

/// <summary>Well-known framework symbols, cached per compilation.</summary>
internal sealed class WellKnownTypes
{
    private static readonly ConditionalWeakTable<Compilation, WellKnownTypes> Cache = new();

    public static WellKnownTypes Get(Compilation compilation) => Cache.GetValue(compilation, static c => new WellKnownTypes(c));

    private WellKnownTypes(Compilation c)
    {
        Compilation = c;
        HttpContext = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpContext");
        HttpRequest = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpRequest");
        HttpResponse = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpResponse");
        ClaimsPrincipal = c.GetTypeByMetadataName("System.Security.Claims.ClaimsPrincipal");
        CancellationToken = c.GetTypeByMetadataName("System.Threading.CancellationToken");
        Stream = c.GetTypeByMetadataName("System.IO.Stream");
        PipeReader = c.GetTypeByMetadataName("System.IO.Pipelines.PipeReader");
        FormFile = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IFormFile");
        FormFileCollection = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IFormFileCollection");
        FormCollection = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IFormCollection");
        StringValues = c.GetTypeByMetadataName("Microsoft.Extensions.Primitives.StringValues");
        Uri = c.GetTypeByMetadataName("System.Uri");
        DateTime = c.GetTypeByMetadataName("System.DateTime");
        DateTimeOffset = c.GetTypeByMetadataName("System.DateTimeOffset");
        DateOnly = c.GetTypeByMetadataName("System.DateOnly");
        TimeOnly = c.GetTypeByMetadataName("System.TimeOnly");
        Parsable = c.GetTypeByMetadataName("System.IParsable`1");
        FormatProvider = c.GetTypeByMetadataName("System.IFormatProvider");
        Result = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IResult");
        BindableFromHttpContext = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IBindableFromHttpContext`1");
        ParameterInfo = c.GetTypeByMetadataName("System.Reflection.ParameterInfo");
        MetadataProvider = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IEndpointMetadataProvider");
        ParameterMetadataProvider = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IEndpointParameterMetadataProvider");
        Task = c.GetTypeByMetadataName("System.Threading.Tasks.Task");
        TaskOfT = c.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        ValueTask = c.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        ValueTaskOfT = c.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        ListOfT = c.GetTypeByMetadataName("System.Collections.Generic.List`1");
        FromQueryMetadata = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromQueryMetadata");
        FromRouteMetadata = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromRouteMetadata");
        FromHeaderMetadata = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromHeaderMetadata");
        FromBodyMetadata = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromBodyMetadata");
        FromFormMetadata = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromFormMetadata");
        FromServiceMetadata = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromServiceMetadata");
        FromKeyedServices = c.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute");
        AsParameters = c.GetTypeByMetadataName("Microsoft.AspNetCore.Http.AsParametersAttribute");
        WebApplication = c.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.WebApplication");
        RouteGroupBuilder = c.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.RouteGroupBuilder");
        EndpointRouteBuilder = c.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");
        Obsolete = c.GetTypeByMetadataName("System.ObsoleteAttribute");
    }

    public Compilation Compilation { get; }
    public INamedTypeSymbol? HttpContext { get; }
    public INamedTypeSymbol? HttpRequest { get; }
    public INamedTypeSymbol? HttpResponse { get; }
    public INamedTypeSymbol? ClaimsPrincipal { get; }
    public INamedTypeSymbol? CancellationToken { get; }
    public INamedTypeSymbol? Stream { get; }
    public INamedTypeSymbol? PipeReader { get; }
    public INamedTypeSymbol? FormFile { get; }
    public INamedTypeSymbol? FormFileCollection { get; }
    public INamedTypeSymbol? FormCollection { get; }
    public INamedTypeSymbol? StringValues { get; }
    public INamedTypeSymbol? Uri { get; }
    public INamedTypeSymbol? DateTime { get; }
    public INamedTypeSymbol? DateTimeOffset { get; }
    public INamedTypeSymbol? DateOnly { get; }
    public INamedTypeSymbol? TimeOnly { get; }
    public INamedTypeSymbol? Parsable { get; }
    public INamedTypeSymbol? FormatProvider { get; }
    public INamedTypeSymbol? Result { get; }
    public INamedTypeSymbol? BindableFromHttpContext { get; }
    public INamedTypeSymbol? ParameterInfo { get; }
    public INamedTypeSymbol? MetadataProvider { get; }
    public INamedTypeSymbol? ParameterMetadataProvider { get; }
    public INamedTypeSymbol? Task { get; }
    public INamedTypeSymbol? TaskOfT { get; }
    public INamedTypeSymbol? ValueTask { get; }
    public INamedTypeSymbol? ValueTaskOfT { get; }
    public INamedTypeSymbol? ListOfT { get; }
    public INamedTypeSymbol? FromQueryMetadata { get; }
    public INamedTypeSymbol? FromRouteMetadata { get; }
    public INamedTypeSymbol? FromHeaderMetadata { get; }
    public INamedTypeSymbol? FromBodyMetadata { get; }
    public INamedTypeSymbol? FromFormMetadata { get; }
    public INamedTypeSymbol? FromServiceMetadata { get; }
    public INamedTypeSymbol? FromKeyedServices { get; }
    public INamedTypeSymbol? AsParameters { get; }
    public INamedTypeSymbol? WebApplication { get; }
    public INamedTypeSymbol? RouteGroupBuilder { get; }
    public INamedTypeSymbol? EndpointRouteBuilder { get; }
    public INamedTypeSymbol? Obsolete { get; }

    public static bool Is(ITypeSymbol? type, INamedTypeSymbol? target)
        => type is not null && target is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, target);

    public static bool Implements(ITypeSymbol? type, INamedTypeSymbol? @interface)
    {
        if (type is null || @interface is null)
            return false;
        if (Is(type, @interface))
            return true;
        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, @interface));
    }

    public static bool InheritsFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (Is(current, baseType))
                return true;
        }
        return false;
    }
}

internal static class SymbolFormats
{
    /// <summary>Fully qualified (<c>global::</c>) including nullable reference annotations.</summary>
    public static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Fully qualified without nullable reference annotations.</summary>
    public static readonly SymbolDisplayFormat FqnNoAnnotations = SymbolDisplayFormat.FullyQualifiedFormat;

    public static readonly SymbolDisplayFormat Short = SymbolDisplayFormat.MinimallyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string ToFqn(this ITypeSymbol type) => type.ToDisplayString(Fqn);
}

/// <summary>C# literal formatting for emitted code. Every string that ends up in generated source goes through here.</summary>
internal static class Literals
{
    public static string String(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>Formats a compile-time constant of <paramref name="type"/> as a C# expression.</summary>
    public static string Constant(object? value, ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        if (value is null)
            return type is { IsValueType: true } ? "default" : "null";

        if (type is { TypeKind: TypeKind.Enum } enumType)
            return $"({enumType.ToDisplayString(SymbolFormats.FqnNoAnnotations)})({Primitive(value)})";

        return Primitive(value);
    }

    public static string Primitive(object value) => value switch
    {
        string s => String(s),
        char c => SymbolDisplay.FormatLiteral(c, quote: true),
        bool b => b ? "true" : "false",
        float f => float.IsNaN(f) ? "float.NaN" : float.IsPositiveInfinity(f) ? "float.PositiveInfinity" : float.IsNegativeInfinity(f) ? "float.NegativeInfinity" : f.ToString("R", CultureInfo.InvariantCulture) + "F",
        double d => double.IsNaN(d) ? "double.NaN" : double.IsPositiveInfinity(d) ? "double.PositiveInfinity" : double.IsNegativeInfinity(d) ? "double.NegativeInfinity" : d.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
        uint ui => ui.ToString(CultureInfo.InvariantCulture) + "U",
        int i => i.ToString(CultureInfo.InvariantCulture),
        short sh => $"((short){sh.ToString(CultureInfo.InvariantCulture)})",
        ushort us => $"((ushort){us.ToString(CultureInfo.InvariantCulture)})",
        byte by => $"((byte){by.ToString(CultureInfo.InvariantCulture)})",
        sbyte sb => $"((sbyte){sb.ToString(CultureInfo.InvariantCulture)})",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "default",
    };
}
