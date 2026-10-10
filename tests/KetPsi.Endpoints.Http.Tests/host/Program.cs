using HttpEndpointGenerator.Tests.Handlers;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var arguments = args.Where(x => !x.StartsWith("--contentRoot")).ToList();
arguments.Add($"--contentRoot={AppContext.BaseDirectory}");
 var builder = WebApplication.CreateBuilder(arguments.ToArray());

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddKeyedScoped<IBlobStorage, PrimaryBlobStorage>("primary");
builder.Services.AddKeyedScoped<IBlobStorage, SecondaryBlobStorage>("secondary");

builder.Services.AddHttpEndpoints();


builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();


app.UseHttpsRedirection();

app.MapHttpEndpoints();

await app.RunAsync();

