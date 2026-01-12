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

    // Validate transaction date
    if (request.TransactionDate > DateTime.UtcNow)
    {
        return Results.BadRequest(new
        {
            error = "Transaction date cannot be in the future."
        });
    }

    // Validate transaction date not too old (reasonable limit, e.g., 30 years)∏
    if (request.TransactionDate < new DateTime(DateTime.UtcNow.Year - 30, 1, 1))
    {
        return Results.BadRequest(new
        {
            error = "Transaction date must be within the last 30 years."
        });
    }

    // Validate quantity reasonably
    if (request.Quantity > 1_000_000)
    {
        return Results.BadRequest(new
        {
            error = "Quantity too large. Maximum allowed is 1,000,000 per transaction."
        });
    }

    // Validate price reasonably
    if (request.Price > 100_000)
    {
        return Results.BadRequest(new
        {
            error = "Price too high. Maximum allowed price $100,000 per share."
        });
    }

    // Validate price not suspiciously low
    if (request.Price < 0.01m)
    {
        return Results.BadRequest(new
        {
            error = "Price too low. Minimum allowed price is $0.01 per share."
        });
    }

    // Validate symbol existence
    if (!symbolExists)
    {
        return Results.BadRequest(new
        {
            error = $"Symbol {request.Symbol} is not found in price database."
        });
    }

    // Validate transaction data 
    if (string.IsNullOrWhiteSpace(request.Symbol))
    {
        return Results.BadRequest(new
        {
            error = "Symbol is required."
        });
    }

    if (!new[] { "BUY", "SELL" }.Contains(request.TransactionType.ToUpper()))
    {
        return Results.BadRequest(new
        {
            error = "TransactionType must be either 'BUY' or 'SELL'."
        });
    }

    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Quantity must be positive."
        });
    }

    if (request.Price <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Price must be positive."
        });
    }

    // Validate: can't over sell
    if (request.TransactionType.ToUpper() == "SELL")
    {
        var parsedTransactionDate = DateTime.TryParse(request.TransactionDate.ToString(), out DateTime transactionDate)
            ? transactionDate
            : DateTime.UtcNow;

        // Get current holdings for the symbol
        var currentHoldings = await db.ExecuteScalarAsync<decimal>(
            @"
            SELECT 
                COALESCE(SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END), 0)
            FROM transactions
            WHERE portfolio_id = @PortfolioId
                AND symbol = @Symbol
                AND transaction_date <= @TransactionDate",
            new
            {
                PortfolioId = portfolioId,
                Symbol = request.Symbol.ToUpper(),
                TransactionDate = parsedTransactionDate
            });

        if (request.Quantity > currentHoldings)
        {
            return Results.BadRequest(new
            {
                error = $"Cannot sell {request.Quantity} shares of {request.Symbol}. Only {Math.Round(currentHoldings, 2)} shares available on {parsedTransactionDate:yyyy-MM-dd}."
            });
        }
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

