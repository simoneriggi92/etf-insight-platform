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
    public class DapperPortfolioRepository : IPortfolioRepository
    {
        private readonly IDbConnection _db;

        public DapperPortfolioRepository(IDbConnection db)
        {
            _db = db;
        }

        private async Task SetTenantContextAsync(Guid userId)
        {
            // true = setting is local to the current transaction / connection
            await _db.ExecuteAsync("SELECT set_config('app.user_id', @UserId, true)",
            new { UserId = userId.ToString() });
        }

        public async Task<IEnumerable<Portfolio>> GetAllPortfoliosWithTransactionsAsync(Guid userId)
        {
            await SetTenantContextAsync(userId);

            var sql = @"
                SELECT id, name, currency, created_at as CreatedAt
                FROM portfolios
                WHERE user_id = @UserId
                ORDER BY created_at DESC;

                SELECT t.id, t.portfolio_id as PortfolioId, t.ticker, t.transaction_date as TransactionDate,
                    t.type, t.units, t.price_per_unit as PricePerUnit, t.fees
                FROM transactions t
                INNER JOIN portfolios p ON p.id = t.portfolio_id
                WHERE p.user_id = @UserId
                ORDER BY t.transaction_date DESC;";

            using var multi = await _db.QueryMultipleAsync(sql, new { UserId = userId });

            var portfolios = (await multi.ReadAsync<Portfolio>()).ToList();
            var transactions = (await multi.ReadAsync<Transaction>()).ToList();

            // Map transactions to their respective portfolios
            var portfolioDict = portfolios.ToDictionary(p => p.Id);
            foreach (var tx in transactions)
            {
                if (portfolioDict.TryGetValue(tx.PortfolioId, out var portfolio))
                {
                    portfolio.Transactions.Add(tx);
                }
            }

            return portfolios;
        }

        public async Task<Portfolio?> GetPortfolioWithTransactionsAsync(Guid id, Guid userId = default)
        {
            await SetTenantContextAsync(userId);

            // When userId is Guid.Empty (internal calls, e.g. from PortfolioAnalyticsService),
            // skip the user_id filter so analytics can access any portfolio.
            var userFilter = userId == Guid.Empty ? "" : "AND user_id = @UserId";

            var sql = $@"
            SELECT 
                id as id,
                name as Name, 
                currency as Currency, 
                created_at as CreatedAt
            FROM portfolios WHERE id = @Id
            {userFilter}
            ;

            SELECT 
                t.id as Id,
                t.portfolio_id as PortfolioId,
                t.ticker as Ticker,
                t.transaction_date as TransactionDate,
                t.type as Type,
                t.units as Units,
                t.price_per_unit as PricePerUnit,
                t.fees as Fees
            FROM transactions t
            INNER JOIN portfolios p ON p.id = t.portfolio_id
            WHERE t.portfolio_id = @Id
            {userFilter}
            ORDER BY t.transaction_date DESC;
            ";

            using var multi = await _db.QueryMultipleAsync(sql, new { Id = id, UserId = userId });

            var portfolio = await multi.ReadSingleOrDefaultAsync<Portfolio>();

            if (portfolio == null)
                return null;

            var transactions = (await multi.ReadAsync<Transaction>()).ToList();
            portfolio.Transactions = transactions;

            return portfolio;
        }
    }
}