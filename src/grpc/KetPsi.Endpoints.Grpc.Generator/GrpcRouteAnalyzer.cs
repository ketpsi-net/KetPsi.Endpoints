using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KetPsi.Endpoints.Grpc.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrpcRouteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "DG001";
    private const string Category = "Routing";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Invalid gRPC Route Format",
        messageFormat: "The gRPC route '{0}' is invalid. It must contain 2 or 3 segments separated by '/' (e.g., 'Namespace/Service/Method' or 'Service/Method').",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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
        var segments = routeValue.Split('/');

        if (segments.Length < 2 || segments.Length > 3 || segments.Any(string.IsNullOrWhiteSpace))
        {
            var diagnostic = Diagnostic.Create(Rule, literalExpression.GetLocation(), routeValue);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
