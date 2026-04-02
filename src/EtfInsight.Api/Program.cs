using Npgsql;
using Dapper;
using System.Data;
using EtfInsight.Api.Services;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.Entities;
using EtfInsight.Infrastructure.Repositories;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Services;
using EtfInsight.Core.Configuration;
using EtfInsight.Infrastructure.Services;
using EtfInsight.Infrastructure.Services.BrokerPdf;
using EtfInsight.DataQuality.Models;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.Rules;
using EtfInsight.DataQuality.Services;
using Hangfire;
using Hangfire.PostgreSql;
using EtfInsight.Api.Filters;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "ETF Insight API",
        Version = "v1",
        Description = "REST API for ETF price data and portfolio analytics"
    });
});

// Database connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=etfinsight;Username=etfinsight;Password=devpassword123";

builder.Services.AddHttpClient("Ollama");
builder.Services.AddHttpClient("Airflow");


// Hangfire configuration
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString))
);

// Add the worker for fire-and-forget jobs
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.ServerName = "etf-insight-bgserver";
});

builder.Services.Configure<AISettings>(builder.Configuration.GetSection("AI"));

// Data Quality Settings
builder.Services.Configure<DataQualitySettings>(
    builder.Configuration.GetSection(DataQualitySettings.SectionName)
);

const string DevCorsPolicy = "DevFrontend";

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();


builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddScoped<IDbConnection>(_ => new Npgsql.NpgsqlConnection(connectionString));
builder.Services.AddScoped<IFxRateService, FxRateService>();
builder.Services.AddScoped<IEtfRepository, PostgresRepository>();
builder.Services.AddScoped<EtfInsight.Core.Interfaces.IEtfPriceRepository, DapperEtfPriceRepository>();
builder.Services.AddScoped<IPortfolioRepository, DapperPortfolioRepository>();
builder.Services.AddScoped<IPerformanceCalculator, TwrrCalculator>();
builder.Services.AddScoped<IPortfolioAnalyticsService, PortfolioAnalyticsService>();
builder.Services.AddScoped<IEmbeddingGenerator, OllamaEmbeddingService>();
builder.Services.AddScoped<ISemanticSearchRepository, DapperSemanticSearchRepository>();
builder.Services.AddScoped<IChatService, OllamaChatService>();

// Data Quality - Register rules
builder.Services.AddTransient<IDataQualityRule, NegativePriceRule>();
builder.Services.AddTransient<IDataQualityRule, FlashCrashRule>();

builder.Services.AddScoped<EtfInsight.DataQuality.Interfaces.IDataQualityRepository, DapperDataQualityRepository>();
builder.Services.AddScoped<EtfInsight.DataQuality.Interfaces.IEtfPriceRepository, DataQualityEtfPriceRepository>();

// Data Quality - Register scanner
builder.Services.AddScoped<DataQualityScanner>();
builder.Services.AddScoped<EtfInsight.Core.Interfaces.IIngestionService, AirflowIngestionService>();
builder.Services.AddScoped<EtfInsight.Core.Interfaces.ICsvImportService, CsvImportService>();
builder.Services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddScoped<ITradeRepublicParser, TradeRepublicParser>();
builder.Services.AddScoped<IBrokerImportRepository, DapperBrokerImportRepository>();
builder.Services.AddScoped<IBrokerPdfImportService, BrokerPdfImportService>();

var app = builder.Build();

// Hangfire dashboard (optional, for monitoring background jobs)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Dev-only: allows all requests (required when running behind Docker/reverse proxy).
    // Replace with a role-based filter in production.
    Authorization = new[] { new EtfInsight.Api.Filters.AllowAllDashboardAuthorizationFilter() }
});

// Hangfire recurring job setup 
RecurringJob.AddOrUpdate<DataQualityScanner>(
    "nightly-data-quality-scan",
    scanner => scanner.ScanRecentPricesAsync(),
    Cron.Daily(2) // Every day at 2:00 AM
);

// Request logging middleware
app.Use(async (context, next) =>
{
    var startTime = DateTime.UtcNow;
    var requestPath = context.Request.Path;
    var requestMethod = context.Request.Method;

    try
    {
        await next(context);

        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        var statusCode = context.Response.StatusCode;

        // Log requests

        Console.WriteLine($"[{startTime:yyyy-MM-dd HH:mm:ss}] {requestMethod} {requestPath} -> {statusCode} ({duration:F0}ms)");

    }
    catch (Exception ex)
    {
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        Console.WriteLine($"[{startTime:yyyy-MM-dd HH:mm:ss}] {requestMethod} {requestPath} -> ERROR ({duration:F0}ms)");
        Console.WriteLine($"    Exception: {ex.Message}");
        throw;
    }
});

app.UseMiddleware<EtfInsight.Api.Middleware.GuestSessionMiddleware>();
app.UseCors(DevCorsPolicy);

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ETF Insight API v1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}

// Map controllers
app.MapControllers();

app.Run();
