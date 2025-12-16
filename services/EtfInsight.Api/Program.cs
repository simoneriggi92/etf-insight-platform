using System.Data;
using System.Globalization;
using Npgsql;
using EtfInsight.Api;

var builder = WebApplication.CreateBuilder(args);

// Config 
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Postgres connection string is not configured.");

builder.Services.AddSingleton<IDbConnectionFactory>(_ =>
    new NpgsqlConnectionFactory(connectionString));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/etfs", async (IDbConnectionFactory dbConnectionFactory) =>
{
    await using var conn = dbConnectionFactory.CreateConnection();
    await conn.OpenAsync();


    var cmd = new NpgsqlCommand("select id, ticker, name, currency, provider from etf order by ticker", (NpgsqlConnection)conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    var results = new List<object>();
    while (await reader.ReadAsync())
    {
        results.Add(new
        {
            Id = reader.GetInt32(0),
            Ticker = reader.GetString(1),
            Name = reader.GetString(2),
            Currency = reader.GetString(3),
            Provider = reader.IsDBNull(4) ? null : reader.GetString(4)
        });
    }

    return Results.Ok(results);
});


app.MapGet("/etfs/{ticker}/prices", async (
    string ticker,
    int? limit,
    DateTime? from,
    DateTime? to,
    IDbConnectionFactory dbConnectionFactory) =>
{
    await using var conn = dbConnectionFactory.CreateConnection();
    await conn.OpenAsync();

    // Resolve ETF id
    var etfIdCmd = new NpgsqlCommand("select id from etf where ticker = @ticker", (NpgsqlConnection)conn);
    etfIdCmd.Parameters.AddWithValue("ticker", ticker);
    var etfIdObj = await etfIdCmd.ExecuteScalarAsync();
    if (etfIdObj == null)
    {
        return Results.NotFound(new { message = $"ETF with ticker '{ticker}' not found." });
    }

    var etfId = (int)etfIdObj;

    // Build query with optional filters
    var sql = @"
        select price_date, open_price, high_price, low_price, close_price, volume
        from etf_price_history
        where etf_id = @etfId
        ";

    if (from.HasValue)
    {
        sql += " and price_date >= @fromDate ";
    }
    if (to.HasValue)
    {
        sql += " and price_date <= @toDate ";
    }

    sql += " order by price_date desc ";

    if (limit.HasValue && limit.Value > 0)
    {
        sql += " limit @limit ";
    }

    var cmd = new NpgsqlCommand(sql, (NpgsqlConnection)conn);
    cmd.Parameters.AddWithValue("etfId", etfId);
    if (from.HasValue) cmd.Parameters.AddWithValue("fromDate", from.Value);
    if (to.HasValue) cmd.Parameters.AddWithValue("toDate", to.Value);
    if (limit.HasValue && limit.Value > 0) cmd.Parameters.AddWithValue("limit", limit.Value);

    await using var reader = await cmd.ExecuteReaderAsync();

    var prices = new List<object>();
    while (await reader.ReadAsync())
    {
        prices.Add(new
        {
            PriceDate = reader.GetDateTime(0),
            OpenPrice = reader.GetDecimal(1),
            HighPrice = reader.GetDecimal(2),
            LowPrice = reader.GetDecimal(3),
            ClosePrice = reader.GetDecimal(4),
            Volume = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5)
        });
    }
    return Results.Ok(prices);
});

