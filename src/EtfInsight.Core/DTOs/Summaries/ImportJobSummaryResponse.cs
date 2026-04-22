namespace EtfInsight.Core.DTOs.Summaries;

public record ImportJobSummary
{
    public Guid JobId { get; init; }
    public string Broker { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int ImportedFiles { get; init; }
    public int DuplicateFiles { get; init; }
    public int FailedFiles { get; init; }
    public string? ErrorSummary { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt  { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record ImportJobSummaryResponse(
    int WaitingForIngestionFiles,
    int ProcessedFiles
) : ImportJobSummary;

public sealed record ImportJobItemDetail(
    string FileName,
    string Status,
    string? Isin,
    string? InstrumentName,
    string? ResolvedTicker,
    string? TransactionType,
    DateOnly? TransactionDate,
    DateOnly? SettlementDate,
    decimal? Units,
    decimal? PricePerUnit,
    decimal? Fees,
    decimal? GrossAmount,
    string? Currency,
    string? BrokerReference,
    string? BrokerSecondaryReference,
    string? ErrorMessage
);

public sealed record ImportJobDetailResponse
(
    IReadOnlyList<ImportJobItemDetail> Items
): ImportJobSummary;