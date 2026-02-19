using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "File Data Skill API",
        Version = "v1",
        Description = "Custom skill that reads a file and returns its content as base64-encoded file_data for Azure AI Search Document Extraction."
    });
});

var app = builder.Build();

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "File Data Skill API v1");
    c.RoutePrefix = string.Empty;
});

app.UseRouting();
app.MapControllers();

Console.WriteLine("===========================================");
Console.WriteLine("  File Data Skill API for Azure AI Search");
Console.WriteLine("===========================================");
Console.WriteLine();
Console.WriteLine("Available Skills:");
Console.WriteLine("  POST /api/skills/file-data  - Read file and return base64-encoded file_data");
Console.WriteLine("  GET  /api/skills/health     - Health check");
Console.WriteLine();
Console.WriteLine("Configuration:");
Console.WriteLine($"  FileData:BasePath = {app.Configuration.GetValue<string>("FileData:BasePath") ?? "(not set)"}");
Console.WriteLine();
Console.WriteLine("Swagger UI: http://localhost:5270");
Console.WriteLine();

app.Run();
