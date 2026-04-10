using System.Text.RegularExpressions;
using System.Globalization;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Infrastructure.Services.BrokerPdf
{
    public sealed class TradeRepublicParser : ITradeRepublicParser
    {
        private static readonly Regex IsinPattern =
            new(@"ISIN:\s*([A-Z]{2}[A-Z0-9]{9}\d)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ExecutionRefPattern =
            new(@"ESECUZIONE\s*(?<ref>[A-Za-z0-9-]+?)(?=\s*(?:PIANO DI ACCUMULO|DATA|CONTO TITOLI|POSIZIONE|\n|$))",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex PlanRefPattern =
            new(@"PIANO DI ACCUMULO\s*(?<ref>[A-Za-z0-9-]+?)(?=\s*(?:ESECUZIONE|DATA|CONTO TITOLI|PIANO D'INVESTIMENTO|\n|$))",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex GrossAmountPattern =
            new(@"TOTALE\s*(-?[\d.]*\d,\d+)\s*([A-Z]{3})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex TransactionDatePattern =
            new(@"DATA\s*(\d{2}\.\d{2}\.\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex SettlementDatePattern =
            new(@"DATA VALUTA[\s\S]*?(\d{4}-\d{2}-\d{2})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InstrumentRowPattern =
            new(@"POSIZIONE\s+QUANTIT[AÀ]\s+PREZZO\s+MEDIO\s+IMPORTO\s*\n(?<name>.+?)\s+(?<units>\d[\d.]*,\d+)\s+(?<price>\d[\d.]*,\d+)\s+EUR\s+(?<amount>\d[\d.]*,\d+)\s+EUR",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex FlattenedInstrumentPattern =
            new(@"POSIZIONE\s*QUANTIT[AÀ]\s*PREZZO\s*MEDIO\s*IMPORTO(?<name>.+?)ISIN:\s*(?<isin>[A-Z]{2}[A-Z0-9]{9}\d)\s*(?<blob>[\d.,\s]+?)\s*EUR\s*(?<amount>-?[\d.]*\d,\d+)\s*(?<currency>[A-Z]{3})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex WhitespacePattern =
            new(@"\s+", RegexOptions.Compiled);

        private static readonly Regex DecimalTokenPattern =
            new(@"^\d[\d.]*,\d+$", RegexOptions.Compiled);

        public TradeRepublicParserResult Parse(PdfExtractionResult extraction)
        {
            var normalized = TradeRepublicTextNormalizer.Normalize(extraction.RawText);
            var kind = TradeRepublicDocumentKindDetector.Detect(extraction.Title, normalized);

            if (kind is TradeRepublicDocumentKind.Unknown)
                return new TradeRepublicParserResult.Failure("Document kind could not be determined", "detection");

            if (kind is TradeRepublicDocumentKind.Dividend
                or TradeRepublicDocumentKind.Tax
                or TradeRepublicDocumentKind.CashMovement)
                return new TradeRepublicParserResult.Unsupported($"Document kind '{kind}' is not supported in V1");

            var isinMatch = IsinPattern.Match(normalized);
            if (!isinMatch.Success)
                return new TradeRepublicParserResult.Failure("ISIN not found in text", "isin");
            var isin = isinMatch.Groups[1].Value.ToUpperInvariant();

            var rowMatch = InstrumentRowPattern.Match(normalized);
            string? instrumentName = null;
            decimal units = 0;
            decimal pricePerUnit = 0;

            if (rowMatch.Success)
            {
                instrumentName = rowMatch.Groups["name"].Value.Trim();

                if (!TryParseDecimal(rowMatch.Groups["units"].Value, out units))
                    return new TradeRepublicParserResult.Failure($"Units decimal parse failed: '{rowMatch.Groups["units"].Value}'", "units");

                if (!TryParseDecimal(rowMatch.Groups["price"].Value, out pricePerUnit))
                    return new TradeRepublicParserResult.Failure($"Price per unit decimal parse failed: '{rowMatch.Groups["price"].Value}'", "price_per_unit");
            }

            var grossMatch = GrossAmountPattern.Match(normalized);
            if (!grossMatch.Success)
                return new TradeRepublicParserResult.Failure("TOTALE field not found", "gross_amount");

            if (!TryParseDecimal(grossMatch.Groups[1].Value, out var grossAmount))
                return new TradeRepublicParserResult.Failure($"Gross amount decimal parse failed: '{grossMatch.Groups[1].Value}'", "gross_amount");

            var currency = grossMatch.Groups[2].Value.ToUpperInvariant();

            if (!rowMatch.Success
                && !TryParseFlattenedInstrumentData(normalized, isin, grossAmount, out instrumentName, out units, out pricePerUnit))
            {
                return new TradeRepublicParserResult.Failure("Instrument data row (POSIZIONE table) not found", "instrument_row");
            }

            var dateMatch = TransactionDatePattern.Match(normalized);
            if (!dateMatch.Success)
                return new TradeRepublicParserResult.Failure("Transaction date (DATA) not found", "transaction_date");

            if (!TryParseDate(dateMatch.Groups[1].Value, "dd.MM.yyyy", out var transactionDate))
                return new TradeRepublicParserResult.Failure($"Transaction date parse failed: '{dateMatch.Groups[1].Value}'", "transaction_date");

            DateOnly? settlementDate = null;
            var settlementMatch = SettlementDatePattern.Match(normalized);
            if (settlementMatch.Success && TryParseDate(settlementMatch.Groups[1].Value, "yyyy-MM-dd", out var sd))
                settlementDate = sd;

            var execMatch = ExecutionRefPattern.Match(normalized);

            var brokerReference = execMatch.Success ? execMatch.Groups["ref"].Value : null;

            var planMatch = PlanRefPattern.Match(normalized);
            var brokerSecondaryReference = planMatch.Success ? planMatch.Groups["ref"].Value : null;

            var transactionType = kind == TradeRepublicDocumentKind.SellConfirmation ? "SELL" : "BUY";

            return new TradeRepublicParserResult.Success(
                new ParsedTransactionResult(
                    BrokerReference: brokerReference,
                    BrokerSecondaryReference: brokerSecondaryReference,
                    InstrumentName: string.IsNullOrWhiteSpace(instrumentName) ? null : instrumentName,
                    Isin: isin,
                    TransactionType: transactionType,
                    TransactionDate: transactionDate,
                    SettlementDate: settlementDate,
                    Units: units,
                    PricePerUnit: pricePerUnit,
                    Fees: null, // Fees are not provided in the current document format
                    GrossAmount: grossAmount,
                    Currency: currency
                ));
        }

        private static bool TryParseDecimal(string raw, out decimal result)
        {
            var normalized = raw.Replace(".", string.Empty).Replace(",", ".");
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseFlattenedInstrumentData(
            string normalized,
            string expectedIsin,
            decimal grossAmount,
            out string instrumentName,
            out decimal units,
            out decimal pricePerUnit)
        {
            instrumentName = string.Empty;
            units = 0;
            pricePerUnit = 0;

            var match = FlattenedInstrumentPattern.Match(normalized);
            if (!match.Success)
                return false;

            var parsedIsin = match.Groups["isin"].Value.ToUpperInvariant();
            if (!string.Equals(parsedIsin, expectedIsin, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryParseDecimal(match.Groups["amount"].Value, out var inlineAmount))
                return false;

            if (inlineAmount != grossAmount)
                return false;

            instrumentName = match.Groups["name"].Value.Trim();
            return TryResolveUnitsAndPrice(match.Groups["blob"].Value, grossAmount, out units, out pricePerUnit);
        }

        private static bool TryResolveUnitsAndPrice(
            string rawBlob,
            decimal grossAmount,
            out decimal units,
            out decimal pricePerUnit)
        {
            units = 0;
            pricePerUnit = 0;

            var compactBlob = WhitespacePattern.Replace(rawBlob, string.Empty);
            NumericSplitCandidate? bestCandidate = null;

            for (var splitIndex = 1; splitIndex < compactBlob.Length; splitIndex++)
            {
                var unitsRaw = compactBlob[..splitIndex];
                var priceRaw = compactBlob[splitIndex..];

                if (!DecimalTokenPattern.IsMatch(unitsRaw) || !DecimalTokenPattern.IsMatch(priceRaw))
                    continue;

                if (!TryParseDecimal(unitsRaw, out var parsedUnits) || !TryParseDecimal(priceRaw, out var parsedPrice))
                    continue;

                var product = parsedUnits * parsedPrice;
                if (decimal.Round(product, 2, MidpointRounding.AwayFromZero) != grossAmount)
                    continue;

                var error = Math.Abs(product - grossAmount);
                if (bestCandidate is null || error < bestCandidate.Value.Error)
                    bestCandidate = new NumericSplitCandidate(parsedUnits, parsedPrice, error);
            }

            if (bestCandidate is null)
                return false;

            units = bestCandidate.Value.Units;
            pricePerUnit = bestCandidate.Value.PricePerUnit;
            return true;
        }

        private static bool TryParseDate(string raw, string format, out DateOnly result)
        {
            return DateOnly.TryParseExact(raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        private readonly record struct NumericSplitCandidate(decimal Units, decimal PricePerUnit, decimal Error);
    }
}
