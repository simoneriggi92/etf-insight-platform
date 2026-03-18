using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;
using System.Globalization;
using Dapper;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;
using System.Data;
using EtfInsight.Core.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Dapper;

namespace EtfInsight.Infrastructure.Services
{
    // ── CSV row model ──────────────────────────────────────────────────────────────
    public record TransactionCsvRow
    {
        [Name("ticker")] public string Ticker { get; init; } = "";
        [Name("transaction_date")] public string TransactionDate { get; init; } = "";
        [Name("type")] public string Type { get; init; } = "";
        [Name("units")] public decimal Units { get; init; }
        [Name("price_per_unit")] public decimal PricePerUnit { get; init; }
        [Name("fees")] public decimal Fees { get; init; }
    }

    public class CsvImportService : Core.Interfaces.ICsvImportService
    {
        private readonly IDbConnection _db;
        private readonly IPortfolioRepository _portfolioRepo;
        private readonly IIngestionService _ingestionService;
        private readonly Microsoft.Extensions.Logging.ILogger<CsvImportService> _logger;

        public CsvImportService(
            IDbConnection db,
            IPortfolioRepository portfolioRepo,
            IIngestionService ingestionService,
            Microsoft.Extensions.Logging.ILogger<CsvImportService> logger)
        {
            _db = db;
            _portfolioRepo = portfolioRepo;
            _ingestionService = ingestionService;
            _logger = logger;
        }

        public async Task<CsvImportResult> ImportAsync(Guid portfolioId, StreamReader reader, Guid userId = default, CancellationToken cancellationToken = default)
        {
            var validRows = new List<Transaction>();
            var invalidRows = new List<object>();
            var validTypes = TransactionType.GetNames(typeof(TransactionType)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null, // Ignore missing fields
            });

            var rowNumber = 1; // Start at 1 to account for header
            await foreach (var raw in csv.GetRecordsAsync<TransactionCsvRow>().WithCancellation(cancellationToken))
            {
                rowNumber++;
                var errors = new List<string>();

                var ticker = raw.Ticker.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(ticker))
                    errors.Add("ticker is required");

                if (!DateOnly.TryParse(raw.TransactionDate, out var txDate))
                    errors.Add($"transaction_date '{raw.TransactionDate}' is not a valid date (YYYY-MM-DD)");

                var typeUpper = raw.Type.Trim().ToUpperInvariant();
                if (!validTypes.Contains(typeUpper))
                    errors.Add($"type '{raw.Type}' must be one of {string.Join(", ", validTypes)}");

                if (raw.Units <= 0)
                    errors.Add("units must be > 0");

                if (raw.PricePerUnit < 0)
                    errors.Add("price_per_unit cannot be negative");

                if (errors.Count > 0)
                {
                    invalidRows.Add(new { row = rowNumber, errors });
                    continue;
                }

                validRows.Add(new Transaction
                {
                    Ticker = ticker,
                    TransactionDate = txDate,
                    Type = Enum.Parse<TransactionType>(typeUpper),
                    Units = raw.Units,
                    PricePerUnit = raw.PricePerUnit,
                    Fees = raw.Fees,
                });
            }

            if (validRows.Count == 0)
                return new CsvImportResult { Message = "No valid rows found.", InvalidRows = invalidRows };

            // JIT: ensure all distinct tickers have (or are getting) price data.
            // Run sequentially — IDbConnection does not support concurrent commands.
            var distinctTickers = validRows.Select(r => r.Ticker).Distinct().ToList();
            var ingestionResults = new List<IngestionStatus>();
            foreach (var ticker in distinctTickers)
                ingestionResults.Add(await _ingestionService.EnsureTickerReadyAsync(ticker, cancellationToken));

            var tickerStatuses = distinctTickers
                .Zip(ingestionResults, (t, s) => new { ticker = t, status = s.ToString().ToLower() })
                .ToList();

            // bulk insert valid transactions
            await _portfolioRepo.BulkAddTransactionsAsync(portfolioId, validRows);

            var anyIngesting = ingestionResults.Any(r => r == IngestionStatus.Ingesting);

            return new CsvImportResult
            {
                Imported = validRows.Count,
                InvalidRows = invalidRows,
                Tickers = tickerStatuses.Cast<object>().ToList(),
                AnyIngesting = anyIngesting,
                Message = anyIngesting
                ? "Transactions saved. Price history is being fetched for some tickers — analytics will update automatically."
                : "All transactions imported and price data is available.",
            };
        }
    }
}