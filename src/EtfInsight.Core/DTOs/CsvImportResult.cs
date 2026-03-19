using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public class CsvImportResult
    {
        public int Imported { get; set; } = 0;
        public List<object> InvalidRows { get; set; } = new();
        public List<object> Tickers { get; set; } = new();
        public string Message { get; set; } = "";
        public bool AnyIngesting { get; set; } = false;
    }
}