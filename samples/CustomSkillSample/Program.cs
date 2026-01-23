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
        Title = "Custom Skills API",
        Version = "v1",
        Description = "Sample custom skills for Azure AI Search. These skills can be used with WebApiSkill in a skillset."
    });
});

var app = builder.Build();

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Custom Skills API v1");
    c.RoutePrefix = string.Empty;
});

app.UseRouting();
app.MapControllers();

Console.WriteLine("===========================================");
Console.WriteLine("  Custom Skills API for Azure AI Search");
Console.WriteLine("===========================================");
Console.WriteLine();
Console.WriteLine("Available Skills:");
Console.WriteLine("  POST /api/skills/text-stats       - Count characters, words, sentences");
Console.WriteLine("  POST /api/skills/extract-keywords - Extract keywords from text");
Console.WriteLine("  POST /api/skills/analyze-sentiment - Analyze sentiment (positive/negative/neutral)");
Console.WriteLine("  POST /api/skills/detect-pii       - Detect and mask PII (email, phone, SSN, CC)");
Console.WriteLine("  POST /api/skills/summarize        - Create extractive summary");
Console.WriteLine("  GET  /api/skills/health           - Health check");
Console.WriteLine();
Console.WriteLine("Swagger UI: http://localhost:5260");
Console.WriteLine();

app.Run();
