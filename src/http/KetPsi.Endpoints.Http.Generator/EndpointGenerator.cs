using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.Endpoints.Http.Generator;

[Generator]
public partial class EndpointGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor GenerationFailedDescriptor = new(
        id: "KP001",
        title: "Source generation failed",
        messageFormat: "Source generation failed: {0}",
        category: "SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoEndpointToGenerateDescriptor = new(
        id: "KP002",
        title: "endpoint generation",
        messageFormat: "No endpoint to generate",
        category: "SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static bool CreateSyntaxProviderPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax maes }
            && IsMapMethod(maes.Name.Identifier.ValueText);
    }

    static bool CreateHandlerSyntaxProviderPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 };
    }

    static HandlerGenerationResult? CreateHandlerSyntaxProviderTransform(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, cancellationToken) is not INamedTypeSymbol classSymbol)
            return null;

        if (classSymbol.IsAbstract)
            return null;

        var implementsIEndpoint = classSymbol.AllInterfaces.Any(i => i.Name == "IEndpoint");
        if (!implementsIEndpoint)
            return null;

        var executeMethod = classSymbol.GetMembers("ExecuteAsync")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.DeclaredAccessibility == Accessibility.Public);

        if (executeMethod == null)
        {
            var diag = Diagnostic.Create(
                Diagnostics.MissingExecuteAsyncMethod,
                classDecl.Identifier.GetLocation(),
                classSymbol.Name);
            return new HandlerGenerationResult(null, [diag]);
        }

        var handlerDisplayName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var handlerAccessibility = GetEffectiveAccessibility(classSymbol);

        var parameters = executeMethod.Parameters.Select(p => new BuilderParameterToGenerate(
            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            p.Name,
            GetEffectiveAccessibility(p.Type)
        )).ToList();

        var handlerInfo = new HandlerBuilderToGenerate(
            handlerDisplayName,
            handlerAccessibility,
            parameters);

        return new HandlerGenerationResult(handlerInfo, []);
    }

    static List<GenerationResult> CreateSyntaxProviderTransform(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
    {
        var results = new List<GenerationResult>();
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax endpointMemberAccess)
        {
            return results;
        }
        var baseExpression = endpointMemberAccess.Expression;

        var symbol = ctx.SemanticModel.GetSymbolInfo(baseExpression).Symbol;
        if (symbol is IParameterSymbol parameter)
        {
            var callSiteInvocations = FindAllCallSitesForParameter(parameter, ctx.SemanticModel.Compilation);
            if (callSiteInvocations.Count == 0 && parameter.Type.ToDisplayString() == "Microsoft.AspNetCore.Builder.WebApplication")
            {
                var result = GenerateForContext(invocation, ctx, null);
                if (result != null)
                {
                    results.Add(result);
                }
                return results;
            }
            foreach (var callSite in callSiteInvocations)
            {
                var argExpression = callSite.ArgumentList.Arguments[parameter.Ordinal].Expression;
                var result = GenerateForContext(invocation, ctx, argExpression);
                if (result != null)
                {
                    results.Add(result);
                }
            }
            return results;
        }
        else
        {
            var result = GenerateForContext(invocation, ctx, null);
            if (result != null)
            {
                results.Add(result);
            }
            return results;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpointProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: CreateSyntaxProviderPredicate,
            transform: CreateSyntaxProviderTransform)
            .SelectMany((list, _) => list)
            .Where(x => x != null)
            .Collect();

        var handlerProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: CreateHandlerSyntaxProviderPredicate,
            transform: CreateHandlerSyntaxProviderTransform)
            .Where(x => x != null)
            .Collect();

        var provider = endpointProvider
            .Combine(handlerProvider)
            .Combine(context.CompilationProvider)
            .Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(provider, static (spc, source) =>
        {
            try
            {
                var endpoints = source.Left.Left.Left;
                var handlers = source.Left.Left.Right;

                // 1. Report diagnostics from IEndpoint class declarations (e.g. KP003)
                if (!handlers.IsDefaultOrEmpty)
                {
                    foreach (var diag in handlers.SelectMany(h => h!.Diagnostics))
                    {
                        spc.ReportDiagnostic(diag);
                    }
                }

                // 2. Report diagnostics from Map[Verb] invocations
                if (!endpoints.IsDefaultOrEmpty)
                {
                    foreach (var diag in endpoints.SelectMany(r => r!.Diagnostics))
                    {
                        spc.ReportDiagnostic(diag);
                    }
                }

                var validEndpoints = endpoints.IsDefaultOrEmpty
                    ? new List<EndpointToGenerate?>()
                    : endpoints
                        .Where(r => r!.Endpoint != null && !r.Endpoint.SkipApiGeneration)
                        .Select(r => r!.Endpoint)
                        .Distinct()
                        .ToList();

                // 3. Collect all handlers to generate HttpEndpointBuilder extensions for
                var buildersByType = new Dictionary<string, HandlerBuilderToGenerate>(StringComparer.Ordinal);

                if (!handlers.IsDefaultOrEmpty)
                {
                    foreach (var h in handlers)
                    {
                        if (h?.Handler != null && !buildersByType.ContainsKey(h.Handler.HandlerTypeName))
                        {
                            buildersByType[h.Handler.HandlerTypeName] = h.Handler;
                        }
                    }
                }

                foreach (var ep in validEndpoints)
                {
                    if (ep != null && !buildersByType.ContainsKey(ep.HandlerTypeName))
                    {
                        buildersByType[ep.HandlerTypeName] = new HandlerBuilderToGenerate(
                            ep.HandlerTypeName,
                            ep.HandlerAccessibility,
                            ep.Parameters.Select(p => new BuilderParameterToGenerate(p.TypeName, p.Name, p.ParameterAccessibility)).ToList());
                    }
                }

                var usedBuilderNames = new HashSet<string>();
                foreach (var builderInfo in buildersByType.Values)
                {
                    var handlerClassName = Sanitize(builderInfo.HandlerTypeName.Split('.').Last());
                    if (usedBuilderNames.Contains(handlerClassName))
                    {
                        int counter = 1;
                        while (usedBuilderNames.Contains(handlerClassName + counter)) counter++;
                        handlerClassName = handlerClassName + counter;
                    }
                    usedBuilderNames.Add(handlerClassName);

                    var builderCode = GenerateBuilderExtensionForHandler(handlerClassName, builderInfo);
                    if (!string.IsNullOrWhiteSpace(builderCode))
                    {
                        spc.AddSource($"{handlerClassName}HttpEndpointBuilder.g.cs", builderCode);
                    }
                }

                // 4. Emit Minimal API mappings & DI registrations if any endpoints are mapped
                if (!validEndpoints.Any())
                {
                    if (!endpoints.IsDefaultOrEmpty)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(NoEndpointToGenerateDescriptor, Location.None));
                    }
                    return;
                }

                var groupedEndpoints = validEndpoints.GroupBy(e => e!.ExtensionClassName).ToList();
                var masterMapCalls = new List<string>();
                var masterAddCalls = new List<string>();
                var usedNames = new HashSet<string>();

                foreach (var group in groupedEndpoints)
                {
                    var className = Sanitize(group.Key);

                    if (usedNames.Contains(className))
                    {
                        int counter = 1;
                        while (usedNames.Contains(className + counter)) counter++;
                        className = className + counter;
                    }
                    usedNames.Add(className);

                    var groupCode = GenerateProjectEndpointExtensionsForGroup(className, group.ToList());
                    spc.AddSource($"{className}HttpEndpoints.g.cs", groupCode);

                    masterMapCalls.Add($"            app.Map{className}Endpoints();");
                    masterAddCalls.Add($"            services.Add{className}HttpEndpoints();");
                }

                var masterCode = GenerateMasterExtension(masterMapCalls, masterAddCalls);
                spc.AddSource("HttpEndpoints.g.cs", masterCode);
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(GenerationFailedDescriptor, Location.None, ex.ToString()));
            }
        });
    }

    private static GenerationResult? GenerateForContext(InvocationExpressionSyntax invocation, GeneratorSyntaxContext ctx, ExpressionSyntax? resolvedArgument)
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

            if (methodSymbol == null && symbolInfo.CandidateSymbols.Any())
            {
                methodSymbol = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault(m => IsMapMethod(m.Name) && m.IsGenericMethod);
            }

            if (methodSymbol == null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.CouldNotResolveMethodSymbol, invocation.GetLocation(), invocation.Expression.ToString()));
                return new GenerationResult(null, diagnostics);
            }

            if (!methodSymbol.IsGenericMethod)
            {
                return null;
            }

            var handlerType = methodSymbol.TypeArguments.FirstOrDefault();
            if (handlerType == null) return null;

            var executeMethod = handlerType.GetMembers("ExecuteAsync")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.DeclaredAccessibility == Accessibility.Public);

            if (executeMethod == null)
            {
                bool isLocalIEndpoint = handlerType.Locations.Any(l => l.IsInSource) &&
                                        handlerType.AllInterfaces.Any(i => i.Name == "IEndpoint");
                if (!isLocalIEndpoint)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.MissingExecuteAsyncMethod, invocation.GetLocation(), handlerType.Name));
                }
                return new GenerationResult(null, diagnostics);
            }

            bool isStatic = executeMethod.IsStatic;
            var assemblyName = ctx.SemanticModel.Compilation.AssemblyName ?? "UnknownAssembly";
            var sourceFileUsings = new HashSet<string>(ctx.Node.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Select(u => u.ToString()));

            var classDeclaration = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            var extensionClassName = classDeclaration != null ? classDeclaration.Identifier.ValueText : "DefaultEndpoints";

            var (endpointRoute, endpointRouteExpression, endpointChains, endpointName, skipApigeneration, usingStatements, endpointDiagMessages) = ExtractRouteAndChains(invocation, ctx.SemanticModel);
            diagnostics.AddRange(endpointDiagMessages.Select(m => CreateDiagnosticInfo(m, invocation.GetLocation())));

            if (string.IsNullOrEmpty(endpointRoute) && string.IsNullOrEmpty(endpointRouteExpression)) return null;

            var obsoleteAttribute = executeMethod.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "ObsoleteAttribute" && attr.AttributeClass?.ContainingNamespace?.Name == "System")
                ?? handlerType.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "ObsoleteAttribute" && attr.AttributeClass?.ContainingNamespace?.Name == "System");

            string? obsoleteMessage = null;
            bool isObsolete = obsoleteAttribute != null;

            if (isObsolete && obsoleteAttribute!.ConstructorArguments.Length > 0)
            {
                obsoleteMessage = obsoleteAttribute.ConstructorArguments[0].Value as string;
            }

            var handlerDisplayName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var handlerShortName = Sanitize(handlerType.Name);
            var handlerAccessibility = GetEffectiveAccessibility(handlerType);

            // Extract external configurations from IHttpEndpointConfiguration<THandler>
            var externalParamConfigs = ExtractExternalParameterConfigurations(handlerType, executeMethod, ctx.SemanticModel.Compilation);

            var parameters = new List<ParameterToGenerate>();

            foreach (var p in executeMethod.Parameters)
            {
                var inlineAttrs = p.GetAttributes()
                    .Select(attr => attr.ApplicationSyntaxReference?.GetSyntax().ToString())
                    .Where(s => s != null)
                    .ToList()!;

                var paramTypeDisplay = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated;
                var paramAccessibility = GetEffectiveAccessibility(p.Type);

                if (externalParamConfigs.TryGetValue(p.Name, out var extConfig))
                {
                    if (extConfig.IsAsParameters && extConfig.PropertyAttributes.Count > 0 && p.Type is INamedTypeSymbol namedParamType)
                    {
                        var surrogateInfo = BuildAsParametersSurrogate(handlerShortName, p.Name, namedParamType, extConfig.PropertyAttributes);
                        if (surrogateInfo != null)
                        {
                            var surrogateAttrs = new List<string>(inlineAttrs);
                            if (!surrogateAttrs.Any(a => a.Contains("AsParameters")))
                            {
                                surrogateAttrs.Add("Microsoft.AspNetCore.Http.AsParameters");
                            }

                            parameters.Add(new ParameterToGenerate(
                                paramTypeDisplay,
                                p.Name,
                                surrogateAttrs,
                                isNullable,
                                paramAccessibility,
                                surrogateInfo));
                            continue;
                        }
                    }

                    foreach (var attr in extConfig.DirectAttributes)
                    {
                        if (!inlineAttrs.Contains(attr))
                        {
                            inlineAttrs.Add(attr);
                        }
                    }
                }

                parameters.Add(new ParameterToGenerate(
                    paramTypeDisplay,
                    p.Name,
                    inlineAttrs,
                    isNullable,
                    paramAccessibility,
                    null));
            }

            var (groupDefinition, groupSteps, groupName, usesVersioning, diagMessages) = ExtractGroupDefinitionExpression(invocation, ctx.SemanticModel, resolvedArgument);
            diagnostics.AddRange(diagMessages.Select(m => CreateDiagnosticInfo(m, invocation.GetLocation())));
            foreach (var u in usingStatements)
                sourceFileUsings.Add(u);

            var endpoint = new EndpointToGenerate(
                assemblyName,
                handlerDisplayName,
                handlerAccessibility,
                isStatic,
                endpointRoute,
                endpointRouteExpression,
                methodSymbol.Name,
                endpointChains,
                groupDefinition,
                groupSteps,
                parameters,
                sourceFileUsings,
                groupName,
                endpointName,
                skipApigeneration,
                usesVersioning,
                obsoleteMessage,
                extensionClassName);

            return new GenerationResult(endpoint, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Diagnostic.Create(new DiagnosticDescriptor("SG000", "EndpointGenerator Crash", $"Generator crashed with exception: {ex}", "Generator.Error", DiagnosticSeverity.Error, true), Location.None));
            return new GenerationResult(null, diagnostics);
        }
    }

    #region IHttpEndpointConfiguration & AsParameters Surrogate Analysis

    private sealed class ExternalParameterConfig
    {
        public bool IsAsParameters { get; set; }
        public List<string> DirectAttributes { get; } = new();
        public Dictionary<string, List<string>> PropertyAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetEffectiveAccessibility(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null || typeSymbol is IErrorTypeSymbol)
            return "internal";

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return GetEffectiveAccessibility(arrayType.ElementType);
        }

        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                if (GetEffectiveAccessibility(typeArg) == "internal")
                    return "internal";
            }
        }

        ITypeSymbol? current = typeSymbol;
        while (current != null)
        {
            var syntaxRef = current.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is MemberDeclarationSyntax memberDecl)
            {
                if (!memberDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
                {
                    return "internal";
                }
            }
            else if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return "internal";
            }

            current = current.ContainingType;
        }

        return "public";
    }

    private static Dictionary<string, ExternalParameterConfig> ExtractExternalParameterConfigurations(
        ITypeSymbol handlerType,
        IMethodSymbol executeMethod,
        Compilation compilation)
    {
        var result = new Dictionary<string, ExternalParameterConfig>(StringComparer.Ordinal);

        var pascalToParamName = executeMethod.Parameters.ToDictionary(
            p => ToPascalCase(p.Name),
            p => p.Name,
            StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var classDeclarations = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDecl in classDeclarations)
            {
                if (semanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
                    continue;

                var implementsConfig = classSymbol.AllInterfaces.Any(i =>
                    i.Name == "IHttpEndpointConfiguration" &&
                    i.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], handlerType));

                if (!implementsConfig)
                    continue;

                var configureMethodSyntax = classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.ValueText == "Configure" && m.ParameterList.Parameters.Count == 1);

                if (configureMethodSyntax == null)
                    continue;

                var builderParamName = configureMethodSyntax.ParameterList.Parameters[0].Identifier.ValueText;

                foreach (var invocation in configureMethodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                        continue;

                    var methodName = memberAccess.Name.Identifier.ValueText;

                    var rootParamPascal = FindTargetParameterMethodName(memberAccess.Expression, builderParamName);
                    if (rootParamPascal == null || !pascalToParamName.TryGetValue(rootParamPascal, out var actualParamName))
                        continue;

                    if (!result.TryGetValue(actualParamName, out var paramConfig))
                    {
                        paramConfig = new ExternalParameterConfig();
                        result[actualParamName] = paramConfig;
                    }

                    if (methodName == "AsParameters")
                    {
                        paramConfig.IsAsParameters = true;
                        if (!paramConfig.DirectAttributes.Contains("Microsoft.AspNetCore.Http.AsParameters"))
                        {
                            paramConfig.DirectAttributes.Add("Microsoft.AspNetCore.Http.AsParameters");
                        }
                    }
                    else if (methodName == "Property" && invocation.ArgumentList.Arguments.Count == 2)
                    {
                        paramConfig.IsAsParameters = true;
                        var propSelectorExpr = invocation.ArgumentList.Arguments[0].Expression;
                        var propConfigureExpr = invocation.ArgumentList.Arguments[1].Expression;

                        var propName = ExtractPropertyNameFromSelector(propSelectorExpr);
                        if (propName != null)
                        {
                            var propAttrs = ExtractAttributesFromNestedLambda(propConfigureExpr);
                            if (propAttrs.Count > 0)
                            {
                                if (!paramConfig.PropertyAttributes.TryGetValue(propName, out var list))
                                {
                                    list = new List<string>();
                                    paramConfig.PropertyAttributes[propName] = list;
                                }
                                foreach (var a in propAttrs)
                                {
                                    if (!list.Contains(a)) list.Add(a);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (IsInsidePropertyActionLambda(invocation, configureMethodSyntax))
                            continue;

                        var attr = MapConfigurationCallToAttribute(methodName, invocation.ArgumentList.Arguments);
                        if (attr != null && !paramConfig.DirectAttributes.Contains(attr))
                        {
                            paramConfig.DirectAttributes.Add(attr);
                        }
                    }
                }
            }
        }

        return result;
    }

    private static bool IsInsidePropertyActionLambda(SyntaxNode node, SyntaxNode stopAtNode)
    {
        var current = node.Parent;
        while (current != null && current != stopAtNode)
        {
            if (current is LambdaExpressionSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }

    private static string? ExtractPropertyNameFromSelector(ExpressionSyntax selector)
    {
        if (selector is LambdaExpressionSyntax lambda)
        {
            var maes = lambda.Body.DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>()
                .LastOrDefault();

            return maes?.Name.Identifier.ValueText;
        }
        return null;
    }

    private static List<string> ExtractAttributesFromNestedLambda(ExpressionSyntax lambdaExpr)
    {
        var attrs = new List<string>();
        foreach (var inv in lambdaExpr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax maes)
            {
                var attr = MapConfigurationCallToAttribute(maes.Name.Identifier.ValueText, inv.ArgumentList.Arguments);
                if (attr != null && !attrs.Contains(attr))
                {
                    attrs.Add(attr);
                }
            }
        }
        return attrs;
    }

    private static string? FindTargetParameterMethodName(ExpressionSyntax expression, string builderParamName)
    {
        var current = expression;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax maes)
            {
                if (maes.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == builderParamName)
                {
                    return maes.Name.Identifier.ValueText;
                }
                current = maes.Expression;
            }
            else if (current is MemberAccessExpressionSyntax outerMaes)
            {
                current = outerMaes.Expression;
            }
            else
            {
                break;
            }
        }
        return null;
    }

    private static string? MapConfigurationCallToAttribute(string methodName, SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var firstArg = args.FirstOrDefault()?.Expression.ToString();

        return methodName switch
        {
            "FromHeader" => string.IsNullOrEmpty(firstArg)
                ? "Microsoft.AspNetCore.Mvc.FromHeader"
                : $"Microsoft.AspNetCore.Mvc.FromHeader(Name = {firstArg})",
            "FromRoute" => string.IsNullOrEmpty(firstArg)
                ? "Microsoft.AspNetCore.Mvc.FromRoute"
                : $"Microsoft.AspNetCore.Mvc.FromRoute(Name = {firstArg})",
            "FromQuery" => string.IsNullOrEmpty(firstArg)
                ? "Microsoft.AspNetCore.Mvc.FromQuery"
                : $"Microsoft.AspNetCore.Mvc.FromQuery(Name = {firstArg})",
            "FromForm" => string.IsNullOrEmpty(firstArg)
                ? "Microsoft.AspNetCore.Mvc.FromForm"
                : $"Microsoft.AspNetCore.Mvc.FromForm(Name = {firstArg})",
            "FromBody" => "Microsoft.AspNetCore.Mvc.FromBody",
            "FromServices" => "Microsoft.AspNetCore.Mvc.FromServices",
            "FromKeyedServices" when !string.IsNullOrEmpty(firstArg) => $"Microsoft.Extensions.DependencyInjection.FromKeyedServices({firstArg})",
            "AsParameters" => "Microsoft.AspNetCore.Http.AsParameters",
            _ => null
        };
    }

    private static AsParametersSurrogateInfo? BuildAsParametersSurrogate(
        string handlerShortName,
        string parameterName,
        INamedTypeSymbol dtoType,
        Dictionary<string, List<string>> configuredPropertyAttributes)
    {
        if (dtoType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && dtoType.TypeArguments.Length == 1 && dtoType.TypeArguments[0] is INamedTypeSymbol innerType)
        {
            dtoType = innerType;
        }

        var dtoFullTypeName = dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var surrogateStructName = $"__{handlerShortName}_{Sanitize(parameterName)}_AsParameters";
        var structAccessibility = GetEffectiveAccessibility(dtoType);

        var targetCtor = dtoType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public || c.DeclaredAccessibility == Accessibility.Internal)
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, dtoType)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        var allProperties = dtoType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
            .ToList();

        var propByNameIgnoreCase = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in allProperties)
        {
            if (!propByNameIgnoreCase.ContainsKey(prop.Name))
                propByNameIgnoreCase[prop.Name] = prop;
        }

        var surrogateFields = new List<SurrogatePropertyInfo>();
        var ctorArgs = new List<string>();
        var matchedPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (targetCtor != null)
        {
            foreach (var ctorParam in targetCtor.Parameters)
            {
                if (GetEffectiveAccessibility(ctorParam.Type) == "internal")
                {
                    structAccessibility = "internal";
                }

                propByNameIgnoreCase.TryGetValue(ctorParam.Name, out var matchingProp);
                var canonicalName = matchingProp?.Name ?? ctorParam.Name;
                matchedPropNames.Add(canonicalName);

                var attrs = new List<string>();

                foreach (var a in ctorParam.GetAttributes())
                {
                    var syn = a.ApplicationSyntaxReference?.GetSyntax().ToString();
                    if (syn != null && !attrs.Contains(syn)) attrs.Add(syn);
                }
                if (matchingProp != null)
                {
                    foreach (var a in matchingProp.GetAttributes())
                    {
                        var syn = a.ApplicationSyntaxReference?.GetSyntax().ToString();
                        if (syn != null && !attrs.Contains(syn)) attrs.Add(syn);
                    }
                }

                if (configuredPropertyAttributes.TryGetValue(canonicalName, out var extAttrs))
                {
                    foreach (var ea in extAttrs)
                    {
                        if (!attrs.Contains(ea)) attrs.Add(ea);
                    }
                }

                var typeStr = ctorParam.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (ctorParam.Type.NullableAnnotation == NullableAnnotation.Annotated && !typeStr.EndsWith("?"))
                {
                    typeStr += "?";
                }

                string? defaultValueExpr = null;
                if (ctorParam.HasExplicitDefaultValue)
                {
                    defaultValueExpr = FormatDefaultValue(ctorParam.ExplicitDefaultValue, ctorParam.Type);
                }

                surrogateFields.Add(new SurrogatePropertyInfo(canonicalName, typeStr, attrs, defaultValueExpr));
                ctorArgs.Add($"this.@{canonicalName}");
            }
        }

        var initializerAssignments = new List<string>();
        foreach (var prop in allProperties)
        {
            if (matchedPropNames.Contains(prop.Name))
                continue;

            if (prop.SetMethod == null || prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (GetEffectiveAccessibility(prop.Type) == "internal")
            {
                structAccessibility = "internal";
            }

            var attrs = new List<string>();
            foreach (var a in prop.GetAttributes())
            {
                var syn = a.ApplicationSyntaxReference?.GetSyntax().ToString();
                if (syn != null && !attrs.Contains(syn)) attrs.Add(syn);
            }

            if (configuredPropertyAttributes.TryGetValue(prop.Name, out var extAttrs))
            {
                foreach (var ea in extAttrs)
                {
                    if (!attrs.Contains(ea)) attrs.Add(ea);
                }
            }

            var typeStr = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (prop.Type.NullableAnnotation == NullableAnnotation.Annotated && !typeStr.EndsWith("?"))
            {
                typeStr += "?";
            }

            surrogateFields.Add(new SurrogatePropertyInfo(prop.Name, typeStr, attrs, null));
            initializerAssignments.Add($"{prop.Name} = this.@{prop.Name}");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"        {structAccessibility} struct {surrogateStructName}");
        sb.AppendLine("        {");
        sb.AppendLine($"            public {surrogateStructName}() {{ }}");

        foreach (var field in surrogateFields)
        {
            foreach (var attr in field.Attributes)
            {
                sb.AppendLine($"            [{attr}]");
            }
            var initPart = field.DefaultValueExpression != null
                ? $" = {field.DefaultValueExpression};"
                : " = default!;";
            sb.AppendLine($"            public {field.TypeName} @{field.Name} {{ get; set; }}{initPart}");
        }

        var ctorCall = $"new {dtoFullTypeName}({string.Join(", ", ctorArgs)})";
        if (initializerAssignments.Count > 0)
        {
            ctorCall += $" {{ {string.Join(", ", initializerAssignments)} }}";
        }

        sb.AppendLine($"            public {dtoFullTypeName} __ToModel() => {ctorCall};");
        sb.AppendLine("        }");

        return new AsParametersSurrogateInfo(surrogateStructName, sb.ToString());
    }

    private static string FormatDefaultValue(object? value, ITypeSymbol type)
    {
        if (value is null) return "default!";
        if (value is string s) return SymbolDisplay.FormatLiteral(s, true);
        if (value is char c) return SymbolDisplay.FormatLiteral(c, true);
        if (value is bool b) return b ? "true" : "false";
        if (type.TypeKind == TypeKind.Enum)
        {
            return $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Convert.ToString(value, CultureInfo.InvariantCulture)}";
        }
        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }
        return value.ToString();
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsUpper(name[0])) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    #endregion

    #region Analysis and Helper Methods
    private static (string GroupDefinition, List<string> GroupSteps, string GroupName, bool UsesVersioning, List<string> DiagMessages)
        ExtractGroupDefinitionExpression(InvocationExpressionSyntax endpointInvocation, SemanticModel semanticModel, ExpressionSyntax? resolvedArgument)
    {
        var diag = new List<string>();
        var groupSteps = new List<string>();
        bool usesVersioning = false;

        if (endpointInvocation.Expression is not MemberAccessExpressionSyntax endpointMemberAccess)
        {
            return (string.Empty, groupSteps, string.Empty, false, diag);
        }

        SyntaxNode? syntaxThatDefinesTheGroup = resolvedArgument ?? endpointMemberAccess.Expression;

        if (resolvedArgument != null && resolvedArgument.SyntaxTree != semanticModel.SyntaxTree)
        {
            semanticModel = semanticModel.Compilation.GetSemanticModel(resolvedArgument.SyntaxTree);
        }

        var groupNames = new List<string>();
        var iterationGuard = 0;

        while (syntaxThatDefinesTheGroup != null && iterationGuard++ < 20)
        {
            while (syntaxThatDefinesTheGroup is IdentifierNameSyntax identifier && iterationGuard++ < 20)
            {
                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is ILocalSymbol local)
                {
                    var decl = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (decl is VariableDeclaratorSyntax vds && vds.Initializer?.Value != null)
                    {
                        syntaxThatDefinesTheGroup = vds.Initializer.Value;
                        if (syntaxThatDefinesTheGroup.SyntaxTree != semanticModel.SyntaxTree)
                        {
                            semanticModel = semanticModel.Compilation.GetSemanticModel(syntaxThatDefinesTheGroup.SyntaxTree);
                        }
                        continue;
                    }
                }
                break;
            }

            if (syntaxThatDefinesTheGroup == null)
                break;

            var groupMappingInvocations = syntaxThatDefinesTheGroup.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Select(inv => (Invocation: inv, MemberAccess: inv.Expression as MemberAccessExpressionSyntax))
                .Where(x => x.MemberAccess != null && (x.MemberAccess.Name.Identifier.ValueText == "MapGroup" || x.MemberAccess.Name.Identifier.ValueText == "MapApiVersionedGroup"))
                .ToList();

            if (!groupMappingInvocations.Any())
                break;

            if (groupMappingInvocations.Any(x => x.MemberAccess!.Name.Identifier.ValueText == "MapApiVersionedGroup"))
            {
                usesVersioning = true;
            }

            var firstGroupInv = groupMappingInvocations.First().Invocation;
            if (firstGroupInv.ArgumentList.Arguments.FirstOrDefault()?.Expression is ExpressionSyntax nameArgument)
            {
                var constName = semanticModel.GetConstantValue(nameArgument);
                if (constName.HasValue && constName.Value is string name && !string.IsNullOrEmpty(name))
                {
                    groupNames.Add(name);
                }
            }

            var rootNode = GetRootOfChain(syntaxThatDefinesTheGroup);
            if (rootNode == null)
                break;

            var sourceText = syntaxThatDefinesTheGroup.SyntaxTree.GetText();
            var chains = sourceText.ToString(TextSpan.FromBounds(rootNode.Span.End, syntaxThatDefinesTheGroup.Span.End));
            groupSteps.Add(chains);

            if (rootNode is IdentifierNameSyntax parentIdentifier)
            {
                var parentSymbol = semanticModel.GetSymbolInfo(parentIdentifier).Symbol;
                if (parentSymbol is ILocalSymbol)
                {
                    syntaxThatDefinesTheGroup = parentIdentifier;
                    continue;
                }
            }

            break;
        }

        if (groupSteps.Count == 0)
        {
            return (string.Empty, groupSteps, string.Empty, false, diag);
        }

        groupSteps.Reverse();
        groupNames.Reverse();

        var finalDefinition = "app" + string.Concat(groupSteps);
        var groupName = string.Join("/", groupNames);

        return (finalDefinition, groupSteps, groupName, usesVersioning, diag);
    }

    private static List<InvocationExpressionSyntax> FindAllCallSitesForParameter(IParameterSymbol parameter, Compilation compilation)
    {
        var callSites = new List<InvocationExpressionSyntax>();
        if (parameter.ContainingSymbol is not IMethodSymbol containingMethod) return callSites;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol invokedSymbol && SymbolEqualityComparer.Default.Equals(invokedSymbol, containingMethod))
                    callSites.Add(invocation);
            }
        }
        return callSites;
    }

    private static Diagnostic CreateDiagnosticInfo(string message, Location location) => Diagnostic.Create(
        new DiagnosticDescriptor("SGINFO", "Generator Trace", message, "Generator.Trace", DiagnosticSeverity.Warning, true), location);

    private static (string RouteValue, string RouteExpression, string Chains, string Name, bool SkipApiGeneration, List<string> usingStatements, List<string> DiagMessages)
        ExtractRouteAndChains(InvocationExpressionSyntax baseMapInvocation, SemanticModel semanticModel)
    {
        var resolvedEndpoints = new Dictionary<string, List<ResolvedEndpointInfo>>();
        var indexOfCall = new Dictionary<string, int>();
        var usingStatements = new List<string>();
        var diagMessages = new List<string>();
        string routeValue = "", routeExpression = "", endpointName = "";
        bool skipApiGeneration = false;
        var chainsBuilder = new StringBuilder();
        var routeArgumentExpression = baseMapInvocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (routeArgumentExpression != null)
        {
            routeExpression = routeArgumentExpression.ToString();
            var constantValue = semanticModel.GetConstantValue(routeArgumentExpression);
            if (constantValue.HasValue && constantValue.Value is string r) routeValue = r;
        }
        var currentInvocation = baseMapInvocation;

        while (currentInvocation.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax parentInvocation } memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.ValueText;
            if (memberName == "WithName" && string.IsNullOrEmpty(endpointName))
            {
                var nameArg = parentInvocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (nameArg != null)
                {
                    string? endpointNameExpression;
                    var constName = semanticModel.GetConstantValue(nameArg);
                    if (constName.HasValue && constName.Value is string s)
                    {
                        endpointNameExpression = $"\"{s}\"";
                    }
                    else
                    {
                        endpointNameExpression = nameArg.ToFullString();

                        if (nameArg is InvocationExpressionSyntax invocation)
                        {
                            for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
                            {
                                ArgumentSyntax? item = invocation.ArgumentList.Arguments[i];
                                var typeInfo = semanticModel.GetTypeInfo(item.Expression);
                                var typeSymbol = typeInfo.Type;
                                if (typeSymbol is not null)
                                {
                                    var ns = typeSymbol.ContainingNamespace?.ToDisplayString();
                                    if (!string.IsNullOrWhiteSpace(ns) && !usingStatements.Contains(ns!))
                                        usingStatements.Add(ns!);
                                }
                            }

                            var res = EndpointResolver.ResolveNamesFromCallSites(parentInvocation, semanticModel).ToList();
                            if (res.Count > 0)
                            {
                                if (!resolvedEndpoints.TryGetValue(res[0].ResolvedName, out var _))
                                    resolvedEndpoints[res[0].ResolvedName] = [.. res];
                                if (!indexOfCall.TryGetValue(res[0].ResolvedName, out var idx))
                                    indexOfCall[res[0].ResolvedName] = 0;

                                if (res.Count >= idx + 1)
                                {
                                    var currentCall = res[idx];
                                    indexOfCall[res[0].ResolvedName] = idx + 1;
                                    var arguments = currentCall.CallSite.ArgumentList.Arguments;
                                    for (int j = 0; j < arguments.Count; j++)
                                    {
                                        var arg = invocation.ArgumentList.Arguments[j];
                                        ArgumentSyntax? argument = arguments[j];
                                        if (arg.Expression is IdentifierNameSyntax)
                                        {
                                            endpointNameExpression = endpointNameExpression.Replace(arg.ToFullString(), argument.ToFullString());
                                        }
                                    }
                                }
                            }
                        }
                    }
                    endpointName = endpointNameExpression;
                }
            }
            else if (memberName == "SkipEndpointGeneration")
            {
                skipApiGeneration = true;
            }
            else
            {
                var sourceText = parentInvocation.SyntaxTree.GetText();
                var chainSegment = sourceText.ToString(TextSpan.FromBounds(memberAccess.OperatorToken.SpanStart, parentInvocation.Span.End));
                chainsBuilder.Append(chainSegment);
            }

            currentInvocation = parentInvocation;
        }
       
        return (routeValue, routeExpression, chainsBuilder.ToString().Trim(), endpointName ?? string.Empty, skipApiGeneration, usingStatements, diagMessages);
    }

    private static bool IsMapMethod(string methodName) => methodName switch
    {
        "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" => true,
        _ => false
    };

    private static string Sanitize(string name) => string.IsNullOrWhiteSpace(name) ? string.Empty : new([.. name.Select(c => char.IsLetterOrDigit(c) ? c : '_')]);

    private static SyntaxNode? GetRootOfChain(SyntaxNode? node)
    {
        while (true)
        {
            if (node is InvocationExpressionSyntax invocation) { node = invocation.Expression; continue; }
            if (node is MemberAccessExpressionSyntax memberAccess) { node = memberAccess.Expression; continue; }
            break;
        }
        return node;
    }
    #endregion

    #region Server-Side Generation
    private static string GenerateBuilderExtensionForHandler(string handlerClassName, HandlerBuilderToGenerate handler)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>\n#nullable enable\n");
        sb.AppendLine("using KetPsi.Endpoints.Abstractions;");
        sb.AppendLine("using KetPsi.Endpoints.Http;");
        sb.AppendLine();
        sb.AppendLine("namespace KetPsi.Endpoints.Http");
        sb.AppendLine("{");
        sb.AppendLine($"    {handler.HandlerAccessibility} static class {handlerClassName}HttpEndpointBuilderExtensions");
        sb.AppendLine("    {");

        foreach (var param in handler.Parameters)
        {
            var methodName = ToPascalCase(param.Name);
            var methodAccessibility = (handler.HandlerAccessibility == "internal" || param.ParameterAccessibility == "internal")
                ? "internal"
                : "public";
            sb.AppendLine($"        {methodAccessibility} static HttpParameterBuilder<{param.TypeName}> {methodName}(this HttpEndpointBuilder<{handler.HandlerTypeName}> builder) => default!;");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateProjectEndpointExtensionsForGroup(string handlerName, List<EndpointToGenerate?> endpoints)
    {
        if (endpoints is null || endpoints.Count == 0)
            return string.Empty;

        var allUsings = new HashSet<string> {
                "using Microsoft.AspNetCore.Builder;",
                "using Microsoft.AspNetCore.Routing;",
                "using Microsoft.AspNetCore.Mvc;",
                "using Microsoft.Extensions.DependencyInjection;",
                "using KetPsi.Endpoints.Abstractions;"
            };
        foreach (var ep in endpoints)
        {
            if (ep is null) continue;
            foreach (var u in ep.SourceFileUsings)
            {
                allUsings.Add(u);
            }
        }

        var mappingLogic = GenerateMappingLogic(endpoints);
        var generatedHelperMethods = endpoints.Select(e => e?.GeneratedHelperMethods).Where(e => e is not null).DefaultIfEmpty(string.Empty).Aggregate((f, s) => $"{f} {s}");

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>\n#nullable enable\n");
        foreach (var u in allUsings.OrderBy(x => x)) { sb.AppendLine(u.StartsWith("using") ? u : $"using {u};"); }
        sb.AppendLine("namespace Microsoft.AspNetCore.Builder");
        sb.AppendLine("{");
        sb.AppendLine($"    public static class {handlerName}HttpEndpointExtensions");
        sb.AppendLine("    {");
        if (!string.IsNullOrWhiteSpace(generatedHelperMethods))
        {
            sb.AppendLine("        // Helper methods generated to support endpoint naming.");
            sb.AppendLine(generatedHelperMethods);
        }

        var emittedSurrogates = new HashSet<string>();
        foreach (var ep in endpoints)
        {
            if (ep is null) continue;
            foreach (var p in ep.Parameters)
            {
                if (p.Surrogate != null && emittedSurrogates.Add(p.Surrogate.StructName))
                {
                    sb.AppendLine(p.Surrogate.StructCode);
                }
            }
        }

        sb.AppendLine($"        public static IEndpointRouteBuilder Map{handlerName}Endpoints(this WebApplication app)");
        sb.AppendLine("        {");
        sb.Append(mappingLogic);
        sb.AppendLine("            return app;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine($"        public static IServiceCollection Add{handlerName}HttpEndpoints(this IServiceCollection services)");
        sb.AppendLine("        {");
        var distinctInstanceHandlers = endpoints
            .Where(e => e != null && !e.IsStatic)
            .Select(e => e!.HandlerTypeName)
            .Distinct();

        foreach (var handler in distinctInstanceHandlers)
        {
            var cleanHandler = handler.StartsWith("global::") ? handler : $"global::{handler}";
            sb.AppendLine($"            services.AddScoped<{cleanHandler}>();");
        }
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateMasterExtension(List<string> mapCalls, List<string> addCalls)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>\n#nullable enable\n");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Microsoft.AspNetCore.Builder");
        sb.AppendLine("{");
        sb.AppendLine("    public static class HttpEndpointExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        public static IEndpointRouteBuilder MapHttpEndpoints(this WebApplication app)");
        sb.AppendLine("        {");
        foreach (var call in mapCalls)
        {
            sb.AppendLine(call);
        }
        sb.AppendLine("            return app;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static IServiceCollection AddHttpEndpoints(this IServiceCollection services)");
        sb.AppendLine("        {");
        foreach (var call in addCalls)
        {
            sb.AppendLine(call);
        }
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateMappingLogic(List<EndpointToGenerate?> endpoints)
    {
        var sb = new StringBuilder();
        var groupVars = new Dictionary<string, string>(StringComparer.Ordinal);
        int groupCounter = 0;

        foreach (var ep in endpoints)
        {
            if (ep is null || ep.GroupSteps.Count == 0)
                continue;

            string parentVar = "app";
            var cumulativeKeyBuilder = new StringBuilder("app");

            for (int i = 0; i < ep.GroupSteps.Count; i++)
            {
                var step = ep.GroupSteps[i];
                cumulativeKeyBuilder.Append(step);
                var currentKey = cumulativeKeyBuilder.ToString();

                if (!groupVars.TryGetValue(currentKey, out var currentGroupVar))
                {
                    currentGroupVar = $"group{groupCounter++}";
                    groupVars[currentKey] = currentGroupVar;
                    sb.AppendLine($"            var {currentGroupVar} = {parentVar}{step};");
                }

                parentVar = currentGroupVar;
            }
        }

        if (groupVars.Any()) sb.AppendLine();

        foreach (var ep in endpoints)
        {
            if (ep is null)
                continue;

            string builderVar = "app";
            if (!string.IsNullOrEmpty(ep.GroupDefinition) && groupVars.TryGetValue(ep.GroupDefinition, out var gVar))
            {
                builderVar = gVar;
            }

            var lambdaParams = new List<string>();
            var invocationArgs = new List<string>();
            foreach (var p in ep.Parameters)
            {
                string attributesString = p.Attributes.Any() ? string.Join(" ", p.Attributes.Select(item => $"[{item}]")) + " " : "";
                if (p.Surrogate != null)
                {
                    lambdaParams.Add($"{attributesString}{p.Surrogate.StructName} @{p.Name}");
                    invocationArgs.Add($"@{p.Name}.__ToModel()");
                }
                else
                {
                    lambdaParams.Add($"{attributesString}{p.TypeName}{(p.IsNullable && !p.TypeName.EndsWith("?") ? "?" : "")} @{p.Name}");
                    invocationArgs.Add($"@{p.Name}");
                }
            }

            if (!ep.IsStatic)
            {
                lambdaParams.Add($"[FromServices] {ep.HandlerTypeName} handler");
            }

            var targetCaller = ep.IsStatic ? ep.HandlerTypeName : "handler";
            var finalChains = new StringBuilder(ep.EndpointChainedCalls);
            if (!string.IsNullOrEmpty(ep.EndpointName)) finalChains.Append($".WithName({ep.EndpointName})");

            sb.Append($"            {builderVar}.{ep.HttpMethod}(\"{ep.RouteValue}\",");
            sb.Append($" ({string.Join(", ", lambdaParams)}) =>");
            sb.Append($"{{  return {targetCaller}.ExecuteAsync({string.Join(", ", invocationArgs)}); }})");
            sb.AppendLine(finalChains.ToString());

            if (!string.IsNullOrEmpty(ep.ObsoleteMessage))
            {
                sb.Append(".WithMetadata(new System.ObsoleteAttribute(\"").Append(ep.ObsoleteMessage).Append("\"))");
            }
            sb.AppendLine(";");
        }

        return sb.ToString();
    }
    #endregion

    #region Data Transfer Classes & Diagnostics
    internal static class Diagnostics
    {
        public static readonly DiagnosticDescriptor CouldNotResolveMethodSymbol = new(
            "KP101",
            "Could not resolve method symbol",
            "Could not resolve the method symbol for '{0}'. Ensure the containing assembly is referenced and the required 'using' directives (e.g. 'using Microsoft.AspNetCore.Builder;') are present in the file.",
            "Generator.Warning",
            DiagnosticSeverity.Warning,
            true);

        public static readonly DiagnosticDescriptor MapMethodNotGeneric = new(
            "KP102",
            "Map method must be generic",
            "The endpoint mapping method '{0}' must be called with a generic type argument specifying the handler",
            "Generator.Error",
            DiagnosticSeverity.Error,
            true);

        public static readonly DiagnosticDescriptor MissingExecuteAsyncMethod = new(
            "KP103",
            "Endpoint missing ExecuteAsync method",
            "Endpoint '{0}' implements 'IEndpoint' but does not define a public 'ExecuteAsync' method.",
            "Generator.Error",
            DiagnosticSeverity.Error,
            true);
    }

    internal class HandlerGenerationResult(HandlerBuilderToGenerate? handler, List<Diagnostic> diagnostics)
    {
        public HandlerBuilderToGenerate? Handler { get; } = handler;
        public List<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    internal class HandlerBuilderToGenerate(
        string handlerTypeName,
        string handlerAccessibility,
        List<BuilderParameterToGenerate> parameters) : IEquatable<HandlerBuilderToGenerate>
    {
        public string HandlerTypeName { get; } = handlerTypeName;
        public string HandlerAccessibility { get; } = handlerAccessibility;
        public List<BuilderParameterToGenerate> Parameters { get; } = parameters;

        public bool Equals(HandlerBuilderToGenerate? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (HandlerTypeName != other.HandlerTypeName ||
                HandlerAccessibility != other.HandlerAccessibility ||
                Parameters.Count != other.Parameters.Count)
            {
                return false;
            }

            for (int i = 0; i < Parameters.Count; i++)
            {
                if (!Parameters[i].Equals(other.Parameters[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as HandlerBuilderToGenerate);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = HandlerTypeName?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (HandlerAccessibility?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ Parameters.Count;
                return hashCode;
            }
        }
    }

    internal class BuilderParameterToGenerate(string typeName, string name, string parameterAccessibility) : IEquatable<BuilderParameterToGenerate>
    {
        public string TypeName { get; } = typeName;
        public string Name { get; } = name;
        public string ParameterAccessibility { get; } = parameterAccessibility;

        public bool Equals(BuilderParameterToGenerate? other)
        {
            if (other is null) return false;
            return TypeName == other.TypeName && Name == other.Name && ParameterAccessibility == other.ParameterAccessibility;
        }

        public override bool Equals(object? obj) => Equals(obj as BuilderParameterToGenerate);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TypeName?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (Name?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (ParameterAccessibility?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }

    internal class GenerationResult(EndpointToGenerate? endpoint, List<Diagnostic> diagnostics)
    {
        public EndpointToGenerate? Endpoint { get; } = endpoint;
        public List<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    internal class EndpointToGenerate(
        string assemblyName,
        string handlerTypeName,
        string handlerAccessibility,
        bool isStatic,
        string routeValue,
        string routeExpression,
        string httpMethod,
        string endpointChainedCalls,
        string groupDefinition,
        List<string> groupSteps,
        List<ParameterToGenerate> parameters,
        HashSet<string> sourceFileUsings,
        string groupName,
        string endpointName,
        bool skipApiGeneration,
        bool usesVersioning,
        string? obsoleteMessage,
        string extensionClassName) : IEquatable<EndpointToGenerate>
    {
        public string AssemblyName { get; } = assemblyName;
        public string HandlerTypeName { get; } = handlerTypeName;
        public string HandlerAccessibility { get; } = handlerAccessibility;
        public bool IsStatic { get; } = isStatic;
        public string RouteValue { get; } = routeValue;
        public string RouteExpression { get; } = routeExpression;
        public string HttpMethod { get; } = httpMethod;
        public string EndpointChainedCalls { get; } = endpointChainedCalls;
        public string GroupDefinition { get; } = groupDefinition;
        public List<string> GroupSteps { get; } = groupSteps;
        public List<ParameterToGenerate> Parameters { get; } = parameters;
        public HashSet<string> SourceFileUsings { get; } = sourceFileUsings;
        public string GroupName { get; } = groupName;
        public string EndpointName { get; } = endpointName;
        public string GeneratedHelperMethods { get; } = string.Empty;
        public bool SkipApiGeneration { get; } = skipApiGeneration;
        public bool UsesVersioning { get; } = usesVersioning;
        public string? ObsoleteMessage { get; } = obsoleteMessage;
        public string ExtensionClassName { get; } = extensionClassName;

        public bool Equals(EndpointToGenerate? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return AssemblyName == other.AssemblyName
                && HandlerTypeName == other.HandlerTypeName
                && HandlerAccessibility == other.HandlerAccessibility
                && IsStatic == other.IsStatic
                && RouteValue == other.RouteValue
                && HttpMethod == other.HttpMethod
                && EndpointChainedCalls == other.EndpointChainedCalls
                && GroupDefinition == other.GroupDefinition
                && EndpointName == other.EndpointName
                && ExtensionClassName == other.ExtensionClassName;
        }

        public override bool Equals(object? obj) => Equals(obj as EndpointToGenerate);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = AssemblyName?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (HandlerTypeName?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (HandlerAccessibility?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsStatic.GetHashCode();
                hashCode = (hashCode * 397) ^ (RouteValue?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (HttpMethod?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (EndpointChainedCalls?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (GroupDefinition?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (EndpointName?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (ExtensionClassName?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }

    internal class ParameterToGenerate(
        string typeName,
        string name,
        List<string> attributes,
        bool isNullable,
        string parameterAccessibility,
        AsParametersSurrogateInfo? surrogate = null)
    {
        public string TypeName { get; } = typeName;
        public string Name { get; } = name;
        public List<string> Attributes { get; } = attributes;
        public bool IsNullable { get; } = isNullable;
        public string ParameterAccessibility { get; } = parameterAccessibility;
        public AsParametersSurrogateInfo? Surrogate { get; } = surrogate;
    }

    internal class AsParametersSurrogateInfo(string structName, string structCode)
    {
        public string StructName { get; } = structName;
        public string StructCode { get; } = structCode;
    }

    internal class SurrogatePropertyInfo(string name, string typeName, List<string> attributes, string? defaultValueExpression)
    {
        public string Name { get; } = name;
        public string TypeName { get; } = typeName;
        public List<string> Attributes { get; } = attributes;
        public string? DefaultValueExpression { get; } = defaultValueExpression;
    }
    #endregion
}

class ParameterSubstitutionRewriter(
    SemanticModel model,
    IMethodSymbol method,
    Dictionary<IParameterSymbol, ExpressionSyntax> argumentMap) : CSharpSyntaxRewriter
{
    private readonly SemanticModel _model = model;
    private readonly IMethodSymbol _method = method;
    private readonly Dictionary<IParameterSymbol, ExpressionSyntax> _argumentMap = argumentMap;

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_model.GetSymbolInfo(node).Symbol is not IParameterSymbol symbol) return base.VisitIdentifierName(node);

        if (SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, _method) &&
            _argumentMap.TryGetValue(symbol, out var replacement))
        {
            return replacement;
        }

        return base.VisitIdentifierName(node);
    }
}

public class ResolvedEndpointInfo(string resolvedName, InvocationExpressionSyntax callSite)
{
    public string ResolvedName { get; } = resolvedName;
    public InvocationExpressionSyntax CallSite { get; } = callSite;
}

public static class EndpointResolver
{
    public static IEnumerable<ResolvedEndpointInfo> ResolveNamesFromCallSites(
        InvocationExpressionSyntax withNameInvocation,
        SemanticModel semanticModel)
    {
        var withNameArgumentExpr = withNameInvocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var parameterIdentifier = withNameArgumentExpr?.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault(id => semanticModel.GetSymbolInfo(id).Symbol is IParameterSymbol);

        if (parameterIdentifier is null || semanticModel.GetSymbolInfo(parameterIdentifier).Symbol is not IParameterSymbol parameterSymbol)
            yield break;

        if (parameterSymbol.ContainingSymbol is not IMethodSymbol helperMethodSymbol)
            yield break;

        var compilation = semanticModel.Compilation;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var callSite in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(callSite.Expression).Symbol is not IMethodSymbol invokedSymbol ||
                    !SymbolEqualityComparer.Default.Equals(invokedSymbol.OriginalDefinition, helperMethodSymbol.OriginalDefinition))
                    continue;

                var finalName = callSite.Expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;

                if (finalName != null)
                {
                    yield return new ResolvedEndpointInfo(finalName, callSite);
                }
            }
        }
    }
}