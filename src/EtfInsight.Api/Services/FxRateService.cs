using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;


namespace EtfInsight.Api.Services
{

    public interface IFxRateService
    {
        Task<decimal?> GetRateAsync(string fromCurrency, string toCurrency, DateTime date);
        Task<decimal> ConvertAmountAsync(decimal amount, string fromCurrency, string toCurrency, DateTime date);
    }

    public class FxRateService : IFxRateService
    {
        private readonly IDbConnection _db;

        public FxRateService(IDbConnection db)
        {
            _db = db;
        }

        /// <summary>
        /// Converts an amount from one currency to another on a specific date.
        /// </summary>
        /// <param name="fromCurrency">(e.g. USD)</param>
        /// <param name="toCurrency">(e.g. EUR)</param>
        /// <param name="date">Date for lookup</param>
        /// <returns>Exchange rate or null if not available</returns>
        public async Task<decimal?> GetRateAsync(string fromCurrency, string toCurrency, DateTime date)
        {

            if (fromCurrency.Equals(toCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0m; // No conversion needed
            }

            var dateStr = date.ToString("yyyy-MM-dd");

            // Try to get direct rate
            var directRateQuery = await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT Rate 
                FROM fx_rates 
                WHERE from_currency = @FromCurrency 
                AND to_currency = @ToCurrency 
                AND rate_date = @Date
                LIMIT 1",
                new { FromCurrency = fromCurrency, ToCurrency = toCurrency, Date = date });

            if (directRateQuery.HasValue)
            {
                return directRateQuery.Value;
            }


            // Carry-forward logic: get the latest available rate before the specified date
            var carryForwardRate = await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT Rate
                    FROM fx_rates 
                    WHERE from_currency = @FromCurrency 
                        AND to_currency = @ToCurrency 
                        AND rate_date <= @Date
                    ORDER BY rate_date DESC
                    LIMIT 1",
                new { FromCurrency = fromCurrency, ToCurrency = toCurrency, Date = date });

            if (carryForwardRate.HasValue)
            {
                return carryForwardRate.Value;
            }

            // Try cross-rate via EUR (most common base currency)
            if (!fromCurrency.Equals("EUR", StringComparison.OrdinalIgnoreCase) && !toCurrency.Equals("EUR", StringComparison.OrdinalIgnoreCase))
            {
                var fromToEur = await GetRateAsync(fromCurrency, "EUR", date);
                var eurToTarget = await GetRateAsync("EUR", toCurrency, date);

                if (fromToEur.HasValue && eurToTarget.HasValue)
                    return fromToEur.Value * eurToTarget.Value;
            }

            // Try cross-rate via USD (alternative pivot)
            if (!fromCurrency.Equals("USD", StringComparison.OrdinalIgnoreCase) && !toCurrency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            {
                var fromToUsd = await GetRateAsync(fromCurrency, "USD", date);
                var usdToTarget = await GetRateAsync("USD", toCurrency, date);

                if (fromToUsd.HasValue && usdToTarget.HasValue)
                    return fromToUsd.Value * usdToTarget.Value;
            }

            return null; // Rate not found
        }

        /// <summary>
        /// Converts an amount from one currency to another on a specific date.
        /// </summary>
        /// <param name="amount">Amount to convert</param>
        /// <param name="fromCurrency">Source currency</param>
        /// <param name="toCurrency"> Target currency</param>
        /// <param name="date">Date for lookup</param>
        /// <returns>Converted amount</returns>
        /// <exception cref="InvalidOperationException">If rate not available</exception>
        public async Task<decimal> ConvertAmountAsync(decimal amount, string fromCurrency, string toCurrency, DateTime date)
        {
            if (amount == 0)
            {
                return 0;
            }

            var rate = await GetRateAsync(fromCurrency, toCurrency, date);
            if (!rate.HasValue)
            {
                throw new InvalidOperationException($"FX rate not available for {fromCurrency}/{toCurrency} on {date:yyyy-MM-dd}");
            }

            return amount * rate.Value;
        }
    }
}