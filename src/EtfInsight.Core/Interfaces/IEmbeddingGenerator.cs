using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Interfaces
{
    public interface IEmbeddingGenerator
    {
        Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken ct = default);
    }
}