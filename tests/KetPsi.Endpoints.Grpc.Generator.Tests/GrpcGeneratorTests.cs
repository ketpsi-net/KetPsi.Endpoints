using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static KetPsi.Endpoints.Grpc.Generator.Tests.GeneratorHarness;

namespace KetPsi.Endpoints.Grpc.Generator.Tests;

public sealed class GrpcGeneratorTests
{
    private const string Dtos = """
        public sealed class Req { public int Id { get; set; } public string Name { get; set; } = ""; }
        public sealed class Res { public int Id { get; set; } }
        """;

    private static string App(string body) => $$"""
        public static class Startup
        {
            public static void Map(WebApplication app)
            {
                {{body}}
            }
        }
        """;

    private static void AssertClean(Result r)
    {
        Assert.Empty(r.CompileErrors);
        Assert.DoesNotContain(r.All, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidProgram_CompilesWithoutErrors_AndEmitsAllArtifacts()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""
            app.MapGrpc<H>("Users/Get").RequireAuthorization("policy");
            app.MapGroup("api").WithGroupName("v2").MapGrpc<H>("Users/Get");
            """));
        AssertClean(r);
        var services = r.GeneratedText("KetPsiGrpcServices");
        Assert.Contains("\"Api.Users\"", services);
        Assert.Contains("\"api.v2.Users\"", services);
        Assert.Contains("RequireAuthorization(\"policy\")", r.GeneratedText("KetPsiGrpcConvention_"));
        Assert.Contains("GrpcMethodConventionBuilder(__service, \"Api.Users\", \"Get\")", r.GeneratedText("KetPsiGrpcRegistration"));
        Assert.DoesNotContain("ServiceContract", string.Join("\n", r.RunResult.GeneratedTrees.Select(t => t.ToString())));
    }

    [Fact]
    public void ConventionWithConstantArguments_IsInlined()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""
            const string policy = "Admins";
            app.MapGrpc<H>("Users/Get").RequireAuthorization(policy);
            """));
        AssertClean(r);
        Assert.Contains("RequireAuthorization(\"Admins\")", r.GeneratedText("KetPsiGrpcConvention_"));
    }

    [Fact]
    public void ConventionCapturingLocal_IsAnError()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""
            var policy = System.Environment.GetEnvironmentVariable("P") ?? "x";
            app.MapGrpc<H>("Users/Get").RequireAuthorization(policy);
            """));
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG006");
    }

    [Fact]
    public void StoredMapResult_Warns()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""
            var ep = app.MapGrpc<H>("Users/Get");
            ep.RequireAuthorization();
            """));
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG007");
    }

    [Fact]
    public void DuplicateFieldNumbers_AreAnError()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class ReqConfig : IProtoTypeConfiguration<Req>
            {
                public void Configure(IProtoTypeBuilder<Req> b) => b.Property(x => x.Id, 1).Property(x => x.Name, 1);
            }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG012");
    }

    [Fact]
    public void InvalidFieldNumber_IsAnError()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class ReqConfig : IProtoTypeConfiguration<Req>
            {
                public void Configure(IProtoTypeBuilder<Req> b) => b.Property(x => x.Id, 19000).Property(x => x.Name, 2);
            }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG013");
    }

    [Fact]
    public void UnconfiguredProperty_IsReportedNotSilentlyDropped()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class ReqConfig : IProtoTypeConfiguration<Req>
            {
                public void Configure(IProtoTypeBuilder<Req> b) { var n = 5; b.Property(x => x.Id, n++); }
            }
            """);
        var d = Assert.Single(r.GeneratorDiagnostics, d => d.Id == "KPG011");
        Assert.Contains("Name", d.GetMessage());
        Assert.Contains("Id = 5;", r.GeneratedText("KetPsiGrpcRegistration"));
    }

    [Fact]
    public void ProtoMemberAttribute_PartialAnnotation_Warns()
    {
        var r = Run(Handler("H", "AReq", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            [AttributeUsage(AttributeTargets.Property)] public sealed class ProtoMemberAttribute : Attribute { public ProtoMemberAttribute(int tag) { } }
            public sealed class AReq { [ProtoMember(3)] public int Id { get; set; } public string Other { get; set; } = ""; }
            public sealed class Res { public int Id { get; set; } }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG011");
        Assert.Contains("Id = 3;", r.GeneratedText("KetPsiGrpcRegistration"));
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void ImplicitNumbering_IsInfo()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""));
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG015" && d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void InvalidMapKey_IsAnError()
    {
        var r = Run(Handler("H", "MReq", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public enum Color { None, Red }
            public sealed class MReq { public Dictionary<Color, int> ByColor { get; set; } = new(); public Dictionary<double, int> ByDouble { get; set; } = new(); }
            public sealed class Res { public int Id { get; set; } }
            """);
        Assert.Equal(2, r.GeneratorDiagnostics.Count(d => d.Id == "KPG018"));
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void EnumValueOutOfRange_IsDiagnosedWithoutCrashing()
    {
        var r = Run(Handler("H", "EReq", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public enum Big : ulong { Zero = 0, Huge = ulong.MaxValue }
            public sealed class EReq { public Big Value { get; set; } }
            public sealed class Res { public int Id { get; set; } }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG019");
        Assert.DoesNotContain(r.GeneratorDiagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal) || d.Id == "AD0001");
    }

    [Fact]
    public void UnsupportedPropertyType_IsAnError()
    {
        var r = Run(Handler("H", "OReq", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class OReq { public object Anything { get; set; } = new(); public List<List<int>> Nested { get; set; } = new(); }
            public sealed class Res { public int Id { get; set; } }
            """);
        Assert.Equal(2, r.GeneratorDiagnostics.Count(d => d.Id == "KPG010"));
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void UnconstructibleType_IsAnError()
    {
        var r = Run(Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class Req { private Req() { } public int Id { get; set; } }
            public sealed class Res { public int Id { get; set; } }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG016");
    }

    [Fact]
    public void ReadOnlyNonCtorProperty_Warns()
    {
        var r = Run(Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class Req { public int Id { get; set; } public int Computed => Id * 2; }
            public sealed class Res { public int Id { get; set; } }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG017");
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void ConflictingDuplicateMapping_IsAnError_IdenticalOneIsDeduplicated()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), Handler("H2", "Req", "Res"), App("""
            app.MapGrpc<H>("Users/Get");
            app.MapGrpc<H>("Users/Get");
            app.MapGrpc<H2>("Users/Get");
            """));
        Assert.Single(r.GeneratorDiagnostics, d => d.Id == "KPG003");
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void StreamingVariantMismatch_IsAnError()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpcServerStream<H>("Users/Get");"""));
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG005");
    }

    [Fact]
    public void StreamingHandlers_CompileForAllShapes()
    {
        var r = Run(Dtos, App("""
            app.MapGrpcServerStream<S>("Users/Server");
            app.MapGrpcClientStream<C>("Users/Client");
            app.MapGrpcDuplexStream<D>("Users/Duplex");
            app.MapGrpc<V>("Users/Void", b => b.AddInterceptor<I1>().AddInterceptor<I2>());
            app.MapGrpc<N>("Users/NoRequest");
            """), """
            public sealed class I1 : Grpc.Core.Interceptors.Interceptor { }
            public sealed class I2 : Grpc.Core.Interceptors.Interceptor { }
            public sealed class S : IEndpoint { public async IAsyncEnumerable<Res> ExecuteAsync(Req r, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.Yield(); yield return new Res(); } }
            public sealed class C : IEndpoint { public ValueTask<Res> ExecuteAsync(IAsyncEnumerable<Req> r, CancellationToken ct) => new(new Res()); }
            public sealed class D : IEndpoint { public IAsyncEnumerable<Res> ExecuteAsync(IAsyncEnumerable<Req> r) => throw new NotImplementedException(); }
            public sealed class V : IEndpoint { public ValueTask ExecuteAsync(Req r, CancellationToken ct) => default; }
            public sealed class N : IEndpoint { public Res ExecuteAsync() => new(); }
            """);
        AssertClean(r);
        var services = r.GeneratedText("KetPsiGrpcServices");
        Assert.Contains("MethodType.ServerStreaming", services);
        Assert.Contains("MethodType.ClientStreaming", services);
        Assert.Contains("MethodType.DuplexStreaming", services);
        Assert.Contains("s_Void_1", services);
        var registration = r.GeneratedText("KetPsiGrpcRegistration");
        Assert.Contains("rpc Server (Req) returns (stream Res);", registration);
        Assert.Contains("rpc Client (stream Req) returns (Res);", registration);
        Assert.Contains("rpc Void (Req) returns (google.protobuf.Empty);", registration);
        Assert.Contains("rpc NoRequest (google.protobuf.Empty) returns (Res);", registration);
    }

    [Fact]
    public void UnsupportedHandlerShape_IsAnError()
    {
        var r = Run(Dtos, App("""app.MapGrpc<Bad>("Users/Get");"""), """
            public sealed class Bad : IEndpoint { public Task<Res> ExecuteAsync(Req a, Req b, CancellationToken ct) => Task.FromResult(new Res()); }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG004");
    }

    [Fact]
    public void ValueTypeRequest_IsDiagnosed()
    {
        var r = Run(App("""app.MapGrpc<H>("Users/Get");"""), """
            public struct SReq { public int Id { get; set; } }
            public sealed class Res { public int Id { get; set; } }
            public sealed class H : IEndpoint { public Task<Res> ExecuteAsync(SReq r, CancellationToken ct) => Task.FromResult(new Res()); }
            """);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG023");
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void RouteSanitizationCollisions_ProduceDistinctValidIdentifiers()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), Handler("H2", "Req", "Res"), App("""
            app.MapGrpc<H>("a_b/c/Svc/Get");
            app.MapGrpc<H2>("a/b_c/Svc/Get");
            app.MapGrpc<H>("ns/Svc/@class");
            """));
        Assert.Empty(r.CompileErrors);
        var services = r.GeneratedText("KetPsiGrpcServices");
        Assert.Contains("a__b_c_Svc_GrpcMethods", services);
        Assert.Contains("a_b__c_Svc_GrpcMethods", services);
    }

    [Fact]
    public void KeywordMethodName_IsEscaped()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Svc/class");"""));
        AssertClean(r);
        Assert.Contains("@class", r.GeneratedText("KetPsiGrpcServices"));
    }

    [Fact]
    public void InvalidRouteIdentifiers_AreReportedByAnalyzer_AndSkippedByGenerator()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("user-profile/Get");"""));
        Assert.Contains(r.AnalyzerDiagnostics, d => d.Id == "DG001");
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void NonConstantRoute_Warns()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""var route = "Users/" + System.Environment.MachineName; app.MapGrpc<H>(route);"""));
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG001");
    }

    [Fact]
    public void NonLambdaConfigure_Warns()
    {
        var r = Run(Dtos, Handler("H", "Req", "Res"), App("""
            Action<IGrpcEndpointBuilder<H>> cfg = b => { };
            app.MapGrpc<H>("Users/Get", cfg);
            """));
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "KPG008");
    }

    [Fact]
    public void InvalidMetadataBinding_IsAnError()
    {
        var r = Run(Handler("H", "BReq", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            public sealed class BReq { public string Tenant { get; init; } = ""; public object Obj { get; set; } = new(); }
            public sealed class Res { public int Id { get; set; } }
            public sealed class Cfg : IGrpcEndpointConfiguration<H>
            {
                public void Configure(IGrpcEndpointBuilder<H> b) => b.Request<BReq>(r =>
                {
                    r.Property(x => x.Tenant, p => p.FromMetadata("x-tenant"));
                    r.Property(x => x.Obj, p => p.FromMetadata());
                });
            }
            """);
        Assert.Equal(2, r.GeneratorDiagnostics.Count(d => d.Id == "KPG020"));
    }

    [Fact]
    public void SameNamedTypes_InDifferentNamespaces_AndGenerics_Compile()
    {
        var r = Run(Handler("H", "Root", "Res"), App("""app.MapGrpc<H>("Users/Get");"""), """
            namespace A { public sealed class Req { public int X { get; set; } } }
            namespace B { public sealed class Req { public string Y { get; set; } = ""; } }
            public sealed class W<T> { public T Value { get; set; } = default!; }
            public sealed class Root
            {
                public A.Req A { get; set; } = new();
                public B.Req B { get; set; } = new();
                public W<List<int>> W1 { get; set; } = new();
                public W<List<string>> W2 { get; set; } = new();
            }
            public sealed class Res { public int Id { get; set; } }
            """);
        AssertClean(r);
        var reg = r.GeneratedText("KetPsiGrpcRegistration");
        Assert.Contains("message A_Req", reg);
        Assert.Contains("message B_Req", reg);
        Assert.Contains("message W_List_Int32", reg);
        Assert.Contains("message W_List_String", reg);
    }

    [Fact]
    public void ReferencedAssemblyRegistrations_AreEnsuredExplicitly()
    {
        // Library with MapGrpc
        var lib = Run(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""));
        AssertClean(lib);
        var libCompilation = lib.Output;
        using var ms = new MemoryStream();
        var emit = libCompilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var libRef = MetadataReference.CreateFromImage(ms.ToArray());

        // Host without MapGrpc that references the library
        var host = GeneratorHarness.Compilation("public static class HostOnly { }").AddReferences(libRef);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GrpcGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(host, out var output, out _);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        var text = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("global::KetPsi.Endpoints.Grpc.Generated.GenTest.KetPsiGrpcRegistration.Ensure();", text);
    }

    [Fact]
    public void UnrelatedEdit_IsFullyCached()
    {
        var compilation = GeneratorHarness.Compilation(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get").RequireAuthorization();"""),
            "public static class Unrelated { public static int Value() => 1; }");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new GrpcGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var tree = compilation.SyntaxTrees.Last();
        var edited = compilation.ReplaceSyntaxTree(tree,
            tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(tree.ToString().Replace("=> 1;", "=> 2 + 40;"))));
        driver = driver.RunGenerators(edited);
        var result = driver.GetRunResult().Results.Single();

        foreach (var step in new[] { GrpcGenerator.CombinedStep })
        {
            var outputs = result.TrackedSteps[step].SelectMany(s => s.Outputs);
            Assert.All(outputs, o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, $"{step}: {o.Reason}"));
        }
        Assert.All(result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs),
            o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, o.Reason.ToString()));
    }

    [Fact]
    public void RelevantEdit_ProducesNewOutput()
    {
        var compilation = GeneratorHarness.Compilation(Dtos, Handler("H", "Req", "Res"), App("""app.MapGrpc<H>("Users/Get");"""));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new GrpcGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var tree = compilation.SyntaxTrees.First();
        var edited = compilation.ReplaceSyntaxTree(tree,
            tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(tree.ToString().Replace("public int Id { get; set; } public string", "public long Id { get; set; } public string"))));
        driver = driver.RunGenerators(edited);
        var result = driver.GetRunResult().Results.Single();
        Assert.Contains(result.TrackedSteps[GrpcGenerator.CombinedStep].SelectMany(s => s.Outputs), o => o.Reason == IncrementalStepRunReason.Modified);
        Assert.Contains("int64 Id", string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString())));
    }
}
