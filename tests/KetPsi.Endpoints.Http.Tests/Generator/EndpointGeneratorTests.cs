using FluentAssertions;

using KetPsi.Endpoints.Http.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static HttpEndpointGenerator.Tests.Generator.GeneratorHarness;

namespace HttpEndpointGenerator.Tests.Generator;

/// <summary>Generator-driver tests: compile-only scenarios in both emit modes, diagnostics and escaping.</summary>
public class EndpointGeneratorTests
{
    private const string Handler = """
        namespace App;
        public class H : IEndpoint
        {
            public Task<IResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Results.Ok());
        }
        """;

    private static IEnumerable<Diagnostic> Diags(GeneratorRun run, string id)
        => run.GeneratorDiagnostics.Where(d => d.Id == id);

    // ------------------------------------------------------------------ helpers (finding 3)

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Helpers_Called_In_Reduced_Form_Are_Resolved(bool rd)
    {
        var run = Run(rd, Usings + Handler, Usings + """
            namespace App;
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    app.MapGroup("/g").MapUsers();
                    app.MapOther();
                    Mappings.MapNamed(prefix: "/named", e: app);
                    app.MapLevel1("/deep");
                }
                public static void MapUsers(this RouteGroupBuilder g) { g.MapGet<H>("/u"); }
                public static void MapOther(this IEndpointRouteBuilder e, string prefix = "/o") { e.MapGet<H>(prefix + "/x"); }
                public static void MapNamed(IEndpointRouteBuilder e, string prefix) { e.MapPost<H>(prefix); }
                public static void MapLevel1(this IEndpointRouteBuilder e, string p) { e.MapGroup(p).MapLevel2(); }
                public static void MapLevel2(this RouteGroupBuilder g) { g.MapDelete<H>("/leaf"); }
            }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("MapGroup(\"/g\")").And.Contain("\"/u\"");
        code.Should().Contain("(\"/o\") + \"/x\"");
        code.Should().Contain("(\"/named\")");
        code.Should().Contain("MapGroup((\"/deep\"))").And.Contain("\"/leaf\"");
        Diags(run, "KP104").Should().BeEmpty();
    }

    [Fact]
    public void Group_Helper_Without_Call_Sites_Reports_Diagnostic_Instead_Of_Dropping()
    {
        var run = Run(true, Usings + Handler, Usings + """
            namespace App;
            public static class Mappings
            {
                public static void MapUsers(this RouteGroupBuilder g) { g.MapGet<H>("/u"); }
                public static void MapRoot(this IEndpointRouteBuilder e) { e.MapGet<H>("/root"); }
            }
            """);

        Diags(run, "KP104").Should().ContainSingle();
        Diags(run, "KP105").Should().ContainSingle("IEndpointRouteBuilder helpers without call sites are mapped on the root");
        run.Generated("MappingsHttpEndpoints").Should().Contain("\"/root\"");
    }

    // ------------------------------------------------------------------ portability (finding 13)

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonConstant_Route_Is_Emitted_Fully_Qualified(bool rd)
    {
        var run = Run(rd, Usings + Handler, Usings + """
            namespace App;
            public static class Routes { public static readonly string Users = "/users"; public const string Tag = "T\"1"; }
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    const string local = "/local";
                    app.MapGet<H>(Routes.Users).WithTags(Routes.Tag);
                    app.MapGet<H>(local);
                }
            }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("global::App.Routes.Users");
        code.Should().Contain("WithTags(\"T\\\"1\")");
        code.Should().Contain("\"/local\"");
    }

    [Fact]
    public void Convention_Referencing_A_Local_Reports_KP106()
    {
        var run = Run(true, Usings + Handler, Usings + """
            namespace App;
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    var tag = Guid.NewGuid().ToString();
                    app.MapGet<H>("/a").WithTags(tag);
                    var group = app.MapGroup("/g").WithTags(tag);
                    group.MapGet<H>("/b");
                }
            }
            """);

        Diags(run, "KP106").Should().NotBeEmpty();
        run.GeneratorDiagnostics.Should().Contain(d => d.Id == "KP106" && d.Severity == DiagnosticSeverity.Error);
    }

    // ------------------------------------------------------------------ names / escaping (findings 6, 14)

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parameter_Names_Never_Collide_With_Generated_Names(bool rd)
    {
        var run = Run(rd, Usings + """
            namespace App;
            public class Colliding : IEndpoint
            {
                public Task<IResult> ExecuteAsync(int httpContext, string? handler, int? __result, int? __handlerTask, string? __hasBody, string? __kp_handler, CancellationToken ct = default)
                    => Task.FromResult(Results.Ok());
            }
            public static class Mappings
            {
                public static void MapAll(this WebApplication app) => app.MapGet<Colliding>("/c");
            }
            """);

        run.ShouldCompile();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Literals_Are_Escaped(bool rd)
    {
        var run = Run(rd, Usings + """
            namespace App;
            [Obsolete("Use \"v2\" \\ now\n")]
            public class Escaped : IEndpoint
            {
                public Task<IResult> ExecuteAsync(
                    [FromHeader(Name = "X-\"Q\"")] string? q,
                    [FromQuery(Name = "a\\b")] int? a,
                    [FromKeyedServices("k\"ey")] IServiceProvider? keyed)
                    => Task.FromResult(Results.Ok());
            }
            public static class Names { public const string Name = "N\"a\\me"; }
            public static class Mappings
            {
                public static void MapAll(this WebApplication app) => app.MapGet<Escaped>("/e/\"x\"").WithName(Names.Name);
            }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("\"N\\\"a\\\\me\"");
        code.Should().Contain("\"Use \\\"v2\\\" \\\\ now\\n\"");
    }

    // ------------------------------------------------------------------ return shapes (finding 5)

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void All_Return_Shapes_Compile(bool rd)
    {
        var run = Run(rd, Usings + """
            namespace App;
            public record Dto(int A);
            public class T1 : IEndpoint { public Task ExecuteAsync() => Task.CompletedTask; }
            public class T2 : IEndpoint { public ValueTask ExecuteAsync() => default; }
            public class T3 : IEndpoint { public void ExecuteAsync(int id) { } }
            public class T4 : IEndpoint { public string ExecuteAsync() => "x"; }
            public class T5 : IEndpoint { public Task<Dto> ExecuteAsync() => Task.FromResult(new Dto(1)); }
            public class T6 : IEndpoint { public ValueTask<Dto?> ExecuteAsync() => default; }
            public class T7 : IEndpoint { public Task<object> ExecuteAsync() => Task.FromResult<object>(1); }
            public class T8 : IEndpoint { public Task<string?> ExecuteAsync() => Task.FromResult<string?>(null); }
            public class T9 : IEndpoint { public Task<Microsoft.AspNetCore.Http.HttpResults.Ok<Dto>> ExecuteAsync() => Task.FromResult(TypedResults.Ok(new Dto(1))); }
            public class T10 : IEndpoint { public static IResult ExecuteAsync() => Results.Ok(); }
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    app.MapGet<T1>("/1"); app.MapGet<T2>("/2"); app.MapGet<T3>("/3/{id}"); app.MapGet<T4>("/4");
                    app.MapGet<T5>("/5"); app.MapGet<T6>("/6"); app.MapGet<T7>("/7"); app.MapGet<T8>("/8");
                    app.MapGet<T9>("/9"); app.MapGet<T10>("/10");
                }
            }
            """);

        run.ShouldCompile();
    }

    // ------------------------------------------------------------------ binding shapes (findings 9, 11, 12, 15)

    private const string BindingShapes = """
        namespace App;
        public enum Mode { A, B }
        public record struct Pt(int X) { public static bool TryParse(string s, out Pt p) { p = default; return int.TryParse(s, out var x) && (p = new Pt(x)) == p; } }
        public sealed class Bound { public static ValueTask<Bound?> BindAsync(HttpContext c, System.Reflection.ParameterInfo p) => default; }
        public sealed class PropsOnly { public int? Page { get; set; } public required string Name { get; set; } }
        public record RecordDto(int? Size, string Sort = "asc");
        public readonly record struct StructBody(int V);
        public interface IService { }
        public class Shapes : IEndpoint
        {
            public Task<IResult> ExecuteAsync(
                short a, byte b, uint c, ulong d, decimal e, float f, double g, bool h, Guid i, DateTime j, DateTimeOffset k,
                DateOnly l, TimeOnly m, TimeSpan n, Mode o, char p, Uri q, Pt r, int[] s, List<string> t,
                Microsoft.Extensions.Primitives.StringValues u, int? v, Mode? w, Bound? x, IService svc,
                [AsParameters] PropsOnly props, [AsParameters] RecordDto rec, [FromHeader] int hdr, [FromHeader] string[] hdrs,
                Microsoft.Extensions.Logging.ILogger<Shapes> logger, System.Security.Claims.ClaimsPrincipal user, HttpRequest request,
                HttpResponse response, HttpContext context, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }
        public class BodyShapes : IEndpoint
        {
            public Task<IResult> ExecuteAsync([FromBody] StructBody body, [FromServices] IService? optional, [FromKeyedServices("k")] IService keyed, System.IO.Stream stream)
                => Task.FromResult(Results.Ok());
        }
        public class FormShapes : IEndpoint
        {
            public Task<IResult> ExecuteAsync([FromForm] int count, [FromForm] string? note, IFormFile? file, IFormFileCollection files, IFormCollection form)
                => Task.FromResult(Results.Ok());
        }
        public class InferredBody : IEndpoint
        {
            public Task<IResult> ExecuteAsync(RecordDto dto, System.IO.Pipelines.PipeReader reader) => Task.FromResult(Results.Ok());
        }
        public static class Mappings
        {
            public static void MapAll(this WebApplication app)
            {
                app.MapGet<Shapes>("/s/{a}");
                app.MapPost<BodyShapes>("/b");
                app.MapPost<FormShapes>("/f");
                app.MapPut<InferredBody>("/i");
            }
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void All_Supported_Binding_Shapes_Compile(bool rd)
    {
        var run = Run(rd, Usings + BindingShapes);
        run.ShouldCompile();
        if (rd)
        {
            var code = run.Generated("MappingsHttpEndpoints");
            code.Should().NotContain("new global::Microsoft.AspNetCore.Http.HttpRequest(");
            code.Should().Contain("new global::App.PropsOnly() { @Page = ");
            code.Should().Contain("GetService<global::App.IService>");
        }
    }

    [Fact]
    public void Unsupported_Types_Are_Errors_Not_Default()
    {
        var run = Run(true, Usings + """
            namespace App;
            public class Complex { public int A { get; set; } }
            public class Bad1 : IEndpoint { public Task<IResult> ExecuteAsync([FromQuery] Dictionary<string, int> d) => Task.FromResult(Results.Ok()); }
            public class Bad2 : IEndpoint { public Task<IResult> ExecuteAsync([FromForm] Complex c) => Task.FromResult(Results.Ok()); }
            public class Bad3 : IEndpoint { public Task<IResult> ExecuteAsync([FromKeyedServices] IServiceProvider sp) => Task.FromResult(Results.Ok()); }
            public class Bad4 : IEndpoint { public Task<IResult> ExecuteAsync([FromBody] Complex a, [FromBody] Complex b) => Task.FromResult(Results.Ok()); }
            public class Bad5 : IEndpoint { public Task<IResult> ExecuteAsync([FromRoute] int[] ids) => Task.FromResult(Results.Ok()); }
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    app.MapGet<Bad1>("/1"); app.MapPost<Bad2>("/2"); app.MapGet<Bad3>("/3"); app.MapPost<Bad4>("/4"); app.MapGet<Bad5>("/5/{ids}");
                }
            }
            """);

        Diags(run, "KP107").Should().HaveCount(3);
        Diags(run, "KP108").Should().ContainSingle();
        Diags(run, "KP109").Should().ContainSingle();
        run.GeneratorDiagnostics.Where(d => d.Id is "KP107" or "KP108" or "KP109").Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Surrogates_For_Same_Handler_Names_In_Different_Namespaces_Do_Not_Collide(bool rd)
    {
        static string Ns(string ns) => Usings + $$"""
            namespace {{ns}};
            public class Filter { public int? Size { get; set; } public string? Q { get; set; } }
            public class Search : IEndpoint { public Task<IResult> ExecuteAsync(Filter filter) => Task.FromResult(Results.Ok()); }
            public class SearchConfig : IHttpEndpointConfiguration<Search>
            {
                public void Configure(IHttpEndpointBuilder<Search> builder)
                    => builder.Filter().AsParameters().Property(x => x.Size, p => p.FromQuery("size")).Property(x => x.Q, p => p.FromQuery(name: "q\"x"));
            }
            """;

        var run = Run(rd, Ns("A"), Ns("B"), Usings + """
            namespace App;
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    app.MapGet<A.Search>("/a");
                    app.MapGet<B.Search>("/b");
                }
            }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("__KetPsi_A_Search_filter_AsParameters").And.Contain("__KetPsi_B_Search_filter_AsParameters");
        code.Should().Contain("\"q\\\"x\"");
    }

    [Fact]
    public void Classic_Mode_Keeps_Default_Values_And_Attributes()
    {
        var run = Run(false, Usings + """
            namespace App;
            public class Search : IEndpoint
            {
                public Task<IResult> ExecuteAsync([FromQuery(Name = "p")] int page = 1, [FromHeader(Name = "X-F")] string format = "json", [System.ComponentModel.DataAnnotations.Range(1, 5)] int size = 2)
                    => Task.FromResult(Results.Ok());
            }
            public static class Mappings { public static void MapAll(this WebApplication app) => app.MapGet<Search>("/s"); }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("[global::Microsoft.AspNetCore.Mvc.FromQueryAttribute(Name = \"p\")] int @page = 1");
        code.Should().Contain("string @format = \"json\"");
        code.Should().Contain("global::System.ComponentModel.DataAnnotations.RangeAttribute(1, 5)");
    }

    [Fact]
    public void Handler_Activation_Avoids_DI_Only_When_Safe()
    {
        var run = Run(true, Usings + """
            namespace App;
            public interface IDep { }
            public class Stateless : IEndpoint { public Task ExecuteAsync() => Task.CompletedTask; }
            public class Stateful : IEndpoint { private int _calls; public Task ExecuteAsync() { _calls++; return Task.CompletedTask; } }
            public class WithDeps(IDep dep) : IEndpoint { public Task ExecuteAsync() => Task.FromResult(dep); }
            public static class Mappings
            {
                public static void MapAll(this WebApplication app)
                {
                    app.MapGet<Stateless>("/a"); app.MapGet<Stateful>("/b"); app.MapGet<WithDeps>("/c");
                }
            }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("private static readonly global::App.Stateless __kp_Handler_0 = new global::App.Stateless();");
        code.Should().Contain("new global::App.Stateful().ExecuteAsync(");
        code.Should().Contain("GetRequiredService<global::App.WithDeps>(");
        code.Should().Contain("TryAddTransient<global::App.WithDeps>(services)");
    }

    [Fact]
    public void SkipEndpointGeneration_Opts_Out()
    {
        var run = Run(true, Usings + Handler, Usings + """
            namespace App;
            public static class Mappings { public static void MapAll(this WebApplication app) { app.MapGet<H>("/skip").SkipEndpointGeneration(); app.MapGet<H>("/keep"); } }
            """);

        run.ShouldCompile();
        var code = run.Generated("MappingsHttpEndpoints");
        code.Should().Contain("\"/keep\"").And.NotContain("\"/skip\"");
    }

    [Fact]
    public void Activation_Marker_Reflects_Mode()
    {
        Run(true, Usings + Handler).Generated("KetPsiRdgActivation").Should().Contain("UseKetPsiRequestDelegate = true");
        Run(false, Usings + Handler).Generated("KetPsiRdgActivation").Should().Contain("UseKetPsiRequestDelegate = false");
    }

    // ------------------------------------------------------------------ robustness (finding 18)

    [Fact]
    public void Failure_Diagnostic_Message_Tolerates_Braces()
    {
        var diagnostic = DiagnosticInfo.Create(Diagnostics.GenerationFailed, null, "boom {0} } {").ToDiagnostic();
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("boom {0} } {");
    }

    [Fact]
    public void Default_EquatableArray_Behaves_Like_Empty()
    {
        var empty = default(EquatableArray<string>);
        empty.Length.Should().Be(0);
        empty.Equals(EquatableArray<string>.Empty).Should().BeTrue();
        empty.GetHashCode().Should().Be(EquatableArray<string>.Empty.GetHashCode());
        empty.Should().BeEmpty();
        EquatableArray<string>.From(new[] { "a", "b" }).Should().Equal(EquatableArray<string>.From(new[] { "a", "b" }));
        EquatableArray<string>.From(new[] { "a", "b" }).Equals(EquatableArray<string>.From(new[] { "b", "a" })).Should().BeFalse();
    }

    // ------------------------------------------------------------------ incrementality (finding 19)

    [Fact]
    public void Unrelated_Edit_Keeps_Pipeline_Cached()
    {
        var sources = new[]
        {
            Usings + Handler,
            Usings + """
                namespace App;
                public static class Mappings
                {
                    public static void MapAll(this WebApplication app) => app.MapGroup("/g").MapUsers();
                    public static void MapUsers(this RouteGroupBuilder g) { g.MapGet<H>("/u").WithName("U"); }
                }
                """,
        };

        var compilation = Compile(sources);
        var driver = CreateDriver(true).RunGenerators(compilation);

        var unrelated = CSharpSyntaxTree.ParseText("namespace Other; public class Unrelated { public int Value => 42; }", TestParseOptions, path: "Unrelated.cs");
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(unrelated));

        var result = driver.GetRunResult().Results.Single();
        foreach (var step in new[] { EndpointGenerator.MapCallsStep, EndpointGenerator.HelperCallSitesStep, EndpointGenerator.CombinedStep })
        {
            result.TrackedSteps.Should().ContainKey(step);
            result.TrackedSteps[step]
                .SelectMany(s => s.Outputs)
                .Should().OnlyContain(o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged, step);
        }

        result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs)
            .Should().OnlyContain(o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void Relevant_Edit_Regenerates()
    {
        var mapping = Usings + """
            namespace App;
            public static class Mappings { public static void MapAll(this WebApplication app) => app.MapGet<H>("/a"); }
            """;
        var compilation = Compile(Usings + Handler, mapping);
        var driver = CreateDriver(true).RunGenerators(compilation);

        var oldTree = compilation.SyntaxTrees.Last();
        var newTree = CSharpSyntaxTree.ParseText(mapping.Replace("\"/a\"", "\"/b\""), TestParseOptions, path: oldTree.FilePath);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(oldTree, newTree));

        var result = driver.GetRunResult().Results.Single();
        result.TrackedSteps[EndpointGenerator.CombinedStep].SelectMany(s => s.Outputs)
            .Should().Contain(o => o.Reason == IncrementalStepRunReason.Modified);
        result.GeneratedSources.Single(s => s.HintName.StartsWith("MappingsHttpEndpoints")).SourceText.ToString().Should().Contain("\"/b\"");
    }
}
