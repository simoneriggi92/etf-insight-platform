using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Infrastructure.Data
{
    public class PostgresRepository : IEtfRepository
    {

        private readonly IDbConnection _db;

        public PostgresRepository(IDbConnection db)
        {
            _db = db;
        }

        public Task<Etf?> GetEtfByIdAsync(Guid id)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Etf?>> GetEtfsBySymbolAsync(string symbol)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Etf>> GetAllEtfsAsync()
        {
            // This method should return actual ETF entities
            // For now, this is a placeholder implementation
            throw new NotImplementedException("GetAllEtfsAsync should return ETF entities, use GetSymbolSummaryAsync for aggregated data");
        }

        public async Task<IEnumerable<SymbolSummaryDto>> GetSymbolSummaryAsync()
        {
            var query = @" 
                SELECT DISTINCT 
                    symbol as Symbol,
                    COUNT(*) as DataPoints,
                    MIN(price_date) as FirstDate,     
                    MAX(price_date) as LastDate
                FROM etf_prices 
                GROUP BY symbol
                ORDER BY symbol";

            var results = await _db.QueryAsync<SymbolSummary>(query);

            return results.Select(item => new SymbolSummaryDto
            {
                Symbol = item.Symbol,
                DataPoints = item.DataPoints,
                FirstDate = item.FirstDate,
                LastDate = item.LastDate
            }).ToList();
        }

        public Task AddEtfAsync(Etf etf)
        {
            throw new NotImplementedException();
        }

        public Task UpdateEtfAsync(Etf etf)
        {
            throw new NotImplementedException();
        }

        public Task DeleteEtfAsync(Guid id)
        {
            throw new NotImplementedException();
        }

        public async Task<LatestSymbolPriceDto?> GetLatestEtfBySymbolAsync(string symbol)
        {
            var query = @"
                SELECT 
                    symbol as Symbol, 
                    price_date as PriceDate, 
                    open_price as OpenPrice, 
                    high_price as HighPrice, 
                    low_price as LowPrice, 
                    close_price as ClosePrice, 
                    volume as Volume
                FROM etf_prices
                WHERE symbol = @Symbol
                ORDER BY price_date DESC
                LIMIT 1";

            var result = await _db.QueryAsync<Etf?>(query, new { Symbol = symbol });

            return new LatestSymbolPriceDto
            {
                Symbol = result.FirstOrDefault()?.Symbol ?? string.Empty,
                PriceDate = result.FirstOrDefault()?.PriceDate ?? DateOnly.MinValue,
                OpenPrice = result.FirstOrDefault()?.OpenPrice ?? 0,
                ClosePrice = result.FirstOrDefault()?.ClosePrice ?? 0,
                HighPrice = result.FirstOrDefault()?.HighPrice ?? 0,
                LowPrice = result.FirstOrDefault()?.LowPrice ?? 0,
                Volume = result.FirstOrDefault()?.Volume ?? 0
            };
        }

        public Task<List<SymbolSummaryDto>> GetPriceHistoryAsync(string symbol, DateTime fromDate, DateTime toDate)
        {
            throw new NotImplementedException();
        }
    }
}