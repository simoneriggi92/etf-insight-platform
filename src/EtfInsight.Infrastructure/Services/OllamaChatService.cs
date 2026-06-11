using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Services;
using Microsoft.Extensions.Logging;
using EtfInsight.Core.Configuration;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Infrastructure.Services
{
    public class OllamaChatService : IChatService
    {
        private readonly HttpClient _httpClient;
        private readonly AISettings _aiSettings;
        private readonly ILogger<OllamaChatService> _logger;
        private readonly IEmbeddingGenerator _embeddingGenerator;
        private readonly ISemanticSearchRepository _semanticSearchRepository;
        private readonly IPortfolioAnalyticsService _portfolioAnalyticsService;
        private readonly IPortfolioRepository _portfolioRepository;

        public OllamaChatService(
            IOptions<AISettings> aiSettings,
            ILogger<OllamaChatService> logger,
            IHttpClientFactory httpClientFactory,
            IEmbeddingGenerator embeddingGenerator,
            ISemanticSearchRepository semanticSearchRepository, 
            IPortfolioRepository portfolioRepository,
            IPortfolioAnalyticsService portfolioAnalyticsService)
        {
            _aiSettings = aiSettings.Value;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("Ollama");
            _httpClient.BaseAddress = new Uri(_aiSettings.OllamaUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _embeddingGenerator = embeddingGenerator;
            _semanticSearchRepository = semanticSearchRepository;
            _portfolioRepository = portfolioRepository;
            _portfolioAnalyticsService = portfolioAnalyticsService;
        }


        public async Task<ChatResponseDto> AskAiAsync(
            string question, 
            Guid userId, 
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(question);
            
            _logger.LogInformation("Processing question: {Question}", question);

            // Step 1: Generate embedding for the question
            var questionEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(
                question,
                ct);

            // Step 2: Perform semantic search to find relevant documents
            var searchResults = await _semanticSearchRepository .SearchAsync(
                    questionEmbedding,
                    limit: _aiSettings.MaxContextChunks,
                    _aiSettings.MinSimilarityThreshold,
                    ct);
            
            var relevantDocs = searchResults.ToList();

            _logger.LogInformation("Found {Count} relevant documents",
                relevantDocs.Count());
            
            string? portfolioContext = null;
            if (userId != Guid.Empty)
            {
                portfolioContext = await BuildPortfolioContextAsync(userId, ct);
            }

            // Step 3: Build the augmented prompt
            var augmentedPrompt = BuildAugmentedPrompt(
                question,
                relevantDocs,
                portfolioContext
                );

            // Step 4: Send the augmented prompt to Ollama to generate a response
            var answer = await GenerateResponseAsync(augmentedPrompt, ct);
            
            _logger.LogInformation("Generated answer with {Length} characters", answer.Length);

            return new ChatResponseDto
            {
                Answer = answer,
                Sources = relevantDocs.Select(r => new SearchResultDto
                {
                    Ticker = r.Ticker,
                    Content = r.Content,
                    Similarity = r.Similarity,
                }).ToList()
            };
        }

        private async Task<string> BuildPortfolioContextAsync(
            Guid userId, 
            CancellationToken ct)
        {
            var portfolios = await _portfolioRepository.GetAllPortfoliosWithTransactionsAsync(userId);
            var portfolio = portfolios.FirstOrDefault();
            if (portfolio == null)
            {
                return string.Empty;
            }
            
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var oneYearAgo = today.AddYears(-1);
            var analytics = await _portfolioAnalyticsService.GetPortfolioAnalyticsAsync(portfolio.Id, oneYearAgo, today);
            
            if(analytics.CurrentTotalValue == 0)
            {
                return string.Empty;
            }

            return $"PORTFOLIO SNAPSHOT (pre-calculated, do NOT recalculate these values): " +
                   $"- Total Value: €{analytics.CurrentTotalValue:N2} " +
                   $"- Total Invested: €{analytics.TotalInvested:N2}" +
                   $"- Absolute P&L: €{analytics.AbsolutePnL:N2}" +
                   $"- Simple Return: {analytics.SimpleReturn:P2}" +
                   $"- Max Drawdown: {analytics.MaxDrawdown:P2}";
        }

        private string BuildAugmentedPrompt(
            string question,
            List<Core.DTOs.SearchResult> relevantDocs,
            string? portfolioContext
            )
        {
            var contextBuilder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(portfolioContext))
            {
                contextBuilder.AppendLine(portfolioContext);
                contextBuilder.AppendLine();
            }
            
            contextBuilder.AppendLine("AVAILABLE ETF CONTEXT:");
            contextBuilder.AppendLine();

            for (int i = 0; i < relevantDocs.Count; i++)
            {
                var doc = relevantDocs[i];
                contextBuilder.AppendLine($"[ETF {i + 1}] {doc.Ticker}:");
                contextBuilder.AppendLine(doc.Content);
                contextBuilder.AppendLine($"(Relevance Score: {doc.Similarity:P1})");
                contextBuilder.AppendLine();
            }

            var prompt = $"You're an AI financial assistant expert in ETFs.{contextBuilder}" +
                         $"INSTRUCTIONS:" +
                         $" - Answer the question using ONLY the provided context." +
                         $"-  NEVER calculate or estimate financial metrics. Use only the pre-calculated values from the PORTFOLIO SNAPSHOT." + 
                         $" - If the answer cannot generated by the available information, reply: \"I don't have enough information to answer this question.\"." +
                         $" - Be accurate and concise in your answers." +
                         $" - Mention the source ETF(s) in your answer if applicable and relevant." +
                         $" - Answer in the same language as the USER QUESTION." +
                         
                         $"USER QUESTION: {question}" +
                         $"ANSWER:                    ";

            return prompt;
        }

        private async Task<string> GenerateResponseAsync(
            string prompt,
            CancellationToken ct = default)
        {
            var request = new OllamaGenerateRequest
            {
                Model = _aiSettings.ChatModel,
                Prompt = prompt,
                Stream = false,
                Temperature = 0.1
            };

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(
                request,
                jsonOptions);
            
            var content = new StringContent(
                json, 
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Calling Ollama /api/generate endpoint with model {Model}",
                _aiSettings.ChatModel);

            var response = await _httpClient.PostAsync(
                "/api/generate",
                content,
                ct);
            
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response
                .Content
                .ReadAsStringAsync(ct);
            
            var result = JsonSerializer.Deserialize<OllamaChatResponse>(
                jsonResponse, 
                jsonOptions);

            if (string.IsNullOrWhiteSpace(result?.Response))
            {
                throw new InvalidOperationException("Ollama returned empty response");
            }

            return result
                .Response
                .Trim();
        }
    }


    internal sealed class OllamaGenerateRequest
    {
        public required string Model { get; set; }
        public required string Prompt { get; set; }
        public bool Stream { get; set; }
        public double Temperature { get; set; }
    }

    internal sealed class OllamaChatResponse
    {
        public string? Response { get; set; }
        public bool Done { get; set; }
    }
}