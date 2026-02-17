using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.DataQuality.Models
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string Severity { get; set; } = "ERROR"; // ERROR, WARNING, INFO
        public string? Message { get; set; } = string.Empty;
        public decimal? CurrentValue { get; set; }
        public string? ExpectedRange { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        public static ValidationResult Success(string ruleName, string message = "")
        {
            return new ValidationResult
            {
                IsValid = true,
                RuleName = ruleName,
            };
        }

        public static ValidationResult Failure(
            string ruleName,
            string message,
            decimal? currentValue = null,
            string? expectedRange = null,
            string severity = "ERROR",
            Dictionary<string, object>? metadata = null)
        {
            return new ValidationResult
            {
                IsValid = false,
                RuleName = ruleName,
                Message = message,
                CurrentValue = currentValue,
                ExpectedRange = expectedRange,
                Severity = severity,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
        }
    }
}