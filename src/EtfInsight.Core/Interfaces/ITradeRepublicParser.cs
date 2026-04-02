using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Core.Interfaces
{
    public interface ITradeRepublicParser
    {
        public TradeRepublicParserResult Parse(PdfExtractionResult extraction);
    }
}