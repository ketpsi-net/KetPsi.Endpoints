using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.Endpoints.Http.Generator;

/// <summary>
/// Rewrites user expressions (routes, convention arguments, group chains) into text that compiles inside the
/// generated mapping class: constants are folded to escaped literals, unqualified type / static member
/// references become fully qualified, and references to parameters of the enclosing helper method become
/// placeholders that are substituted from the helper's call sites. Anything else that cannot move (locals,
/// instance members, <c>this</c>) produces a <see cref="Diagnostics.NonPortableExpression"/> error.
/// </summary>
internal static class Portable
{
    private const char PlaceholderStart = '\u0001';
    private const char PlaceholderEnd = '\u0002';

    public static string Placeholder(int ordinal) => PlaceholderStart + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + PlaceholderEnd;

    public static bool HasPlaceholders(string text) => text.IndexOf(PlaceholderStart) >= 0;

    /// <summary>Replaces helper placeholders with the call-site argument text. Returns null when an argument is missing.</summary>
    public static string? Substitute(string template, IReadOnlyDictionary<int, string> arguments)
    {
        if (!HasPlaceholders(template))
            return template;

        var sb = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c != PlaceholderStart)
            {
                sb.Append(c);
                i++;
                continue;
            }

            var end = template.IndexOf(PlaceholderEnd, i + 1);
            var ordinal = int.Parse(template.Substring(i + 1, end - i - 1), System.Globalization.CultureInfo.InvariantCulture);
            if (!arguments.TryGetValue(ordinal, out var replacement))
                return null;
            sb.Append('(').Append(replacement).Append(')');
            i = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>Makes <paramref name="expression"/> portable; returns null (and reports) when it cannot be.</summary>
    public static string? Write(
        ExpressionSyntax expression,
        SemanticModel model,
        IMethodSymbol? helperMethod,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        // Whole-expression constants (including const locals and string concatenations) become literals.
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue && expression is not LiteralExpressionSyntax)
        {
            var type = model.GetTypeInfo(expression, cancellationToken).Type;
            if (constant.Value is not null || type is not null)
                return Literals.Constant(constant.Value, type);
        }

