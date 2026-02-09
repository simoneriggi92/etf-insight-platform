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

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("health")]
    [Produces("application/json")]
    public class HealthCheckController : ControllerBase
    {
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

        [HttpGet("embed-test")]
        public async Task<IActionResult> TestEmbedding([FromServices] IEmbeddingGenerator embeddingService)
        {
            try
            {
                var embedding = await embeddingService.GenerateEmbeddingAsync("This is a test");
                return Ok(new
                {
                    success = true,
                    dimensions = embedding.Length,
                    sample = embedding[..5] // First 5 values
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}