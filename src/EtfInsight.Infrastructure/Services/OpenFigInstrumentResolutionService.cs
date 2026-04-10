using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EtfInsight.Infrastructure.Services
{
    public class OpenFigInstrumentResolutionService(
        IDbConnection db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<OpenFigInstrumentResolutionService> logger
    ) : IInstrumentResolutionService
    {
        private static readonly IReadOnlyDictionary<string, string> ExchangeSuffixes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IM"] = ".MI",   // Borsa Italiana (Milan)
                ["GR"] = ".DE",   // XETRA (Frankfurt)
                ["LN"] = ".L",    // London Stock Exchange
                ["EO"] = ".AS",   // Euronext Amsterdam
                ["EP"] = ".PA",   // Euronext Paris
                ["SM"] = ".MC",   // Bolsa de Madrid
                ["SW"] = ".SW",   // SIX Swiss Exchange
                ["HE"] = ".HE",   // Helsinki
                ["SS"] = ".ST",   // Stockholm
                ["DC"] = ".CO",   // Copenhagen
                ["OS"] = ".OL",   // Oslo
            };

        private static readonly string[] PreferredExchanges = ["IM", "GR", "LN", "EO", "EP"];

        public async Task<string?> ResolveTickerByIsinAsync(
            string isin,
            string? instrumentName = null,
             CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(isin);
            isin = isin.ToUpperInvariant();

            var dbTicker = await db.ExecuteScalarAsync<string?>(
                "SELECT ticker FROM etf_metadata WHERE isin = @Isin LIMIT 1",
                new { Isin = isin });

            if (dbTicker is not null)
                return dbTicker;

            return await ResolveViaOpenFigiAsync(isin, ct);
        }

        private async Task<string?> ResolveViaOpenFigiAsync(string isin, CancellationToken ct)
        {
            var apiKey = config["OpenFigi:ApiKey"];
            var http = httpClientFactory.CreateClient("OpenFigi");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openfigi.com/v3/mapping")
            {
                Content = JsonContent.Create(new[] { new { idType = "ID_ISIN", idValue = isin } })
            };

            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-OPENFIGI-APIKEY", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OpenFIGI HTTP call failed for ISIN {Isin}", isin);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "OpenFIGI returned {Status} for ISIN {Isin}",
                    (int)response.StatusCode, isin);
                return null;
            }

            OpenFigiResponse[]? results;
            try
            {
                results = await response.Content.ReadFromJsonAsync<OpenFigiResponse[]>(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OpenFIGI response deserialization failed for ISIN {Isin}", isin);
                return null;
            }

            if (results is null || results.Length == 0 || results[0].Data is null)
                return null;

            foreach (var exchCode in PreferredExchanges)
            {
                var match = results[0].Data!
                    .FirstOrDefault(d =>
                        string.Equals(d.ExchCode, exchCode, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.Ticker));

                if (match is null)
                    continue;

                if (!ExchangeSuffixes.TryGetValue(exchCode, out var suffix))
                    continue;

                var composedTicker = match.Ticker! + suffix;
                logger.LogInformation(
                    "OpenFIGI resolved ISIN {Isin} to ticker {Ticker} via exchange {ExchCode}",
                    isin, composedTicker, exchCode);
                return composedTicker;
            }

            var available = results[0].Data!
                .Where(d => !string.IsNullOrWhiteSpace(d.ExchCode))
                .Select(d => d.ExchCode!)
                .Distinct();

            logger.LogWarning(
                "OpenFIGI has results for ISIN {Isin} but none matched preferred exchanges. Available: {Exchanges}",
                isin, string.Join(", ", available));

            return null;
        }


        private sealed record OpenFigiResponse(
            [property: JsonPropertyName("data")] OpenFigiData[]? Data);

        private sealed record OpenFigiData(
            [property: JsonPropertyName("figi")] string? Figi,
            [property: JsonPropertyName("name")] string? Name,
            [property: JsonPropertyName("ticker")] string? Ticker,
            [property: JsonPropertyName("exchCode")] string? ExchCode,
            [property: JsonPropertyName("securityType")] string? SecurityType);
    }
}