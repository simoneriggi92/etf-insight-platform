using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EtfInsight.Core.Models;
using EtfInsight.Core.Valuation;
using EtfInsight.Core.Math;

namespace EtfInsight.Core.Tests
{
    public class DrawdownCalculatorTests
    {
        [Fact]
        public void CalculateMaxDrawdown_Works()
        {
            var history = new List<ValuationPoint>
        {
            new(new DateOnly(2026,1,1), 1000m, 0m, 0m, 0m, 0m),
            new(new DateOnly(2026,1,2), 1200m, 0m, 0m, 0m, 0m), // peak
            new(new DateOnly(2026,1,3), 900m, 0m, 0m, 0m, 0m),  // trough
            new(new DateOnly(2026,1,4), 1100m, 0m, 0m, 0m, 0m),
        };

            var dd = DrawdownCalculator.CalculateMaxDrawdown(history);

            // (1200-900)/1200 = 0.25 => 25%
            Assert.Equal(25.0000m, dd.MaxDrawdownPercent);
            Assert.Equal(new DateOnly(2026, 1, 2), dd.PeakDate);
            Assert.Equal(new DateOnly(2026, 1, 3), dd.TroughDate);
        }
    }
}