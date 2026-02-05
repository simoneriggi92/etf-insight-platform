using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Services;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Tests.Services
{
    public class PortfolioAnalyticsServiceTests
    {
        private readonly PortfolioAnalyticsService _service;
        private readonly MockPortfolioRepository _portfolioRepo;
        private readonly MockEtfPriceRepository _priceRepo;
        private readonly MockPerformanceCalculator _performanceCalculator;

        public PortfolioAnalyticsServiceTests()
        {
            _portfolioRepo = new MockPortfolioRepository();
            _priceRepo = new MockEtfPriceRepository();
            _performanceCalculator = new MockPerformanceCalculator();
            _service = new PortfolioAnalyticsService(_performanceCalculator, _portfolioRepo, _priceRepo);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_SimpleScenario_CalculatesCorrectMetrics()
        {
            // Arrange: Single BUY transaction, price increases over 3 days
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 3);

            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Test Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
                {
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 10m,
                        PricePerUnit = 100m,
                        Fees = 5m
                    }
                }
            };

            var prices = new List<EtfPrice>
            {
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 105m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 110m }
            };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(portfolioId, from, to);

            // Assert
            Assert.Equal(portfolioId, result.PortfolioId);
            Assert.Equal(to, result.ReferenceDate);

            // TotalInvested = 10 * 100 + 5 = 1005
            Assert.Equal(1005m, result.TotalInvested);

            // CurrentTotalValue = 10 * 110 = 1100
            Assert.Equal(1100m, result.CurrentTotalValue);

            // AbsolutePnL = 1100 - 1005 = 95
            Assert.Equal(95m, result.AbsolutePnL);

            // SimpleReturn = 95 / 1005 ≈ 0.0945
            Assert.Equal(0.0945m, result.SimpleReturn, 4);

            // History should have 3 data points
            Assert.Equal(3, result.History.Count());

            // Verify first day
            var day1 = result.History.First();
            Assert.Equal(new DateOnly(2026, 1, 1), day1.Date);
            Assert.Equal(1000m, day1.TotalValue); // 10 * 100
            Assert.Equal(1005m, day1.NetFlow); // Initial investment
            Assert.Equal(1005m, day1.CumulativeNetFlow);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_BuyAndSell_TracksNetFlowCorrectly()
        {
            // Arrange: BUY then SELL scenario
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 5);

            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Test Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
                {
                    // Day 1: Buy 10 units at 100
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 10m,
                        PricePerUnit = 100m,
                        Fees = 5m
                    },
                    // Day 3: Sell 5 units at 120
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 3),
                        Type = TransactionType.SELL,
                        Units = 5m,
                        PricePerUnit = 120m,
                        Fees = 3m
                    }
                }
            };

            var prices = new List<EtfPrice>
            {
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 110m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 120m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 4), ClosePrice = 115m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 5), ClosePrice = 125m }
            };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(portfolioId, from, to);

            // Assert
            // Day 1: NetFlow = +1005 (10*100 + 5)
            // Day 3: NetFlow = -(5*120 - 3) = -597
            // CumulativeNetFlow = 1005 - 597 = 408
            Assert.Equal(408m, result.TotalInvested);

            // Final holdings: 5 units at 125 = 625
            Assert.Equal(625m, result.CurrentTotalValue);

            // PnL = 625 - 408 = 217
            Assert.Equal(217m, result.AbsolutePnL);

            // Check day 3 specifically
            var day3 = result.History.ElementAt(2);
            Assert.Equal(new DateOnly(2026, 1, 3), day3.Date);
            Assert.Equal(-597m, day3.NetFlow);
            Assert.Equal(408m, day3.CumulativeNetFlow);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_MultipleETFs_CalculatesCorrectly()
        {
            // Arrange: Portfolio with 2 different ETFs
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 3);

            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Multi ETF Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
                {
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 10m,
                        PricePerUnit = 100m,
                        Fees = 0m
                    },
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF2",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 5m,
                        PricePerUnit = 200m,
                        Fees = 0m
                    }
                }
            };

            var prices = new List<EtfPrice>
            {
                // ETF1 prices
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 105m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 110m },
                // ETF2 prices
                new EtfPrice { Ticker = "ETF2", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 200m },
                new EtfPrice { Ticker = "ETF2", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 210m },
                new EtfPrice { Ticker = "ETF2", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 220m }
            };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(portfolioId, from, to);

            // Assert
            // TotalInvested = (10*100) + (5*200) = 2000
            Assert.Equal(2000m, result.TotalInvested);

            // CurrentTotalValue = (10*110) + (5*220) = 1100 + 1100 = 2200
            Assert.Equal(2200m, result.CurrentTotalValue);

            // PnL = 2200 - 2000 = 200
            Assert.Equal(200m, result.AbsolutePnL);

            // Return = 200 / 2000 = 0.10
            Assert.Equal(0.10m, result.SimpleReturn);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_MissingPriceData_UsesCarryForward()
        {
            // Arrange: Price data missing for day 2, should use day 1 price
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 3);

            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Test Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
                {
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 10m,
                        PricePerUnit = 100m,
                        Fees = 0m
                    }
                }
            };

            var prices = new List<EtfPrice>
            {
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                // Missing day 2
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 110m }
            };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(portfolioId, from, to);

            // Assert
            var day2 = result.History.ElementAt(1);
            Assert.Equal(new DateOnly(2026, 1, 2), day2.Date);
            // Should use carried-forward price from day 1: 10 * 100 = 1000
            Assert.Equal(1000m, day2.TotalValue);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_DrawdownCalculation_TracksCorrectly()
        {
            // Arrange: Price goes up then down to test drawdown
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 5);

            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Test Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
                {
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 10m,
                        PricePerUnit = 100m,
                        Fees = 0m
                    }
                }
            };

            var prices = new List<EtfPrice>
            {
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 120m }, // Peak
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 110m }, // Down
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 4), ClosePrice = 90m },  // Further down
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 5), ClosePrice = 105m }  // Recovery
            };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(portfolioId, from, to);

            // Assert
            var day2 = result.History.ElementAt(1);
            Assert.Equal(1200m, day2.Peak); // Peak at day 2: 10 * 120
            Assert.Equal(0m, day2.Drawdown); // No drawdown at peak

            var day4 = result.History.ElementAt(3);
            // Drawdown = (900 - 1200) / 1200 = -0.25 (25% down from peak)
            Assert.Equal(-0.25m, day4.Drawdown);

            // Max drawdown should be -25%
            Assert.Equal(-0.25m, result.MaxDrawdown);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_DailyChangePercentage_CalculatesCorrectly()
        {
            // Arrange
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 3);

            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Test Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
                {
                    new Transaction
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Ticker = "ETF1",
                        TransactionDate = new DateOnly(2026, 1, 1),
                        Type = TransactionType.BUY,
                        Units = 10m,
                        PricePerUnit = 100m,
                        Fees = 0m
                    }
                }
            };

            var prices = new List<EtfPrice>
            {
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 100m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 110m },
                new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 121m }
            };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(portfolioId, from, to);

            // Assert
            var day1 = result.History.ElementAt(0);
            Assert.Equal(0m, day1.DailyChangePercentage); // First day has no previous day

            var day2 = result.History.ElementAt(1);
            // (1100 - 1000) / 1000 = 0.10
            Assert.Equal(0.10m, day2.DailyChangePercentage);

            var day3 = result.History.ElementAt(2);
            // (1210 - 1100) / 1100 = 0.10
            Assert.Equal(0.10m, day3.DailyChangePercentage);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_EmptyPortfolio_ReturnsEmptyResult()
        {
            // Arrange
            var portfolioId = Guid.NewGuid();
            _portfolioRepo.SetPortfolio(null);

            // Act
            var result = await _service.GetPortfolioAnalyticsAsync(
                portfolioId,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 3));

            // Assert
            Assert.Equal(portfolioId, result.PortfolioId);
            Assert.Equal(0m, result.CurrentTotalValue);
            Assert.Equal(0m, result.TotalInvested);
            Assert.Empty(result.History);
        }

        [Fact]
        public async Task GetPortfolioAnalyticsAsync_TransactionsBeforeFromDate_IncludesInHoldings()
        {
            // Arrange: Transaction in Dec 2025, but querying Jan 2026
            // Should include the transaction to calculate holdings correctly
            var portfolioId = Guid.NewGuid();
            var portfolio = new Portfolio
            {
                Id = portfolioId,
                Name = "Test Portfolio",
                Currency = Currency.USD,
                Transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = Guid.NewGuid(),
                PortfolioId = portfolioId,
                Ticker = "ETF1",
                TransactionDate = new DateOnly(2025, 12, 1), // Before 'from'
                Type = TransactionType.BUY,
                Units = 10m,
                PricePerUnit = 100m,
                Fees = 5m
            }
        }
            };

            var prices = new List<EtfPrice>
    {
        new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 1), ClosePrice = 110m },
        new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 2), ClosePrice = 115m },
        new EtfPrice { Ticker = "ETF1", PriceDate = new DateOnly(2026, 1, 3), ClosePrice = 120m }
    };

            _portfolioRepo.SetPortfolio(portfolio);
            _priceRepo.SetPrices(prices);

            // Act: Query from Jan 1 to Jan 3, 2026
            var result = await _service.GetPortfolioAnalyticsAsync(
                portfolioId,
                new DateOnly(2026, 1, 1),  // from
                new DateOnly(2026, 1, 3)); // to

            // Assert: Should include holdings from Dec transaction
            Assert.Equal(portfolioId, result.PortfolioId);
            Assert.Equal(3, result.History.Count()); // 3 days: Jan 1-3

            // First day should have 10 units valued at 110
            var day1 = result.History.First();
            Assert.Equal(new DateOnly(2026, 1, 1), day1.Date);
            Assert.Equal(1100m, day1.TotalValue); // 10 * 110
            Assert.Equal(1005m, day1.CumulativeNetFlow); // 10*100 + 5 from Dec
            Assert.Equal(0m, day1.NetFlow); // No transaction on this day

            // Final value
            Assert.Equal(1200m, result.CurrentTotalValue); // 10 * 120
        }
    }

    // Mock Implementations
    internal class MockPortfolioRepository : IPortfolioRepository
    {
        private Portfolio? _portfolio;

        public void SetPortfolio(Portfolio? portfolio) => _portfolio = portfolio;

        public Task<Portfolio?> GetPortfolioWithTransactionsAsync(Guid id)
            => Task.FromResult(_portfolio);
    }

    internal class MockEtfPriceRepository : IEtfPriceRepository
    {
        private List<EtfPrice> _prices = new();

        public void SetPrices(List<EtfPrice> prices) => _prices = prices;

        public Task<IEnumerable<EtfPrice>> GetPricesByTickersAsync(
            IEnumerable<string> tickers,
            DateOnly from,
            DateOnly to)
        {
            var result = _prices.Where(p =>
                tickers.Contains(p.Ticker) &&
                p.PriceDate >= from &&
                p.PriceDate <= to);
            return Task.FromResult(result);
        }
    }

    internal class MockPerformanceCalculator : IPerformanceCalculator
    {
        public decimal CalculateTWRR(
            IEnumerable<Transaction> transactions,
            IEnumerable<EtfPrice> etfPrices)
            => 0m; // Not used in analytics tests
    }
}