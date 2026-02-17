using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.DataQuality.Entities;

namespace EtfInsight.DataQuality.Interfaces
{
    public interface IDataQualityRepository
    {
        Task InsertAnomalyAsync(DataAnomaly anomaly);
        Task<IEnumerable<DataAnomaly>> GetUnresolvedAnomaliesAsync();
        Task<IEnumerable<DataAnomaly>> GetAnomaliesByTickerAsync(string ticker, int days = 30);
    }
}