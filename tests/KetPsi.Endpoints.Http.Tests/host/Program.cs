using HttpEndpointGenerator.Tests.Handlers;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddKeyedScoped<IBlobStorage, PrimaryBlobStorage>("primary");
builder.Services.AddKeyedScoped<IBlobStorage, SecondaryBlobStorage>("secondary");

builder.Services.AddHttpEndpoints();


builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();


app.UseHttpsRedirection();

app.MapHttpEndpoints();

app.Run();

public partial class Program { }
