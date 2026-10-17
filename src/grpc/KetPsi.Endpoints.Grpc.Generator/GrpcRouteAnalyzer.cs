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
        messageFormat: "The gRPC route '{0}' is invalid. It must contain at least 2 non-empty, non-parameter segments separated by '/' (e.g. 'Service/Method' or 'area/Service/Method'). Empty segments and a leading/trailing '/' are not allowed. Route parameters like '{{id}}' are ignored when counting.",
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

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol ||
            !methodSymbol.Name.StartsWith("MapGrpc", StringComparison.Ordinal))
        {
            return;
        }

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument?.Expression is not LiteralExpressionSyntax literalExpression ||
            !literalExpression.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return;
        }

        var routeValue = literalExpression.Token.ValueText;
        if (string.IsNullOrWhiteSpace(routeValue))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, literalExpression.GetLocation(), routeValue ?? ""));
            return;
        }

        // Keep empty entries so leading/trailing/duplicate '/' are detected
        var segments = routeValue.Split('/');
        if (segments.Any(s => s.Length == 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, literalExpression.GetLocation(), routeValue));
            return;
        }

        var meaningful = 0;
        foreach (var s in segments)
        {
            if (s.Length >= 2 && s[0] == '{' && s[s.Length - 1] == '}')
                continue;
            if (string.IsNullOrWhiteSpace(s))
                continue;
            meaningful++;
        }

        // At least service + method; no maximum (MapGroup adds package prefix segments separately)
        if (meaningful < 2)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, literalExpression.GetLocation(), routeValue));
        }
    }
}
