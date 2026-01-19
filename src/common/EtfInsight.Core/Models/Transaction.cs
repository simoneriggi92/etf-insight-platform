using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Models
{
    public enum TransactionType
    {
        Buy,
        Sell
    }
    public sealed record Transaction(
        string Symbol,
        TransactionType Type,
        decimal Quantity,
        decimal Price,
        DateOnly Date,
        string Currency = "USD"
    );
}