
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Entities;

namespace EtfInsight.Core.Interfaces
{
    public interface IEtfRepository
    {
        // DTO operations (for API responses) - only expose what's needed
        Task<IEnumerable<SymbolSummaryDto>> GetSymbolSummaryAsync();
        Task<LatestSymbolPriceDto?> GetLatestEtfBySymbolAsync(string ticker);
        Task<List<Etf>> GetPriceHistoryAsync(string ticker, DateTime fromDate, DateTime toDate);
        Task<IEnumerable<Etf>> GetPriceHistoryAsync(IEnumerable<string> tickers, DateTime fromDate, DateTime toDate);
    }
}