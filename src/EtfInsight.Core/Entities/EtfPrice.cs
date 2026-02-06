using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Entities
{
    public class EtfPrice : Etf
    {
        public DateOnly PriceDate { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public long Volume { get; set; }
        public string Currency { get; set; } = string.Empty;

        public decimal GetFormattePrice(int decimals)
        {
            return Math.Round(ClosePrice, decimals);
        }
    }
}