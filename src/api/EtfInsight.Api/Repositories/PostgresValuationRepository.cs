using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.Models;

namespace EtfInsight.Api.Repositories
{
    public class PostgresValuationRepository : IValuationRepository
    {
        private readonly IDbConnection _db;

        public PostgresValuationRepository(IDbConnection db)
            => _db = db;

        public async Task<IReadOnlyList<DateOnly>> GetTradingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT DISTINCT price_date
                FROM etf_prices
                WHERE price_date >= @FromDate AND price_date <= @ToDate
                ORDER BY price_date ASC
            ";

            var parameters = new
            {
                FromDate = from.ToDateTime(TimeOnly.MinValue),
                ToDate = to.ToDateTime(TimeOnly.MinValue)
            };

            var rows = await _db.QueryAsync(sql, parameters);
            var result = new List<DateOnly>();

            foreach (var row in rows)
            {
                result.Add(ToDateOnly(row.price_date, "price_date"));
            }

            return result;
        }

        public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int portfolioId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT symbol, transaction_type, quantity, price, transaction_date, COALESCE(transaction_currency, 'USD') as currency
                FROM transactions
                WHERE portfolio_id = @PortfolioId
                AND transaction_date >= @From
                AND transaction_date <= @To
                ORDER BY transaction_date ASC, created_at ASC;
            ";

            var rows = await _db.QueryAsync(sql, new
            {
                PortfolioId = portfolioId,
                From = from.ToDateTime(TimeOnly.MinValue),
                To = to.ToDateTime(TimeOnly.MinValue)
            });

            var list = new List<Transaction>();

            foreach (var r in rows)
            {
                string symbol = ((string)r.symbol).ToUpperInvariant();
                string type = ((string)r.transaction_type).ToUpperInvariant();

                var txType = type switch
                {
                    "BUY" => TransactionType.Buy,
                    "SELL" => TransactionType.Sell,
                    _ => throw new InvalidOperationException($"Unknown transaction_type: {type}")
                };

                // Nota: Transaction nel Core oggi non include portfolioId (nel tuo modello attuale). Ok.
                list.Add(new Transaction(
                    Symbol: symbol,
                    Type: txType,
                    Quantity: (decimal)r.quantity,
                    Price: (decimal)r.price,
                    Date: ToDateOnly(r.transaction_date, "transaction_date"),
                    Currency: (string)r.currency
                ));
            }
            return list;
        }

        public async Task<IReadOnlyList<DailyPrice>> GetPricesAsync(IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            var symbolList = symbols
                .Select(s => s.ToUpperInvariant())
                .Distinct()
                .ToList();

            if (symbolList.Count == 0)
            {
                return Array.Empty<DailyPrice>();
            }

            const string sql = @"
                SELECT symbol, price_date, close_price, COALESCE(currency, 'USD') as currency
                FROM etf_prices
                WHERE symbol = ANY(@Symbols)
                AND price_date >= @FromDate
                AND price_date <= @ToDate
                ORDER BY symbol, price_date;
            ";

            var rows = await _db.QueryAsync(sql, new
            {
                Symbols = symbolList,
                FromDate = from.ToDateTime(TimeOnly.MinValue),
                ToDate = to.ToDateTime(TimeOnly.MinValue)
            });

            var list = new List<DailyPrice>();
            foreach (var r in rows)
            {
                list.Add(new DailyPrice(
                    Symbol: ((string)r.symbol).ToUpperInvariant(),
                    Date: ToDateOnly(r.price_date, "price_date"),
                    Close: (decimal)r.close_price,
                    Currency: (string)r.currency
                ));
            }
            return list;
        }

        private static DateOnly ToDateOnly(object value, string fieldName)
        {
            return value switch
            {
                DateOnly d => d,
                DateTime dt => DateOnly.FromDateTime(dt),
                DateTimeOffset dto => DateOnly.FromDateTime(dto.DateTime),
                null => throw new InvalidOperationException($"Null value encountered for {fieldName}"),
                _ => throw new InvalidOperationException($"Unexpected type {value.GetType().FullName} for {fieldName}")
            };
        }
    }
}
