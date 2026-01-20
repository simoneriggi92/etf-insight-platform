using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Api.Repositories;
using EtfInsight.Core.Models;
using EtfInsight.Core.Valuation;

namespace EtfInsight.Api.Services
{
    public sealed class ValuationService
    {
        private readonly IValuationRepository _repo;

        public ValuationService(IValuationRepository repo)
            => _repo = repo;

        public async Task<IReadOnlyList<ValuationPoint>> GetHistoryAsync(
            int portfolioId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken
        )
        {
            var tradingDays = await _repo.GetTradingDaysAsync(
                from,
                to,
                cancellationToken);

            // If no trading days, return empty
            if (tradingDays.Count == 0)
            {
                return Array.Empty<ValuationPoint>();
            }

            var transactions = await _repo.GetTransactionsAsync(
                portfolioId,
                from,
                to,
                cancellationToken);

            // The set of symbols involved in transactions
            var symbols = transactions
                .Select(t => t.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var prices = await _repo.GetPricesAsync(
                symbols,
                from,
                to,
                cancellationToken);

            return ValuationCalculator.CalculateHistory(
                transactions,
                prices,
                tradingDays);
        }

    }
}