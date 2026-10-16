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
        messageFormat: "The gRPC route '{0}' is invalid. It must contain at least 2 non-parameter segments separated by '/' (e.g. 'Service/Method' or 'area/Service/Method'). Route parameters like '{{id}}' are ignored when counting.",
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

        // Validate the relative MapGrpc route only (MapGroup prefixes are added separately).
        // Require at least 2 meaningful segments (service + method). No upper bound.
        var routeValue = literalExpression.Token.ValueText;
        var segments = routeValue.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var meaningful = 0;
        foreach (var s in segments)
        {
            if (s.Length >= 2 && s[0] == '{' && s[s.Length - 1] == '}')
                continue;
            if (string.IsNullOrWhiteSpace(s))
                continue;
            meaningful++;
        }

        if (meaningful < 2)
        {
            var diagnostic = Diagnostic.Create(Rule, literalExpression.GetLocation(), routeValue);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
