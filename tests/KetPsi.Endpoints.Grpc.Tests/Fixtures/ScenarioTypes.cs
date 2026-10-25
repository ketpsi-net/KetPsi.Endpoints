using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Grpc.Core;
using Grpc.Core.Interceptors;

using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KetPsi.Endpoints.Grpc.Tests.Scenarios
{
    // ------------------------------------------------------------------ generic echo

    public sealed class Echo<T> : IEndpoint
    {
        public Task<T> ExecuteAsync(T request, CancellationToken cancellationToken) => Task.FromResult(request);
    }

    // ------------------------------------------------------------------ streaming / empty

    public sealed class CountRequest
    {
        public int Count { get; set; }
    }

    public sealed class NumberMessage
    {
        public int Value { get; set; }
    }

    public sealed class SumResponse
    {
        public int Total { get; set; }
        public int Items { get; set; }
    }

    public sealed class CountdownHandler : IEndpoint
    {
        public async IAsyncEnumerable<NumberMessage> ExecuteAsync(CountRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = request.Count; i > 0; i--)
            {
                await Task.Yield();
                yield return new NumberMessage { Value = i };
            }
        }
    }

    public sealed class SumHandler : IEndpoint
    {
        public async Task<SumResponse> ExecuteAsync(IAsyncEnumerable<NumberMessage> numbers, CancellationToken ct)
        {
            var result = new SumResponse();
            await foreach (var n in numbers.WithCancellation(ct))
            {
                result.Total += n.Value;
                result.Items++;
            }
            return result;
        }
    }

    public sealed class DoubleHandler : IEndpoint
    {
        public async IAsyncEnumerable<NumberMessage> ExecuteAsync(IAsyncEnumerable<NumberMessage> numbers, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var n in numbers.WithCancellation(ct))
                yield return new NumberMessage { Value = n.Value * 2 };
        }
    }

    public sealed class SevenHandler : IEndpoint
    {
        public Task<NumberMessage> ExecuteAsync(CancellationToken ct) => Task.FromResult(new NumberMessage { Value = 7 });
    }

    public sealed class FireHandler : IEndpoint
    {
        public static int Total;

        public Task ExecuteAsync(NumberMessage message, CancellationToken ct)
        {
            Interlocked.Add(ref Total, message.Value);
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------ auth

    public sealed class VaultReadHandler : IEndpoint
    {
        public Task<NumberMessage> ExecuteAsync(CountRequest request, CancellationToken ct) => Task.FromResult(new NumberMessage { Value = 42 });
    }

    public sealed class VaultPingHandler : IEndpoint
    {
        public Task<NumberMessage> ExecuteAsync(CountRequest request, CancellationToken ct) => Task.FromResult(new NumberMessage { Value = 1 });
    }

    /// <summary>Authenticates only when the request carries <c>authorization: Test ok</c>.</summary>
    public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "Test";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization.ToString() != "Test ok")
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }

    // ------------------------------------------------------------------ interceptors

    public abstract class TagInterceptor : Interceptor
    {
        protected abstract string Tag { get; }

        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            context.ResponseTrailers.Add("x-interceptor", Tag);
            return continuation(request, context);
        }

        public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            context.ResponseTrailers.Add("x-interceptor", Tag);
            return continuation(request, responseStream, context);
        }
    }

    public sealed class TagInterceptorA : TagInterceptor
    {
        protected override string Tag => "A";
    }

    public sealed class TagInterceptorB : TagInterceptor
    {
        protected override string Tag => "B";
    }

    public sealed class InterceptedHandler : IEndpoint
    {
        public Task<NumberMessage> ExecuteAsync(CountRequest request, CancellationToken ct) => Task.FromResult(new NumberMessage { Value = request.Count });
    }

    public sealed class InterceptedHandlerConfig : IGrpcEndpointConfiguration<InterceptedHandler>
    {
        public void Configure(IGrpcEndpointBuilder<InterceptedHandler> builder)
        {
            builder.AddInterceptor<TagInterceptorA>();
            builder.AddInterceptor(typeof(TagInterceptorB));
        }
    }

    public sealed class InlineInterceptedHandler : IEndpoint
    {
        public Task<NumberMessage> ExecuteAsync(CountRequest request, CancellationToken ct) => Task.FromResult(new NumberMessage { Value = request.Count });
    }

    public sealed class PlainHandler : IEndpoint
    {
        public Task<NumberMessage> ExecuteAsync(CountRequest request, CancellationToken ct) => Task.FromResult(new NumberMessage { Value = request.Count });
    }

    public sealed class StreamWithConfigHandler : IEndpoint
    {
        public async IAsyncEnumerable<NumberMessage> ExecuteAsync(CountRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new NumberMessage { Value = request.Count };
        }
    }

    public sealed class ExplicitConfig : IGrpcEndpointConfiguration<StreamWithConfigHandler>
    {
        public void Configure(IGrpcEndpointBuilder<StreamWithConfigHandler> builder) => builder.AddInterceptor<TagInterceptorB>();
    }

    // ------------------------------------------------------------------ metadata binding

    public sealed class TenantRequest
    {
        public string Body { get; set; } = "";
        public string? Tenant { get; set; }
        public int Count { get; set; }
        public double Ratio { get; set; }
        public Fixtures.SampleStatus Status { get; set; }
        public Guid? CorrelationId { get; set; }
    }

    public sealed class TenantResponse
    {
        public string Summary { get; set; } = "";
    }

    public sealed class TenantEchoHandler : IEndpoint
    {
        public Task<TenantResponse> ExecuteAsync(TenantRequest r, CancellationToken ct)
            => Task.FromResult(new TenantResponse
            {
                Summary = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{r.Body}|{r.Tenant}|{r.Count}|{r.Ratio}|{r.Status}|{r.CorrelationId}")
            });
    }

    public sealed class TenantEchoConfig : IGrpcEndpointConfiguration<TenantEchoHandler>
    {
        public void Configure(IGrpcEndpointBuilder<TenantEchoHandler> builder)
        {
            builder.Request<TenantRequest>(req =>
            {
                req.Property(x => x.Tenant, p => p.FromMetadata("x-tenant"));
                req.Property(x => x.Count, p => p.FromMetadata("x-count"));
                req.Property(x => x.Ratio, p => p.FromMetadata("x-ratio"));
                req.Property(x => x.Status, p => p.FromMetadata("x-status"));
                req.Property(x => x.CorrelationId, p => p.FromMetadata());
            });
        }
    }

    // ------------------------------------------------------------------ codec edge cases

    public class BaseDto
    {
        public int BaseId { get; set; }
        public string BaseName { get; set; } = "";
    }

    public sealed class DerivedDto : BaseDto
    {
        public string Extra { get; set; } = "";
    }

    public sealed record CtorInitRecord(int A, string Name)
    {
        public string B { get; init; } = "";
        public List<int> Values { get; init; } = new();
    }

    public sealed class TemporalMessage
    {
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public DateOnly? MaybeDate { get; set; }
        public TimeOnly? MaybeTime { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset At { get; set; }
        public TimeSpan Span { get; set; }
        public decimal Money { get; set; }
        public decimal? MaybeMoney { get; set; }
        public Guid Id { get; set; }
        public Guid? MaybeId { get; set; }
        public char Letter { get; set; }
    }

    public enum Priority
    {
        Low = 1,
        High = 2,
    }

    public enum Delta
    {
        Minus = -1,
        Zero = 0,
        Plus = 1,
    }

    public sealed class EnumMessage
    {
        public Priority P { get; set; } = Priority.Low;
        public Delta D { get; set; }
        public List<Delta> Ds { get; set; } = new();
    }

    public sealed class TreeNode
    {
        public int Value { get; set; }
        public TreeNode? Child { get; set; }
    }

    public sealed class EdgeMessage
    {
        public string Text { get; set; } = "";
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public float F { get; set; }
        public double D { get; set; }
        public long L { get; set; }
        public ulong U { get; set; }
        public int I { get; set; }
        public List<double> Doubles { get; set; } = new();
        public Dictionary<string, NumberMessage> Map { get; set; } = new();
        public Dictionary<long, string> LongMap { get; set; } = new();
        public HashSet<string> Tags { get; set; } = new();
        public IReadOnlyList<int> RoList { get; set; } = Array.Empty<int>();
        public ImmutableArray<int> Imm { get; set; } = ImmutableArray<int>.Empty;
        public int[] Arr { get; set; } = Array.Empty<int>();
        public string? Optional { get; set; }
    }

    public struct PointStruct
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class StructHolder
    {
        public PointStruct P { get; set; }
        public PointStruct? Q { get; set; }
    }

    public sealed class Wrapper<T>
    {
        public T Value { get; set; } = default!;
    }

    public static class Outer1
    {
        public sealed class Inner
        {
            public int X { get; set; }
        }
    }

    public static class Outer2
    {
        public sealed class Inner
        {
            public string Y { get; set; } = "";
        }
    }

    public sealed class SameNameRequest
    {
        public Alpha.Req A { get; set; } = new();
        public Beta.Req B { get; set; } = new();
        public Wrapper<List<int>> W1 { get; set; } = new();
        public Wrapper<List<string>> W2 { get; set; } = new();
        public Outer1.Inner I1 { get; set; } = new();
        public Outer2.Inner I2 { get; set; } = new();
    }
}

namespace KetPsi.Endpoints.Grpc.Tests.Scenarios.Alpha
{
    public sealed class Req
    {
        public int A { get; set; }
    }
}

namespace KetPsi.Endpoints.Grpc.Tests.Scenarios.Beta
{
    public sealed class Req
    {
        public string B { get; set; } = "";
    }
}
