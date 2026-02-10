using System;
using System.Collections.Generic;
using System.Data;
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
                    embedding = $"[{string.Join(",", embedding)}]",
                    Metadata = "{\"source\": \"manual_seed\", \"version\": \"1.0\"}",
                    IsMandatory = false
                };

                await _connection.ExecuteAsync(sql, parameters);

                _logger.LogInformation("Saved embedding for ticker {Ticker} with {Dimensions} dimensions",
                  ticker, embedding.Length);
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
                var sql = @"
                    SELECT 
                        ticker,
                        content,
                        1 - (embedding <=> @QueryEmbedding::vector) AS similarity
                    FROM etf_documents
                    ORDER BY embedding <=> @QueryEmbedding::vector
                    LIMIT @Limit";

                var parameters = new
                {
                    QueryEmbedding = $"[{string.Join(",", queryEmbedding)}]",
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