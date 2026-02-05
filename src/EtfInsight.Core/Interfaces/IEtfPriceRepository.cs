using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;

namespace EtfInsight.Core.Interfaces
{
    public interface IEtfPriceRepository
    {
        Task<IEnumerable<EtfPrice>> GetPricesByTickersAsync(IEnumerable<string> tickers, DateOnly from, DateOnly to);
    }
}