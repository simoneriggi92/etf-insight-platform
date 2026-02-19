using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EtfInsight.Core.Services;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.Configuration;
using EtfInsight.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Dapper;


namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("health")]
    [Produces("application/json")]
    public class HealthCheckController : ControllerBase
    {
        private record EtfDocumentSample(string Ticker, int Dims);
        private readonly ILogger<HealthCheckController> _logger;

        public HealthCheckController(ILogger<HealthCheckController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow
            });
        }

        [HttpGet("embedding-test")]
        public async Task<IActionResult> TestEmbedding(
            [FromServices] IEmbeddingGenerator embeddingService,
            [FromServices] IOptions<AISettings> settings)
        {
            try
            {
                var config = settings.Value;
                var embedding = await embeddingService.GenerateEmbeddingAsync("test");

                var result = new
                {
                    configuredModel = config.EmbeddingModel,
                    configuredUrl = config.OllamaUrl,
                    configuredDimensions = config.EmbeddingDimensions,
                    actualEmbeddingDimensions = embedding.Length,
                    expectedDimensions = 768,
                    isCorrect = embedding.Length == 768,
                    status = embedding.Length == 768 ? "PASS" : "FAIL"
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding test failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("db-check")]
        public async Task<IActionResult> CheckDatabase(
            [FromServices] System.Data.IDbConnection connection)
        {
            try
            {
                var count = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<int>(
                    connection, "SELECT COUNT(*) FROM etf_documents");

                var sample = await Dapper.SqlMapper.QueryAsync<EtfDocumentSample>(
                    connection,
                    "SELECT ticker, vector_dims(embedding) as dims FROM etf_documents LIMIT 5");

                return Ok(new
                {
                    status = "✅ Connected",
                    documentCount = count,
                    sampleDimensions = sample
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}