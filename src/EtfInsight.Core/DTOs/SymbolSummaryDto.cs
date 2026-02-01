using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public class SymbolSummaryDto
    {
        public string Symbol { get; set; } = string.Empty;
        public long DataPoints { get; set; }
        public DateOnly FirstDate { get; set; }
        public DateOnly LastDate { get; set; }
    }
}