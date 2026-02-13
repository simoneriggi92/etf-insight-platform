using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;
using EtfInsight.DataQuality.Entities;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.Models;
using Microsoft.Extensions.Logging;

namespace EtfInsight.DataQuality.Services
{
    public class DataQualityScanner
    {
        private readonly IEnumerable<IDataQualityRule> _rules;
        private readonly ILogger<DataQualityScanner> _logger;
        private readonly IDataQualityRepository _anomalyRepository;
        private readonly IEtfPriceRepository _priceRepository;

        public DataQualityScanner(
            IEnumerable<IDataQualityRule> rules,
            ILogger<DataQualityScanner> logger,
            IDataQualityRepository anomalyRepository,
            IEtfPriceRepository priceRepository)
        {
            _rules = rules;
            _logger = logger;
            _anomalyRepository = anomalyRepository;
            _priceRepository = priceRepository;
        }

        public async Task<ScanResult> ScanRecentPricesAsync()
        {
            _logger.LogInformation("Starting data quality scan....");

            var recentPrices = await _priceRepository.GetRecentPricesAsync();
            var result = new ScanResult();

            foreach (var price in recentPrices)
            {
                await ScanPriceAsync(price, result);
            }

            _logger.LogInformation(
                "Scan completed. Checked: {Checked}, Anomalies Detected: {Anomalies}",
                result.PricesChecked,
                result.AnomaliesDetected
            );

            return result;
        }

        private async Task ScanPriceAsync(EtfPrice price, ScanResult? result = null)
        {
            result ??= new ScanResult();
            result.PricesChecked++;

            // Get previous price for comparison (if needed by rules)
            var previousPrice = await _priceRepository.GetPreviousPriceAsync(
                price.Ticker,
                price.PriceDate
            );

            foreach (var rule in _rules)
            {
                try
                {
                    var validationResult = await rule.ValidateAsync(price, previousPrice);
                    result.RulesExecuted++;

                    if (!validationResult.IsValid)
                    {
                        _logger.LogWarning(
                         "Anomaly detected: {Rule} for {Ticker} on {Date}. {Message}",
                         validationResult.RuleName,
                         price.Ticker,
                         price.PriceDate,
                         validationResult.Message
                     );

                        // Insert anomaly into repository
                        var anomaly = new DataAnomaly
                        {
                            Id = Guid.NewGuid(),
                            Ticker = price.Ticker,
                            PriceDate = price.PriceDate,
                            RuleName = validationResult.RuleName,
                            Severity = validationResult.Severity,
                            CurrentValue = validationResult.CurrentValue,
                            ExpectedRange = validationResult.ExpectedRange,
                            Message = validationResult.Message,
                            Metadata = validationResult.Metadata != null
                           ? JsonSerializer.Serialize(validationResult.Metadata)
                           : null,
                            DetectedAt = DateTime.UtcNow,
                            Resolved = false
                        };

                        await _anomalyRepository.InsertAnomalyAsync(anomaly);
                        result.AnomaliesDetected++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing rule {Rule} for {Ticker}", rule.RuleName, price.Ticker);
                    result.Errors++;
                }
            }
        }
    }
}