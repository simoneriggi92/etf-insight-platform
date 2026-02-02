
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Entities;

namespace EtfInsight.Core.Interfaces
{
    public interface IEtfRepository
    {
        // DTO operations (for API responses) - only expose what's needed
        Task<IEnumerable<SymbolSummaryDto>> GetSymbolSummaryAsync();
        Task<LatestSymbolPriceDto?> GetLatestEtfBySymbolAsync(string symbol);
        Task<List<SymbolSummaryDto>> GetPriceHistoryAsync(string symbol, DateTime fromDate, DateTime toDate);
    }
}