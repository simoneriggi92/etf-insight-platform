using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EtfInsight.Core.Configuration;
using EtfInsight.Core.Services;
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

        public async Task<float[]> GenerateEmbeddingAsync(string input)
        {
            try
            {
                var request = new OllamaEmbeddingRequest
                {
                    Model = _aiSettings.EmbeddingModel,
                    Prompt = input
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(request, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogInformation("Generating embedding for text of length {Length} using model {Model}",
                    input.Length, _aiSettings.EmbeddingModel);

                var response = await _httpClient.PostAsync("/api/embeddings", content);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(jsonResponse, jsonOptions);

                if (result?.Embedding == null || result.Embedding.Length == 0)
                {
                    throw new InvalidOperationException("Ollama returned empty embedding");
                }
                _logger.LogInformation("Successfully generated embedding with {Dimensions} dimensions",
                                  result.Embedding.Length);

                return result.Embedding;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to connect to Ollama at {Url}. Is Ollama running?",
              _aiSettings.OllamaUrl);
                throw new InvalidOperationException(
                    $"Cannot connect to Ollama at {_aiSettings.OllamaUrl}. Ensure Ollama is running on your host machine.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding");
                throw;
            }
        }

        public class OllamaEmbeddingResponse
        {
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }

    internal class OllamaEmbeddingRequest
    {
        public required string Model { get; set; }
        public required string Prompt { get; set; }
    }
}