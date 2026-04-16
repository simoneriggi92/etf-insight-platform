using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.DTOs
{
    public sealed record ParsedTransactionResult(
    string? BrokerReference,
    string? BrokerSecondaryReference,
    string? InstrumentName,
    string Isin,
    string TransactionType,
    DateOnly TransactionDate,
    DateOnly? SettlementDate,
    decimal Units,
    decimal PricePerUnit,
    decimal? Fees,
    decimal GrossAmount,
    string Currency);
}