using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public class SearchResult
    {
        public string Ticker { get; set; }
        public string Content { get; set; }
        public double Similarity { get; set; }
    }
}