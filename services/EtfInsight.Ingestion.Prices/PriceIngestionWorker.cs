using System.Globalization;
using Npgsql;

namespace EtfInsight.Ingestion.Prices;

public class PriceIngestionWorker : BackgroundService
{
    private readonly ILogger<PriceIngestionWorker> _logger;
    private readonly IConfiguration _configuration;

    public PriceIngestionWorker(ILogger<PriceIngestionWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Price Ingestion Worker starting with PricesCsvPath: {path}", 
            _configuration["Ingestion:PricesCsvPath"]);

        try
        {
            await IngestPricesAsync(stoppingToken);    
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "An error occurred during price ingestion.");
        }
    }

    private async Task IngestPricesAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string is not configured.");

        var pricesCsvPath = _configuration["Ingestion:PricesCsvPath"];        
        
        if (string.IsNullOrWhiteSpace(pricesCsvPath))
        {
            throw new InvalidOperationException("PricesCsvPath is not configured.");
        }

        _logger.LogInformation("Starting price ingestion from CSV: {path}", pricesCsvPath);

        var lines = await File.ReadAllLinesAsync(pricesCsvPath, cancellationToken);
        if (lines.Length <= 1)
        {
            _logger.LogWarning("No data found in CSV file: {path}", pricesCsvPath);
            return;
        }

        var header = lines[0];
        _logger.LogInformation("CSV Header: {header}", header);

        var records = lines.Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        _logger.LogInformation("Found {count} data rows to ingest.", records.Count);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Preload ETF ticker -> id mapping
        var etfIdByTicker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = new NpgsqlCommand("SELECT id, ticker FROM etf", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32(0);
                var ticker = reader.GetString(1);
                etfIdByTicker[ticker] = id;
            }
        }

        _logger.LogInformation("Loaded {count} ETFs from database.", etfIdByTicker.Count);

        var inserted = 0;
        var skippedUnknownTicker = 0;
        var skippedErrors = 0;

        foreach(var line in records)
        {
            if(cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested. Stopping ingestion.");
                break;
            }
        
            var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if(parts.Length < 7)
            {
                _logger.LogWarning("Invalid CSV line (expected 7 columns): {line}", line);
                skippedErrors++;
                continue;
            }

            var ticker = parts[0];
            if(!etfIdByTicker.TryGetValue(ticker, out var etfId))
            {
                _logger.LogWarning("Unknown ETF ticker: {ticker}. Skipping line: {line}.", ticker, line);
                skippedUnknownTicker++;
                continue;
            }

            if(!DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var priceDate))
            {
                _logger.LogWarning("Invalid date '{Date}' in line: {Line}", parts[1], line);
                skippedErrors++;
                continue;
            }

            if(!TryParseDecimal(parts[2], out var openPrice) ||
               !TryParseDecimal(parts[3], out var highPrice) ||
               !TryParseDecimal(parts[4], out var lowPrice) ||
               !TryParseDecimal(parts[5], out var closePrice))
            {
                _logger.LogWarning("Invalid numeric data in line: {Line}", line);
                skippedErrors++;
                continue;
            }

            long? volume = null;
            if(long.TryParse(parts[6], NumberStyles.Number, CultureInfo.InvariantCulture, out var vol))
            {
                volume = vol;
            }

            const string upsertSql = @"
                INSERT INTO etf_price_history
                    (etf_id, price_date, open_price, high_price, low_price, close_price, volume)
                VALUES
                    (@etf_id, @price_date, @open_price, @high_price, @low_price, @close_price, @volume)
                ON CONFLICT (etf_id, price_date) DO UPDATE 
                SET 
                    open_price = EXCLUDED.open_price,
                    high_price = EXCLUDED.high_price,
                    low_price = EXCLUDED.low_price,
                    close_price = EXCLUDED.close_price,
                    volume = EXCLUDED.volume;
                ";

            await using var upsertCmd = new NpgsqlCommand(upsertSql, conn);
            upsertCmd.Parameters.AddWithValue("etf_id", etfId);
            upsertCmd.Parameters.AddWithValue("price_date", priceDate);
            upsertCmd.Parameters.AddWithValue("open_price", openPrice);
            upsertCmd.Parameters.AddWithValue("high_price", highPrice);
            upsertCmd.Parameters.AddWithValue("low_price", lowPrice);
            upsertCmd.Parameters.AddWithValue("close_price", closePrice);
            upsertCmd.Parameters.AddWithValue("volume", (object?)volume ?? DBNull.Value);

            try
            {
                await upsertCmd.ExecuteNonQueryAsync(cancellationToken);
                inserted++;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error inserting/updating line: {Line}", line);
                skippedErrors++;
            }
        }

         _logger.LogInformation(
            "Ingestion completed. Inserted/updated: {Inserted}, Unknown ticker: {Unknown}, Errors: {Errors}",
            inserted, skippedUnknownTicker, skippedErrors);
    }

    private static bool TryParseDecimal(string input, out decimal result)
    {
        return decimal.TryParse(
            input, 
            NumberStyles.Number, 
            CultureInfo.InvariantCulture, 
            out result);
    }
}


