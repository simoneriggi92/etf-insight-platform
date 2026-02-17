using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.DataQuality.Models
{
    public class DataQualitySettings
    {
        public const string SectionName = "DataQuality";

        public double FlashCrashThresholdPercent { get; set; } = 20.0; // 20% drop in price
        public bool EnableAutoScan { get; set; } = true;
        public TimeSpan ScanIntervalInMinutes { get; set; } = TimeSpan.FromMinutes(5);
    }
}