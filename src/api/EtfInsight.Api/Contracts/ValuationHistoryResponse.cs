using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Models;

namespace EtfInsight.Api.Contracts
{
    public sealed record ValuationHistoryResponse(
        int PortfolioId,
        DateOnly From,
        DateOnly To,
        int Count,
        IReadOnlyList<ValuationPoint> Points
    );
}
