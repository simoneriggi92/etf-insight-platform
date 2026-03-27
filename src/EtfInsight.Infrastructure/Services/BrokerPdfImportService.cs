using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Hangfire;


namespace EtfInsight.Infrastructure.Services
{
    public class BrokerPdfImportService(
        IBrokerImportRepository brokerImportRepository,
        IPortfolioRepository portfolioRepository,
        ILogger<BrokerPdfImportService> logger,
        IConfiguration config) : IBrokerPdfImportService
    {
        private readonly string _tempRoot = config["BrokerImport:TempRoot"] ?? Path.Combine(Path.GetTempPath(), "broker-imports");

        /// <summary>
        /// Start import PDF files - called by HTTP request 
        /// </summary>
        /// <param name="portfolioId"></param>
        /// <param name="userId"></param>
        /// <param name="files"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<StartBrokerImportResponse> StartImportAsync(
            Guid portfolioId, Guid userId, IReadOnlyList<IFormFile> files, CancellationToken ct = default)
        {
            // Ownership and existence check - includes userId, unlike the existing CSV pattern
            var portfolio = await portfolioRepository.GetByIdAndUserAsync(portfolioId, userId, ct);
            if (portfolio is null)
            {
                return new StartBrokerImportResponse(Guid.Empty, "not_found", 0, "Portfolio not found.");
            }

            var jobId = Guid.NewGuid();
            var jobFolder = Path.Combine(_tempRoot, jobId.ToString());
            Directory.CreateDirectory(jobFolder);

            var jobItems = new List<BrokerImportJobItem>();

            foreach (var file in files)
            {
                var safeName = Path.GetFileName(file.FileName);
                var destPath = Path.Combine(jobFolder, safeName);

                string sha256;
                await using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    await file.CopyToAsync(fs, ct);
                }
                await using (var fs = new FileStream(destPath, FileMode.Open, FileAccess.Read))
                {
                    sha256 = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
                }

                jobItems.Add(new BrokerImportJobItem
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    PortfolioId = portfolioId,
                    OriginalFileName = safeName,
                    TempFilePath = destPath,
                    FileSha256 = sha256,
                    Status = "queued",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }

            var job = new BrokerImportJob
            {
                Id = jobId,
                PortfolioId = portfolioId,
                UserId = userId,
                Broker = "trade_republic", // for now we only support Trade Republic, but this can be extended in the future with some PDF content-based detection
                Status = "queued",
                TotalFiles = files.Count,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await brokerImportRepository.CreateJobAsync(
                job,
                jobItems,
                ct
            );

            // Enqueue background processing (e.g. with Hangfire, or just trigger it directly here)
            var hangfireJob = BackgroundJob.Enqueue<IBrokerPdfImportService>(
                service => service.ProcessTradeRepublicImportAsync(jobId, userId, CancellationToken.None));

            logger.LogInformation("Broker PDF import job {JobId} enqueued (Hangfire {HangfireId})", jobId, hangfireJob);

            return new StartBrokerImportResponse(
                jobId,
                "queued",
                files.Count,
                $"{files.Count} file(s) queued for processing."
            );
        }

        /// <summary>
        /// Get status of an import job - called by HTTP request, polled by frontend
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="userId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<ImportJobStatusResponse> GetJobStatusAsync(Guid jobId, Guid userId, CancellationToken ct = default)
        {
            var job = await brokerImportRepository.GetJobAsync(jobId, userId, ct);
            if (job is null)
            {
                return null;
            }

            var items = await brokerImportRepository.GetItemsAsync(jobId, ct);
            var tickerStatuses = await brokerImportRepository.GetTickerStatusesForJobAsync(jobId, ct);

            var recentItems = items
                .OrderByDescending(i => i.UpdatedAt)
                .Take(10)
                .Select(i => new ImportJobItemResult(
                    i.OriginalFileName,
                    i.Status,
                    i.Isin,
                    i.ResolvedTicker,
                    i.ErrorMessage))
                .ToList();

            return new ImportJobStatusResponse(
                job.Id,
                job.Status,
                job.TotalFiles,
                job.ProcessedFiles,
                job.ImportedFiles,
                job.DuplicateFiles,
                job.FailedFiles,
                job.WaitingForIngestionFiles,
                job.CurrentFileName,
                job.CurrentMessage,
                job.ErrorSummary,
                job.CreatedAt,
                job.StartedAt,
                job.CompletedAt,
                recentItems,
                tickerStatuses);
        }

        /// <summary>
        /// Hangfire background job to process the imported PDF files - one job per import
        /// </summary>
        /// <param name="importJobId"></param>
        /// <param name="userId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [AutomaticRetry(Attempts = 0)] // no retry: state is in DB, retry would double-process
        public async Task ProcessTradeRepublicImportAsync(Guid importJobId, Guid userId, CancellationToken ct = default)
        {
            await brokerImportRepository.UpdateJobStatusAsync(importJobId, "processing");
            var items = await brokerImportRepository.GetItemsAsync(importJobId, ct);

            foreach (var item in items)
            {
                await brokerImportRepository.UpdateJobStatusAsync(importJobId, "processing",
                item.OriginalFileName,
                "Processing file...");

                //TODO Phase 3: PDF parsing + instrument resolution
                var updated = item with
                {
                    Status = "failed",
                    ErrorMessage = "PDF parsing not implemented yet.",
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                await brokerImportRepository.UpdateItemAsync(updated, ct);
            }

            await brokerImportRepository.UpdateJobCountersAsync(importJobId, ct); // update counters based on item statuses

            var finalItems = await brokerImportRepository.GetItemsAsync(importJobId, ct);
            var allDone = finalItems.All(i => i.Status is "imported" or "duplicate" or "unsupported" or "failed");
            var anyWaiting = finalItems.Any(i => i.Status == "waiting_for_ingestion");

            var finalStatus = anyWaiting ? "waiting_for_ingestion"
            : finalItems.Any(i => i.Status == "failed") ? "completed_with_errors"
            : "completed";

            await brokerImportRepository.MarkJobCompletedAsync(importJobId, finalStatus);
            logger.LogInformation("Import job {JobId} finished with status {Status}", importJobId, finalStatus);
        }

    }
}