using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Infrastructure.Services
{
    public sealed class EtfMetadataInstrumentResolutionService(IDbConnection db) : IInstrumentResolutionService
    {
        public async Task<string?> ResolveTickerByIsinAsync(
            string isin,
        string? instrumentName = null,
        CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(isin);

            return await db.ExecuteScalarAsync<string?>(
                "SELECT Ticker FROM EtfMetadata WHERE isin = @Isin LIMIT 1",
                new { Isin = isin.ToUpperInvariant() });
        }
    }
}