using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Api.Extensions;
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

        public ChatController(
            ILogger<ChatController> logger,
            IChatService chatService)
        {
            _logger = logger;
            _chatService = chatService;
        }

        /// <summary>
        /// Endpoint to ask a question to the AI. T
        /// he system will use RAG to find relevant information from the database and provide an answer along with the sources used.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> Ask(
            [FromBody] ChatRequest request,
            CancellationToken ct)
        {
            
            if(string.IsNullOrWhiteSpace(request.Question))
            {
                return BadRequest(new { error = "Question cannot be empty" });
            }
            
            _logger.LogInformation("Received question: {Question}", request.Question);
            
            try
            {
                var userId = HttpContext.GetGuestId();
                
                // Get AI answer using RAG
                var response = await _chatService.AskAiAsync(
                    request.Question,
                    userId,
                    ct);

             
                return Ok(new
                {
                    question = request.Question,
                    answer = response.Answer,
                    sources = response.Sources.Select(s => new
                    {
                        ticker = s.Ticker,
                        similarity = Math.Round(s.Similarity, 3),
                        excerpt = s.Content.Length > 100 ? s.Content[..100] + "..." : s.Content
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

    public sealed class ChatRequest
    {
        public string Question { get; set; } = string.Empty;
    }
}