// Get portfolio valuation history over date range 
app.MapGet("/api/portfolios/{portfolioId:int}/valuation/history", async (
    int portfolioId,
    string? from,
    string? to,
    IDbConnection db) =>
{

    var portfolioExists = await db.ExecuteScalarAsync<bool>(
          "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
          new { Id = portfolioId });

    if (!portfolioExists)
    {
        return Results.NotFound(new { error = $"Portfolio with ID {portfolioId} not found." });
    }

    // Default date range: last 30 days
    var toDate = string.IsNullOrWhiteSpace(to) ?
        DateTime.UtcNow.Date
        : DateTime.Parse(to).Date;

    var fromDate = string.IsNullOrWhiteSpace(from) ?
        DateTime.UtcNow.AddDays(-30).Date
        : DateTime.Parse(from).Date;

    // Get all trading days in the date range
    var tradingDaysQuery = @"
        SELECT DISTINCT price_date::timestamp as price_date
        FROM etf_prices
        WHERE price_date >= @FromDate AND price_date <= @ToDate
        ORDER BY price_date ASC
    ";

    var tradingDays = await db.QueryAsync<DateTime>(tradingDaysQuery, new
    {
        FromDate = fromDate,
        ToDate = toDate
    });

    var valuationHistory = new List<object>();

    foreach (var day in tradingDays)
    {
        var dateStr = day.ToString("yyyy-MM-dd");

        // Calculate holdings as of the day
        var holdingsQuery = @"
        SELECT
            symbol,
            SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) as total_quantity
        FROM transactions
        WHERE portfolio_id = @PortfolioId
        AND transaction_date <= @Date
        GROUP BY symbol
        HAVING SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) > 0
    ";

        var holdings = await db.QueryAsync(holdingsQuery, new
        {
            PortfolioId = portfolioId,
            Date = day
        });

        if (!holdings.Any())
        {
            valuationHistory.Add(new
            {
                date = dateStr,
                total_value = 0.00m
            });
            continue;
        }


        decimal totalValue = 0;

        foreach (var holding in holdings)
        {
            string symbol = (string)holding.symbol;
            decimal quantity = (decimal)holding.total_quantity;

            var priceQuery = @"
            SELECT close_price
            FROM etf_prices
            WHERE symbol = @Symbol
            AND price_date <= @Date
            ORDER BY price_date DESC
            LIMIT 1
        ";

            var priceRecord = await db.ExecuteScalarAsync<decimal?>(priceQuery, new
            {
                Symbol = symbol,
                Date = day
            });

            if (priceRecord != null)
            {
                totalValue += quantity * priceRecord.Value;
            }
        }

        valuationHistory.Add(new
        {
            date = dateStr,
            total_value = Math.Round(totalValue, 2)
        });
    }
    return Results.Ok(new
    {
        portfolio_id = portfolioId,
        date_range = new { from = fromDate.ToString("yyyy-MM-dd"), to = toDate.ToString("yyyy-MM-dd") },
        data = valuationHistory.Count,
        history = valuationHistory
    });
})
.WithName("GetPortfolioValuationHistory")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/portfolios/{portfolioId:int}/performance", async (
    int portfolioId,
    string? from,
    string? to,
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

    // Default range: inception to today
    var toDate = string.IsNullOrWhiteSpace(to) ?
        DateTime.UtcNow.ToString("yyyy-MM-dd")
        : to;

    // Get first transaction date as default start
    var firstTransactionDateResult = await db.ExecuteScalarAsync(
        "SELECT MIN(transaction_date) FROM transactions WHERE portfolio_id = @Id",
        new { Id = portfolioId });

    if (firstTransactionDateResult == null)
    {
        return Results.Ok(new
        {
            portfolio_id = portfolioId,
            message = "No transactions found in the portfolio to calculate performance.",
            metrics = new { }
        });
    }

    var firstTransactionDate = firstTransactionDateResult is DateOnly dateOnly
        ? dateOnly.ToString("yyyy-MM-dd")
        : firstTransactionDateResult.ToString() ?? string.Empty;

    var fromDateStr = string.IsNullOrWhiteSpace(from) ?
        firstTransactionDate
        : from;

    var fromDate = DateTime.Parse(fromDateStr);
    var toDateParsed = DateTime.Parse(toDate);

    // Calculate cost basis (total invested)
    var costBasisQuery = @"
        SELECT 
            SUM(CASE WHEN transaction_type = 'BUY' THEN quantity * price ELSE 0 END) as total_bought,
            SUM(CASE WHEN transaction_type = 'SELL' THEN quantity * price ELSE 0 END) as total_sold
        FROM transactions
        WHERE portfolio_id = @PortfolioId
        AND transaction_date >= @FromDate
        AND transaction_date <= @ToDate";

    var costBasis = await db.QuerySingleAsync(costBasisQuery, new
    {
        PortfolioId = portfolioId,
        FromDate = fromDate,
        ToDate = toDateParsed
    });

    decimal totalInvested = costBasis.total_bought ?? 0;
    decimal totalProceeds = costBasis.total_sold ?? 0;
    decimal netInvested = totalInvested - totalProceeds;

    // Get valuation history for the period
    var valuationHistoryQuery = @"
        WITH trading_days AS (
            SELECT DISTINCT price_date as date
            FROM etf_prices
            WHERE price_date >= @FromDate
              AND price_date <= @ToDate
            ORDER BY price_date
        ),
        daily_holdings AS (
            SELECT 
                td.date,
                t.symbol,
                SUM(CASE WHEN t.transaction_type = 'BUY' THEN t.quantity ELSE -t.quantity END) as quantity
            FROM trading_days td
            CROSS JOIN (SELECT DISTINCT symbol FROM transactions WHERE portfolio_id = @PortfolioId) symbols
            LEFT JOIN transactions t ON t.symbol = symbols.symbol
                AND t.portfolio_id = @PortfolioId
                AND t.transaction_date <= td.date
            WHERE t.symbol IS NOT NULL
            GROUP BY td.date, t.symbol
            HAVING SUM(CASE WHEN t.transaction_type = 'BUY' THEN t.quantity ELSE -t.quantity END) > 0
        )
        SELECT 
            dh.date,
            SUM(dh.quantity * p.close_price) as total_value
        FROM daily_holdings dh
        JOIN etf_prices p ON p.symbol = dh.symbol AND p.price_date = dh.date
        GROUP BY dh.date
        ORDER BY dh.date";

    var valuationHistory = await db.QueryAsync(valuationHistoryQuery, new
    {
        PortfolioId = portfolioId,
        FromDate = fromDate,
        ToDate = toDateParsed
    });

    if (!valuationHistory.Any())
    {
        return Results.Ok(new
        {
            portfolio_id = portfolioId,
            date_range = new { from = fromDate.ToString("yyyy-MM-dd"), to = toDateParsed.ToString("yyyy-MM-dd") },
            message = "No valuation data available for the specified period.",
            metrics = new { }
        });
    }

    // Calculate metrics
    var startValue = valuationHistory.First().total_value;
    var endValue = valuationHistory.Last().total_value;

    // Total P&L = current value - net invested
    var totalPnL = endValue - netInvested;

    // Total Return % = (end value - net invested) / net invested * 100
    decimal totalReturn = netInvested != 0 ? (totalPnL / netInvested) * 100 : 0m;

    // Find best and worst days
    var dailyChanges = new List<dynamic>();
    for (int i = 1; i < valuationHistory.Count(); i++)
    {
        var prevValue = (decimal)valuationHistory.ElementAt(i - 1).total_value;
        var currValue = (decimal)valuationHistory.ElementAt(i).total_value;
        var change = currValue - prevValue;
        var changePercent = prevValue != 0 ? (change / prevValue) * 100 : 0;

        dailyChanges.Add(new
        {

            date = valuationHistory.ElementAt(i).date.ToString("yyyy-MM-dd"),
            value = currValue,
            daily_change = Math.Round(change, 2),
            daily_change_percent = changePercent
        });
    }

    var bestDay = dailyChanges.OrderByDescending(d => d.daily_change_percent).FirstOrDefault();
    var worstDay = dailyChanges.OrderBy(d => d.daily_change_percent).FirstOrDefault();

    // Calculate max drawdown
    decimal maxValue = startValue;
    decimal maxDrawdown = 0;
    DateTime? dradownStartDate = null;
    DateTime? drawdownEndDate = null;

    foreach (var point in valuationHistory)
    {
        var currentValue = point.total_value;
        var currentDate = point.date is DateOnly pointDateOnly ? pointDateOnly.ToDateTime(TimeOnly.MinValue) : (DateTime)point.date;

        if (currentValue > maxValue)
        {
            maxValue = currentValue;
        }

        var drawdown = ((maxValue - currentValue) / maxValue) * 100;

        if (drawdown > maxDrawdown)
        {
            maxDrawdown = drawdown;
            drawdownEndDate = currentDate;

            // Find the start date of the drawdown
            dradownStartDate = valuationHistory
                .Where(v => (v.date is DateOnly vDateOnly1 ? vDateOnly1.ToDateTime(TimeOnly.MinValue) : (DateTime)v.date) <= currentDate && v.total_value == maxValue)
                .Select(v => v.date is DateOnly vDateOnly2 ? vDateOnly2.ToDateTime(TimeOnly.MinValue) : (DateTime)v.date)
                .FirstOrDefault();
        }
    }

    return Results.Ok(new
    {
        portfolio_id = portfolioId,
        date_range = new { from = fromDate.ToString("yyyy-MM-dd"), to = toDateParsed.ToString("yyyy-MM-dd") },
        period_days = valuationHistory.Count(),

        investment_summary = new
        {
            total_invested = Math.Round(totalInvested, 2),
            total_proceeds = Math.Round(totalProceeds, 2),
            net_invested = Math.Round(netInvested, 2),

        },

        valuation = new
        {
            start_value = Math.Round(startValue, 2),
            end_value = Math.Round(endValue, 2),
            start_date = valuationHistory.First().date.ToString("yyyy-MM-dd"),
            end_date = valuationHistory.Last().date.ToString("yyyy-MM-dd")
        },

        performance = new
        {
            total_pnl = Math.Round(totalPnL, 2),
            total_return_percent = Math.Round(totalReturn, 2),

            best_day = bestDay != null ? new
            {
                date = bestDay.date,
                change = Math.Round(bestDay.daily_change, 2),
                change_percent = Math.Round(bestDay.daily_change_percent, 2)
            } : null,
            worst_day = worstDay != null ? new
            {
                date = worstDay.date,
                change = Math.Round(worstDay.daily_change, 2),
                change_percent = Math.Round(worstDay.daily_change_percent, 2)
            } : null,

            max_drawdown = new
            {
                percent = Math.Round(maxDrawdown, 2),
                from_date = dradownStartDate?.ToString("yyyy-MM-dd"),
                to_date = drawdownEndDate?.ToString("yyyy-MM-dd")
            }
        }
    }

    );
})
.WithName("GetPortfolioPerformance")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);


