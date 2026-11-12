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

    [Theory]
    [InlineData("user-profile/Get")]
    [InlineData("Users/v1.0")]
    [InlineData("1Users/Get")]
    public async Task NonIdentifierSegments_ProduceDG001(string route)
    {
        var diags = await GetDiagnosticsAsync(SourceWithRoute(route));
        Assert.Contains(diags, d => d.Id == GrpcRouteAnalyzer.DiagnosticId);
    }

    [Fact]
    public void GroupSuppliesService_SingleSegmentIsValid()
    {
        var r = GeneratorHarness.Run(GeneratorHarness.Handler("H", "Q", "Q"), """
            public sealed class Q { public int Id { get; set; } }
            public static class S
            {
                public static void M(WebApplication app)
                {
                    app.MapGroup("Users").MapGrpc<H>("GetById");
                    var g = app.MapGroup("Orders");
                    g.MapGrpc<H>("Create");
                    app.MapGrpc<H>("Lonely");
                    app.MapGroup("bad-prefix").MapGrpc<H>("Svc/Get");
                }
            }
            """);
        var dg = r.AnalyzerDiagnostics.Where(d => d.Id == GrpcRouteAnalyzer.DiagnosticId).ToList();
        Assert.Equal(2, dg.Count); // "Lonely" and "bad-prefix"
        Assert.Contains("\"Api.Users\"", r.GeneratedText("KetPsiGrpcServices"));
        Assert.Contains("\"Api.Orders\"", r.GeneratedText("KetPsiGrpcServices"));
    }

    [Fact]
    public void TwoTypeArgumentOverload_IsValidated()
    {
        var r = GeneratorHarness.Run(GeneratorHarness.Handler("H", "Q", "Q"), """
            public sealed class Q { public int Id { get; set; } }
            public sealed class Cfg : IGrpcEndpointConfiguration<H> { public void Configure(IGrpcEndpointBuilder<H> b) { } }
            public static class S { public static void M(WebApplication app) { app.MapGrpc<H, Cfg>("Only"); app.MapGrpc<H, Cfg>("Svc/Ok"); } }
            """);
        Assert.Single(r.AnalyzerDiagnostics, d => d.Id == GrpcRouteAnalyzer.DiagnosticId);
        Assert.Empty(r.CompileErrors);
    }
}
