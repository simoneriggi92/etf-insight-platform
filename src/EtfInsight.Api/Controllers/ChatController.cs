using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using EtfInsight.Core.Services;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Produces("application/json")]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        private readonly IChatService _chatService;
        private readonly IEmbeddingGenerator _embeddingGenerator;
        private readonly ISemanticSearchRepository _semanticSearchRepository;

        public ChatController(
            ILogger<ChatController> logger,
            IChatService chatService,
            IEmbeddingGenerator embeddingGenerator,
            ISemanticSearchRepository semanticSearchRepository)
        {
            _logger = logger;
            _chatService = chatService;
            _embeddingGenerator = embeddingGenerator;
            _semanticSearchRepository = semanticSearchRepository;
        }

        /// <summary>
        /// Endpoint to ask a question to the AI. The system will use RAG to find relevant information from the database and provide an answer along with the sources used.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                {
                    return BadRequest(new { error = "Question cannot be empty" });
                }

                _logger.LogInformation("Received question: {Question}", request.Question);

                // Get AI answer using RAG
                var answer = await _chatService.AskAiAsync(request.Question);

                // Also get the sources for transparency
                var questionEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(request.Question);
                var sources = await _semanticSearchRepository.SearchAsync(questionEmbedding, limit: 5);

                return Ok(new
                {
                    question = request.Question,
                    answer = answer,
                    sources = sources.Select(r => new
                    {
                        ticker = r.Ticker,
                        similarity = Math.Round(r.Similarity, 3),
                        excerpt = r.Content.Length > 100
                            ? r.Content.Substring(0, 100) + "..."
                            : r.Content
                    }),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat request failed");
                return StatusCode(500, new
                {
                    error = "Error during the processing of the request. Make sure Ollama is running and the database is accessible.",
                    details = ex.Message
                });
            }
        }

        /// <summary>
        /// Get suggested questions
        /// </summary>
        [HttpGet("suggestions")]
        public IActionResult GetSuggestions()
        {
            var suggestions = new[]
            {
                "Quali ETF sono più adatti per investire in tecnologia USA?",
                "Dimmi gli ETF obbligazionari più sicuri",
                "Voglio investire nei mercati emergenti, cosa consigli?",
                "Qual è la differenza tra SWDA e VWCE?",
                "Quali ETF hanno esposizione all'Europa?",
                "Consigliami ETF per un portafoglio difensivo"
            };

            return Ok(new { suggestions });
        }
    }

    public class ChatRequest
    {
        public string Question { get; set; } = string.Empty;
    }
}
