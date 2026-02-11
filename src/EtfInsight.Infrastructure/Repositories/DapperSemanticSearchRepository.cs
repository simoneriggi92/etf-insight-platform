using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EtfInsight.Infrastructure.Repositories
{
    public class DapperSemanticSearchRepository : ISemanticSearchRepository
    {
        private readonly IDbConnection _connection;
        private readonly ILogger<DapperSemanticSearchRepository> _logger;

        public DapperSemanticSearchRepository(
            IDbConnection connection,
            ILogger<DapperSemanticSearchRepository> logger)
        {
            _connection = connection;
            _logger = logger;
        }

        public async Task SaveEmbeddingAsync(string ticker, string content, float[] embedding)
        {
            try
            {
                _logger.LogInformation("Saving embedding for ticker {Ticker}", ticker);

                // Use InvariantCulture to ensure decimal points (.) not commas (,)
                var embeddingString = $"[{string.Join(",", embedding.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]";

                var sql = @"
                    INSERT INTO etf_documents (ticker, content, embedding, metadata, is_mandatory)
                    VALUES (@Ticker, @Content, @Embedding::vector, @Metadata::jsonb, @IsMandatory)
                    ON CONFLICT (ticker) 
                    DO UPDATE SET 
                        content = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        metadata = EXCLUDED.metadata,
                        created_at = NOW()";

                var parameters = new
                {
                    Ticker = ticker,
                    Content = content,
                    Embedding = embeddingString,
                    Metadata = "{\"source\": \"manual_seed\", \"version\": \"1.0\"}",
                    IsMandatory = false
                };

                await _connection.ExecuteAsync(sql, parameters);

                _logger.LogInformation("Successfully saved embedding for {Ticker}", ticker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save embedding for ticker {Ticker}", ticker);
                throw;
            }
        }

        public async Task<IEnumerable<SearchResult>> SearchAsync(float[] queryEmbedding, int limit = 5)
        {
            try
            {
                _logger.LogInformation("Performing semantic search with limit {Limit}", limit);

                var sql = @"
                    SELECT 
                        ticker,
                        content,
                        1 - (embedding <=> @QueryEmbedding::vector) AS similarity
                    FROM etf_documents
                    ORDER BY embedding <=> @QueryEmbedding::vector
                    LIMIT @Limit";

                // Use InvariantCulture to ensure decimal points (.) not commas (,)
                var parameters = new
                {
                    QueryEmbedding = $"[{string.Join(",", queryEmbedding.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]",
                    Limit = limit
                };

                var results = await _connection.QueryAsync<SearchResult>(sql, parameters);

                _logger.LogInformation("Semantic search returned {Count} results", results.Count());

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform semantic search");
                throw;
            }
        }
    }
}