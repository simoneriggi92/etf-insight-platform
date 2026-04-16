using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Interfaces
{
    public interface IInstrumentResolutionService
    {
        Task<string?> ResolveTickerByIsinAsync(string isin, string? instrumentName = null, CancellationToken ct = default);
    }
}