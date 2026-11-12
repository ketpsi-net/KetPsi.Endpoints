using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Validates <c>MapGrpc</c> route string literals (relative path only; <c>MapGroup</c> prefixes are separate).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrpcRouteAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic id for invalid gRPC route format.</summary>
    public const string DiagnosticId = "DG001";
    private const string Category = "Routing";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Invalid gRPC Route Format",
        messageFormat: "The gRPC route '{0}' is invalid: {1}. Routes need at least 2 non-parameter segments (Service/Method) counting MapGroup prefixes, every segment must be a protobuf identifier ([A-Za-z_][A-Za-z0-9_]*), and empty segments or a leading/trailing '/' are not allowed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMethodInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name }
            || !name.StartsWith("MapGrpc", StringComparison.Ordinal) || name is "MapGrpcService" or "MapGrpcEndpoints")
            return;

        // Covers MapGrpc<T>(...) and MapGrpc<TEndpoint, TConfig>(...) alike.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol ||
            !methodSymbol.Name.StartsWith("MapGrpc", StringComparison.Ordinal) || methodSymbol.TypeArguments.Length is < 1 or > 2)
            return;

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "route")
                       ?? invocation.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon is null);
        if (argument is null)
            return;
        if (context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken).Value is not string routeValue)
            return; // non-constant routes are reported by the generator (KPG001)

        var receiver = RouteResolver.ResolveReceiver(invocation, context.SemanticModel, context.CancellationToken);
        var location = argument.Expression.GetLocation();
        var badPrefix = RouteResolver.InvalidPrefixSegment(receiver.Prefixes, receiver.Version);
        if (badPrefix is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, routeValue,
                $"MapGroup/WithGroupName segment '{badPrefix}' is not a valid protobuf identifier"));
            return;
        }

        var reason = RouteResolver.ValidateRelativeRoute(routeValue, RouteResolver.CountSegments(receiver.Prefixes));
        if (reason is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, routeValue, reason));
    }
}
