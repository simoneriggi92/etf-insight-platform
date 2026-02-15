using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.Entities;

namespace EtfInsight.Infrastructure.Repositories
{
    public class DataQualityEtfPriceRepository : EtfInsight.DataQuality.Interfaces.IEtfPriceRepository
    {
        private readonly IDbConnection _db;

        public DataQualityEtfPriceRepository(IDbConnection db)
        {
            _db = db;
        }

        public async Task<EtfPrice?> GetPreviousPriceAsync(string ticker, DateOnly beforeDate)
        {
            var query = @"
                SELECT 
                    ticker as Ticker,
                    price_date as PriceDate,
                    open_price as OpenPrice,
                    high_price as HighPrice,
                    low_price as LowPrice,
                    close_price as ClosePrice,
                    volume as Volume,
                    currency as Currency
                FROM etf_prices
                WHERE ticker = @Ticker 
                    AND price_date < @BeforeDate
                ORDER BY price_date DESC
                LIMIT 1";

            return await _db.QueryFirstOrDefaultAsync<EtfPrice>(query, new
            {
                Ticker = ticker,
                BeforeDate = beforeDate.ToDateTime(TimeOnly.MinValue)
            });
        }

        public async Task<IEnumerable<EtfPrice>> GetPricesByTickersAsync(IEnumerable<string> tickers, DateOnly from, DateOnly to)
        {
            var query = @"
                SELECT 
                    ticker as Ticker,
                    price_date as PriceDate,
                    open_price as OpenPrice,
                    high_price as HighPrice,
                    low_price as LowPrice,
                    close_price as ClosePrice,
                    volume as Volume,
                    currency as Currency
                FROM etf_prices
                WHERE ticker = ANY(@Tickers)
                    AND price_date BETWEEN @FromDate AND @ToDate
                ORDER BY ticker, price_date";

            return await _db.QueryAsync<EtfPrice>(query, new
            {
                Tickers = tickers,
                FromDate = from.ToDateTime(TimeOnly.MinValue),
                ToDate = to.ToDateTime(TimeOnly.MinValue)
            });
        }

        public async Task<IEnumerable<EtfPrice>> GetRecentPricesAsync(int limitPerTicker = 2)
        {
            var query = @"
                WITH ranked_prices AS (
                    SELECT 
                        ticker,
                        price_date,
                        open_price,
                        high_price,
                        low_price,
                        close_price,
                        volume,
                        currency,
                        ROW_NUMBER() OVER (PARTITION BY ticker ORDER BY price_date DESC) as rn
                    FROM etf_prices
                )
                SELECT 
                    ticker as Ticker,
                    price_date as PriceDate,
                    open_price as OpenPrice,
                    high_price as HighPrice,
                    low_price as LowPrice,
                    close_price as ClosePrice,
                    volume as Volume,
                    currency as Currency
                FROM ranked_prices
                WHERE rn <= @Limit
                ORDER BY ticker, price_date DESC";

            return await _db.QueryAsync<EtfPrice>(query, new { Limit = limitPerTicker });
        }
    }
}