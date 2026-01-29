using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Models;

namespace EtfInsight.Core.Interfaces
{
    public interface IEtfRepository
    {
        Task<Etf?> GetEtfByIdAsync(Guid id);
        Task AddEtfAsync(Etf etf);
        Task UpdateEtfAsync(Etf etf);
        Task DeleteEtfAsync(Guid id);

        // DTO operations (for API responses) - only expose what's needed
        Task<IEnumerable<Etf?>> GetEtfsBySymbolAsync(string symbol);
        Task<IEnumerable<Etf>> GetAllEtfsAsync();
        Task<IEnumerable<SymbolSummaryDto>> GetSymbolSummaryAsync();
        Task<LatestSymbolPriceDto?> GetLatestEtfBySymbolAsync(string symbol);
        Task<List<SymbolSummaryDto>> GetPriceHistoryAsync(string symbol, DateTime fromDate, DateTime toDate);
    }
}