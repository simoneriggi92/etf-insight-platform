using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.DataQuality.DTOs
{
    /// <summary>
    /// Data Transfer Object for Data Anomalies, used for database interactions and API responses.
    /// </summary>
    public class DataAnomalyDto
    {
        public Guid Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public DateOnly PriceDate { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public decimal? CurrentValue { get; set; }
        public string? ExpectedRange { get; set; }
        public string? Message { get; set; }
        public string? Metadata { get; set; } // JSON
        public DateTime DetectedAt { get; set; }
        public bool Resolved { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
    }
}