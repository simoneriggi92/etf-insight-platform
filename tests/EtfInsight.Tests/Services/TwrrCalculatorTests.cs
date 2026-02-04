using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Services;

namespace EtfInsight.Tests.Services
{
    public class TwrrCalculatorTests
    {
        private readonly TwrrCalculator _calculator;

        public TwrrCalculatorTests()
        {
            _calculator = new TwrrCalculator();
        }

        [Fact]
        public void CalculateTWRR_MultiDayWithCashFlow_ReturnsCorrectCompoundedReturn()
        {
            // Arrange: Build the scenario
            // T0 (01/01): Buy 1 unit at 100 euros -> Invested: 100, Value: 100
            // T1 (02/01): Price increases to 110 euros -> Value: 110, Return: (110/100) - 1 = 10%
            // T2 (03/01): Price increases to 120 euros, buy another unit at 120
            //             Value pre-flow: 120
            //             Cash Flow: +120
            //             Final Value: 240 (2 units × 120)
            //             Return T2: (120/110) - 1 = 9.09%
            // Total TWRR: (1.10 × 1.0909) - 1 = 1.20 - 1 = 20%

            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    TransactionDate = new DateOnly(2026, 1, 1),
                    Type = TransactionType.BUY,
                    PricePerUnit = 100m,
                    Units = 1m,
                    Fees = 0m
                },
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    TransactionDate = new DateOnly(2026, 1, 3),
                    Type = TransactionType.BUY,
                    PricePerUnit = 120m,
                    Units = 1m,
                    Fees = 0m
                }
            };

            var prices = new List<Etf>
            {
                new Etf
                {
                    Id = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    PriceDate = new DateOnly(2026, 1, 1),
                    ClosePrice = 100m,
                    Currency = "EUR"
                },
                new Etf
                {
                    Id = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    PriceDate = new DateOnly(2026, 1, 2),
                    ClosePrice = 110m,
                    Currency = "EUR"
                },
                new Etf
                {
                    Id = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    PriceDate = new DateOnly(2026, 1, 3),
                    ClosePrice = 120m,
                    Currency = "EUR"
                }
            };

            var result = _calculator.CalculateTWRR(transactions, prices);

            // Expected: 20% return = 0.20
            Assert.Equal(0.20m, result, 4); // 4 decimal places precision
        }

        [Fact]
        public void CalculateTWRR_NoTransactions_ReturnsZero()
        {
            // Arrange
            var transactions = new List<Transaction>();
            var prices = new List<Etf>
            {
                new Etf
                {
                    Id = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    PriceDate = new DateOnly(2026, 1, 1),
                    ClosePrice = 100m,
                    Currency = "EUR"
                }
            };

            // Act
            var result = _calculator.CalculateTWRR(transactions, prices);

            // Assert
            Assert.Equal(0m, result);

        }

        [Fact]
        public void CalculateTWRR_NoPrices_ReturnsZero()
        {
            // Arrange
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    TransactionDate = new DateOnly(2026, 1, 1),
                    Type = TransactionType.BUY,
                    PricePerUnit = 100m,
                    Units = 1m,
                    Fees = 0m
                }
            };
            var prices = new List<Etf>();

            // Act
            var result = _calculator.CalculateTWRR(transactions, prices);

            // Assert
            Assert.Equal(0m, result);
        }

        [Fact]
        public void CalculateTWRR_SimplePriceIncrease_ReturnsCorrectReturn()
        {
            // Arrange
            // Day 1: Buy 1 unit at 100
            // Day 2: Price increases to 105
            // Expected return: 5%
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    TransactionDate = new DateOnly(2026, 1, 1),
                    Type = TransactionType.BUY,
                    PricePerUnit = 100m,
                    Units = 1m,
                    Fees = 0m
                }
            };

            var prices = new List<Etf>
            {
                new Etf
                {
                    Id = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    PriceDate = new DateOnly(2026, 1, 1),
                    ClosePrice = 100m,
                    Currency = "EUR"
                },
                new Etf
                {
                    Id = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    PriceDate = new DateOnly(2026, 1, 2),
                    ClosePrice = 105m,
                    Currency = "EUR"
                }
            };

            // Act
            var result = _calculator.CalculateTWRR(transactions, prices);

            // Assert
            Assert.Equal(0.05m, result, 4); // 5% return
        }

        [Fact]
        public void CalculateTWRR_WithSell_ReturnsCorrectReturn()
        {
            // Arrange
            // Day 1: Buy 2 units at 100 (total: 200)
            // Day 2: Price increases to 110 (value: 220)
            // Day 3: Price at 120, sell 1 unit at 120 (cash out: 120, remaining value: 120)
            // Day 4: Price at 125 (value: 125)

            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    TransactionDate = new DateOnly(2026, 1, 1),
                    Type = TransactionType.BUY,
                    PricePerUnit = 100m,
                    Units = 2m,
                    Fees = 0m
                },
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = Guid.NewGuid(),
                    Ticker = "TEST.ETF",
                    TransactionDate = new DateOnly(2026, 1, 3),
                    Type = TransactionType.SELL,
                    PricePerUnit = 120m,
                    Units = 1m,
                    Fees = 0m
                }
            };

            var prices = new List<Etf>
            {
                new Etf { Ticker = "TEST.ETF", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new Etf { Ticker = "TEST.ETF", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 110m },
                new Etf { Ticker = "TEST.ETF", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 120m },
                new Etf { Ticker = "TEST.ETF", PriceDate = new DateOnly(2026, 1, 4), ClosePrice = 125m }
            };

            // Act
            var result = _calculator.CalculateTWRR(transactions, prices);

            // Assert
            // Day 2: (110/100) - 1 = 10%
            // Day 3: (120/110) - 1 = 9.09%
            // Day 4: (125/120) - 1 = 4.17%
            // Total: 1.10 × 1.0909 × 1.0417 - 1 = 0.25 = 25%
            Assert.Equal(0.25m, result, 2);
        }

        [Fact]
        public void CalculateTWRR_MultipleSymbols_ReturnsCorrectReturn()
        {
            // Arrange
            // Day 1: Buy 1 unit of ETF-A at 100 and 1 unit of ETF-B at 50
            // Day 2: ETF-A at 110, ETF-B at 55
            // Total value: 150 -> 165
            // Return: (165/150) - 1 = 10%

            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Ticker = "ETF-A",
                    TransactionDate = new DateOnly(2026, 1, 1),
                    Type = TransactionType.BUY,
                    PricePerUnit = 100m,
                    Units = 1m,
                    Fees = 0m
                },
                new Transaction
                {
                    Ticker = "ETF-B",
                    TransactionDate = new DateOnly(2026, 1, 1),
                    Type = TransactionType.BUY,
                    PricePerUnit = 50m,
                    Units = 1m,
                    Fees = 0m
                }
            };

            var prices = new List<Etf>
            {
                new Etf { Ticker = "ETF-A", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new Etf { Ticker = "ETF-A", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 110m },
                new Etf { Ticker = "ETF-B", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 50m },
                new Etf { Ticker = "ETF-B", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 55m }
            };

            // Act
            var result = _calculator.CalculateTWRR(transactions, prices);

            // Assert
            Assert.Equal(0.10m, result, 4); // 10% return
        }
    }
}