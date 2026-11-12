using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KetPsi.Endpoints.Grpc.Generator.Tests;

internal static class GeneratorHarness
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
    {
        _ = typeof(KetPsi.Endpoints.Grpc.GrpcGeneratedRegistrationHub);
        _ = typeof(KetPsi.Endpoints.Abstractions.IEndpoint);
        _ = typeof(KetPsi.Endpoints.Grpc.Abstractions.IProtoTypeConfiguration<>);
        _ = typeof(global::Grpc.AspNetCore.Server.GrpcServiceOptions);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return tpa.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    });

    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using KetPsi.Endpoints.Abstractions;
        using KetPsi.Endpoints.Grpc;
        using KetPsi.Endpoints.Grpc.Abstractions;
        using Microsoft.AspNetCore.Builder;

        """;

    public static CSharpCompilation Compilation(params string[] sources)
        => CSharpCompilation.Create(
            "GenTest",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(Usings + s, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), path: $"File{i}.cs")),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public sealed record Result(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompileErrors,
        ImmutableArray<Diagnostic> AnalyzerDiagnostics,
        GeneratorDriverRunResult RunResult,
        Compilation Output)
    {
        public IEnumerable<Diagnostic> All => GeneratorDiagnostics.Concat(AnalyzerDiagnostics);
        public string GeneratedText(string hintPrefix)
            => string.Join("\n", RunResult.GeneratedTrees.Where(t => Path.GetFileName(t.FilePath).StartsWith(hintPrefix, StringComparison.Ordinal)).Select(t => t.ToString()));
    }

    public static Result Run(params string[] sources)
    {
        var compilation = Compilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GrpcGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiags);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        var analyzer = output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new GrpcRouteAnalyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        return new Result(genDiags, errors, analyzer, driver.GetRunResult(), output);
    }

    public static string Handler(string name, string request, string response)
        => $$"""
        public sealed class {{name}} : IEndpoint
        {
            public Task<{{response}}> ExecuteAsync({{request}} request, CancellationToken ct) => Task.FromResult(new {{response}}());
        }
        """;
}
