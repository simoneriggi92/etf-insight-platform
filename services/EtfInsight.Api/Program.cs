using System.Data;
using Npgsql;

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


app.MapGet("/etfs/{ticker}/prices", async(
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

        if(from.HasValue)
        {
            sql += " and price_date >= @fromDate ";
        }
        if(to.HasValue)
        {
            sql += " and price_date <= @toDate ";
        }

        sql += " order by price_date desc ";

        if(limit.HasValue && limit.Value > 0)
        {
            sql += " limit @limit ";
        }

        var cmd = new NpgsqlCommand(sql, (NpgsqlConnection)conn);
        cmd.Parameters.AddWithValue("etfId", etfId);
        if(from.HasValue) cmd.Parameters.AddWithValue("fromDate", from.Value);
        if(to.HasValue) cmd.Parameters.AddWithValue("toDate", to.Value);
        if(limit.HasValue && limit.Value > 0) cmd.Parameters.AddWithValue("limit", limit.Value);

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