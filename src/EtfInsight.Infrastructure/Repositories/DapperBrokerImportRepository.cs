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
    public class DapperBrokerImportRepository(IDbConnection db) : IBrokerImportRepository
    {
        public async Task CreateJobAsync(BrokerImportJob job, IEnumerable<BrokerImportJobItem> items, CancellationToken ct = default)
        {
            await db.ExecuteAsync(
                 """
                INSERT INTO broker_import_jobs
                    (id, portfolio_id, user_id, broker, status, total_files, created_at)
                VALUES
                    (@Id, @PortfolioId, @UserId, @Broker, @Status, @TotalFiles, @CreatedAt)
                """, job);

            await db.ExecuteAsync(
                """
                INSERT INTO broker_import_job_items
                    (id, job_id, portfolio_id, original_file_name, temp_file_path,
                    file_sha256, status, created_at, updated_at)
                VALUES
                    (@Id, @JobId, @PortfolioId, @OriginalFileName, @TempFilePath,
                    @FileSha256, @Status, @CreatedAt, @UpdatedAt)
                """, items);
        }

        public async Task<IReadOnlyList<BrokerImportJobItem>> GetItemsAsync(Guid jobId, CancellationToken ct = default)
        {
            var rows = await db.QueryAsync<BrokerImportJobItem>(
                """
                SELECT id, job_id AS JobId, portfolio_id AS PortfolioId,
                    original_file_name AS OriginalFileName, temp_file_path AS TempFilePath,
                    file_sha256 AS FileSha256, status, broker_reference AS BrokerReference,
                    broker_secondary_reference AS BrokerSecondaryReference, isin,
                    instrument_name AS InstrumentName, resolved_ticker AS ResolvedTicker,
                    transaction_type AS TransactionType, transaction_date AS TransactionDate,
                    settlement_date AS SettlementDate, units, price_per_unit AS PricePerUnit,
                    fees, gross_amount AS GrossAmount, currency,
                    created_transaction_id AS CreatedTransactionId, error_message AS ErrorMessage,
                    created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM broker_import_job_items
                WHERE job_id = @JobId
                ORDER BY created_at
                """,
                new { JobId = jobId });

            return rows.ToList();
        }

        public async Task<BrokerImportJob?> GetJobAsync(Guid jobId, Guid userId, CancellationToken ct = default)
        {
            return await db.QueryFirstOrDefaultAsync<BrokerImportJob>(
                """
                SELECT id, portfolio_id AS PortfolioId, user_id AS UserId, broker, status,
                    hangfire_job_id AS HangfireJobId, total_files AS TotalFiles,
                    processed_files AS ProcessedFiles, imported_files AS ImportedFiles,
                    duplicate_files AS DuplicateFiles, failed_files AS FailedFiles,
                    waiting_for_ingestion_files AS WaitingForIngestionFiles,
                    current_file_name AS CurrentFileName, current_message AS CurrentMessage,
                    error_summary AS ErrorSummary, created_at AS CreatedAt,
                    started_at AS StartedAt, completed_at AS CompletedAt
                FROM broker_import_jobs
                WHERE id = @JobId AND user_id = @UserId
                """,
                new { JobId = jobId, UserId = userId });
        }

        public async Task<IReadOnlyDictionary<string, string>> GetTickerStatusesForJobAsync(Guid jobId, CancellationToken ct = default)
        {
            var rows = await db.QueryAsync<(string ticker, string status)>(
               """
                SELECT DISTINCT m.ticker, m.status
                FROM broker_import_job_items i
                JOIN etf_metadata m ON m.ticker = i.resolved_ticker
                WHERE i.job_id = @JobId
                AND i.resolved_ticker IS NOT NULL
                """,
               new { JobId = jobId });

            return rows.ToDictionary(r => r.ticker, r => r.status);
        }

        public Task<bool> IsDocumentAlreadyImportedAsync(Guid portfolioId, string documentHash, string? brokerReference, CancellationToken ct = default)
        {
            return db.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM broker_import_job_items
                    WHERE portfolio_id = @PortfolioId
                    AND file_sha256 = @DocumentHash
                    AND (@BrokerReference IS NULL OR broker_reference = @BrokerReference)
                )
                """,
                new { PortfolioId = portfolioId, DocumentHash = documentHash, BrokerReference = brokerReference });
        }

        public async Task MarkJobCompletedAsync(Guid jobId, string finalStatus, string? errorSummary = null, CancellationToken ct = default)
        {
            await db.ExecuteAsync(
               """
                UPDATE broker_import_jobs
                SET status        = @Status::broker_import_job_status,
                    error_summary = @ErrorSummary,
                    completed_at  = NOW()
                WHERE id = @JobId
                """,
               new { JobId = jobId, Status = finalStatus, ErrorSummary = errorSummary });
        }

        public async Task UpdateItemAsync(BrokerImportJobItem item, CancellationToken ct = default)
        {
            await db.ExecuteAsync(
                """
                UPDATE broker_import_job_items
                SET status                       = @Status::broker_import_item_status,
                    broker_reference             = @BrokerReference,
                    broker_secondary_reference   = @BrokerSecondaryReference,
                    isin                         = @Isin,
                    instrument_name              = @InstrumentName,
                    resolved_ticker              = @ResolvedTicker,
                    transaction_type             = @TransactionType,
                    transaction_date             = @TransactionDate,
                    settlement_date              = @SettlementDate,
                    units                        = @Units,
                    price_per_unit               = @PricePerUnit,
                    fees                         = @Fees,
                    gross_amount                 = @GrossAmount,
                    currency                     = @Currency,
                    created_transaction_id       = @CreatedTransactionId,
                    error_message                = @ErrorMessage,
                    updated_at                   = @UpdatedAt
                WHERE id = @Id
                """, item);
        }

        public async Task UpdateJobCountersAsync(Guid jobId, CancellationToken ct = default)
        {
            // Derives all counters from the actual item statuses — no manual increment drift
            await db.ExecuteAsync(
                """
                UPDATE broker_import_jobs j
                SET processed_files             = counts.total,
                    imported_files              = counts.imported,
                    duplicate_files             = counts.duplicate,
                    failed_files                = counts.failed,
                    waiting_for_ingestion_files = counts.waiting
                FROM (
                    SELECT
                        COUNT(*)                                              AS total,
                        COUNT(*) FILTER (WHERE status = 'imported')          AS imported,
                        COUNT(*) FILTER (WHERE status = 'duplicate')         AS duplicate,
                        COUNT(*) FILTER (WHERE status = 'failed')            AS failed,
                        COUNT(*) FILTER (WHERE status = 'waiting_for_ingestion') AS waiting
                    FROM broker_import_job_items
                    WHERE job_id = @JobId
                ) counts
                WHERE j.id = @JobId
                """,
                new { JobId = jobId });
        }

        public async Task UpdateJobStatusAsync(Guid jobId, string status, string? currentFileName = null, string? currentMessage = null, CancellationToken ct = default)
        {
            await db.ExecuteAsync(
            """
                UPDATE broker_import_jobs
                SET status            = @Status::broker_import_job_status,
                    current_file_name = COALESCE(@CurrentFileName, current_file_name),
                    current_message   = COALESCE(@CurrentMessage,  current_message),
                    started_at        = CASE WHEN @Status = 'processing' AND started_at IS NULL
                                            THEN NOW() ELSE started_at END
                WHERE id = @JobId
                """,
            new { JobId = jobId, Status = status, CurrentFileName = currentFileName, CurrentMessage = currentMessage });
        }
    }
}