using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Core.Services
{
    public class TwrrCalculator : IPerformanceCalculator
    {
        private readonly IPortfolioRepository? _portfolioRepo;
        private readonly IEtfPriceRepository? _priceRepo;

        /// <summary>Parameterless constructor for unit-testing with pre-loaded data.</summary>
        public TwrrCalculator() { }

        /// <summary>Production constructor — resolves data from repositories.</summary>
        public TwrrCalculator(
            IPortfolioRepository portfolioRepo,
            IEtfPriceRepository priceRepo)
        {
            _portfolioRepo = portfolioRepo;
            _priceRepo = priceRepo;
        }

        // ── IPerformanceCalculator (repo-based) ────────────────────────────────
        public async Task<decimal> CalculateTWRR(Guid portfolioId, DateOnly from, DateOnly to)
        {
            if (_portfolioRepo is null || _priceRepo is null)
                throw new InvalidOperationException(
                    "Repository constructor must be used when calling the async overload.");

            var portfolio = await _portfolioRepo.GetPortfolioWithTransactionsAsync(portfolioId);
            if (portfolio == null || !portfolio.Transactions.Any())
                return 0m;

            var allTransactions = portfolio.Transactions
                .Where(t => t.TransactionDate <= to)
                .OrderBy(t => t.TransactionDate)
                .ToList();

            if (!allTransactions.Any())
                return 0m;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var effectiveTo = to > today ? today : to;

            var tickers = allTransactions.Select(t => t.Ticker).Distinct().ToList();
            var prices = await _priceRepo.GetPricesByTickersAsync(tickers, from, effectiveTo);

            return CalculateTWRR(allTransactions, prices);
        }

        // ── Pure overload — accepts pre-loaded data (used by unit tests) ───────
        public decimal CalculateTWRR(
            IEnumerable<Transaction> transactions,
            IEnumerable<EtfPrice> prices)
        {
            var transactionList = (transactions ?? Enumerable.Empty<Transaction>())
                .OrderBy(t => t.TransactionDate)
                .ToList();

            var priceList = (prices ?? Enumerable.Empty<EtfPrice>()).ToList();

            if (!transactionList.Any())
                return 0m;

            var minDate = transactionList.First().TransactionDate;
            var maxDate = priceList.Any()
                ? priceList.Max(p => p.PriceDate)
                : minDate;

            // Build price lookup [Ticker][Date] => ClosePrice
            var priceLookup = priceList
                .GroupBy(p => p.Ticker)
                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.PriceDate, p => p.ClosePrice));

            // Group transactions by date
            var transactionsByDate = transactionList
                .GroupBy(t => t.TransactionDate)
                .ToDictionary(g => g.Key, g => g.ToList());

            var holdings = new Dictionary<string, decimal>();
            var lastKnownPrices = new Dictionary<string, decimal>();
            decimal totalReturn = 0m;
            decimal previousDayValue = 0m;
            var currentDate = minDate;

            while (currentDate <= maxDate)
            {
                decimal valueStart = previousDayValue;
                decimal cashFlow = 0m;

                if (transactionsByDate.TryGetValue(currentDate, out var dayTx))
                {
                    foreach (var tx in dayTx)
                    {
                        holdings.TryAdd(tx.Ticker, 0m);

                        switch (tx.Type)
                        {
                            case TransactionType.BUY:
                                holdings[tx.Ticker] += tx.Units;
                                cashFlow += tx.Units * tx.PricePerUnit + tx.Fees;
                                break;
                            case TransactionType.SELL:
                                holdings[tx.Ticker] -= tx.Units;
                                cashFlow -= tx.Units * tx.PricePerUnit + tx.Fees;
                                break;
                            case TransactionType.DEPOSIT:
                                cashFlow += tx.PricePerUnit;
                                break;
                            case TransactionType.WITHDRAW:
                                cashFlow -= tx.PricePerUnit;
                                break;
                        }
                    }
                }

                decimal valueEnd = 0m;
                foreach (var (ticker, units) in holdings)
                {
                    if (units == 0m) continue;
                    valueEnd += units * GetPriceForDate(ticker, currentDate, priceLookup, lastKnownPrices);
                }

                if (valueStart > 0)
                {
                    decimal subPeriodReturn = (valueEnd - cashFlow) / valueStart - 1m;
                    totalReturn = (1m + totalReturn) * (1m + subPeriodReturn) - 1m;
                }

                previousDayValue = valueEnd;
                currentDate = currentDate.AddDays(1);
            }

            return totalReturn;
        }

        public decimal GetPriceForDate(
            string ticker,
            DateOnly date,
            Dictionary<string, Dictionary<DateOnly, decimal>> priceLookup,
            Dictionary<string, decimal> lastKnownPrices)
        {
            if (priceLookup.ContainsKey(ticker) && priceLookup[ticker].ContainsKey(date))
            {
                var price = priceLookup[ticker][date];
                lastKnownPrices[ticker] = price; // Update last known price
                return price;
            }

            // Fallback: check previous dates for last known price
            if (lastKnownPrices.ContainsKey(ticker))
            {
                return lastKnownPrices[ticker];
            }

            // If no price for the date, return 0
            return 0m;
        }
    }
}