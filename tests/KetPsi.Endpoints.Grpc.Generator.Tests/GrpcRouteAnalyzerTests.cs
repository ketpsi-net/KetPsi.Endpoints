using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Generator.Tests;

public class GrpcRouteAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new GrpcRouteAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static string SourceWithRoute(string route) => $$"""
        public class WebApplication { }
        public static class C {
            public static void M(WebApplication app) {
                app.MapGrpc<Handler>("{{route}}");
            }
        }
        public class Handler { }
        public static class Ext {
            public static WebApplication MapGrpc<T>(this WebApplication app, string route) => app;
        }
        """;

    [Theory]
    [InlineData("Users/GetById")]
    [InlineData("Api/Users/GetById")]
    [InlineData("A/B/C/D")]
    [InlineData("Orders/{id}/Get")]
    public async Task ValidRoutes_NoDG001(string route)
    {
        var diags = await GetDiagnosticsAsync(SourceWithRoute(route));
        Assert.DoesNotContain(diags, d => d.Id == GrpcRouteAnalyzer.DiagnosticId);
    }

    [Theory]
    [InlineData("OnlyOne")]
    [InlineData("")]
    [InlineData("Api//GetById")]
    [InlineData("/Users/GetById")]
    [InlineData("Users/GetById/")]
    public async Task InvalidRoutes_ProduceDG001(string route)
    {
        var diags = await GetDiagnosticsAsync(SourceWithRoute(route));
        Assert.Contains(diags, d => d.Id == GrpcRouteAnalyzer.DiagnosticId);
    }
}
