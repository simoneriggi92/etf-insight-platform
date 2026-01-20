using System.Data;
using Dapper;
using EtfInsight.Api.Repositories;
using EtfInsight.Api.Services;
using EtfInsight.Core.Models;

namespace EtfInsight.Api.IntegrationTests;

[Collection("Database")]
public class ValuationServiceTests
{
    private readonly DatabaseFixture _fixture;

    public ValuationServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetHistoryAsync_WithSimplePortfolio_ReturnsCorrectValuations()
    {
        // Arrange
        await _fixture.CleanupAsync();

        using var conn = _fixture.CreateConnection();
        conn.Open();

        // Create portfolio
        var portfolioId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO portfolios (name, description, base_currency, created_at)
            VALUES ('Test Portfolio', 'Integration test', 'USD', NOW())
            RETURNING id
        ");

        // Insert ETF prices for 3 days
        var day1 = new DateOnly(2026, 1, 13);
        var day2 = new DateOnly(2026, 1, 14);
        var day3 = new DateOnly(2026, 1, 15);

        await conn.ExecuteAsync(@"
            INSERT INTO etf_prices (symbol, price_date, open_price, high_price, low_price, close_price, volume, created_at)
            VALUES 
                ('EUNL.DE', @Day1, 100.00, 105.00, 99.00, 102.50, 10000, NOW()),
                ('EUNL.DE', @Day2, 102.50, 108.00, 102.00, 106.00, 12000, NOW()),
                ('EUNL.DE', @Day3, 106.00, 110.00, 105.50, 108.75, 15000, NOW()),
                ('IS3N.DE', @Day1, 50.00, 52.00, 49.50, 51.25, 20000, NOW()),
                ('IS3N.DE', @Day2, 51.25, 53.00, 51.00, 52.50, 22000, NOW()),
                ('IS3N.DE', @Day3, 52.50, 54.00, 52.00, 53.00, 25000, NOW())
        ", new { Day1 = day1.ToDateTime(TimeOnly.MinValue), Day2 = day2.ToDateTime(TimeOnly.MinValue), Day3 = day3.ToDateTime(TimeOnly.MinValue) });

        // Insert transactions: buy EUNL.DE on day1, buy IS3N.DE on day2
        await conn.ExecuteAsync(@"
            INSERT INTO transactions (portfolio_id, symbol, transaction_type, quantity, price, transaction_date, notes, transaction_currency, created_at)
            VALUES 
                (@PortfolioId, 'EUNL.DE', 'BUY', 10, 102.50, @Day1, 'Initial purchase', 'USD', NOW()),
                (@PortfolioId, 'IS3N.DE', 'BUY', 20, 51.25, @Day2, 'Second purchase', 'USD', NOW())
        ", new { PortfolioId = portfolioId, Day1 = day1.ToDateTime(TimeOnly.MinValue), Day2 = day2.ToDateTime(TimeOnly.MinValue) });

        // Create service
        var repository = new PostgresValuationRepository(conn);
        var service = new ValuationService(repository);

        // Act
        var history = await service.GetHistoryAsync(
            portfolioId,
            day1,
            day3,
            CancellationToken.None);

        // Assert
        Assert.NotNull(history);
        Assert.Equal(3, history.Count);

        // Day 1: Only EUNL.DE (10 shares @ 102.50) = 1025.00
        var valDay1 = history[0];
        Assert.Equal(day1, valDay1.Date);
        Assert.Equal(1025.00m, valDay1.TotalValue);

        // Day 2: EUNL.DE (10 @ 106.00) + IS3N.DE (20 @ 52.50) = 2110.00
        var valDay2 = history[1];
        Assert.Equal(day2, valDay2.Date);
        Assert.Equal(2110.00m, valDay2.TotalValue);

        // Day 3: EUNL.DE (10 @ 108.75) + IS3N.DE (20 @ 53.00) = 2147.50
        var valDay3 = history[2];
        Assert.Equal(day3, valDay3.Date);
        Assert.Equal(2147.50m, valDay3.TotalValue);
    }

    [Fact]
    public async Task GetHistoryAsync_WithSellTransaction_ReturnsCorrectQuantities()
    {
        // Arrange
        await _fixture.CleanupAsync();

        using var conn = _fixture.CreateConnection();
        conn.Open();

        var portfolioId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO portfolios (name, description, base_currency, created_at)
            VALUES ('Sell Test Portfolio', 'Test sells', 'USD', NOW())
            RETURNING id
        ");

        var day1 = new DateOnly(2026, 1, 10);
        var day2 = new DateOnly(2026, 1, 11);
        var day3 = new DateOnly(2026, 1, 12);

        // Insert prices
        await conn.ExecuteAsync(@"
            INSERT INTO etf_prices (symbol, price_date, open_price, high_price, low_price, close_price, volume, created_at)
            VALUES 
                ('EUNA.DE', @Day1, 80.00, 82.00, 79.50, 81.00, 5000, NOW()),
                ('EUNA.DE', @Day2, 81.00, 83.00, 80.50, 82.50, 5500, NOW()),
                ('EUNA.DE', @Day3, 82.50, 85.00, 82.00, 84.00, 6000, NOW())
        ", new { Day1 = day1.ToDateTime(TimeOnly.MinValue), Day2 = day2.ToDateTime(TimeOnly.MinValue), Day3 = day3.ToDateTime(TimeOnly.MinValue) });

        // Transactions: Buy 100 on day1, Sell 30 on day2
        await conn.ExecuteAsync(@"
            INSERT INTO transactions (portfolio_id, symbol, transaction_type, quantity, price, transaction_date, notes, transaction_currency, created_at)
            VALUES 
                (@PortfolioId, 'EUNA.DE', 'BUY', 100, 81.00, @Day1, 'Buy', 'USD', NOW()),
                (@PortfolioId, 'EUNA.DE', 'SELL', 30, 82.50, @Day2, 'Partial sell', 'USD', NOW())
        ", new { PortfolioId = portfolioId, Day1 = day1.ToDateTime(TimeOnly.MinValue), Day2 = day2.ToDateTime(TimeOnly.MinValue) });

        var repository = new PostgresValuationRepository(conn);
        var service = new ValuationService(repository);

        // Act
        var history = await service.GetHistoryAsync(
            portfolioId,
            day1,
            day3,
            CancellationToken.None);

        // Assert
        Assert.Equal(3, history.Count);

        // Day 1: 100 shares @ 81.00 = 8100.00
        Assert.Equal(8100.00m, history[0].TotalValue);

        // Day 2: 70 shares remaining @ 82.50 (after selling 30) = 5775.00
        Assert.Equal(5775.00m, history[1].TotalValue);

        // Day 3: 70 shares @ 84.00 = 5880.00
        Assert.Equal(5880.00m, history[2].TotalValue);
    }

    [Fact]
    public async Task GetHistoryAsync_EmptyPortfolio_ReturnsEmptyHistory()
    {
        // Arrange
        await _fixture.CleanupAsync();

        using var conn = _fixture.CreateConnection();
        conn.Open();

        var portfolioId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO portfolios (name, description, base_currency, created_at)
            VALUES ('Empty Portfolio', 'No transactions', 'USD', NOW())
            RETURNING id
        ");

        var repository = new PostgresValuationRepository(conn);
        var service = new ValuationService(repository);

        // Act
        var history = await service.GetHistoryAsync(
            portfolioId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 10),
            CancellationToken.None);

        // Assert
        Assert.NotNull(history);
        Assert.Empty(history);
    }
}
