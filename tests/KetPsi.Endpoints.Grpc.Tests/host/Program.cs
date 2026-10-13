using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var arguments = args.Where(x => !x.StartsWith("--contentRoot")).ToList();
arguments.Add($"--contentRoot={AppContext.BaseDirectory}");
var builder = WebApplication.CreateBuilder(arguments.ToArray());

builder.AddGrpc();
builder.Services.AddGrpcServices();
builder.Services.AddProtoEndpoint();


builder.Services.AddScoped<GetUserByIdHandler>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<Api_Users_Proxy>();
builder.Services.AddScoped<Api_Orders_Proxy>();

var app = builder.Build();

app.MapGrpc<GetUserByIdHandler>("Api/Users/GetById");
app.MapGrpc<CreateOrderHandler>("Orders/Create");

app.MapGrpcEndpoints();
app.UseProtoEndpoint();

app.Run();