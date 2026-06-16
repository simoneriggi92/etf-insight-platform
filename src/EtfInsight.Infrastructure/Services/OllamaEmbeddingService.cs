using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EtfInsight.Core.Configuration;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EtfInsight.Infrastructure.Services
{
    public class OllamaEmbeddingService : IEmbeddingGenerator
    {
        private readonly HttpClient _httpClient;
        private readonly AISettings _aiSettings;
        private readonly ILogger<OllamaEmbeddingService> _logger;

        public OllamaEmbeddingService(
            IOptions<AISettings> aiSettings,
            ILogger<OllamaEmbeddingService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _aiSettings = aiSettings.Value;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("Ollama");
            _httpClient.BaseAddress = new Uri(_aiSettings.OllamaUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<float[]> GenerateEmbeddingAsync(
            string input,
            CancellationToken ct = default)
        {
            try
            {
                var request = new OllamaEmbedRequest
                {
                    Model = _aiSettings.EmbeddingModel,
                    Input = input
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(request, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogInformation(
                    "Generating embedding using model {Model}",
                    _aiSettings.EmbeddingModel);

                var response = await _httpClient.PostAsync("/api/embed", content, ct);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(jsonResponse, jsonOptions);

                if (result?.Embeddings is not { Length: > 0 } || result.Embeddings[0].Length == 0)
                    throw new InvalidOperationException("Ollama returned empty embedding");

                _logger.LogInformation(
                    "Generated embedding with {Dimensions} dimensions",
                    result.Embeddings[0].Length);

                return result.Embeddings[0];
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to connect to Ollama at {Url}. Is Ollama running?",
                    _aiSettings.OllamaUrl);

                throw new InvalidOperationException(
                    $"Cannot connect to Ollama at {_aiSettings.OllamaUrl}. Ensure Ollama is running on your host machine.",
                    ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding");
                throw;
            }
        }
    }

    internal sealed class OllamaEmbedRequest
    {
        public required string Model { get; set; }
        public required string Input { get; set; }
    }

    internal sealed class OllamaEmbedResponse
    {
        public float[][] Embeddings { get; set; } = Array.Empty<float[]>();
    }
}
