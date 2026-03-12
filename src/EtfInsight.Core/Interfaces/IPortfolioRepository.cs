using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;

namespace EtfInsight.Core.Interfaces
{
    public interface IPortfolioRepository
    {
        Task<Portfolio?> GetPortfolioWithTransactionsAsync(Guid id, Guid userId = default);

        Task<IEnumerable<Portfolio>> GetAllPortfoliosWithTransactionsAsync(Guid userId);
    }
}