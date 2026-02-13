using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;

namespace EtfInsight.DataQuality.Interfaces
{
    public interface IEtfPriceRepository
    {
        Task<IEnumerable<EtfPrice>> GetRecentPricesAsync(int limitPerTicker = 2);
        Task<EtfPrice?> GetPreviousPriceAsync(string ticker, DateOnly beforeDate);
    }
}