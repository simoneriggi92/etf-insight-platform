
using System.Threading;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EtfInsight.Infrastructure.Repositories;

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("api/search")]
    [Produces("application/json")]
    public class SemanticSearchController : ControllerBase
    {
        private readonly ILogger<SemanticSearchController> _logger;
        private readonly IEmbeddingGenerator _embeddingGenerator;
        private readonly ISemanticSearchRepository _semanticSearchRepository;

        public SemanticSearchController(
            ILogger<SemanticSearchController> logger,
            IEmbeddingGenerator embeddingGenerator,
            ISemanticSearchRepository semanticSearchRepository)
        {
            _logger = logger;
            _embeddingGenerator = embeddingGenerator;
            _semanticSearchRepository = semanticSearchRepository;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query(
            [FromBody] SearchRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { error = "Query text cannot be empty" });
            }

            _logger.LogInformation("Received semantic search query: {Query}", request.Query);
            
            try
            {

                // Generate embedding for the query
                var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(
                    request.Query,
                    ct);

                // Search for similar documents
                var results = await _semanticSearchRepository.SearchAsync(
                    queryEmbedding,
                    request.Limit ?? 5,
                    ct: ct);

                return Ok(new
                {
                    success = true,
                    query = request.Query,
                    results = results.Select(r => new
                    {
                        ticker = r.Ticker,
                        description = r.Content,
                        similarity = Math.Round(r.Similarity, 4)
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public sealed record SearchRequest
        {
            public string Query { get; set; } = string.Empty;
            public int? Limit { get; set; }
        }
    }
}