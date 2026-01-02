using Npgsql;
using Dapper;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
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

builder.Services.AddScoped<IDbConnection>(_ => new Npgsql.NpgsqlConnection(connectionString));

var app = builder.Build();


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

// ============================================================================
// ENDPOINTS
// ============================================================================


// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow
}))
.WithName("HealthCheck")
.WithTags("Health");


// Get all tracked symbols

app.MapGet("/api/symbols", async (IDbConnection db) =>
{
    var query = @" 
    SELECT DISTINCT symbol,
        COUNT(*) as data_points,
        MIN(price_date) as first_date,     
        MAX(price_date) as last_date
    FROM etf_prices 
    GROUP BY symbol
    ORDER BY symbol";

    var symbols = await db.QueryAsync(query);
    return Results.Ok(symbols);
})
.WithName("GetSymbols")
.WithTags("Symbols")
.Produces<IEnumerable<object>>(StatusCodes.Status200OK);


// Get latest price for a given symbol
app.MapGet("/api/prices/latest", async (string symbol, IDbConnection db) =>
{
    var query = @"
    SELECT symbol, price_date, open_price, high_price, low_price, close_price, volume
    FROM etf_prices
    WHERE symbol = @Symbol
    ORDER BY price_date DESC
    LIMIT 1";

    var latestPrice = await db.QueryFirstOrDefaultAsync(query, new { Symbol = symbol.ToUpper() });
    if (latestPrice == null)
    {
        return Results.NotFound(new { error = $"No data found for symbol {symbol}" });
    }

    return Results.Ok(latestPrice);
})
.WithName("GetLatestPrice")
.WithTags("Prices")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);


// Get price history with date range
app.MapGet("/api/prices", async (
    string symbol,
    string? from = null,
    string? to = null,
    IDbConnection db = null!) =>
{
    if (string.IsNullOrWhiteSpace(symbol))
    {
        return Results.BadRequest(new { error = "Symbol parameter is required" });
    }

    // Default date range: last 30 days
    var toDate = string.IsNullOrWhiteSpace(to) ?
        DateTime.UtcNow.Date
        : DateTime.Parse(to).Date;

    var fromDate = string.IsNullOrWhiteSpace(from) ?
        DateTime.UtcNow.AddDays(-30).Date
        : DateTime.Parse(from).Date;

    var query = @"
        SELECT symbol, price_date, open_price, high_price, low_price, close_price, volume
        FROM etf_prices
        WHERE symbol = @Symbol
            AND price_date >= @FromDate
            AND price_date <= @ToDate
        ORDER BY price_date DESC";

    var prices = await db.QueryAsync(query, new
    {
        Symbol = symbol.ToUpper(),
        FromDate = fromDate,
        ToDate = toDate
    });

    if (!prices.Any())
    {
        return Results.NotFound(new { error = $"No data found for symbol {symbol} between {fromDate} and {toDate}." });
    }

    return Results.Ok(new
    {
        symbol = symbol.ToUpper(),
        from = fromDate.ToString("yyyy-MM-dd"),
        to = toDate.ToString("yyyy-MM-dd"),
        count = prices.Count(),
        data = prices
    });
})
.WithName("GetPriceHistory")
.WithTags("Prices")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);

// Price statistic for a symbol
app.MapGet("/api/prices/stats", async (
    string symbol,
    string? from = null,
    string? to = null,
    IDbConnection db = null!) =>
{
    if (string.IsNullOrWhiteSpace(symbol))
    {
        return Results.BadRequest(new { error = "Symbol parameter is required." });
    }

    // Default date range: last 30 days
    var toDate = string.IsNullOrWhiteSpace(to) ?
        DateTime.UtcNow.Date
        : DateTime.Parse(to).Date;

    var fromDate = string.IsNullOrWhiteSpace(from) ?
        DateTime.UtcNow.AddDays(-365).Date // Default: 1 year
        : DateTime.Parse(from).Date;

    var query = @"
    SELECT 
        symbol,
        COUNT(*) as trading_days,
        ROUND(MIN(low_price)::numeric, 2) as min_price,
        ROUND(MAX(high_price)::numeric, 2) as max_price,
        ROUND(AVG(close_price)::numeric, 2) as avg_price,
        ROUND(((MAX(high_price) - MIN(low_price)) / NULLIF(MIN(low_price), 0)) * 100, 2) as price_range_pct, -- Percentage price range, NULLIF to avoid division by zero
        SUM(volume) as total_volume,
        ROUND(AVG(volume)::numeric, 0) as avg_daily_volume
    FROM etf_prices
    WHERE symbol = @Symbol
        AND price_date >= @FromDate
        AND price_date <= @ToDate
    GROUP BY symbol";

    var stats = await db.QueryFirstOrDefaultAsync(query, new
    {
        Symbol = symbol.ToUpper(),
        FromDate = fromDate,
        ToDate = toDate
    });

    if (stats == null)
    {
        return Results.NotFound(new { error = $"No data found for symbol {symbol} between {fromDate} and {toDate}." });
    }

    return Results.Ok(new
    {
        symbol = symbol.ToUpper(),
        date_range = new { from = fromDate.ToString("yyyy-MM-dd"), to = toDate.ToString("yyyy-MM-dd") },
        statistics = stats
    });
})
.WithName("GetPriceStats")
.WithTags("Prices")
.Produces<object>(StatusCodes.Status200OK);

app.Run();
