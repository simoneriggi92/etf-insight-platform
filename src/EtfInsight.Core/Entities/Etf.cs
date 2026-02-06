using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Entities
{
    public class Etf
    {
        public Guid Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }


    }
}