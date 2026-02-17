using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using EtfInsight.Core.Services;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.Services;
using Microsoft.Extensions.Logging;

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("api/data-quality")]
    [Produces("application/json")]
    public class DataQualityController : ControllerBase
    {
        private readonly DataQualityScanner _scanner;
        private readonly IDataQualityRepository _repository;
        private readonly ILogger<DataQualityController> _logger;

        public DataQualityController(
            DataQualityScanner scanner,
            IDataQualityRepository repository,
            ILogger<DataQualityController> logger)
        {
            _scanner = scanner;
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Trigger data quality scan manually. 
        /// </summary>
        /// <returns></returns>
        [HttpPost("scan")]
        public async Task<IActionResult> Scan()
        {
            _logger.LogInformation("Manual data quality scan triggered.");

            try
            {
                var result = await _scanner.ScanRecentPricesAsync();
                return Ok(new
                {
                    success = true,
                    message = "Data quality scan completed.",
                    stats = new
                    {
                        pricesChecked = result.PricesChecked,
                        rulesExecuted = result.RulesExecuted,
                        anomaliesDetected = result.AnomaliesDetected,
                        errors = result.Errors,
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during data quality scan.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while performing the data quality scan.",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get list of unresolved anomalies
        /// </summary>
        /// <returns></returns>
        [HttpGet("anomalies")]
        public async Task<IActionResult> GetUnresolvedAnomalies()
        {
            try
            {
                var anomalies = await _repository.GetUnresolvedAnomaliesAsync();
                return Ok(new
                {
                    success = true,
                    count = anomalies.Count(),
                    anomalies = anomalies.Select(a => new
                    {
                        a.Id,
                        a.Ticker,
                        priceDate = a.PriceDate.ToString("yyyy-MM-dd"),
                        ruleName = a.RuleName,
                        severity = a.Severity,
                        currentValue = a.CurrentValue,
                        expectedRange = a.ExpectedRange,
                        message = a.Message,
                        metadata = a.Metadata,
                        detectedAt = a.DetectedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving anomalies");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get anomalies for a specific ticker
        /// </summary>
        [HttpGet("anomalies/{ticker}")]
        public async Task<IActionResult> GetAnomaliesByTicker(string ticker, [FromQuery] int days = 30)
        {
            try
            {
                var anomalies = await _repository.GetAnomaliesByTickerAsync(ticker, days);

                return Ok(new
                {
                    success = true,
                    ticker = ticker,
                    days = days,
                    count = anomalies.Count(),
                    anomalies = anomalies.Select(a => new
                    {
                        id = a.Id,
                        ticker = a.Ticker,
                        priceDate = a.PriceDate.ToString("yyyy-MM-dd"),
                        ruleName = a.RuleName,
                        severity = a.Severity,
                        currentValue = a.CurrentValue,
                        expectedRange = a.ExpectedRange,
                        message = a.Message,
                        metadata = a.Metadata,
                        detectedAt = a.DetectedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        resolved = a.Resolved
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving anomalies for ticker {Ticker}", ticker);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }
    }
}