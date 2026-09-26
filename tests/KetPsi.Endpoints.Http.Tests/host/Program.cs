using HttpEndpointGenerator.Tests.Handlers;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register services that handlers depend on
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddKeyedScoped<IBlobStorage, PrimaryBlobStorage>("primary");
builder.Services.AddKeyedScoped<IBlobStorage, SecondaryBlobStorage>("secondary");

// This is what the generator produces
builder.Services.AddHttpEndpoints();

// Also register handlers manually if the generator's Add* is not yet present
// (in real usage the generated code does this)

builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

// Fallback: call the explicit mappings so the project works even before generator runs
app.MapHttpEndpoints();

app.Run();

// Make the Program class visible to WebApplicationFactory
public partial class Program { }
