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

// ============================================================================
// PORTFOLIO ENDPOINTS
// ============================================================================

// Create a new portfolio
app.MapPost("/api/portfolios", async (
    IDbConnection db,
    PortfolioCreateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Portfolio name is required." });
    }

    var query = @"
    INSERT INTO portfolios (name, description, base_currency)
    VALUES (@Name, @Description, @BaseCurrency)
    RETURNING id, name, description, base_currency, created_at";

    var portfolio = await db.QueryFirstAsync(query, new
    {
        Name = request.Name,
        Description = request.Description ?? string.Empty,
        BaseCurrency = request.BaseCurrency ?? "USD"
    });

    return Results.Created($"/api/portfolios/{portfolio.id}", portfolio);
})
.WithName("CreatePortfolio")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);

// Get all portfolios
app.MapGet("/api/portfolios", async (IDbConnection db) =>
{
    var query = @"
    SELECT p.id, p.name, p.description, p.base_currency, p.created_at,
        COUNT (t.id) as transaction_count,
        MIN(t.transaction_date) as first_transaction_date,
        MAX(t.transaction_date) as last_transaction_date
    FROM portfolios p
    LEFT JOIN transactions t on p.id = t.portfolio_id
    GROUP BY p.id, p.name, p.description, p.base_currency, p.created_at
    ORDER BY p.created_at DESC";

    var portfolios = await db.QueryAsync(query);
    return Results.Ok(portfolios);
})
.WithName("GetPortfolios")
.WithTags("Portfolios")
.Produces<IEnumerable<object>>(StatusCodes.Status200OK);


// Get portfolio by ID with sumamry
app.MapGet("/api/portfolios/{id:int}", async (int id, IDbConnection db) =>
{
    var query = @"
        SELECT id, name, description, base_currency, created_at
        FROM portfolios
        WHERE id = @id";

    var portfolio = await db.QuerySingleOrDefaultAsync(query, new { Id = id });

    if (portfolio == null)
    {
        return Results.NotFound(new { error = $"Portfolio with ID {id} not found." });
    }

    // Get transactions summary
    var transactionQuery = @"
        SELECT 
            COUNT(*) as total_transactions,
            COUNT(DISTINCT symbol) as unique_symbols,
            SUM(CASE WHEN transaction_type = 'BUY' THEN quantity * price ELSE 0 END) as total_invested,
            SUM(CASE WHEN transaction_type = 'SELL' THEN quantity * price ELSE 0 END) as total_proceeds
        FROM transactions
        WHERE portfolio_id = @Id";

    var summary = await db.QuerySingleAsync(transactionQuery, new { Id = id });

    return Results.Ok(new
    {
        portfolio,
        summary
    });
})
.WithName("GetPortfolio")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// Add transaction to portfolio
app.MapPost("/api/portfolios/{portfolioId:int}/transactions", async (
    int portfolioId,
    IDbConnection db,
    TransactionCreateRequest request) =>
{
    // Validate portfolio existence
    var portfolioExists = await db.ExecuteScalarAsync<bool>(
        "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
        new { Id = portfolioId });

    if (!portfolioExists)
    {
        return Results.NotFound(new { error = $"Portfolio with ID {portfolioId} not found." });
    }

    var symbolExists = await db.ExecuteScalarAsync<bool>(
        "SELECT EXISTS(SELECT 1 FROM etf_prices WHERE symbol = @Symbol)",
        new { Symbol = request.Symbol.ToUpper() });

    if (!symbolExists)
    {
        return Results.BadRequest(new { error = $"Symbol {request.Symbol} is not found in price database." });
    }

    // Validate transaction data 
    if (string.IsNullOrWhiteSpace(request.Symbol))
    {
        return Results.BadRequest(new { error = "Symbol is required." });
    }

    if (!new[] { "BUY", "SELL" }.Contains(request.TransactionType.ToUpper()))
    {
        return Results.BadRequest(new { error = "TransactionType must be either 'BUY' or 'SELL'." });
    }

    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new { error = "Quantity must be positive." });
    }

    if (request.Price <= 0)
    {
        return Results.BadRequest(new { error = "Price must be positive." });
    }

    // Insert transaction
    var query = @"
    INSERT INTO transactions (portfolio_id, symbol, transaction_type, quantity, price, transaction_date, notes)
    VALUES (@PortfolioId, @Symbol, @TransactionType, @Quantity, @Price, @TransactionDate, @Notes)
    RETURNING id, portfolio_id, symbol, transaction_type, quantity, price, transaction_date, notes, created_at";

    var transaction = await db.QuerySingleAsync(query, new
    {
        PortfolioId = portfolioId,
        Symbol = request.Symbol.ToUpper(),
        TransactionType = request.TransactionType.ToUpper(),
        Quantity = request.Quantity,
        Price = request.Price,
        TransactionDate = request.TransactionDate,
        Notes = request.Notes ?? string.Empty
    });

    return Results.Created(
        $"/api/portfolios/{portfolioId}/transactions/{transaction.id}",
        transaction);
})
.WithName("AddTransaction")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);


