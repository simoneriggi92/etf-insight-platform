
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
                    // ── ETFs ──────────────────────────────────────────────────────────────
                    ["SPY"] = "SPDR S&P 500 ETF Trust. Replica le 500 maggiori aziende USA per capitalizzazione. Il benchmark di riferimento del mercato azionario americano, altissima liquidità.",

                    ["QQQ"] = "Invesco QQQ Trust. Replica il NASDAQ-100, i 100 titoli non-finanziari più capitalizzati del NASDAQ. Alta concentrazione in tech: Apple, Microsoft, NVIDIA, Amazon.",

                    ["VTI"] = "Vanguard Total Stock Market ETF. Copre l'intero mercato azionario USA: large, mid e small cap. Massima diversificazione domestica con oltre 3500 aziende.",

                    ["VGT"] = "Vanguard Information Technology ETF. Settore tecnologico americano puro: software, hardware e semiconduttori. Alta crescita ma concentrato nel comparto IT.",

                    ["BND"] = "Vanguard Total Bond Market ETF. Obbligazionario USA investment grade diversificato: Treasury, corporate e mortgage-backed securities. Stabilità e reddito fisso.",

                    ["GLD"] = "SPDR Gold Shares. Replica il prezzo dell'oro fisico. Bene rifugio per eccellenza, ideale per proteggere il portafoglio dall'inflazione e dalla volatilità di mercato.",

                    ["SCHD"] = "Schwab US Dividend Equity ETF. Investe in aziende USA con solida storia di dividendi e fondamentali robusti. Ottimo per chi cerca reddito passivo e qualità.",

                    ["AGG"] = "iShares Core US Aggregate Bond ETF. Obbligazionario USA aggregato: government, corporate e securitized. Alternativa diversificata a BND per il reddito fisso americano.",

                    // ── Equities ──────────────────────────────────────────────────────────
                    ["MSFT"] = "Microsoft Corporation. Colosso del software e cloud computing (Azure). Tra le aziende più capitalizzate al mondo, con ricavi ricorrenti e forte posizione nell'AI enterprise.",

                    ["NVDA"] = "NVIDIA Corporation. Leader nei chip grafici (GPU) e nell'intelligenza artificiale. L'azienda più rilevante dell'era AI: i suoi processori alimentano i principali modelli LLM.",

                    ["AAPL"] = "Apple Inc. La più grande azienda al mondo per capitalizzazione. Ecosistema chiuso (iPhone, Mac, Services) con margini elevatissimi e base clienti fidelizzata.",

                    ["SMCI"] = "Super Micro Computer Inc. Produttore di server ad alte prestazioni e infrastrutture AI. Beneficia direttamente della domanda di data center per l'addestramento di modelli AI.",

                    ["TSLA"] = "Tesla Inc. Pioneer dei veicoli elettrici e dell'energia rinnovabile. Alta volatilità, ma posizionata su automazione, robotica e storage energetico oltre all'EV."
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