// Get portfolio dashboard summary

app.MapGet("/api/portfolios/{portfolioId:int}/dashboard", async (
    int portfolioId,
    IDbConnection db) =>
{

    var portfolioExists = await db.ExecuteScalarAsync<bool>(
          "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
          new { Id = portfolioId });

    if (!portfolioExists)
    {
        return Results.NotFound(new { error = $"Portfolio with ID {portfolioId} not found." });
    }

    // Get portfolio info
    var portfolioQuery = @"
        SELECT id, name, description, base_currency, created_at
        FROM portfolios
        WHERE id = @Id";

    var portfolio = await db.QuerySingleAsync(portfolioQuery, new { Id = portfolioId });

    // Get current valuation
    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

    var holdingsQuery = @"
        SELECT
            symbol,
            SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) as total_quantity
        FROM transactions
        WHERE portfolio_id = @PortfolioId
        AND transaction_date <= @Today
        GROUP BY symbol
        HAVING SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) > 0";

    var holdings = await db.QueryAsync(holdingsQuery, new
    {
        PortfolioId = portfolioId,
        Today = DateTime.Parse(today)
    });

    var currentValue = 0m;
    var holdingDetails = new List<object>();

    foreach (var holding in holdings)
    {
        string symbol = (string)holding.symbol;
        decimal quantity = (decimal)holding.total_quantity;

        var priceQuery = @"
            SELECT close_price
            FROM etf_prices
            WHERE symbol = @Symbol
                AND price_date <= @Today
            ORDER BY price_date DESC
            LIMIT 1
        ";

        var priceRecord = await db.ExecuteScalarAsync<decimal?>(priceQuery, new
        {
            Symbol = symbol,
            Today = DateTime.Parse(today)
        });

        if (priceRecord != null)
        {
            var value = quantity * priceRecord.Value;
            currentValue += value;

            holdingDetails.Add(new
            {
                symbol,
                quantity = Math.Round(quantity, 2),
                price = Math.Round(priceRecord.Value, 2),
                value = Math.Round(value, 2),
                allocation_percent = 0.00m // Placeholder, will calculate later
            });
        }
    }

    // Calculate allocation percentages
    holdingDetails = holdingDetails.Select(h =>
    {
        dynamic hDyn = h;
        var value = (decimal)hDyn.value;
        return new
        {
            symbol = (string)hDyn.symbol,
            quantity = (decimal)hDyn.quantity,
            price = (decimal)hDyn.price,
            value,
            allocation_percent = currentValue > 0 ? Math.Round((value / currentValue) * 100, 2) : 0
        };
    })
    .OrderByDescending(h => h.value)
    .ToList<object>();

    // Get total invested amount
    var totalInvested = await db.ExecuteScalarAsync<decimal>(
        @"
        SELECT 
            SUM(CASE WHEN transaction_type = 'BUY' THEN quantity * price ELSE 0 END) -
            SUM(CASE WHEN transaction_type = 'SELL' THEN quantity * price ELSE 0 END)
        FROM transactions
        WHERE portfolio_id = @PortfolioId",
        new { PortfolioId = portfolioId });

    var totalPnL = currentValue - totalInvested;
    var totalReturnPercent = totalInvested != 0 ? (totalPnL / totalInvested) * 100 : 0m;

    return Results.Ok(new
    {
        portfolio,
        summary = new
        {
            current_value = Math.Round(currentValue, 2),
            total_invested = Math.Round(totalInvested, 2),
            total_pnl = Math.Round(totalPnL, 2),
            total_return_percent = Math.Round(totalReturnPercent, 2),
            holdings_count = holdingDetails.Count,
            as_of_date = today
        },
        holdings = holdingDetails
    });

})
.WithName("GetPortfolioDashboard")
.WithTags("Portfolios")
.Produces<object>(StatusCodes.Status200OK)
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