// Get portfolio transactions
app.MapGet("/api/portfolios/{portfolioId:int}/transactions", async (int portfolioId, IDbConnection db) =>
{
    // Validate portfolio existence
    var portfolioExists = await db.ExecuteScalarAsync<bool>(
        "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
        new { Id = portfolioId });

    if (!portfolioExists)
    {
        return Results.NotFound(new { error = $"Portfolio with ID {portfolioId} not found." });
    }

    var query = @"
    SELECT id, portfolio_id, symbol, transaction_type, quantity, price, transaction_date, notes, created_at
    FROM transactions
    WHERE portfolio_id = @PortfolioId
    ORDER BY transaction_date DESC, created_at DESC";

    var transactions = await db.QueryAsync(query, new { PortfolioId = portfolioId });
    return Results.Ok(new
    {
        portfolio_id = portfolioId,
        count = transactions.Count(),
        transactions
    });
})
.WithName("GetPortfolioTransactions")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);


app.MapGet("/api/portfolios/{portfolioId:int}/valuation", async (
    int portfolioId,
    string? date,
    IDbConnection db) =>
{

    // Validate portfolio existence
    var portfolioExists = await db.ExecuteScalarAsync<bool>(
        "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
        new { Id = portfolioId });

    if (!portfolioExists)
    {
        return Results.NotFound(new { error = $"Portfolio with ID {portfolioId} not found." });
    }

    // Default valuation date: today
    var valuationDate = string.IsNullOrWhiteSpace(date) ?
        DateTime.UtcNow.Date.ToString("yyyy-MM-dd")
        : date;

    // Validate date format
    if (!DateTime.TryParse(valuationDate, out DateTime parsedDate))
    {
        return Results.BadRequest(new { error = "Invalid date format. Use YYYY-MM-DD." });
    }

    // Calculate holdings as of the valuation date
    var holdingsQuery = @"
        SELECT
            symbol,
            SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) as total_quantity
        FROM transactions
        WHERE portfolio_id = @PortfolioId
        AND transaction_date <= @ValuationDate
        GROUP BY symbol
        HAVING SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) > 0
    ";

    var holdings = await db.QueryAsync(holdingsQuery, new
    {
        PortfolioId = portfolioId,
        ValuationDate = parsedDate
    });

    if (!holdings.Any())
    {
        return Results.Ok(new
        {
            portfolio_id = portfolioId,
            valuation_date = parsedDate.Date.ToString("yyyy-MM-dd"),
            total_value = 0.00m,
            message = "No holdings in the portfolio as of the specified date.",
            holdings = new List<object>()
        });
    }

    // Get prices for each holding on valuation date
    var valuationDetails = new List<object>();
    decimal totalValue = 0;

    foreach (var holding in holdings)
    {
        string symbol = (string)holding.symbol;
        decimal quantity = (decimal)holding.total_quantity;

        var priceQuery = @"
            SELECT close_price
            FROM etf_prices
            WHERE symbol = @Symbol
            AND price_date <= @ValuationDate
            ORDER BY price_date DESC
            LIMIT 1
        ";

        var priceRecord = await db.ExecuteScalarAsync<decimal?>(priceQuery, new
        {
            Symbol = symbol,
            ValuationDate = parsedDate
        });

        if (priceRecord == null)
        {
            valuationDetails.Add(new
            {
                symbol,
                quantity,
                price = (decimal?)null,
                value = (decimal?)null,
                note = "No price data available on or before valuation date."
            });
            continue;
        }

        var value = quantity * priceRecord.Value;
        totalValue += value;

        valuationDetails.Add(new
        {
            symbol,
            quantity,
            price = Math.Round(priceRecord.Value, 2),
            value = Math.Round(value, 2)
        });
    }

    return Results.Ok(new
    {
        portfolio_id = portfolioId,
        valuation_date = parsedDate.Date.ToString("yyyy-MM-dd"),
        total_value = Math.Round(totalValue, 2),
        details = valuationDetails
    });
})
.WithName("GetPortfolioValuation")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);


app.Run();

record PortfolioCreateRequest
(
    string Name,
    string? Description,
    string? BaseCurrency
);

record TransactionCreateRequest
(
    string Symbol,
    string TransactionType,
    decimal Quantity,
    decimal Price,
    DateTime TransactionDate,
    string? Notes
);
