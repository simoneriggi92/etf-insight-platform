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
using EtfInsight.DataQuality.Models;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.Rules;
using EtfInsight.DataQuality.Services;

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

builder.Services.Configure<AISettings>(builder.Configuration.GetSection("AI"));

// Data Quality Settings
builder.Services.Configure<DataQualitySettings>(
    builder.Configuration.GetSection(DataQualitySettings.SectionName)
);

builder.Services.AddHttpClient("Ollama");

// Database connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=etfinsight;Username=etfinsight;Password=devpassword123";

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

var app = builder.Build();

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

