using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.Models;

namespace EtfInsight.DataQuality.Rules
{
    public class FlashCrashRule : IDataQualityRule
    {
        private readonly DataQualitySettings _settings;

        public FlashCrashRule(Microsoft.Extensions.Options.IOptions<DataQualitySettings> settings)
        {
            _settings = settings.Value;
        }
        public string RuleName => "FlashCrashRule";

        public Task<ValidationResult> ValidateAsync(EtfPrice etfPrice, EtfPrice? previousPrice)
        {
            // Skip if there's no previous price to compare against
            if (previousPrice == null || previousPrice.ClosePrice <= 0)
            {
                return Task.FromResult(ValidationResult.Success(RuleName)); // No previous price to compare
            }

            var priceDropPercent = Math.Abs(
                ((previousPrice.ClosePrice - etfPrice.ClosePrice) / previousPrice.ClosePrice) * 100
            );

            if (priceDropPercent >= (decimal)_settings.FlashCrashThresholdPercent)
            {
                return Task.FromResult(ValidationResult.Failure(
                    ruleName: RuleName,
                    message: $"Price changed {priceDropPercent:F2}% exceeds threshold of {_settings.FlashCrashThresholdPercent}%",
                    currentValue: etfPrice.ClosePrice,
                    expectedRange: $"No drop greater than {_settings.FlashCrashThresholdPercent}%",
                    severity: "WARNING",
                    metadata: new Dictionary<string, object>
                    {
                        { "previous_price", previousPrice.ClosePrice },
                        { "previous_date", previousPrice.PriceDate.ToString("yyyy-MM-dd") },
                        { "change_percent", priceDropPercent },
                        { "threshold_percent", _settings.FlashCrashThresholdPercent }
                    }
                ));
            }

            return Task.FromResult(ValidationResult.Success(RuleName));
        }
    }
}