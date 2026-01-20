using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using EtfInsight.Core.Models;

namespace EtfInsight.Api.Repositories
{
    public interface IValuationRepository
    {
        Task<IReadOnlyList<DateOnly>> GetTradingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
        Task<IReadOnlyList<Transaction>> GetTransactionsAsync(int portfolioId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
        Task<IReadOnlyList<DailyPrice>> GetPricesAsync(IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    }
}