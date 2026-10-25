using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Shared by the generator and the DG001 analyzer: resolves MapGroup / WithGroupName receiver chains
/// (including locals, fields and properties, possibly declared in other files) and parses routes.
/// </summary>
internal static class RouteResolver
{
    internal sealed class ReceiverChain
    {
        public List<string> Prefixes { get; } = new();
        public string? Version { get; set; }
        public bool HasNonConstantPrefix { get; set; }
        /// <summary>Convention calls on the receiver chain (excluding MapGroup), in source order.</summary>
        public List<(InvocationExpressionSyntax Invocation, MemberAccessExpressionSyntax Access, SemanticModel Model)> Conventions { get; } = new();
    }

    internal sealed record ParsedRoute(string Package, string Service, string Method);

    public static bool IsMapGrpcMethod(IMethodSymbol method)
        => method.Name.StartsWith("MapGrpc", StringComparison.Ordinal)
           && method.ContainingType?.ToDisplayString() == "KetPsi.Endpoints.Grpc.WebApplicationExtensions";

    public static ReceiverChain ResolveReceiver(InvocationExpressionSyntax mapInvocation, SemanticModel model, CancellationToken ct)
    {
        var chain = new ReceiverChain();
        var visited = new HashSet<SyntaxNode>();
        var collected = new List<(InvocationExpressionSyntax, MemberAccessExpressionSyntax, SemanticModel)>();
        ExpressionSyntax? current = (mapInvocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        var currentModel = model;

        while (current is not null && visited.Add(current))
        {
            ct.ThrowIfCancellationRequested();
            if (current is not ParenthesizedExpressionSyntax && !IsConventionBuilder(currentModel.GetTypeInfo(current, ct).Type))
                break; // e.g. 'app' (WebApplication): nothing above it belongs to the endpoint chain
            switch (current)
            {
                case ParenthesizedExpressionSyntax p:
                    current = p.Expression;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ma } inv:
                {
                    var name = ma.Name.Identifier.ValueText;
                    if (name == "MapGroup")
                    {
                        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                        var value = arg is null ? null : currentModel.GetConstantValue(arg, ct).Value as string;
                        if (value is null)
                            chain.HasNonConstantPrefix = true;
                        else if (value.Trim('/').Length > 0)
                            chain.Prefixes.Add(value.Trim('/'));
                    }
                    else
                    {
                        if (name == "WithGroupName" && chain.Version is null)
                        {
                            var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                            if (arg is not null && currentModel.GetConstantValue(arg, ct).Value is string v && v.Trim().Length > 0)
                                chain.Version = v.Trim();
                        }
                        collected.Add((inv, ma, currentModel));
                    }
                    current = ma.Expression;
                    continue;
                }
                case IdentifierNameSyntax id:
                {
                    var (init, initModel) = ResolveInitializer(id, currentModel, ct);
                    current = init;
                    if (initModel is not null)
                        currentModel = initModel;
                    continue;
                }
                default:
                    current = null;
                    break;
            }
        }

        chain.Prefixes.Reverse();
        collected.Reverse();
        chain.Conventions.AddRange(collected);
        return chain;
    }

    private static bool IsConventionBuilder(ITypeSymbol? type)
    {
        if (type is null)
            return false;
        if (type.ToDisplayString() == "Microsoft.AspNetCore.Builder.WebApplication")
            return false;
        if (type.ToDisplayString() == "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder")
            return true;
        return type.AllInterfaces.Any(i => i.ToDisplayString() == "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder");
    }

    private static (ExpressionSyntax? Expression, SemanticModel? Model) ResolveInitializer(IdentifierNameSyntax id, SemanticModel model, CancellationToken ct)
    {
        var info = model.GetSymbolInfo(id, ct);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (symbol is not (ILocalSymbol or IFieldSymbol or IPropertySymbol))
            return (null, null);

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax(ct);
            ExpressionSyntax? init = node switch
            {
                VariableDeclaratorSyntax vd => vd.Initializer?.Value,
                PropertyDeclarationSyntax pd => pd.Initializer?.Value ?? pd.ExpressionBody?.Expression,
                _ => null,
            };
            if (init is null)
                continue;
            var initModel = init.SyntaxTree == model.SyntaxTree ? model : model.Compilation.GetSemanticModel(init.SyntaxTree);
            return (init, initModel);
        }
        return (null, null);
    }

    public static bool IsRouteParameter(string segment)
        => segment.Length >= 2 && segment[0] == '{' && segment[segment.Length - 1] == '}';

    public static bool IsProtoIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        var c0 = s[0];
        if (!(c0 is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_'))
            return false;
        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Validates the relative route literal. Returns null when valid, otherwise a short reason.
    /// <paramref name="prefixSegmentCount"/> is the number of segments contributed by MapGroup prefixes.
    /// </summary>
    public static string? ValidateRelativeRoute(string route, int prefixSegmentCount)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "the route is empty";
        var segments = route.Split('/');
        if (segments.Any(s => s.Length == 0))
            return "empty segments and leading/trailing '/' are not allowed";
        var meaningful = 0;
        foreach (var s in segments)
        {
            if (IsRouteParameter(s))
                continue;
            if (!IsProtoIdentifier(s))
                return $"segment '{s}' is not a valid protobuf identifier ([A-Za-z_][A-Za-z0-9_]*)";
            meaningful++;
        }
        if (meaningful + prefixSegmentCount < 2)
            return "at least 2 non-parameter segments (Service/Method) are required, counting MapGroup prefixes";
        if (meaningful < 1)
            return "the route must contain the method name";
        return null;
    }

    /// <summary>
    /// Last two non-parameter segments are service + rpc; earlier segments (MapGroup prefixes first) form
    /// the package ("Api" if none), and WithGroupName is appended as the final package segment.
    /// </summary>
    public static ParsedRoute? Parse(IReadOnlyList<string> prefixes, string relativeRoute, string? version)
    {
        var meaningful = new List<string>();
        foreach (var part in prefixes.Concat(new[] { relativeRoute }))
        {
            foreach (var s in part.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsRouteParameter(s))
                    continue;
                if (!IsProtoIdentifier(s))
                    return null;
                meaningful.Add(s);
            }
        }
        if (meaningful.Count < 2)
            return null;
        var method = meaningful[meaningful.Count - 1];
        var service = meaningful[meaningful.Count - 2];
        var package = meaningful.Count == 2 ? "Api" : string.Join(".", meaningful.Take(meaningful.Count - 2));
        if (!string.IsNullOrEmpty(version))
        {
            if (version!.Split('.').Any(v => !IsProtoIdentifier(v)))
                return null;
            package += "." + version;
        }
        return new ParsedRoute(package, service, method);
    }

    public static int CountSegments(IEnumerable<string> prefixes)
        => prefixes.Sum(p => p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Count(s => !IsRouteParameter(s)));

    public static string? InvalidPrefixSegment(IEnumerable<string> prefixes, string? version)
    {
        foreach (var p in prefixes)
        {
            foreach (var s in p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IsRouteParameter(s) && !IsProtoIdentifier(s))
                    return s;
            }
        }
        if (version is not null)
        {
            foreach (var v in version.Split('.'))
            {
                if (!IsProtoIdentifier(v))
                    return version;
            }
        }
        return null;
    }

    public static string EscapeKeyword(string identifier)
        => SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
}
