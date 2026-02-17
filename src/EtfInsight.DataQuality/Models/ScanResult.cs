using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.DataQuality.Models
{
    public class ScanResult
    {
        public int PricesChecked { get; set; }
        public int RulesExecuted { get; set; }
        public int AnomaliesDetected { get; set; }
        public int Errors { get; set; }
    }
}