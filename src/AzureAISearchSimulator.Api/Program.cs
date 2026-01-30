using AzureAISearchSimulator.Api.Middleware;
using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Api.Services.Authorization;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Core.Services.Authentication;
using AzureAISearchSimulator.DataSources;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.DataSources;
using AzureAISearchSimulator.Search.DocumentCracking;
using AzureAISearchSimulator.Search.Hnsw;
using AzureAISearchSimulator.Search.Skills;
using AzureAISearchSimulator.Storage.Repositories;
using Scalar.AspNetCore;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Azure AI Search Simulator");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from appsettings
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Bind configuration sections
    builder.Services.Configure<SimulatorSettings>(
        builder.Configuration.GetSection(SimulatorSettings.SectionName));
    builder.Services.Configure<AuthenticationSettings>(
        builder.Configuration.GetSection(AuthenticationSettings.SectionName));
    builder.Services.Configure<OutboundAuthenticationSettings>(
        builder.Configuration.GetSection(OutboundAuthenticationSettings.SectionName));
    builder.Services.Configure<LuceneSettings>(
        builder.Configuration.GetSection(LuceneSettings.SectionName));
    builder.Services.Configure<IndexerSettings>(
        builder.Configuration.GetSection(IndexerSettings.SectionName));
    builder.Services.Configure<VectorSearchSettings>(
        builder.Configuration.GetSection(VectorSearchSettings.SectionName));
    builder.Services.Configure<AzureOpenAISettings>(
        builder.Configuration.GetSection(AzureOpenAISettings.SectionName));

    // Register authentication handlers
    builder.Services.AddSingleton<IAuthenticationHandler, ApiKeyAuthenticationHandler>();
    builder.Services.AddSingleton<IAuthenticationHandler, SimulatedAuthenticationHandler>();
    builder.Services.AddSingleton<IAuthenticationHandler, EntraIdAuthenticationHandler>();
    
    // Register authentication and authorization services
    builder.Services.AddSingleton<ISimulatedTokenService, SimulatedTokenService>();
    builder.Services.AddSingleton<IEntraIdTokenValidator, EntraIdTokenValidator>();
    builder.Services.AddSingleton<IAuthorizationService, AuthorizationService>();
    
    // Add memory cache for OpenID Connect configuration caching
    builder.Services.AddMemoryCache();

    // Add services
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.DefaultIgnoreCondition = 
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            // Allow Infinity and NaN values in JSON (can happen with search scores)
            options.JsonSerializerOptions.NumberHandling = 
                System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
        });

    // Add OpenAPI documentation
    builder.Services.AddOpenApi();

    // Register repositories
    builder.Services.AddSingleton<IIndexRepository, LiteDbIndexRepository>();
    builder.Services.AddSingleton<IDataSourceRepository, LiteDbDataSourceRepository>();
    builder.Services.AddSingleton<IIndexerRepository, LiteDbIndexerRepository>();
    builder.Services.AddSingleton<ISkillsetRepository, LiteDbSkillsetRepository>();

    // Register Search infrastructure
    builder.Services.AddSingleton<LuceneIndexManager>();
    builder.Services.AddSingleton<VectorStore>();
    
    // Register HNSW vector search infrastructure
    builder.Services.AddSingleton<IHnswIndexManager, HnswIndexManager>();
    builder.Services.AddSingleton<IVectorSearchService, HnswVectorSearchService>();
    builder.Services.AddSingleton<IHybridSearchService, HybridSearchService>();

    // Register data source connectors
    builder.Services.AddSingleton<IDataSourceConnector, FileSystemConnector>();
    builder.Services.AddAzureDataSourceConnectors(); // Add Azure Blob Storage and ADLS Gen2 connectors
    builder.Services.AddSingleton<IDataSourceConnectorFactory, DataSourceConnectorFactory>();

    // Register document crackers
    builder.Services.AddSingleton<IDocumentCracker, PlainTextCracker>();
    builder.Services.AddSingleton<IDocumentCracker, JsonCracker>();
    builder.Services.AddSingleton<IDocumentCracker, CsvCracker>();
    builder.Services.AddSingleton<IDocumentCracker, HtmlCracker>();
    builder.Services.AddSingleton<IDocumentCracker, PdfCracker>();
    builder.Services.AddSingleton<IDocumentCracker, WordDocCracker>();
    builder.Services.AddSingleton<IDocumentCracker, ExcelCracker>();
    builder.Services.AddSingleton<IDocumentCrackerFactory, DocumentCrackerFactory>();

    // Register skill executors
    builder.Services.AddSingleton<ISkillExecutor, TextSplitSkillExecutor>();
    builder.Services.AddSingleton<ISkillExecutor, TextMergeSkillExecutor>();
    builder.Services.AddSingleton<ISkillExecutor, ShaperSkillExecutor>();
    builder.Services.AddSingleton<ISkillExecutor, ConditionalSkillExecutor>();
    builder.Services.AddSingleton<ISkillExecutor, CustomWebApiSkillExecutor>();
    builder.Services.AddSingleton<ISkillExecutor, AzureOpenAIEmbeddingSkillExecutor>();
    builder.Services.AddSingleton<ISkillPipeline, SkillPipeline>();

    // Register HTTP client factory for custom skills
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient("AzureOpenAI", (serviceProvider, client) =>
    {
        var config = serviceProvider.GetRequiredService<IConfiguration>();
        var apiKey = config.GetValue<string>("AzureOpenAI:ApiKey");
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add("api-key", apiKey);
        }
    });

    // Register services
    builder.Services.AddScoped<IIndexService, IndexService>();
    builder.Services.AddScoped<IDocumentService, DocumentService>();
    builder.Services.AddScoped<ISearchService, SearchService>();
    builder.Services.AddScoped<IDataSourceService, DataSourceService>();
    builder.Services.AddScoped<IIndexerService, IndexerService>();
    builder.Services.AddScoped<ISkillsetService, SkillsetService>();

    // Register background services
    builder.Services.AddSingleton<IndexerSchedulerService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<IndexerSchedulerService>());

    var app = builder.Build();

    // Configure the HTTP request pipeline
    // IMPORTANT: OData URL rewriter must run BEFORE routing
    app.UseODataUrlRewriter(); // Rewrite OData-style URLs early, before routing
    
    app.UseRouting(); // Explicitly add routing after URL rewriter
    
    app.UseGlobalExceptionHandler(); // Add global exception handler
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "Azure AI Search Simulator API";
            options.Theme = ScalarTheme.Default;
        });
    }

    // Add API key authentication middleware (unified authentication)
    app.UseUnifiedAuthentication();

    app.MapControllers();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
        .ExcludeFromDescription();

    Log.Information("Azure AI Search Simulator is ready at {Urls}", 
        string.Join(", ", app.Urls.DefaultIfEmpty("http://localhost:5000")));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
