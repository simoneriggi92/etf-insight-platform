using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public record StartBrokerImportResponse(
        Guid JobId,
        string Status,
        int TotalFiles,
        string Message
    );

    public record ImportJobItemResult(
        string FileName,
        string Status,
        string? Isin,
        string? ResolvedTicker,
        string? ErrorMessage
    );

    public record ImportJobStatusResponse(
        Guid JobId,
        string Status,
        int TotalFiles,
        int ProcessedFiles,
        int ImportedFiles,
        int DuplicateFiles,
        int FailedFiles,
        int WaitingForIngestionFiles,
        string? CurrentFileName,
        string? CurrentMessage,
        string? ErrorSummary,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        IReadOnlyList<ImportJobItemResult> RecentItems,
        IReadOnlyDictionary<string, string> TickerIngestionStatuses
    );
}