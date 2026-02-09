using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Configuration
{
    public class AISettings
    {
        public string OllamaUrl { get; set; } = "http://localhost:11434";
        public string EmbeddingModel { get; set; } = "nomic-embed-text";
        public int EmbeddingDimensions { get; set; } = 768;
    }
}