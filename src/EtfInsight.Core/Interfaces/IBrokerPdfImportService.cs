using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.DTOs.Summaries;
using Microsoft.AspNetCore.Http;

namespace EtfInsight.Core.Interfaces
{
    public interface IBrokerPdfImportService
    {
        Task<StartBrokerImportResponse> StartImportAsync(
            Guid portfolioId,
            Guid userId,
            IReadOnlyList<IFormFile> files,
            CancellationToken ct = default);

        Task<ImportJobStatusResponse?> GetJobStatusAsync(
            Guid jobId,
            Guid userId,
            CancellationToken ct = default);

        /// <summary>
        /// Performed by Hangfire worker, processes the PDF files for the given import job, extracts the relevant data, and updates the job status accordingly.
        /// </summary>
        /// <param name="importJobId"></param>
        /// <param name="userId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ProcessTradeRepublicImportAsync(
            Guid importJobId,
            Guid userId,
            CancellationToken ct = default);

        /// <summary>
        /// Performs cleanup of temporary folders created during the import process that are older than a certain threshold (e.g., 24 hours) to prevent disk space issues. This is intended to be run as a scheduled background job (e.g., daily via Hangfire).
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CleanupStaleTempFoldersAsync(CancellationToken ct = default);
        
        Task<IReadOnlyList<ImportJobSummaryResponse>?> GetJobsByPortfolioIdAsync(Guid portfolioId, Guid userId, CancellationToken ct = default);
        
        Task<ImportJobDetailResponse?> GetJobDetailAsync(Guid jobId, Guid userId, CancellationToken ct = default);

    }
}