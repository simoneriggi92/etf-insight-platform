using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Entities
{
    public record BrokerImportJob
    {
        public Guid Id { get; init; }
        public Guid PortfolioId { get; init; }
        public Guid UserId { get; init; }
        public string Broker { get; init; } = "";
        public string Status { get; set; } = "queued";
        public string? HangfireJobId { get; set; }
        public int TotalFiles { get; init; }
        public int ProcessedFiles { get; set; }
        public int ImportedFiles { get; set; }
        public int DuplicateFiles { get; set; }
        public int FailedFiles { get; set; }
        public int WaitingForIngestionFiles { get; set; }
        public string? CurrentFileName { get; set; }
        public string? CurrentMessage { get; set; }
        public string? ErrorSummary { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}