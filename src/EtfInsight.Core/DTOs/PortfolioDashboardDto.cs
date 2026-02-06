using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public class PortfolioDashboardDto
    {
        public Guid PortfolioId { get; set; }
        public DateOnly ReferenceDate { get; set; }

        // Point-in-time Data (Today)
        public decimal CurrentTotalValue { get; set; } // TotalValue(D)
        public decimal TotalInvested { get; set; } // CumulativeNetFlow(D)
        public decimal AbsolutePnL { get; set; } // PnL(D)
        public decimal SimpleReturn { get; set; } // Return(D)
        public decimal MaxDrawdown { get; set; } // MaxDrawdown(on all the history up to D)

        // Time Series Data (for charts)
        public IEnumerable<DailyValuationPointDto> History { get; set; } = Array.Empty<DailyValuationPointDto>();
    }

    public class DailyValuationPointDto
    {
        public DateOnly Date { get; set; }
        public decimal TotalValue { get; set; }
        public decimal NetFlow { get; set; } // Cash added/removed on that day
        public decimal CumulativeNetFlow { get; set; }
        public decimal Drawdown { get; set; } // % from peak
        public decimal Return { get; set; } // Simple Return since inception
        public decimal PnL { get; set; } // Absolute PnL since inception
        public decimal Peak { get; set; } // Peak value up to that day
        public decimal DailyChangePercentage { get; set; } // Daily % change from previous day
    }
}