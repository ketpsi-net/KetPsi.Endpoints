using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>
/// Reads configuration classes syntactically (they are never executed):
/// <c>IProtoTypeConfiguration&lt;T&gt;</c>, <c>IOpenProtoTypeConfiguration</c> and <c>IGrpcEndpointConfiguration&lt;THandler&gt;</c>.
/// </summary>
internal static class ConfigParser
{
    private const string AbstractionsNs = "KetPsi.Endpoints.Grpc.Abstractions";
    private const string RuntimeNs = "KetPsi.Endpoints.Grpc";

    public static ConfigResult? Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var decl = (TypeDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(decl, ct) is not INamedTypeSymbol symbol || symbol.IsAbstract)
            return null;
        // Partial classes: process once (first declaration)
        var first = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (first is null || first.SyntaxTree != decl.SyntaxTree || first.Span != decl.Span)
            return null;

        var protoConfigs = new List<ProtoTypeConfigModel>();
        var endpointConfigs = new List<EndpointConfigModel>();
        var diags = new List<DiagnosticInfo>();
        var compilation = ctx.SemanticModel.Compilation;
        var configName = symbol.ToDisplayString();

        foreach (var iface in symbol.AllInterfaces)
        {
            var ns = iface.ContainingNamespace?.ToDisplayString();
            if (iface.Name == "IProtoTypeConfiguration" && ns == AbstractionsNs && iface.TypeArguments.Length == 1)
            {
                var target = iface.TypeArguments[0];
                foreach (var (body, model) in ConfigureBodies(symbol, compilation, ct))
                {
                    var parsed = ParseTypeMap(body, model, target, isOpen: false, configName, diags, ct);
                    protoConfigs.Add(parsed with { ConfigType = configName });
                }
            }
            else if (iface.Name == "IOpenProtoTypeConfiguration" && ns == AbstractionsNs)
            {
                foreach (var (body, model) in ConfigureBodies(symbol, compilation, ct))
                    protoConfigs.AddRange(ParseOpenConfigs(body, model, configName, diags, ct));
            }
            else if (iface.Name == "IGrpcEndpointConfiguration" && ns == RuntimeNs && iface.TypeArguments.Length == 1)
            {
                var handler = iface.TypeArguments[0];
                var interceptors = new List<string>();
                var bindings = new List<BindingModel>();
                foreach (var (body, model) in ConfigureBodies(symbol, compilation, ct))
                    ParseEndpointBody(body, model, interceptors, bindings, diags, ct);
                endpointConfigs.Add(new EndpointConfigModel(
                    TypeGraphBuilder.Fqn(symbol),
                    TypeGraphBuilder.Fqn(handler),
                    interceptors.Distinct(StringComparer.Ordinal).ToEquatableArray(),
                    bindings.ToEquatableArray()));
            }
        }

