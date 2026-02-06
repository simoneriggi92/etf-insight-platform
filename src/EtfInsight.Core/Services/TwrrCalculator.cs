using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;

namespace EtfInsight.Core.Services
{
    public class TwrrCalculator : IPerformanceCalculator
    {
        public decimal CalculateTWRR(IEnumerable<Transaction> transactions, IEnumerable<EtfPrice> etfPrices)
        {
            // input validation
            var transactionList = transactions.OrderBy(t => t.TransactionDate).ToList();
            if (!transactionList.Any())
            {
                return 0m;
            }

            var priceList = etfPrices.OrderBy(p => p.PriceDate).ToList();
            if (!priceList.Any())
            {
                return 0m;
            }

            //STEP 1: Find the date range (minimum = first transaction date, maximum = last price date)
            var minDate = transactionList.First().TransactionDate;
            var maxDate = priceList.Last().PriceDate;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (maxDate > today)
            {
                maxDate = today;
            }

            // STEP 2: Build price lookup dictionary for quick access [Ticker][Date] => Price
            var priceLookup = priceList
            .GroupBy(p => p.Ticker)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(p => p.PriceDate, p => p.ClosePrice)
            );


            // STEP 3: Group transactions by date for efficient daily lookup
            var transactionsByDate = transactionList
                .GroupBy(t => t.TransactionDate)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Initialize holdings tracker: [Ticker] => Units Held
            var holdings = new Dictionary<string, decimal>();

            // Initialize last known prices for handling missing price data
            var lastKnownPrices = new Dictionary<string, decimal>();

            // Initialize TWRR calculation variables
            decimal totalReturn = 0m;
            decimal previousDayValue = 0m;
            var currentDate = minDate;

            // STEP 2: Daily Iteration from minDate to maxDate
            while (currentDate <= maxDate)
            {
                // STEP 3: Calculate portfolio value before cash flows (valueStart)
                // Use yesterday's ending value, NOT recalculated with today's prices
                decimal valueStart = previousDayValue;

                // STEP 4: Process cash flows (transactions) for the day
                decimal cashFlow = 0m;

                if (transactionsByDate.ContainsKey(currentDate))
                {
                    foreach (var transaction in transactionsByDate[currentDate])
                    {
                        // Ensure ticker exists in holdings
                        if (!holdings.ContainsKey(transaction.Ticker))
                        {
                            holdings[transaction.Ticker] = 0m;
                        }

                        // Update holdings based on transaction type
                        switch (transaction.Type)
                        {
                            case TransactionType.BUY:
                                // External cash in: buy adds units
                                holdings[transaction.Ticker] += transaction.Units;
                                cashFlow += transaction.Units * transaction.PricePerUnit + transaction.Fees;
                                break;

                            case TransactionType.SELL:
                                // External cash out: sell removes units
                                holdings[transaction.Ticker] -= transaction.Units;
                                cashFlow -= (transaction.Units * transaction.PricePerUnit + transaction.Fees);
                                break;

                            case TransactionType.DEPOSIT:
                                // Pure cash deposit (no units change, just cash in), use PricePerUnit as amount
                                cashFlow += transaction.PricePerUnit;
                                break;

                            case TransactionType.WITHDRAW:
                                // Pure cash withdrawal (no units change, just cash out), use PricePerUnit as amount
                                cashFlow -= transaction.PricePerUnit;
                                break;
                        }
                    }
                }

                // Recalculate valueEnd after processing transactions using updated holdings
                decimal valueEnd = 0m;
                foreach (var ticker in holdings.Keys)
                {
                    var units = holdings[ticker];
                    if (units == 0) continue;

                    decimal todayPrice = GetPriceForDate(ticker, currentDate, priceLookup, lastKnownPrices);
                    valueEnd += units * todayPrice;
                }
                // STEP 5: Calaculate sub-period return for the day
                // Formula rn = (valueEnd - cashFlow) / valueStart - 1
                // Simplified when no cash flows: rn. = (todayPrice / yesterdayPrice) - 1
                if (valueStart > 0)
                {
                    decimal subPeriodReturn = ((valueEnd - cashFlow) / valueStart) - 1;

                    // STEP 6: Aggregate sub-period returns into TotalReturn
                    totalReturn = (1 + totalReturn) * (1 + subPeriodReturn) - 1;
                }

                // Save today's ending value for tomorrow's starting value
                previousDayValue = valueEnd;

                // Move to next day
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