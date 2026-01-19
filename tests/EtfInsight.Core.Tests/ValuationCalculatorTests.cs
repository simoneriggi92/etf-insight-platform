namespace EtfInsight.Core.Tests;

using EtfInsight.Core.Models;
using EtfInsight.Core.Valuation;
using Xunit;

public class ValuationCalculatorTests
{
    [Fact]
    public void CalculateHistory_BasicScenario_Works()
    {
        var tx = new List<Transaction>
        {
            new("SPY", TransactionType.Buy, 10m, 100m, new DateOnly(2026,1,1))
        };

        var prices = new List<DailyPrice>
        {
            new("SPY", new DateOnly(2026,1,1), 100m),
            new("SPY", new DateOnly(2026,1,2), 110m),
        };

        var days = new List<DateOnly>
        {
            new(2026,1,1),
            new(2026,1,2)
        };

        var history = ValuationCalculator.CalculateHistory(tx, prices, days);

        Assert.Equal(2, history.Count);

        Assert.Equal(1000m, history[0].TotalValue);
        Assert.Equal(1000m, history[0].CumNetFlow);
        Assert.Equal(0m, history[0].PnL);

        Assert.Equal(1100m, history[1].TotalValue);
        Assert.Equal(1000m, history[1].CumNetFlow);
        Assert.Equal(100m, history[1].PnL);
        Assert.Equal(0.1000m, history[1].Return);
    }

    [Fact]
    public void CalculateHistory_SellCannotOversell_Throws()
    {
        var tx = new List<Transaction>
        {
            new("SPY", TransactionType.Sell, 1m, 100m, new DateOnly(2026,1,1))
        };

        var prices = new List<DailyPrice>
        {
            new("SPY", new DateOnly(2026,1,1), 100m)
        };

        var days = new List<DateOnly> { new(2026, 1, 1) };

        Assert.Throws<InvalidOperationException>(() =>
            ValuationCalculator.CalculateHistory(tx, prices, days));
    }

    [Fact]
    public void CalculateHistory_MissingPrice_Throws()
    {
        var tx = new List<Transaction>
        {
            new("SPY", TransactionType.Buy, 1m, 100m, new DateOnly(2026,1,1))
        };

        var prices = new List<DailyPrice>(); // empty
        var days = new List<DateOnly> { new(2026, 1, 1) };

        Assert.Throws<InvalidOperationException>(() =>
            ValuationCalculator.CalculateHistory(tx, prices, days));
    }

    [Fact]
    public void CalculateHistory_NetFlowSigns_BuyPositiveSellNegative()
    {
        var tx = new List<Transaction>
        {
            new("SPY", TransactionType.Buy, 10m, 100m, new DateOnly(2026,1,1)),
            new("SPY", TransactionType.Sell, 2m, 120m, new DateOnly(2026,1,3))
        };

        var prices = new List<DailyPrice>
        {
            new("SPY", new DateOnly(2026,1,1), 100m),
            new("SPY", new DateOnly(2026,1,2), 110m),
            new("SPY", new DateOnly(2026,1,3), 120m),
        };

        var days = new List<DateOnly>
        {
            new(2026,1,1),
            new(2026,1,2),
            new(2026,1,3)
        };

        var history = ValuationCalculator.CalculateHistory(tx, prices, days);

        Assert.Equal(1000m, history[0].NetFlow);  // BUY
        Assert.Equal(0m, history[1].NetFlow);     // no tx
        Assert.Equal(-240m, history[2].NetFlow);  // SELL
    }
}