        if (protoConfigs.Count == 0 && endpointConfigs.Count == 0 && diags.Count == 0)
            return null;
        return new ConfigResult(protoConfigs.ToEquatableArray(), endpointConfigs.ToEquatableArray(), diags.ToEquatableArray());
    }

    private static IEnumerable<(SyntaxNode Body, SemanticModel Model)> ConfigureBodies(INamedTypeSymbol symbol, Compilation compilation, CancellationToken ct)
    {
        foreach (var m in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.Name != "Configure" && !m.Name.EndsWith(".Configure", StringComparison.Ordinal))
                continue;
            if (m.Parameters.Length != 1)
                continue;
            foreach (var r in m.DeclaringSyntaxReferences)
            {
                if (r.GetSyntax(ct) is not MethodDeclarationSyntax md)
                    continue;
                SyntaxNode? body = (SyntaxNode?)md.Body ?? md.ExpressionBody;
                if (body is null)
                    continue;
                yield return (body, compilation.GetSemanticModel(md.SyntaxTree));
            }
        }
    }

    private static string? InvokedName(InvocationExpressionSyntax inv)
        => inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax gn => gn.Identifier.ValueText,
            _ => null,
        };

    // ---------------------------------------------------------------- proto type maps

    private static ProtoTypeConfigModel ParseTypeMap(
        SyntaxNode body, SemanticModel model, ITypeSymbol target, bool isOpen, string configName,
        List<DiagnosticInfo> diags, CancellationToken ct)
    {
        var fields = new List<ConfiguredField>();
        FactoryModel? factory = null;
        var evaluator = new IntEvaluator(model, ct);
        evaluator.Walk(body, inv =>
        {
            var name = InvokedName(inv);
            var args = inv.ArgumentList.Arguments;
            if (name == "Property" && args.Count >= 2)
            {
                var propName = ReadPropertyName(args[0].Expression, model, ct);
                var number = evaluator.Eval(args[1].Expression, sideEffects: true);
                if (propName is null)
                    return true;
                if (number is null || number > int.MaxValue || number < int.MinValue)
                {
                    diags.Add(DiagnosticInfo.Create(Diagnostics.FieldNumberNotConstant, LocationInfo.From(args[1]), configName + "." + propName));
                    return true;
                }
                fields.Add(new ConfiguredField(propName, (int)number.Value, LocationInfo.From(inv)));
                return true;
            }
            if (name == "InstantiateWith")
            {
                factory = ParseFactory(inv, model, target, ct) ?? factory;
                return true;
            }
            return false;
        });

        var key = isOpen ? TypeGraphBuilder.Fqn(((INamedTypeSymbol)target).OriginalDefinition) : TypeGraphBuilder.Fqn(target);
        return new ProtoTypeConfigModel(configName, key, isOpen, fields.ToEquatableArray(), factory, LocationInfo.From(body));
    }

    private static IEnumerable<ProtoTypeConfigModel> ParseOpenConfigs(SyntaxNode body, SemanticModel model, string configName,
        List<DiagnosticInfo> diags, CancellationToken ct)
    {
        foreach (var inv in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (InvokedName(inv) != "ConfigureGeneric" || inv.ArgumentList.Arguments.Count < 2)
                continue;
            if (inv.ArgumentList.Arguments[0].Expression is not TypeOfExpressionSyntax toe)
                continue;
            if (model.GetTypeInfo(toe.Type, ct).Type is not INamedTypeSymbol openType)
                continue;
            SyntaxNode? lambdaBody = inv.ArgumentList.Arguments[1].Expression switch
            {
                SimpleLambdaExpressionSyntax s => s.Body,
                ParenthesizedLambdaExpressionSyntax p => p.Body,
                _ => null,
            };
            if (lambdaBody is null)
                continue;
            yield return ParseTypeMap(lambdaBody, model, openType.OriginalDefinition, isOpen: true, configName, diags, ct);
        }
    }

    private static string? ReadPropertyName(ExpressionSyntax expr, SemanticModel model, CancellationToken ct)
    {
        switch (expr)
        {
            case SimpleLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax m }:
                return m.Name.Identifier.ValueText;
            case ParenthesizedLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax m2 }:
                return m2.Name.Identifier.ValueText;
        }
        return model.GetConstantValue(expr, ct).Value as string;
    }

    private static FactoryModel? ParseFactory(InvocationExpressionSyntax inv, SemanticModel model, ITypeSymbol target, CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0)
            return null;
        var a0 = args[0].Expression;

        if (args.Count >= 2 && a0 is TypeOfExpressionSyntax toe
            && model.GetConstantValue(args[1].Expression, ct).Value is string m2
            && model.GetTypeInfo(toe.Type, ct).Type is INamedTypeSymbol declaring)
        {
            return new FactoryModel(m2, TypeGraphBuilder.Fqn(declaring), ParamNames(FindStatic(declaring, m2)));
        }

        if (model.GetConstantValue(a0, ct).Value is string m1)
            return new FactoryModel(m1, null, ParamNames(FindStatic(target as INamedTypeSymbol, m1)));

        var info = model.GetSymbolInfo(a0, ct);
        if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method && method.IsStatic)
        {
            var containing = method.ContainingType;
            string? declaringType = null;
            if (containing is not null
                && !SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, (target as INamedTypeSymbol)?.OriginalDefinition))
                declaringType = TypeGraphBuilder.Fqn(containing);
            return new FactoryModel(method.Name, declaringType, ParamNames(method));
        }
        return null;
    }

    private static IMethodSymbol? FindStatic(INamedTypeSymbol? type, string name)
        => type?.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);

    private static EquatableArray<string> ParamNames(IMethodSymbol? method)
        => method is null ? EquatableArray<string>.Empty : method.Parameters.Select(p => p.Name).ToEquatableArray();

    // ---------------------------------------------------------------- endpoint configuration

    /// <summary>Parses AddInterceptor / Request&lt;T&gt;(...FromMetadata...) calls (config class bodies and inline MapGrpc lambdas).</summary>
    public static void ParseEndpointBody(SyntaxNode body, SemanticModel model, List<string> interceptors,
        List<BindingModel> bindings, List<DiagnosticInfo> diags, CancellationToken ct)
    {
        foreach (var inv in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            var name = InvokedName(inv);
            if (name == "AddInterceptor")
            {
                ITypeSymbol? type = null;
                var nameSyntax = inv.Expression switch
                {
                    MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g,
                    GenericNameSyntax g => g,
                    _ => null,
                };
                if (nameSyntax is { TypeArgumentList.Arguments.Count: 1 })
                    type = model.GetTypeInfo(nameSyntax.TypeArgumentList.Arguments[0], ct).Type;
                else if (inv.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax toe)
                    type = model.GetTypeInfo(toe.Type, ct).Type;
                if (type is not null && DerivesFrom(type, "Grpc.Core.Interceptors.Interceptor"))
                    interceptors.Add(TypeGraphBuilder.Fqn(type));
                continue;
            }

            if (name != "FromMetadata")
                continue;

            var propInv = inv.Ancestors().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(a => InvokedName(a) == "Property" && a.ArgumentList.Arguments.Count >= 2);
            if (propInv is null)
                continue;
            var propName = ReadPropertyName(propInv.ArgumentList.Arguments[0].Expression, model, ct);
            if (propName is null)
                continue;

            var requestInv = propInv.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault(a =>
                a.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "Request", TypeArgumentList.Arguments.Count: 1 } }
                || a.Expression is GenericNameSyntax { Identifier.ValueText: "Request" });
            var reqGeneric = requestInv?.Expression switch
            {
                MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g,
                GenericNameSyntax g => g,
                _ => null,
            };
            if (reqGeneric is null || model.GetTypeInfo(reqGeneric.TypeArgumentList.Arguments[0], ct).Type is not INamedTypeSymbol requestType)
                continue;

            string? key = null;
            var keyArg = inv.ArgumentList.Arguments.FirstOrDefault();
            if (keyArg is not null)
                key = model.GetConstantValue(keyArg.Expression, ct).Value as string;
            if (string.IsNullOrEmpty(key))
                key = propName;

            var location = LocationInfo.From(propInv);
            var prop = FindProperty(requestType, propName);
            if (prop is null)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidBinding, location, requestType.Name, propName, "property not found"));
                continue;
            }
            if (prop.SetMethod is null || prop.SetMethod.IsInitOnly
                || prop.SetMethod.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidBinding, location, requestType.Name, propName,
                    "the property needs an accessible (non-init) setter"));
                continue;
            }
            var underlying = TypeGraphBuilder.UnwrapNullable(prop.Type);
            var parse = ParseKindOf(underlying);
            if (parse is null)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidBinding, location, requestType.Name, propName,
                    $"type '{underlying.ToDisplayString()}' cannot be parsed from metadata"));
                continue;
            }
            bindings.Add(new BindingModel(
                TypeGraphBuilder.Fqn(requestType),
                propName,
                key!,
                parse.Value,
                TypeGraphBuilder.Fqn(underlying.WithNullableAnnotation(NullableAnnotation.NotAnnotated))));
        }
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var p = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(x => !x.IsStatic && !x.IsIndexer);
            if (p is not null)
                return p;
        }
        return null;
    }

    private static bool DerivesFrom(ITypeSymbol type, string baseName)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == baseName)
                return true;
        }
        return false;
    }

    private static BindingParseKind? ParseKindOf(ITypeSymbol t)
    {
        if (t.TypeKind == TypeKind.Enum)
            return BindingParseKind.Enum;
        switch (t.SpecialType)
        {
            case SpecialType.System_String: return BindingParseKind.String;
            case SpecialType.System_Int32: return BindingParseKind.Int32;
            case SpecialType.System_Int64: return BindingParseKind.Int64;
            case SpecialType.System_Int16: return BindingParseKind.Int16;
            case SpecialType.System_Byte: return BindingParseKind.Byte;
            case SpecialType.System_SByte: return BindingParseKind.SByte;
            case SpecialType.System_UInt16: return BindingParseKind.UInt16;
            case SpecialType.System_UInt32: return BindingParseKind.UInt32;
            case SpecialType.System_UInt64: return BindingParseKind.UInt64;
            case SpecialType.System_Single: return BindingParseKind.Single;
            case SpecialType.System_Double: return BindingParseKind.Double;
            case SpecialType.System_Decimal: return BindingParseKind.Decimal;
            case SpecialType.System_Boolean: return BindingParseKind.Bool;
            case SpecialType.System_Char: return BindingParseKind.Char;
            case SpecialType.System_DateTime: return BindingParseKind.DateTime;
        }
        return t.ToDisplayString() switch
        {
            "System.Guid" => BindingParseKind.Guid,
            "System.DateTimeOffset" => BindingParseKind.DateTimeOffset,
            "System.TimeSpan" => BindingParseKind.TimeSpan,
            "System.DateOnly" => BindingParseKind.DateOnly,
            "System.TimeOnly" => BindingParseKind.TimeOnly,
            _ => null,
        };
    }

    /// <summary>
    /// Tiny evaluator for field numbers: constants, int locals, <c>n++</c>/<c>++n</c>, <c>+ - *</c>, and
    /// simple assignments, applied in C# evaluation order (receiver chain before arguments).
    /// </summary>
    private sealed class IntEvaluator
    {
        private readonly SemanticModel _model;
        private readonly CancellationToken _ct;
        private readonly Dictionary<ISymbol, long?> _locals = new(SymbolEqualityComparer.Default);

        public IntEvaluator(SemanticModel model, CancellationToken ct)
        {
            _model = model;
            _ct = ct;
        }

        /// <summary>Walks in evaluation order; <paramref name="onInvocation"/> returns true when it consumed the arguments.</summary>
        public void Walk(SyntaxNode node, Func<InvocationExpressionSyntax, bool> onInvocation)
        {
            _ct.ThrowIfCancellationRequested();
            switch (node)
            {
                case LocalDeclarationStatementSyntax lds:
                    foreach (var v in lds.Declaration.Variables)
                    {
                        if (v.Initializer is null)
                            continue;
                        Walk(v.Initializer.Value, onInvocation);
                        if (_model.GetDeclaredSymbol(v, _ct) is { } sym)
                            _locals[sym] = Eval(v.Initializer.Value, sideEffects: false);
                    }
                    return;
                case ExpressionStatementSyntax { Expression: PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax or AssignmentExpressionSyntax } es:
                    Eval(es.Expression, sideEffects: true);
                    return;
                case InvocationExpressionSyntax inv:
                    Walk(inv.Expression, onInvocation);
                    if (!onInvocation(inv))
                    {
                        foreach (var a in inv.ArgumentList.Arguments)
                            Walk(a.Expression, onInvocation);
                    }
                    return;
                default:
                    foreach (var child in node.ChildNodes())
                        Walk(child, onInvocation);
                    return;
            }
        }

        public long? Eval(ExpressionSyntax expr, bool sideEffects)
        {
            var constant = _model.GetConstantValue(expr, _ct);
            if (constant.HasValue)
            {
                return constant.Value switch
                {
                    int i => i,
                    long l => l,
                    short s => s,
                    byte b => b,
                    uint u => u,
                    _ => null,
                };
            }

            switch (expr)
            {
                case ParenthesizedExpressionSyntax p:
                    return Eval(p.Expression, sideEffects);
                case IdentifierNameSyntax id:
                    return Symbol(id) is { } s && _locals.TryGetValue(s, out var v) ? v : null;
                case PostfixUnaryExpressionSyntax post when post.Operand is IdentifierNameSyntax pid && Symbol(pid) is { } ps && _locals.TryGetValue(ps, out var pv) && pv is not null:
                {
                    var delta = post.IsKind(SyntaxKind.PostIncrementExpression) ? 1 : -1;
                    if (sideEffects)
                        _locals[ps] = pv + delta;
                    return pv;
                }
                case PrefixUnaryExpressionSyntax pre when pre.Operand is IdentifierNameSyntax qid && Symbol(qid) is { } qs && _locals.TryGetValue(qs, out var qv) && qv is not null
                                                          && (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression)):
                {
                    var next = qv + (pre.IsKind(SyntaxKind.PreIncrementExpression) ? 1 : -1);
                    if (sideEffects)
                        _locals[qs] = next;
                    return next;
                }
                case BinaryExpressionSyntax bin:
                {
                    var l = Eval(bin.Left, sideEffects);
                    var r = Eval(bin.Right, sideEffects);
                    if (l is null || r is null)
                        return null;
                    return bin.Kind() switch
                    {
                        SyntaxKind.AddExpression => l + r,
                        SyntaxKind.SubtractExpression => l - r,
                        SyntaxKind.MultiplyExpression => l * r,
                        _ => null,
                    };
                }
                case AssignmentExpressionSyntax asg when asg.Left is IdentifierNameSyntax aid && Symbol(aid) is { } asym:
                {
                    var rhs = Eval(asg.Right, sideEffects);
                    _locals.TryGetValue(asym, out var cur);
                    long? result = asg.Kind() switch
                    {
                        SyntaxKind.SimpleAssignmentExpression => rhs,
                        SyntaxKind.AddAssignmentExpression => cur + rhs,
                        SyntaxKind.SubtractAssignmentExpression => cur - rhs,
                        _ => null,
                    };
                    if (sideEffects)
                        _locals[asym] = result;
                    return result;
                }
                default:
                    return null;
            }
        }

        private ISymbol? Symbol(IdentifierNameSyntax id) => _model.GetSymbolInfo(id, _ct).Symbol;
    }
}