        var edits = new List<(int Start, int Length, string Text)>();
        var ok = true;
        var span = expression.Span;

        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                Report(expression, "'this'", diagnostics);
                ok = false;
                continue;
            }

            if (node is not SimpleNameSyntax name || !IsLeftMost(name))
                continue;

            // Do not descend into names nested in a node we already rewrote.
            if (edits.Any(e => name.SpanStart >= e.Start && name.SpanStart < e.Start + e.Length))
                continue;

            var info = model.GetSymbolInfo(name, cancellationToken);
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            switch (symbol)
            {
                case null:
                    break;

                case IMethodSymbol { MethodKind: MethodKind.Constructor } ctor when name.Parent is AttributeSyntax || name.Parent is ObjectCreationExpressionSyntax:
                    edits.Add((name.SpanStart, name.Span.Length, ctor.ContainingType.ToDisplayString(SymbolFormats.FqnNoAnnotations)));
                    break;

                case ILocalSymbol local:
                    if (local.IsConst)
                    {
                        edits.Add((name.SpanStart, name.Span.Length, Literals.Constant(local.ConstantValue, local.Type)));
                    }
                    else if (!IsDeclaredInside(local, span))
                    {
                        Report(expression, $"the local variable '{local.Name}'", diagnostics);
                        ok = false;
                    }
                    break;

                case IParameterSymbol parameter:
                    if (IsDeclaredInside(parameter, span))
                        break;
                    if (helperMethod is not null && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, helperMethod))
                    {
                        edits.Add((name.SpanStart, name.Span.Length, Placeholder(parameter.Ordinal)));
                    }
                    else
                    {
                        Report(expression, $"the parameter '{parameter.Name}'", diagnostics);
                        ok = false;
                    }
                    break;

                case IRangeVariableSymbol:
                    break;

                case INamespaceSymbol ns:
                    if (!ns.IsGlobalNamespace)
                        edits.Add((name.SpanStart, name.Span.Length, "global::" + ns.ToDisplayString()));
                    break;

                case ITypeSymbol typeSymbol when typeSymbol is not ITypeParameterSymbol:
                    if (name.Identifier.ValueText == "var" && name.Parent is VariableDeclarationSyntax or DeclarationExpressionSyntax)
                        break;
                    edits.Add((name.SpanStart, name.Span.Length, typeSymbol.ToDisplayString(SymbolFormats.FqnNoAnnotations)));
                    break;

                case IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction:
                    Report(expression, $"the local function '{localFunction.Name}'", diagnostics);
                    ok = false;
                    break;

                case IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol:
                    if (symbol.IsStatic || symbol is IFieldSymbol { IsConst: true })
                    {
                        if (symbol is IFieldSymbol { IsConst: true } constField && name.Parent is not InvocationExpressionSyntax)
                        {
                            edits.Add((name.SpanStart, name.Span.Length, Literals.Constant(constField.ConstantValue, constField.Type)));
                        }
                        else if (symbol.ContainingType is { } containing)
                        {
                            // Prefix with the fully qualified containing type; nested names (generic args) are still visited.
                            edits.Add((name.SpanStart, 0, containing.ToDisplayString(SymbolFormats.FqnNoAnnotations) + "."));
                        }
                    }
                    else if (symbol is IMethodSymbol { IsExtensionMethod: true })
                    {
                        // Unqualified extension call on 'this' – not portable.
                        Report(expression, $"the instance member '{symbol.Name}'", diagnostics);
                        ok = false;
                    }
                    else
                    {
                        Report(expression, $"the instance member '{symbol.Name}'", diagnostics);
                        ok = false;
                    }
                    break;
            }
        }

        if (!ok)
            return null;

        var text = expression.ToString();
        if (edits.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length + 32);
        var position = 0;
        foreach (var edit in edits.OrderBy(e => e.Start).ThenByDescending(e => e.Length))
        {
            var relative = edit.Start - span.Start;
            if (relative < position)
                continue;
            sb.Append(text, position, relative - position);
            sb.Append(edit.Text);
            position = relative + edit.Length;
        }
        sb.Append(text, position, text.Length - position);
        return sb.ToString();
    }

    /// <summary>Portable text of a chained call: <c>.Name&lt;TArgs&gt;(args)</c>.</summary>
    public static string? WriteChainCall(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel model,
        IMethodSymbol? helperMethod,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append('.').Append(memberAccess.Name.Identifier.Text);
        if (memberAccess.Name is GenericNameSyntax generic)
        {
            sb.Append('<');
            var first = true;
            foreach (var typeArg in generic.TypeArgumentList.Arguments)
            {
                if (!first) sb.Append(", ");
                first = false;
                var typeSymbol = model.GetTypeInfo(typeArg, cancellationToken).Type;
                if (typeSymbol is null || typeSymbol is ITypeParameterSymbol)
                {
                    Report(typeArg, $"the type '{typeArg}'", diagnostics);
                    return null;
                }
                sb.Append(typeSymbol.ToFqn());
            }
            sb.Append('>');
        }

        var args = WriteArguments(invocation.ArgumentList, model, helperMethod, diagnostics, cancellationToken);
        if (args is null)
            return null;
        sb.Append('(').Append(args).Append(')');
        return sb.ToString();
    }

    public static string? WriteArguments(
        ArgumentListSyntax argumentList,
        SemanticModel model,
        IMethodSymbol? helperMethod,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var parts = new List<string>();
        foreach (var argument in argumentList.Arguments)
        {
            var value = Write(argument.Expression, model, helperMethod, diagnostics, cancellationToken);
            if (value is null)
                return null;
            var prefix = argument.NameColon is not null ? argument.NameColon.Name.Identifier.Text + ": " : string.Empty;
            var refKind = argument.RefKindKeyword.IsKind(SyntaxKind.None) ? string.Empty : argument.RefKindKeyword.Text + " ";
            parts.Add(prefix + refKind + value);
        }
        return string.Join(", ", parts);
    }

    private static bool IsLeftMost(SimpleNameSyntax name)
    {
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax ma when ma.Name == name:
            case QualifiedNameSyntax qn when qn.Right == name:
            case AliasQualifiedNameSyntax aq when aq.Name == name:
            case MemberBindingExpressionSyntax:
            case NameColonSyntax:
            case NameEqualsSyntax:
                return false;
            case AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ObjectInitializerExpression or (int)SyntaxKind.WithInitializerExpression } } assignment
                when assignment.Left == name:
                return false;
            default:
                return true;
        }
    }

    private static bool IsDeclaredInside(ISymbol symbol, Microsoft.CodeAnalysis.Text.TextSpan span)
        => symbol.DeclaringSyntaxReferences.Any(r => span.Contains(r.Span));

    private static void Report(SyntaxNode node, string what, List<DiagnosticInfo> diagnostics)
        => diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NonPortableExpression, LocationInfo.From(node), node.ToString(), what));
}
