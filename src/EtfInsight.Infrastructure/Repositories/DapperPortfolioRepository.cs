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

        public async Task<IEnumerable<Portfolio>> GetAllPortfoliosWithTransactionsAsync()
        {
            var sql = @"
            SELECT 
                id as id,
                name as Name, 
                currency as Currency, 
                created_at as CreatedAt
            FROM portfolios ORDER BY created_at DESC;

            SELECT 
                id as Id,
                portfolio_id as PortfolioId,
                ticker as Ticker,
                transaction_date as TransactionDate,
                type as Type,
                units as Units,
                price_per_unit as PricePerUnit,
                fees as Fees
            FROM transactions ORDER BY transaction_date DESC;
            ";


            using var multi = await _db.QueryMultipleAsync(sql);

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

        public async Task<Portfolio?> GetPortfolioWithTransactionsAsync(Guid id)
        {

            var sql = @"
            SELECT 
                id as id,
                name as Name, 
                currency as Currency, 
                created_at as CreatedAt
            FROM portfolios WHERE id = @Id;

            SELECT 
                id as Id,
                portfolio_id as PortfolioId,
                ticker as Ticker,
                transaction_date as TransactionDate,
                type as Type,
                units as Units,
                price_per_unit as PricePerUnit,
                fees as Fees
            FROM transactions WHERE portfolio_id = @Id ORDER BY transaction_date DESC;
            ";

            using var multi = await _db.QueryMultipleAsync(sql, new { Id = id });

            var portfolio = await multi.ReadSingleOrDefaultAsync<Portfolio>();

            if (portfolio == null)
                return null;

            var transactions = (await multi.ReadAsync<Transaction>()).ToList();
            portfolio.Transactions = transactions;

            return portfolio;
        }
    }
}