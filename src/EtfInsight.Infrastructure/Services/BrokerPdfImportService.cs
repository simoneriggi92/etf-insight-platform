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
        IInstrumentResolutionService instrumentResolutionService,
        IIngestionService ingestionService,
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
                "broker-imports", // specify the queue for better isolation and to avoid blocking other jobs
                service => service.ProcessTradeRepublicImportAsync(jobId, userId, CancellationToken.None));

            logger.LogInformation(
               "Broker PDF import job {JobId} for portfolio {PortfolioId} enqueued as Hangfire job {HangfireJobId}",
               jobId, portfolioId, hangfireJob);

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
        public async Task<ImportJobStatusResponse?> GetJobStatusAsync(Guid jobId, Guid userId, CancellationToken ct = default)
        {
            var job = await brokerImportRepository.GetJobAsync(jobId, userId, ct);
            if (job is null)
            {
                return null;
            }

            var items = await brokerImportRepository.GetItemsAsync(jobId, ct);
            var tickerStatuses = await brokerImportRepository.GetTickerStatusesForJobAsync(jobId, ct);

            if (job.Status == "waiting_for_ingestion")
            {
                var waitingItems = items
                    .Where(i => i.Status == "waiting_for_ingestion")
                    .ToList();

                var allTerminal = waitingItems.Count > 0 && waitingItems.All(i =>
                    i.ResolvedTicker is not null
                    && tickerStatuses.TryGetValue(i.ResolvedTicker, out var s)
                    && s is "ready" or "error");

                if (allTerminal)
                {
                    foreach (var w in waitingItems)
                    {
                        await brokerImportRepository.UpdateItemAsync(
                            w with { Status = "imported", UpdatedAt = DateTimeOffset.UtcNow }, ct);
                    }

                    await brokerImportRepository.UpdateJobCountersAsync(jobId, ct);

                    items = await brokerImportRepository.GetItemsAsync(jobId, ct);

                    var terminalStatus = items.Any(i => i.Status is "failed" or "unresolved_instrument")
                        ? "completed_with_errors"
                        : "completed";

                    await brokerImportRepository.MarkJobCompletedAsync(jobId, terminalStatus);
                    DeleteJobTempFolder(jobId);

                    job = await brokerImportRepository.GetJobAsync(jobId, userId, ct) ?? job;
                    logger.LogInformation(
                        "Import job {JobId} for portfolio {PortfolioId} auto-transitioned from waiting_for_ingestion to {FinalStatus}",
                        jobId, job.PortfolioId, terminalStatus);
                }
            }

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
            var portfolioId = items.FirstOrDefault()?.PortfolioId ?? Guid.Empty;


            foreach (var item in items)
            {
                await brokerImportRepository.UpdateJobStatusAsync(importJobId, "processing",
                    item.OriginalFileName, "Processing file...");

                await brokerImportRepository.UpdateItemAsync(
                    item with { Status = "parsing", UpdatedAt = DateTimeOffset.UtcNow }, ct);

                PdfExtractionResult extraction;
                try
                {
                    extraction = await pdfTextExtractor.ExtractTextAsync(item.TempFilePath, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "PDF extraction failed for item {ItemId} ({FileName}) in job {JobId} for portfolio {PortfolioId}",
                        item.Id, item.OriginalFileName, importJobId, portfolioId);

                    await UpdateItemAsync(
                        item,
                        "failed",
                        $"extraction: {ex.Message}",
                        ct);

                    continue;
                }

                var normalized = TradeRepublicTextNormalizer.Normalize(extraction.RawText);
                var kind = TradeRepublicDocumentKindDetector.Detect(extraction.Title, normalized);

                if (kind is not (TradeRepublicDocumentKind.BuyConfirmation
                    or TradeRepublicDocumentKind.SellConfirmation
                    or TradeRepublicDocumentKind.SavingsPlanExecution))
                {
                    await UpdateItemAsync(
                        item,
                        "unsupported",
                        $"Document kind not supported in V1: {kind}",
                        ct);

                    continue;
                }

                var parseResult = tradeRepublicParser.Parse(extraction);

                if (parseResult is TradeRepublicParserResult.Failure failure)
                {
                    await UpdateItemAsync(
                        item,
                        "failed",
                        $"{failure.Stage}: {failure.Reason}",
                        ct);

                    continue;
                }

                if (parseResult is TradeRepublicParserResult.Unsupported unsupported)
                {
                    await UpdateItemAsync(
                        item,
                        "unsupported",
                        unsupported.Reason,
                        ct);

                    continue;
                }

                var parsed = ((TradeRepublicParserResult.Success)parseResult).Transaction;

                var isDuplicate = await brokerImportRepository.IsDocumentAlreadyImportedAsync(
                    item.PortfolioId, item.FileSha256, parsed.BrokerReference, ct);

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

                var parsedItem = item with
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
                };
                await brokerImportRepository.UpdateItemAsync(parsedItem, ct);

                var ticker = await instrumentResolutionService.ResolveTickerByIsinAsync(
                    parsed.Isin, parsed.InstrumentName, ct);

                if (ticker is null)
                {
                    await UpdateItemAsync(
                        parsedItem,
                        "unresolved_instrument",
                        $"ISIN {parsed.Isin} could not be resolved to a known ticker",
                        ct);

                    continue;
                }

                var transactionId = await brokerImportRepository.InsertBrokerTransactionAsync(
                    new BrokerTransactionInsertRequest(
                        PortfolioId: item.PortfolioId,
                        Ticker: ticker,
                        TransactionType: parsed.TransactionType,
                        TransactionDate: parsed.TransactionDate,
                        Units: parsed.Units,
                        PricePerUnit: parsed.PricePerUnit,
                        Fees: parsed.Fees,
                        SourceBroker: "trade_republic",
                        SourceReference: parsed.BrokerReference,
                        SourceSecondaryReference: parsed.BrokerSecondaryReference,
                        SourceDocumentHash: item.FileSha256,
                        SourceIsin: parsed.Isin,
                        TradeCurrency: parsed.Currency),
                    ct);

                var ingestionStatus = await ingestionService.EnsureTickerReadyAsync(
                    ticker, parsed.Isin, parsed.InstrumentName, ct);

                var finalItemStatus = ingestionStatus == IngestionStatus.Ingesting
                    ? "waiting_for_ingestion"
                    : "imported";

                await brokerImportRepository.UpdateItemAsync(
                    parsedItem with
                    {
                        Status = finalItemStatus,
                        ResolvedTicker = ticker,
                        CreatedTransactionId = transactionId,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, ct);

                logger.LogInformation(
                    "Item {ItemId} in job {JobId} for portfolio {PortfolioId}: isin={Isin}, ticker={Ticker}, itemStatus={ItemStatus}",
                    item.Id, importJobId, portfolioId, parsed.Isin, ticker, finalItemStatus);
            }

            await brokerImportRepository.UpdateJobCountersAsync(importJobId, ct);

            var finalItems = await brokerImportRepository.GetItemsAsync(importJobId, ct);
            var anyWaiting = finalItems.Any(i => i.Status == "waiting_for_ingestion");
            var anyFailed = finalItems.Any(i => i.Status is "failed" or "unresolved_instrument");

            var finalStatus = anyWaiting ? "waiting_for_ingestion"
                : anyFailed ? "completed_with_errors"
                : "completed";

            await brokerImportRepository.MarkJobCompletedAsync(importJobId, finalStatus);

            if (!anyWaiting)
            {
                DeleteJobTempFolder(importJobId);
            }

            logger.LogInformation(
                "Import job {JobId} for portfolio {PortfolioId} finished with status {FinalStatus}",
                importJobId, portfolioId, finalStatus);
        }

        private async Task UpdateItemAsync(BrokerImportJobItem item, string status, string? errorMessage, CancellationToken ct)
        {
            static string Truncate(string s) => s.Length > 500 ? s[..500] : s;

            await brokerImportRepository.UpdateItemAsync(
                item with
                {
                    Status = status,
                    ErrorMessage = errorMessage is not null ? Truncate(errorMessage) : null,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct);
        }

        private void DeleteJobTempFolder(Guid jobId)
        {
            var folder = Path.Combine(_tempRoot, jobId.ToString());
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp folder for job {JobId}", jobId);
            }
        }

        public Task CleanupStaleTempFoldersAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(_tempRoot))
                return Task.CompletedTask;

            var threshold = DateTime.UtcNow.AddHours(-24);

            foreach (var folder in Directory.GetDirectories(_tempRoot))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(folder) < threshold)
                        Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to delete stale broker import temp folder {Folder}", folder);
                }
            }

            return Task.CompletedTask;
        }
    }
}
