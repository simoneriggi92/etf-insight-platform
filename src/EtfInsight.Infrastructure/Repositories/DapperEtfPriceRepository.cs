using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Infrastructure.Repositories
{
    public class DapperEtfPriceRepository : EtfInsight.Core.Interfaces.IEtfPriceRepository
    {
        private readonly IDbConnection _db;

        public DapperEtfPriceRepository(IDbConnection db)
        {
            _db = db;
        }

        public Task<IEnumerable<EtfPrice>> GetPricesByTickersAsync(IEnumerable<string> tickers, DateOnly from, DateOnly to)
        {
            var query = @"
                SELECT 
                    ticker as Ticker,
                    price_date as PriceDate,
                    open_price as OpenPrice,
                    high_price as HighPrice,
                    low_price as LowPrice,
                    close_price as ClosePrice,
                    volume as Volume
                FROM etf_prices
                WHERE ticker = ANY(@Tickers) AND price_date BETWEEN @FromDate AND @ToDate
                ORDER BY ticker, price_date ASC";

            return _db.QueryAsync<EtfPrice>(query, new
            {
                Tickers = tickers.ToArray(),
                FromDate = from.ToDateTime(TimeOnly.MinValue),
                ToDate = to.ToDateTime(TimeOnly.MinValue)
            });
        }
    }
}