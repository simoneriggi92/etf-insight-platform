using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Api;
using FluentAssertions;
using Xunit;

namespace EtfInsight.Tests
{
    public class ValuationSummaryTests
    {
        [Fact]
        public void ComputeSummary_FromPoints_IsCorrect()
        {
            var points = new List<ValuationPoint>
            {
                new(new DateOnly(2025,11,20), 1000m, 0m,     0m,     1000m, 1000m, 0m,   0m),
                new(new DateOnly(2025,11,21), 1100m, 100m,   0.1m,   0m,    1000m, 100m, 0.1m),
                new(new DateOnly(2025,11,22), 1050m, -50m,  -0.045m, 0m,    1000m, 50m,  0.05m),
            };

            var summary = ValuationSummaryCalculator.ComputeSummary(points, 1, "EUR");

            summary.StartValue.Should().Be(1000m);
            summary.EndValue.Should().Be(1050m);
            summary.NetContributions.Should().Be(1000m);
            summary.PnL.Should().Be(50m);
            summary.TotalReturn.Should().Be(0.05m);
            summary.MaxDrawdown.Should().BeLessThanOrEqualTo(0m);
            summary.PortfolioId.Should().Be(1);
            summary.BaseCurrency.Should().Be("EUR");
        }


        [Fact]
        public void ComputeSummary_MaxDrawdown_PeakToTrough_IsCorrect()
        {
            // TotalValue series: 1000 -> 1200 -> 900 -> 1300
            // Max drawdown should be from peak 1200 to trough 900: (900-1200)/1200 = -0.25
            var points = new List<ValuationPoint>
            {
                new(new DateOnly(2025, 11, 20), 1000m, 0m,     0m,      1000m, 1000m, 0m,    0m),
                new(new DateOnly(2025, 11, 21), 1200m, 200m,   0.2m,    0m,    1000m, 200m,  0.2m),
                new(new DateOnly(2025, 11, 22), 900m,  -300m, -0.25m,  0m,    1000m, -100m, -0.1m),
                new(new DateOnly(2025, 11, 23), 1300m, 400m,   0.444m, 0m,    1000m, 300m,  0.3m),
            };

            var summary = ValuationSummaryCalculator.ComputeSummary(points, 1, "EUR");

            summary.StartValue.Should().Be(1000m);
            summary.EndValue.Should().Be(1300m);
            summary.NetContributions.Should().Be(1000m);
            summary.PnL.Should().Be(300m);
            summary.TotalReturn.Should().Be(0.3m);
            summary.MaxDrawdown.Should().Be(-0.25m);
            summary.PortfolioId.Should().Be(1);
            summary.BaseCurrency.Should().Be("EUR");
        }

        [Fact]
        public void ComputeSummary_MaxDrawdown_IsZero_WhenMonotonicIncrease()
        {
            var points = new List<ValuationPoint>
            {
                new(new DateOnly(2025, 11, 20), 1000m, 0m,    0m,   1000m, 1000m, 0m,   0m),
                new(new DateOnly(2025, 11, 21), 1100m, 100m,  0.1m, 0m,    1000m, 100m, 0.1m),
                new(new DateOnly(2025, 11, 22), 1200m, 100m,  0.091m, 0m,  1000m, 200m, 0.2m),
            };

            var summary = ValuationSummaryCalculator.ComputeSummary(points, 1, "EUR");

            summary.StartValue.Should().Be(1000m);
            summary.EndValue.Should().Be(1200m);
            summary.NetContributions.Should().Be(1000m);
            summary.PnL.Should().Be(200m);
            summary.TotalReturn.Should().Be(0.2m);
            summary.MaxDrawdown.Should().Be(0m);
            summary.PortfolioId.Should().Be(1);
            summary.BaseCurrency.Should().Be("EUR");
        }
    }
}