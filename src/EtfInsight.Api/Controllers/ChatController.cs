using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EtfInsight.Api.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using EtfInsight.Core.Services;

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

        [HttpPost]
        public async Task<IActionResult> Ask(
            [FromBody] ChatRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest(new { error = "Question cannot be empty" });

            _logger.LogInformation("Received question: {Question}", request.Question);

            try
            {
                var userId = HttpContext.GetGuestId();
                var response = await _chatService.AskAiAsync(request.Question, userId, ct);

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

        [HttpPost("stream")]
        public async Task StreamAsync([FromBody] ChatRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new { error = "Question cannot be empty" }, ct);
                return;
            }

            var userId = HttpContext.GetGuestId();

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            try
            {
                var result = await _chatService.AskStreamAsync(request.Question, userId, ct);

                await foreach (var token in result.Tokens)
                {
                    var escaped = JsonSerializer.Serialize(token);
                    await Response.WriteAsync($"data: {escaped}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }

                var sourcesJson = JsonSerializer.Serialize(result.Sources.Select(s => new
                {
                    ticker = s.Ticker,
                    similarity = Math.Round(s.Similarity, 3),
                    excerpt = s.Content.Length > 100 ? s.Content[..100] + "..." : s.Content
                }));
                await Response.WriteAsync($"event: sources\ndata: {sourcesJson}\n\n", ct);
                await Response.WriteAsync("data: [DONE]\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — normal, no error to log
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streaming chat failed");
                await Response.WriteAsync("data: [ERROR]\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }

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
