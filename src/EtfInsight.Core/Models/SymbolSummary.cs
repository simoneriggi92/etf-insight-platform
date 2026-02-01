using System;

namespace EtfInsight.Core.Models
{
    public class SymbolSummary
    {
        public string Symbol { get; set; } = string.Empty;
        public long DataPoints { get; set; }
        public DateOnly FirstDate { get; set; }
        public DateOnly LastDate { get; set; }
    }
}