app.MapGet("/portfolios/{id:int}/valuation", async (
    int id,
    DateTime? date,
    IDbConnectionFactory dbConnectionFactory) =>
{
    var asOfDate = date?.Date ?? DateTime.UtcNow.Date;

    await using var conn = dbConnectionFactory.CreateConnection();
    await conn.OpenAsync();

    // 1. Check portfolio exists
    var portfolioCmd = new NpgsqlCommand(
        "select id, name from portfolio where id = @id",
        (NpgsqlConnection)conn);

    portfolioCmd.Parameters.AddWithValue("id", id);

    var portfolioName = string.Empty;
    await using (var portfolioReader = await portfolioCmd.ExecuteReaderAsync())
    {
        if (!await portfolioReader.ReadAsync())
        {
            return Results.NotFound(new { message = $"Portfolio with id '{id}' not found." });
        }
        portfolioName = portfolioReader.GetString(1);
    }

    // 2. Positions (quantity per ETF as of date)

    var positionsSql = @"
        with positions as (
            select
                pt.portfolio_id,
                pt.etf_id,
                sum(case 
                        when pt.trade_type = 'BUY' then pt.quantity
                        when pt.trade_type = 'SELL' then -pt.quantity
                        else 0
                    end) as quantity
            from portfolio_transaction pt
            where pt.portfolio_id = @portfolio_id
            and pt.trade_date <= @as_of
            group by pt.portfolio_id, pt.etf_id
        ),
        prices as (
            select
                eph.etf_id,
                eph.price_date,
                eph.close_price
            from etf_price_history eph
            where eph.price_date = @as_of
        )
        select
            p.portfolio_id,
            e.ticker,
            p.quantity,
            pr.close_price
        from positions p
        join etf e on e.id = p.etf_id
        left join prices pr on pr.etf_id = p.etf_id
        where p.quantity <> 0
        order by e.ticker;
    ";

    var positionsCmd = new NpgsqlCommand(
        positionsSql,
        (NpgsqlConnection)conn);

    positionsCmd.Parameters.AddWithValue("portfolio_id", id);
    positionsCmd.Parameters.AddWithValue("as_of", asOfDate);

    await using var positionsReader = await positionsCmd.ExecuteReaderAsync();

    var positions = new List<object>();
    decimal totalValue = 0;

    while (await positionsReader.ReadAsync())
    {
        var ticker = positionsReader.GetString(1);
        var quantity = positionsReader.GetDecimal(2);
        decimal? closePrice = positionsReader.IsDBNull(3) ? null : positionsReader.GetDecimal(3);
        decimal? positionValue = null;

        if (closePrice.HasValue)
        {
            positionValue = quantity * closePrice.Value;
            totalValue += positionValue.Value;
        }

        positions.Add(new
        {
            Ticker = ticker,
            Quantity = quantity,
            ClosePrice = closePrice,
            PositionValue = positionValue
        });
    }

    var response = new
    {
        PortfolioId = id,
        PortfolioName = portfolioName,
        AsOfDate = asOfDate,
        Positions = positions,
        TotalValue = totalValue
    };

    return Results.Ok(response);
});

app.MapGet("/portfolios/{id:int}/valuation/history", async (
    int id,
    DateTime? from,
    DateTime? to,
    IDbConnectionFactory dbConnectionFactory) =>
{

    try
    {
        (string? baseCurrency, List<ValuationPoint> valuations) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(id, from, to, dbConnectionFactory);

        var response = new
        {
            PortfolioId = id,
            BaseCurrency = string.IsNullOrEmpty(baseCurrency) ? null : baseCurrency,
            Points = valuations
        };

        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { Error = ex.Message });
    }
});

app.MapGet("/portfolios/{id:int}/valuation/summary", async (
    int id,
    DateTime? from,
    DateTime? to,
    IDbConnectionFactory dbConnectionFactory) =>
{
    try
    {
        (string? baseCurrency, List<ValuationPoint> points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(id, from, to, dbConnectionFactory);

        var valuationSummary = await ValuationSummaryCalculator.CalculateSummaryAsync(points);

        valuationSummary.PortfolioId = id;
        valuationSummary.BaseCurrency = baseCurrency;

        return Results.Ok(valuationSummary);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { Error = ex.Message });
    }
});


app.Run();

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}

public class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public NpgsqlConnection CreateConnection()
    {
        return new Npgsql.NpgsqlConnection(_connectionString);
    }
}