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
    }
}