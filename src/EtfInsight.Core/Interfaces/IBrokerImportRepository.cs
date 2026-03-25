using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Interfaces
{
    public interface IBrokerImportRepository
    {
        Task CreateJobAsync(BrokerImportJob job, IEnumerable<BrokerImportJobItem> items, CancellationToken ct = default);
        Task<BrokerImportJob?> GetJobAsync(Guid jobId, Guid userId, CancellationToken ct = default);
        Task<IReadOnlyList<BrokerImportJobItem>> GetItemsAsync(Guid jobId, CancellationToken ct = default);
        Task UpdateJobStatusAsync(Guid jobId, string status, string? currentFileName = null, string? currentMessage = null, CancellationToken ct = default);
        Task UpdateJobCountersAsync(Guid jobId, CancellationToken ct = default); // ricalcola da items
        Task MarkJobCompletedAsync(Guid jobId, string finalStatus, string? errorSummary = null, CancellationToken ct = default);
        Task UpdateItemAsync(BrokerImportJobItem item, CancellationToken ct = default);
        Task<IReadOnlyDictionary<string, string>> GetTickerStatusesForJobAsync(Guid jobId, CancellationToken ct = default);
    }
}