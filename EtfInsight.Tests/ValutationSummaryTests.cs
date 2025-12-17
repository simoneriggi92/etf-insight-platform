using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Api;
using FluentAssertions;
using Xunit;

namespace EtfInsight.Tests
{
    public class ValutationSummaryTests
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
        }
    }
}