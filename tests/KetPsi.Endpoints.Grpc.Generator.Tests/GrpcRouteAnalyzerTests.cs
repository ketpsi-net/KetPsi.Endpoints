using System.Collections.Immutable;

using KetPsi.Endpoints.Grpc.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Generator.Tests;

public class GrpcRouteAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(source));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new GrpcRouteAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Theory]
    [InlineData("Api/Users/GetById")]          // 3 segments – valid
    [InlineData("Users/GetById")]              // 2 segments – valid
    public async Task ValidRoutes_ProduceNoDiagnostics(string route)
    {
        var source = $$"""
            using Microsoft.AspNetCore.Builder;
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

        var diags = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diags, d => d.Id == GrpcRouteAnalyzer.DiagnosticId);
    }

    [Theory]
    [InlineData("OnlyOne")]                    // 1 segment
    [InlineData("A/B/C/D")]                    // 4 segments
    [InlineData("")]                           // empty
    [InlineData("Api//GetById")]               // empty middle segment
    [InlineData("/Users/GetById")]             // leading empty
    [InlineData("Users/GetById/")]             // trailing empty
    public async Task InvalidRoutes_ProduceDG001(string route)
    {
        var source = $$"""
            using Microsoft.AspNetCore.Builder;
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

        var diags = await GetDiagnosticsAsync(source);
        Assert.Contains(diags, d => d.Id == GrpcRouteAnalyzer.DiagnosticId);
    }
}
