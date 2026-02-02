using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EtfInsight.Core.Entities
{
    public class Transaction
    {
        public Guid Id { get; set; }
        public Guid PortfolioId { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public DateOnly TransactionDate { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TransactionType Type { get; set; }
        public decimal PricePerUnit { get; set; }
        public decimal Units { get; set; }
        public decimal Fees { get; set; }
    }

    public enum TransactionType
    {
        Buy,
        Sell,
        Deposit,
        Withdraw
    }
}