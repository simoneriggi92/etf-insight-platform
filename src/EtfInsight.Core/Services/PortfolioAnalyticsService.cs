using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Core.Services
{
    public class PortfolioAnalyticsService : IPortfolioAnalyticsService
    {
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly IPortfolioRepository _portfolioRepo;
        private readonly IEtfPriceRepository _priceRepo;

        public PortfolioAnalyticsService(IPerformanceCalculator performanceCalculator, IPortfolioRepository portfolioRepo, IEtfPriceRepository priceRepo)
        {
            _performanceCalculator = performanceCalculator;
            _portfolioRepo = portfolioRepo;
            _priceRepo = priceRepo;
        }

        public decimal CalculateTWRR(IEnumerable<Transaction> transactions, IEnumerable<EtfPrice> etfPrices)
        {
            return _performanceCalculator.CalculateTWRR(transactions, etfPrices);
        }

        public async Task<PortfolioDashboardDto> GetPortfolioAnalyticsAsync(Guid portfolioId, DateOnly from, DateOnly to)
        {
            var portfolio = await _portfolioRepo.GetPortfolioWithTransactionsAsync(portfolioId);
            if (portfolio == null || !portfolio.Transactions.Any())
                return new PortfolioDashboardDto { PortfolioId = portfolioId };

            // 1. Get all transactions up to 'to' date
            var allTransactions = portfolio.Transactions
                .Where(t => t.TransactionDate <= to)
                .OrderBy(t => t.TransactionDate)
                .ToList();

            if (!allTransactions.Any())
                return new PortfolioDashboardDto { PortfolioId = portfolioId };

            var minDate = from;
            var maxDate = to;
            var tickers = allTransactions.Select(t => t.Ticker).Distinct().ToList();

            var prices = await _priceRepo.GetPricesByTickersAsync(tickers, minDate, maxDate);

            var priceLookup = prices
                .GroupBy(p => p.Ticker)
                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.PriceDate, p => p.ClosePrice));

            var history = new List<DailyValuationPointDto>();
            var currentHoldings = new Dictionary<string, decimal>();
            var lastKnownPrices = new Dictionary<string, decimal>();
            decimal cumulativeNetFlow = 0m;
            decimal peakValue = 0m;
            decimal globalMaxDrawdown = 0m;

            // 2. Process all transactions BEFORE the 'from' date to build initial state
            var preWindowTxs = allTransactions.Where(t => t.TransactionDate < from);
            foreach (var tx in preWindowTxs)
            {
                if (!currentHoldings.ContainsKey(tx.Ticker))
                    currentHoldings[tx.Ticker] = 0;

                decimal txAmount = tx.Units * tx.PricePerUnit + tx.Fees;

                switch (tx.Type)
                {
                    case TransactionType.BUY:
                        currentHoldings[tx.Ticker] += tx.Units;
                        cumulativeNetFlow += txAmount;
                        break;

                    case TransactionType.SELL:
                        currentHoldings[tx.Ticker] -= tx.Units;
                        cumulativeNetFlow -= (tx.Units * tx.PricePerUnit - tx.Fees);
                        break;

                    case TransactionType.DEPOSIT:
                        cumulativeNetFlow += tx.Units;
                        break;

                    case TransactionType.WITHDRAW:
                        cumulativeNetFlow -= tx.Units;
                        break;
                }
            }

            // 3. Group transactions in the window by date
            var txByDate = allTransactions
                .Where(t => t.TransactionDate >= from)
                .GroupBy(t => t.TransactionDate)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 4. Loop only through the requested date range
            for (var date = minDate; date <= maxDate; date = date.AddDays(1))
            {
                decimal dailyNetFlow = 0m;

                if (txByDate.TryGetValue(date, out var dailyTxs))
                {
                    foreach (var tx in dailyTxs)
                    {
                        if (!currentHoldings.ContainsKey(tx.Ticker))
                            currentHoldings[tx.Ticker] = 0;

                        decimal txAmount = tx.Units * tx.PricePerUnit + tx.Fees;

                        switch (tx.Type)
                        {
                            case TransactionType.BUY:
                                currentHoldings[tx.Ticker] += tx.Units;
                                dailyNetFlow += txAmount;
                                break;

                            case TransactionType.SELL:
                                currentHoldings[tx.Ticker] -= tx.Units;
                                dailyNetFlow -= (tx.Units * tx.PricePerUnit - tx.Fees);
                                break;

                            case TransactionType.DEPOSIT:
                                dailyNetFlow += tx.Units;
                                break;

                            case TransactionType.WITHDRAW:
                                dailyNetFlow -= tx.Units;
                                break;
                        }
                    }
                }

                cumulativeNetFlow += dailyNetFlow;

                decimal totalValue = 0m;
                foreach (var (ticker, units) in currentHoldings)
                {
                    if (units == 0) continue;

                    if (priceLookup.ContainsKey(ticker) && priceLookup[ticker].ContainsKey(date))
                    {
                        lastKnownPrices[ticker] = priceLookup[ticker][date];
                    }

                    if (lastKnownPrices.TryGetValue(ticker, out var price))
                    {
                        totalValue += units * price;
                    }
                }

                if (totalValue > peakValue) peakValue = totalValue;

                decimal drawdown = peakValue > 0 ? (totalValue - peakValue) / peakValue : 0;
                if (drawdown < globalMaxDrawdown) globalMaxDrawdown = drawdown;

                decimal pnl = totalValue - cumulativeNetFlow;
                decimal simpleReturn = cumulativeNetFlow > 0 ? pnl / cumulativeNetFlow : 0;

                decimal dailyChange = 0m;
                if (history.Any())
                {
                    decimal prevValue = history.Last().TotalValue;
                    if (prevValue > 0)
                        dailyChange = (totalValue - prevValue) / prevValue;
                }

                history.Add(new DailyValuationPointDto
                {
                    Date = date,
                    TotalValue = totalValue,
                    NetFlow = dailyNetFlow,
                    CumulativeNetFlow = cumulativeNetFlow,
                    PnL = pnl,
                    Return = simpleReturn,
                    Peak = peakValue,
                    Drawdown = drawdown,
                    DailyChangePercentage = dailyChange
                });
            }

            var lastPoint = history.LastOrDefault();

            return new PortfolioDashboardDto
            {
                PortfolioId = portfolioId,
                ReferenceDate = maxDate,
                CurrentTotalValue = lastPoint?.TotalValue ?? 0,
                TotalInvested = lastPoint?.CumulativeNetFlow ?? 0,
                AbsolutePnL = lastPoint?.PnL ?? 0,
                SimpleReturn = lastPoint?.Return ?? 0,
                MaxDrawdown = globalMaxDrawdown,
                History = history
            };
        }
    }
}