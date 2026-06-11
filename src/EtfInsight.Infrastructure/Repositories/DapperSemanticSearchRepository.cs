using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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

        public async Task SaveEmbeddingAsync(
            string ticker, 
            string content, 
            float[] embedding,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(ticker);
            ArgumentNullException.ThrowIfNull(embedding);
            
            _logger.LogInformation(
                "Saving embedding for ticker {Ticker}",
                ticker);

            // Use InvariantCulture to ensure decimal points (.) not commas (,)
            var embeddingString = GetEmbeddingString(embedding);

            var sql = @"
                INSERT INTO etf_documents (ticker, content, embedding, metadata, is_mandatory, chunk_index, source)
                VALUES (@Ticker, @Content, @Embedding::vector, @Metadata::jsonb, @IsMandatory, 0, 'manual_seed')
                ON CONFLICT (ticker, chunk_index) 
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

            var cmd = new CommandDefinition(
                sql, 
                parameters, 
                cancellationToken: ct);
            
            await _connection.ExecuteAsync(cmd);

            _logger.LogInformation(
                "Successfully saved embedding for {Ticker}",
                ticker);
        }

        public async Task BulkReplaceAsync(
            string ticker,
            IReadOnlyList<IngestChunkDto> chunks, 
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(ticker);
            ArgumentNullException.ThrowIfNull(chunks);
            
            if(_connection.State != ConnectionState.Open)
                _connection.Open();
            
            using var transaction = _connection.BeginTransaction();

            try
            {
                await _connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM etf_documents WHERE ticker = @Ticker",
                        new {Ticker = ticker},
                        transaction,
                        cancellationToken: ct));

                foreach (var chunk in chunks)
                {
                    var embeddingString = GetEmbeddingString(chunk.Embedding);
                    var metadataJson = JsonSerializer.Serialize(chunk.Metadata);
                    var source = chunk.Metadata.GetValueOrDefault("source", "factsheet").ToString();
                    
                    const string sql = @"
                        INSERT INTO etf_documents (ticker, content, embedding, metadata, is_mandatory, chunk_index, source)
                        VALUES (@Ticker, @Content, @Embedding::vector, @Metadata::jsonb, false, @ChunkIndex, @Source)";
                    
                    await _connection.ExecuteAsync(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                Ticker = ticker,
                                Content = chunk.Content,
                                Embedding = embeddingString,
                                Metadata = metadataJson,
                                ChunkIndex = chunk.ChunkIndex,
                                Source = source
                            },
                            transaction,
                            cancellationToken: ct));
                }
                
                transaction.Commit();
                
                _logger.LogInformation(
                    "Replaced {Count} chunks for {Ticker}", 
                    chunks.Count, 
                    ticker);

            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }


        public async Task<IEnumerable<SearchResult>> SearchAsync(
            float[] queryEmbedding, 
            int limit = 5,
            double minSimilarity = 0.65,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(queryEmbedding);
            
            _logger.LogInformation(
                "Performing semantic search with limit {Limit}, minSimilarity {MinSimilarity}",
                limit,
                minSimilarity);
            
            var sql = @"
                SELECT 
                    ticker,
                    content,
                    1 - (embedding <=> @QueryEmbedding::vector) AS similarity
                FROM etf_documents
                WHERE 1 - (embedding <=> @QueryEmbedding::vector) >= @MinSimilarity
                ORDER BY embedding <=> @QueryEmbedding::vector
                LIMIT @Limit";

            // Use InvariantCulture to ensure decimal points (.) not commas (,)
            var parameters = new
            {
                QueryEmbedding = GetEmbeddingString(queryEmbedding),
                Limit = limit,
                MinSimilarity = minSimilarity
            };

            var cmd = new CommandDefinition(
                sql, 
                parameters, 
                cancellationToken: ct);
            
            var results = await _connection
                .QueryAsync<SearchResult>(cmd);

            _logger.LogInformation("Semantic search returned {Count} results", 
                results.Count());

            return results;
        }

        private string GetEmbeddingString(float[] embedding)
        {
            return $"[{string.Join(",", embedding.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]";
        }
    }
}