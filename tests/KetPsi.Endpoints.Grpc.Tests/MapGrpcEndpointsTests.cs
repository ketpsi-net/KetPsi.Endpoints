using Grpc.Core;
using System.Net;
using System.Net.Http;

using Grpc.Net.Client;

using KetPsi.Endpoints.Abstractions;
using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Abstractions;
using KetPsi.Endpoints.Abstractions;
using KetPsi.Endpoints.Grpc;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;


using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>
/// Integration tests for mapped gRPC endpoints via client contracts (no CallContext).
/// </summary>
public sealed class MapGrpcEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MapGrpcEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    [Fact]
    public async Task MappedEndpoint_GetById_ReturnsUserViaGrpcClient()
    {
        using var channel = CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();
        var call = invoker.AsyncUnaryCall(
            Api_Users_GrpcMethods.GetById,
            host: null,
            options: default,
            request: new GetUserRequest { Id = 42 });
        var response = await call.ResponseAsync;

        Assert.Equal(42, response.Id);
        Assert.Equal("User-42", response.Name);
    }

    [Fact]
    public async Task MappedEndpoint_CreateOrder_ReturnsOrderIdViaGrpcClient()
    {
        using var channel = CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();
        var call = invoker.AsyncUnaryCall(
            Api_Orders_GrpcMethods.Create,
            host: null,
            options: default,
            request: new CreateOrderRequest { Sku = "SKU-1", Quantity = 2 });
        var response = await call.ResponseAsync;

        Assert.NotEqual(Guid.Empty, response.OrderId);
    }

    [Fact]
    public async Task MappedProxy_FromDi_InvokesHandlerThroughProxy()
    {
        using var scope = _factory.Services.CreateScope();
        var proxy = scope.ServiceProvider.GetRequiredService<Api_Users_Proxy>();

        var response = await proxy.GetByIdAsync(new GetUserRequest { Id = 7 });

        Assert.Equal(7, response.Id);
        Assert.Equal("User-7", response.Name);
    }

    [Fact]
    public async Task MappedProxy_Orders_FromDi_InvokesHandlerThroughProxy()
    {
        using var scope = _factory.Services.CreateScope();
        var proxy = scope.ServiceProvider.GetRequiredService<Api_Orders_Proxy>();

        var response = await proxy.CreateAsync(new CreateOrderRequest
        {
            Sku = "SKU-2",
            Quantity = 1
        });

        Assert.NotEqual(Guid.Empty, response.OrderId);
    }

    [Fact]
    public void MappedEndpoints_ProxiesAndHandlers_AreRegisteredInDi()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetService<GetUserByIdHandler>());
        Assert.NotNull(sp.GetService<CreateOrderHandler>());
        Assert.NotNull(sp.GetService<Api_Users_Proxy>());
        Assert.NotNull(sp.GetService<Api_Orders_Proxy>());
    }

    [Fact]
    public async Task ProtoEndpoint_WhenMapped_ReturnsPlainTextSchema()
    {
        var http = _factory.CreateClient();
        var response = await http.GetAsync("/proto");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrWhiteSpace(body));
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("Api/Users/GetById")]
    [InlineData("Orders/Create")]
    [InlineData("Billing/Invoices/GetById")]
    public void MapGrpc_ValidRoute_DoesNotThrow(string route)
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddGrpc();
        var app = builder.Build();

        Assert.Null(Record.Exception(() => app.MapGrpc<GetUserByIdHandler>(route)));
    }

    [Fact]
    public void MapGrpc_AllVariants_DoNotThrow()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddGrpc();
        var app = builder.Build();

        Assert.Null(Record.Exception(() => app.MapGrpcUnary<GetUserByIdHandler>("Api/Users/GetByIdUnary")));
        Assert.Null(Record.Exception(() => app.MapGrpcClientStream<CreateOrderHandler>("Orders/CreateClientStream")));
        Assert.Null(Record.Exception(() => app.MapGrpcServerStream<GetUserByIdHandler>("Api/Users/Stream")));
        Assert.Null(Record.Exception(() => app.MapGrpcDuplexStream<CreateOrderHandler>("Orders/Duplex")));
    }

    [Fact]
    public void AddGrpc_And_AddProtoEndpoint_AreIdempotent()
    {
        var builder = WebApplication.CreateBuilder();

        Assert.Null(Record.Exception(() =>
        {
            builder.AddGrpc();
            builder.Services.AddProtoEndpoint();
            builder.AddGrpc();
            builder.Services.AddProtoEndpoint();
        }));
    }

    private GrpcChannel CreateGrpcChannel()
    {
        var httpClient = _factory.CreateDefaultClient(new ResponseVersionHandler());
        httpClient.BaseAddress = _factory.Server.BaseAddress;
        httpClient.DefaultRequestVersion = HttpVersion.Version20;
        httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
    }

    private sealed class ResponseVersionHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Version = request.Version;
            return response;
        }
    }
}

// ---------------------------------------------------------------------------
// DTOs + IProtoTypeConfiguration maps
// ---------------------------------------------------------------------------

public sealed class GetUserRequest
{
    public int Id { get; set; }
}

public sealed class GetUserResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class CreateOrderRequest
{
    public string Sku { get; set; } = "";

    public int Quantity { get; set; }
}

public sealed class CreateOrderResponse
{
    public Guid OrderId { get; set; }
}

public sealed class GetUserRequestProtoConfig : IProtoTypeConfiguration<GetUserRequest>
{
    public void Configure(IProtoTypeBuilder<GetUserRequest> builder)
    {
        builder.Property(x => x.Id, 1);
    }
}

public sealed class GetUserResponseProtoConfig : IProtoTypeConfiguration<GetUserResponse>
{
    public void Configure(IProtoTypeBuilder<GetUserResponse> builder)
    {
        builder.Property(x => x.Id, 1)
               .Property(x => x.Name, 2);
    }
}

public sealed class CreateOrderRequestProtoConfig : IProtoTypeConfiguration<CreateOrderRequest>
{
    public void Configure(IProtoTypeBuilder<CreateOrderRequest> builder)
    {
        builder.Property(x => x.Sku, 1)
               .Property(x => x.Quantity, 2);
    }
}

public sealed class CreateOrderResponseProtoConfig : IProtoTypeConfiguration<CreateOrderResponse>
{
    public void Configure(IProtoTypeBuilder<CreateOrderResponse> builder)
    {
        builder.Property(x => x.OrderId, 1);
    }
}

public sealed class GetUserByIdHandler : IEndpoint
{
    public Task<GetUserResponse> ExecuteAsync(GetUserRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GetUserResponse
        {
            Id = request.Id,
            Name = $"User-{request.Id}"
        });
    }
}

public sealed class CreateOrderHandler : IEndpoint
{
    public Task<CreateOrderResponse> ExecuteAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new CreateOrderResponse { OrderId = Guid.NewGuid() });
    }
}
