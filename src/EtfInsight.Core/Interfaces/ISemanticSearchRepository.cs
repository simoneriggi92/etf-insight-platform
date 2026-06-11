using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Core.Interfaces
{
    public interface ISemanticSearchRepository
    {
        Task SaveEmbeddingAsync(string ticker, string content, float[] embedding, CancellationToken ct = default);
        
        Task BulkReplaceAsync(string ticker, IReadOnlyList<IngestChunkDto> chunks, CancellationToken ct = default);

        Task<IEnumerable<SearchResult>> SearchAsync(float[] queryEmbedding, int limit = 5, double minSimilarity = 0.65, CancellationToken ct = default);
    }
}