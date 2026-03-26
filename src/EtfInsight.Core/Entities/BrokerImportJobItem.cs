using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Entities
{
    public record BrokerImportJobItem
    {
        public Guid Id { get; init; }
        public Guid JobId { get; init; }
        public Guid PortfolioId { get; init; }
        public string OriginalFileName { get; init; } = "";
        public string TempFilePath { get; init; } = "";
        public string FileSha256 { get; init; } = "";
        public string Status { get; init; } = "queued";
        public string? BrokerReference { get; init; }
        public string? BrokerSecondaryReference { get; init; }
        public string? Isin { get; init; }
        public string? InstrumentName { get; init; }
        public string? ResolvedTicker { get; init; }
        public string? TransactionType { get; init; }
        public DateOnly? TransactionDate { get; init; }
        public DateOnly? SettlementDate { get; init; }
        public decimal? Units { get; init; }
        public decimal? PricePerUnit { get; init; }
        public decimal? Fees { get; init; }
        public decimal? GrossAmount { get; init; }
        public string? Currency { get; init; }
        public Guid? CreatedTransactionId { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}