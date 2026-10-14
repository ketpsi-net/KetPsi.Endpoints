using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Tests;
using KetPsi.Endpoints.Grpc.Tests.Fixtures;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var arguments = args.Where(x => !x.StartsWith("--contentRoot")).ToList();
arguments.Add($"--contentRoot={AppContext.BaseDirectory}");
var builder = WebApplication.CreateBuilder(arguments.ToArray());

builder.AddGrpc();
builder.Services.AddGrpcServices();
builder.Services.AddProtoEndpoint();

builder.Services.AddScoped<GetUserByIdHandler>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<AllScalarsHandler>();
builder.Services.AddScoped<NullableScalarsHandler>();
builder.Services.AddScoped<CollectionsHandler>();
builder.Services.AddScoped<NestedRootHandler>();
builder.Services.AddScoped<PrimaryCtorOrderHandler>();
builder.Services.AddScoped<PrivateSetterOrderHandler>();
builder.Services.AddScoped<InitOnlyOrderHandler>();
builder.Services.AddScoped<MapOnlyHandler>();

var app = builder.Build();

app.MapGrpc<GetUserByIdHandler>("Api/Users/GetById");
app.MapGrpc<CreateOrderHandler>("Orders/Create");
app.MapGrpc<AllScalarsHandler>("Codec/Scalars/Echo");
app.MapGrpc<NullableScalarsHandler>("Codec/Nullable/Echo");
app.MapGrpc<CollectionsHandler>("Codec/Collections/Echo");
app.MapGrpc<NestedRootHandler>("Codec/Nested/Echo");
app.MapGrpc<PrimaryCtorOrderHandler>("Codec/PrimaryCtor/Echo");
app.MapGrpc<PrivateSetterOrderHandler>("Codec/PrivateSet/Echo");
app.MapGrpc<InitOnlyOrderHandler>("Codec/InitOnly/Echo");
app.MapGrpc<MapOnlyHandler>("Codec/Map/Echo");

app.MapGrpcEndpoints();
app.UseProtoEndpoint();

app.Run();

public partial class Program;
