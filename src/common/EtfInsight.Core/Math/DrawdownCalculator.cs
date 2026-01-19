using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Models;
using EtfInsight.Core.Mathematics;


namespace EtfInsight.Core.Math
{
    public sealed record DrawdownResult(
        decimal MaxDrawdownPercent,
        DateOnly? PeakDate,
        DateOnly? TroughDate
    );

    public static class DrawdownCalculator
    {
        public static DrawdownResult CalculateMaxDrawdown(IReadOnlyList<ValuationPoint> history)
        {
            if (history.Count == 0)
                return new DrawdownResult(0m, null, null);

            decimal peak = history[0].TotalValue;
            DateOnly peakDate = history[0].Date;

            decimal maxDd = 0m;
            DateOnly? troughDate = null;
            DateOnly? ddPeakDate = null;

            foreach (var p in history)
            {
                if (p.TotalValue > peak)
                {
                    peak = p.TotalValue;
                    peakDate = p.Date;
                }

                if (peak == 0) continue;

                var dd = (peak - p.TotalValue) / peak; // ratio (0..1)
                if (dd > maxDd)
                {
                    maxDd = dd;
                    ddPeakDate = peakDate;
                    troughDate = p.Date;
                }
            }

            return new DrawdownResult(
                MaxDrawdownPercent: RoundingPolicy.Ratio(maxDd) * 100m,
                PeakDate: ddPeakDate,
                TroughDate: troughDate
            );
        }
    }
}