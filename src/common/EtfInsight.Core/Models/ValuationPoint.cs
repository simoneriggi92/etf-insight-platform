using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Models
{
    public sealed record ValuationPoint(
        DateOnly Date,
        decimal TotalValue,
        decimal NetFlow,
        decimal CumNetFlow,
        decimal PnL,
        decimal Return
    );
}