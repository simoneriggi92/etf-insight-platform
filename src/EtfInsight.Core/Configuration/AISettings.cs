using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Configuration
{
    public sealed class AISettings
    {
        public string OllamaUrl { get; set; } = "http://localhost:11434";
        
        public string EmbeddingModel { get; set; } = "nomic-embed-text";
        
        public string ChatModel { get; set; } = "llama3.2";
        
        public int EmbeddingDimensions { get; set; } = 768;
        
        public string IngestAPIKey { get; set; } = string.Empty;

        public double MinSimilarityThreshold { get; set; } = 0.65;

        public int MaxContextChunks { get; set; } = 7;
    }
}