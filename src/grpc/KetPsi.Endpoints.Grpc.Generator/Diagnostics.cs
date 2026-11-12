using Microsoft.CodeAnalysis;

namespace KetPsi.Endpoints.Grpc.Generator;

internal static class Diagnostics
{
    private const string Category = "KetPsi.Grpc";

    public static readonly DiagnosticDescriptor RouteNotConstant = new(
        "KPG001", "gRPC route is not a compile-time constant",
        "The route of '{0}' is not a compile-time constant string; the endpoint will not be mapped",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor DuplicateMapping = new(
        "KPG003", "Duplicate gRPC method mapping",
        "gRPC method '{0}/{1}' is already mapped to a different handler or signature ('{2}'); this mapping is ignored",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedHandler = new(
        "KPG004", "Unsupported gRPC handler signature",
        "Handler '{0}' cannot be mapped: {1}",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor StreamingMismatch = new(
        "KPG005", "Handler signature does not match the MapGrpc variant",
        "'{0}' requires a {1} handler but '{2}' has a {3} signature",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConventionCapturesState = new(
        "KPG006", "Endpoint convention references local state",
        "The convention '{0}' chained on MapGrpc references '{1}', which is not accessible from generated code. Use constants, static members or inline lambdas.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConventionsNotChained = new(
        "KPG007", "MapGrpc result is not chained",
        "The result of '{0}' is stored or passed on; only conventions chained directly on the call, such as RequireAuthorization, are applied",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor ConfigureNotLambda = new(
        "KPG008", "MapGrpc configure argument must be an inline lambda",
        "The configure argument of '{0}' is not an inline lambda; it is read at compile time only and will be ignored",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        "KPG010", "Unsupported protobuf property type",
        "Property '{0}.{1}' cannot be mapped to protobuf: {2}",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor PropertyNotSerialized = new(
        "KPG011", "Property is not serialized",
        "Property '{0}.{1}' has no protobuf field number ({2}) and will not be serialized",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor DuplicateFieldNumber = new(
        "KPG012", "Duplicate protobuf field number",
        "Field number {0} is used by both '{1}' and '{2}' on '{3}'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidFieldNumber = new(
        "KPG013", "Invalid protobuf field number",
        "Field number {0} of '{1}.{2}' is invalid (allowed: 1..536870911 excluding 19000..19999)",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor FieldNumberNotConstant = new(
        "KPG014", "Field number is not a compile-time value",
        "The field number of '{0}' could not be determined at compile time; use a constant or a simple counter",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor ImplicitFieldNumbers = new(
        "KPG015", "Implicit protobuf field numbers",
        "'{0}' uses field numbers derived from property declaration order; reordering or inserting properties is a breaking wire change. Prefer explicit numbers via IProtoTypeConfiguration or [ProtoMember].",
        Category, DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor CannotConstruct = new(
        "KPG016", "Message type cannot be constructed",
        "'{0}' cannot be created when deserializing: {1}. Add an accessible constructor/setters or configure InstantiateWith.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor PropertyNotSettable = new(
        "KPG017", "Property cannot be set when deserializing",
        "Property '{0}.{1}' is serialized but is neither settable nor a constructor parameter; its value is dropped when deserializing",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor InvalidMapKey = new(
        "KPG018", "Invalid protobuf map key type",
        "Property '{0}.{1}': map key type '{2}' is not allowed (protobuf map keys must be integral, bool or string)",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor EnumValueOutOfRange = new(
        "KPG019", "Enum value outside int32 range",
        "Enum member '{0}.{1}' has a value outside the int32 range and cannot be represented in protobuf",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidBinding = new(
        "KPG020", "Invalid metadata binding",
        "FromMetadata binding '{0}.{1}': {2}",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConfiguredPropertyNotFound = new(
        "KPG022", "Configured property not found",
        "'{0}' configures property '{1}' which is not a public readable property of '{2}'",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UnsupportedMessageType = new(
        "KPG023", "Unsupported message type",
        "'{0}' cannot be used as a protobuf message: {1}",
        Category, DiagnosticSeverity.Error, true);

    private static readonly Dictionary<string, DiagnosticDescriptor> All = new[]
    {
        RouteNotConstant, DuplicateMapping, UnsupportedHandler, StreamingMismatch, ConventionCapturesState,
        ConventionsNotChained, ConfigureNotLambda, UnsupportedPropertyType, PropertyNotSerialized,
        DuplicateFieldNumber, InvalidFieldNumber, FieldNumberNotConstant, ImplicitFieldNumbers, CannotConstruct,
        PropertyNotSettable, InvalidMapKey, EnumValueOutOfRange, InvalidBinding, ConfiguredPropertyNotFound,
        UnsupportedMessageType,
    }.ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static DiagnosticDescriptor ById(string id) => All[id];
}
