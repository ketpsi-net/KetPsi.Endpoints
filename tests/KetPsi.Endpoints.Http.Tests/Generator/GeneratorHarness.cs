using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using KetPsi.Endpoints.Http.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpEndpointGenerator.Tests.Generator;

/// <summary>Runs <see cref="EndpointGenerator"/> through <see cref="CSharpGeneratorDriver"/> against in-memory sources.</summary>
internal static class GeneratorHarness
{
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using KetPsi.Endpoints.Abstractions;
        using KetPsi.Endpoints.Http;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[]
            {
                typeof(KetPsi.Endpoints.Abstractions.IEndpoint).Assembly.Location,
                typeof(KetPsi.Endpoints.Http.MapExtensions).Assembly.Location,
                typeof(KetPsi.Endpoints.Http.IHttpEndpointConfiguration<>).Assembly.Location,
            })
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.First()));
        return paths.ToImmutableArray();
    });

    public static readonly CSharpParseOptions TestParseOptions = new(LanguageVersion.Latest);

    public static CSharpCompilation Compile(params string[] sources)
        => CSharpCompilation.Create(
            "GeneratorTestAssembly",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, TestParseOptions, path: $"Source{i}.cs")),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver CreateDriver(bool requestDelegate)
        => CSharpGeneratorDriver.Create(
            new[] { new EndpointGenerator().AsSourceGenerator() },
            parseOptions: TestParseOptions,
            optionsProvider: new OptionsProvider(requestDelegate),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public static GeneratorRun Run(bool requestDelegate, params string[] sources)
    {
        var compilation = Compile(sources);
        var driver = CreateDriver(requestDelegate).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GeneratorRun(driver, (CSharpCompilation)output, diagnostics);
    }

    private sealed class OptionsProvider(bool requestDelegate) : AnalyzerConfigOptionsProvider
    {
        private readonly Options _global = new(new Dictionary<string, string>
        {
            ["build_property.PublishAot"] = requestDelegate ? "true" : "false",
            ["build_property.EnableRequestDelegateGenerator"] = "",
        });

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new(new Dictionary<string, string>());

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            => values.TryGetValue(key, out value);
    }
}

internal sealed record GeneratorRun(GeneratorDriver Driver, CSharpCompilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics)
{
    public IEnumerable<Diagnostic> CompilationErrors
        => Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

    public string Generated(string hintNameContains)
        => string.Join("\n", Driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources)
            .Where(s => s.HintName.Contains(hintNameContains, StringComparison.Ordinal))
            .Select(s => s.SourceText.ToString()));

    public string AllGenerated
        => string.Join("\n", Driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

    public void ShouldCompile()
    {
        var errors = GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Concat(CompilationErrors)
            .Select(d => d.ToString())
            .ToList();
        if (errors.Count > 0)
            throw new Xunit.Sdk.XunitException("Generated code does not compile:\n" + string.Join("\n", errors) + "\n\n" + AllGenerated);
    }
}
