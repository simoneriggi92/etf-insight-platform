using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.Models;
using EtfInsight.Core.Entities;

namespace EtfInsight.DataQuality.Rules
{
    public class NegativePriceRule : IDataQualityRule
    {
        public string RuleName => "NegativePriceRule";

        public Task<ValidationResult> ValidateAsync(EtfPrice etfPrice, EtfPrice? previousPrice)
        {
            if (etfPrice.ClosePrice <= 0)
            {
                return Task.FromResult(ValidationResult.Failure(
                    ruleName: RuleName,
                    message: $"Price must be greater than 0. Found {etfPrice.ClosePrice}.",
                    currentValue: etfPrice.ClosePrice,
                    expectedRange: "ClosePrice >= 0",
                    severity: "ERROR",
                    metadata: new Dictionary<string, object>
                    {
                        { "open_price", etfPrice.OpenPrice },
                        { "high_price", etfPrice.HighPrice },
                        { "low_price", etfPrice.LowPrice }
                    }
                ));
            }

            return Task.FromResult(ValidationResult.Success(RuleName));
        }
    }
}