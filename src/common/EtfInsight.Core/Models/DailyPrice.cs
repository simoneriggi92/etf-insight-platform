using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Models
{
    public sealed record DailyPrice(
        string Symbol,
        DateOnly Date,
        decimal Close,
        string Currency = "USD"
    );
}