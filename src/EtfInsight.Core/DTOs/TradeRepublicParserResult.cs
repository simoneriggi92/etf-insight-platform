using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public abstract record TradeRepublicParserResult
    {
        private TradeRepublicParserResult() { }
        public sealed record Success(ParsedTransactionResult Transaction) : TradeRepublicParserResult;
        public sealed record Unsupported(string Reason) : TradeRepublicParserResult;
        public sealed record Failure(string Reason, string Stage) : TradeRepublicParserResult;
    }
}