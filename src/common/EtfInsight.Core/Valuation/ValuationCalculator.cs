using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using EtfInsight.Core.Models;
using EtfInsight.Core.Mathematics;

namespace EtfInsight.Core.Valuation
{
    public static class ValuationCalculator
    {
        public static IReadOnlyList<ValuationPoint> CalculateHistory(
            IReadOnlyList<EtfInsight.Core.Models.Transaction> transactions,
            IReadOnlyList<DailyPrice> prices,
            IReadOnlyList<DateOnly> tradingDays
        )
        {
            if (tradingDays.Count == 0)
            {
                return Array.Empty<ValuationPoint>();
            }

            // Index prices by (symbol, date) for fast lookup
            var priceMap = prices
                .ToDictionary(
                    p => (p.Symbol.ToUpperInvariant(), p.Date)
                );

            // Sort transactions by date
            var orderedTransactions = transactions
                .OrderBy(t => t.Date)
                .ThenBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();


            // Running holdings per symbol
            var holdings = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            decimal cumNetFlow = 0m;

            var result = new List<ValuationPoint>(tradingDays.Count);

            int txIndex = 0;

            foreach (var day in tradingDays.OrderBy(d => d))
            {
                decimal netFlow = 0m;

                // Process all transactions for this day
                while (txIndex < orderedTransactions.Count && orderedTransactions[txIndex].Date <= day)
                {
                    var tx = orderedTransactions[txIndex];
                    var symbol = tx.Symbol.ToUpperInvariant();

                    if (tx.Quantity <= 0) throw new InvalidOperationException("Quantity must be positive.");
                    if (tx.Price <= 0) throw new InvalidOperationException("Price must be positive.");

                    holdings.TryGetValue(symbol, out var currentQty);

                    if (tx.Type == TransactionType.Buy)
                    {
                        holdings[symbol] = currentQty + tx.Quantity;
                        netFlow += tx.Quantity * tx.Price;
                    }
                    else
                    {
                        var newQty = currentQty - tx.Quantity;
                        if (newQty < 0)
                            throw new InvalidOperationException($"Oversell detected for {symbol} on {tx.Date:yyyy-MM-dd}.");

                        holdings[symbol] = newQty;
                        netFlow -= tx.Quantity * tx.Price;
                    }

                    txIndex++;
                }

                cumNetFlow += netFlow;

                // Compute total value using close price on that day
                decimal totalValue = 0m;

                foreach (var (sym, qty) in holdings.Where(h => h.Value > 0))
                {
                    if (!priceMap.TryGetValue((sym, day), out var price))
                        throw new InvalidOperationException($"Missing price for {sym} on {day:yyyy-MM-dd}.");

                    totalValue += qty * price.Close;
                }

                totalValue = RoundingPolicy.Money(totalValue);
                var pnl = RoundingPolicy.Money(totalValue - cumNetFlow);

                var ret = cumNetFlow != 0m
                    ? RoundingPolicy.Ratio(pnl / cumNetFlow)
                    : 0m;

                result.Add(new ValuationPoint(
                    day,
                    totalValue,
                    RoundingPolicy.Money(netFlow),
                    RoundingPolicy.Money(cumNetFlow),
                    pnl,
                    ret
                ));
            }

            return result;

        }
    }
}



