
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

        [HttpPost("seed")]
        public async Task<IActionResult> SeedData()
        {
            try
            {
                var etfDescriptions = new Dictionary<string, string>
                {
                    ["SWDA.MI"] = "Fondo globale che investe in aziende dei paesi sviluppati, molto diversificato. Replica l'indice MSCI World con esposizione a USA, Europa e Giappone.",

                    ["VWCE.DE"] = "ETF azionario globale che copre sia mercati sviluppati che emergenti. Include oltre 3000 aziende da tutto il mondo per massima diversificazione.",

                    ["QDVE.DE"] = "Settore tecnologico USA, focus su software e hardware. Investe nelle principali aziende tech americane con alta crescita.",

                    ["EIMI.MI"] = "Mercati emergenti, focus su Cina, India e Brasile. Alta volatilità ma potenziale di crescita elevato nei paesi in via di sviluppo.",

                    ["EUNL.DE"] = "ETF azionario Europa, diversificato su tutti i settori. Include grandi aziende europee da vari paesi dell'Eurozona e UK.",

                    ["IS3N.DE"] = "Obbligazionario corporate investment grade EUR. Basso rischio, fornisce reddito stabile attraverso bond di aziende europee solide.",

                    ["EUNA.DE"] = "Obbligazionario governativo europeo. Molto sicuro, investe in titoli di stato dell'Eurozona con rating elevato.",

                    ["CSPX.MI"] = "S&P 500, le 500 maggiori aziende USA. Esposizione concentrata al mercato azionario americano, alta qualità.",

                    ["VUSA.MI"] = "Replica l'indice S&P 500 con basse commissioni. Alternativa a CSPX per investire nelle grandi aziende americane.",

                    ["AGGH.MI"] = "Obbligazionario globale diversificato investment grade. Mix di bond governativi e corporate da tutto il mondo per stabilità."
                };

                var savedCount = 0;
                var errors = new List<string>();

                foreach (var (ticker, description) in etfDescriptions)
                {
                    try
                    {
                        _logger.LogInformation("Generating embedding for {Ticker}", ticker);

                        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(description);

                        await _semanticSearchRepository.SaveEmbeddingAsync(ticker, description, embedding);

                        savedCount++;

                        _logger.LogInformation("Saved {Ticker} with {Dimensions} dimensions", ticker, embedding.Length);
                    }
                    catch (System.Exception ex)
                    {
                        var error = $"{ticker}: {ex.Message}";
                        errors.Add(error);
                        _logger.LogError(ex, "Failed to process {Ticker}", ticker);
                    }
                }

                return Ok(new
                {
                    success = true,
                    Message = $"Seeded {savedCount}/{etfDescriptions.Count} ETF descriptions with embeddings.",
                    savedCount,
                    totalCount = etfDescriptions.Count,
                    Errors = errors.Any() ? errors : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Seed operation failed");
                return StatusCode(500, "Failed to seed semantic search data");
            }
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] SearchRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return BadRequest(new { error = "Query text cannot be empty" });
                }

                _logger.LogInformation("Received semantic search query: {Query}", request.Query);

                // Generate embedding for the query
                var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(request.Query);

                // Search for similar documents
                var results = await _semanticSearchRepository.SearchAsync(
                    queryEmbedding,
                    request.Limit ?? 5);

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

        public class SearchRequest
        {
            public string Query { get; set; } = string.Empty;
            public int? Limit { get; set; }
        }
    }
}