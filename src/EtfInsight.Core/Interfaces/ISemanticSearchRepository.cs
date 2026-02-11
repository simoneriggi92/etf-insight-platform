using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Core.Interfaces
{
    public interface ISemanticSearchRepository
    {
        Task SaveEmbeddingAsync(string ticker, string content, float[] embedding);

        Task<IEnumerable<SearchResult>> SearchAsync(float[] queryEmbedding, int limit = 5);
    }
}