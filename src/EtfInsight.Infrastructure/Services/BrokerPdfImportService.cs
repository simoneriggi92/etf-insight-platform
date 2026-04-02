using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EtfInsight.Core.Entities;
using Hangfire;
using Microsoft.AspNetCore.Http;
using EtfInsight.Infrastructure.Services.BrokerPdf;

namespace EtfInsight.Infrastructure.Services
{
    public class BrokerPdfImportService(
        IBrokerImportRepository brokerImportRepository,
        IPortfolioRepository portfolioRepository,
        IPdfTextExtractor pdfTextExtractor,
        ITradeRepublicParser tradeRepublicParser,
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
        [AutomaticRetry(Attempts = 0)]
        public async Task ProcessTradeRepublicImportAsync(Guid importJobId, Guid userId, CancellationToken ct = default)
        {
            await brokerImportRepository.UpdateJobStatusAsync(importJobId, "processing");
            var items = await brokerImportRepository.GetItemsAsync(importJobId, ct);

            static string Truncate(string s) => s.Length > 500 ? s[..500] : s;

            foreach (var item in items)
            {
                await brokerImportRepository.UpdateJobStatusAsync(importJobId, "processing",
                    item.OriginalFileName, "Processing file...");

                // Step 1
                await brokerImportRepository.UpdateItemAsync(
                    item with { Status = "parsing", UpdatedAt = DateTimeOffset.UtcNow }, ct);

                // Step 2
                PdfExtractionResult extraction;
                try
                {
                    extraction = await pdfTextExtractor.ExtractTextAsync(item.TempFilePath, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "PDF extraction failed for {FileName} in job {JobId}", item.OriginalFileName, importJobId);
                    await brokerImportRepository.UpdateItemAsync(
                        item with
                        {
                            Status = "failed",
                            ErrorMessage = Truncate($"extraction: {ex.Message}"),
                            UpdatedAt = DateTimeOffset.UtcNow
                        }, ct);
                    continue;
                }

                // Steps 3–4
                var normalized = TradeRepublicTextNormalizer.Normalize(extraction.RawText);
                var kind = TradeRepublicDocumentKindDetector.Detect(extraction.Title, normalized);

                // Step 5
                if (kind is not (TradeRepublicDocumentKind.BuyConfirmation
                    or TradeRepublicDocumentKind.SellConfirmation
                    or TradeRepublicDocumentKind.SavingsPlanExecution))
                {
                    await brokerImportRepository.UpdateItemAsync(
                        item with
                        {
                            Status = "unsupported",
                            ErrorMessage = Truncate($"Document kind not supported in V1: {kind}"),
                            UpdatedAt = DateTimeOffset.UtcNow
                        }, ct);
                    continue;
                }

                // Step 6
                var parseResult = tradeRepublicParser.Parse(extraction);

                // Step 7
                if (parseResult is TradeRepublicParserResult.Failure failure)
                {
                    await brokerImportRepository.UpdateItemAsync(
                        item with
                        {
                            Status = "failed",
                            ErrorMessage = Truncate($"{failure.Stage}: {failure.Reason}"),
                            UpdatedAt = DateTimeOffset.UtcNow
                        }, ct);
                    continue;
                }

                // Step 8
                if (parseResult is TradeRepublicParserResult.Unsupported unsupported)
                {
                    await brokerImportRepository.UpdateItemAsync(
                        item with
                        {
                            Status = "unsupported",
                            ErrorMessage = Truncate(unsupported.Reason),
                            UpdatedAt = DateTimeOffset.UtcNow
                        }, ct);
                    continue;
                }

                var parsed = ((TradeRepublicParserResult.Success)parseResult).Transaction;

                // Step 9
                var isDuplicate = await brokerImportRepository.IsDocumentAlreadyImportedAsync(
                    item.PortfolioId, item.FileSha256, parsed.BrokerReference, ct);

                // Step 10
                if (isDuplicate)
                {
                    await brokerImportRepository.UpdateItemAsync(
                        item with
                        {
                            Status = "duplicate",
                            Isin = parsed.Isin,
                            BrokerReference = parsed.BrokerReference,
                            UpdatedAt = DateTimeOffset.UtcNow
                        }, ct);
                    continue;
                }

                // Step 11 — all parsed fields persisted; Phase 4 instrument resolution picks up here
                await brokerImportRepository.UpdateItemAsync(
                    item with
                    {
                        Status = "parsed",
                        BrokerReference = parsed.BrokerReference,
                        BrokerSecondaryReference = parsed.BrokerSecondaryReference,
                        Isin = parsed.Isin,
                        InstrumentName = parsed.InstrumentName,
                        TransactionType = parsed.TransactionType,
                        TransactionDate = parsed.TransactionDate,
                        SettlementDate = parsed.SettlementDate,
                        Units = parsed.Units,
                        PricePerUnit = parsed.PricePerUnit,
                        Fees = parsed.Fees,
                        GrossAmount = parsed.GrossAmount,
                        Currency = parsed.Currency,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, ct);
            }

            await brokerImportRepository.UpdateJobCountersAsync(importJobId, ct);

            var finalItems = await brokerImportRepository.GetItemsAsync(importJobId, ct);
            var anyWaiting = finalItems.Any(i => i.Status == "waiting_for_ingestion");
            var anyParsedPendingPhase4 = finalItems.Any(i => i.Status == "parsed");

            var finalStatus = anyWaiting ? "waiting_for_ingestion"
                : anyParsedPendingPhase4 || finalItems.Any(i => i.Status == "failed")
                    ? "completed_with_errors"
                : "completed";

            await brokerImportRepository.MarkJobCompletedAsync(importJobId, finalStatus);
            logger.LogInformation("Import job {JobId} finished with status {Status}", importJobId, finalStatus);
        }
    }
}
