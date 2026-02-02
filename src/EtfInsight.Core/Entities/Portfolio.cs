using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace EtfInsight.Core.Entities
{
    public class Portfolio
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Currency Currency { get; set; } = Currency.EUR;
        public DateTime CreatedAt { get; set; }

        // Navigation property
        public List<Transaction> Transactions { get; set; } = new();
    }

    public enum Currency
    {
        USD,
        EUR,
        GBP,
        JPY
    }
}