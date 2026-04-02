using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.DTOs;
using System.Text.RegularExpressions;
using System.Globalization;

namespace EtfInsight.Infrastructure.Services.BrokerPdf
{
    internal sealed class TradeRepublicParser : ITradeRepublicParser
    {
        private static readonly Regex IsinPattern =
        new(@"ISIN:\s*([A-Z]{2}[A-Z0-9]{9}\d)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ExecutionRefPattern =
            new(@"ESECUZIONE\s+([A-Za-z0-9\-]+)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex PlanRefPattern =
            new(@"PIANO DI ACCUMULO\s+([A-Za-z0-9\-]+)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex GrossAmountPattern =
            new(@"TOTALE\s+([\d,]+)\s+([A-Z]{3})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex TransactionDatePattern =
            new(@"\bDATA\s+(\d{2}\.\d{2}\.\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex SettlementDatePattern =
            new(@"DATA VALUTA[\s\S]*?(\d{4}-\d{2}-\d{2})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InstrumentRowPattern =
            new(@"POSIZIONE QUANTIT[AÀ] PREZZO MEDIO IMPORTO\n(.+?)\s+([\d]+,[\d]+)\s+([\d]+,[\d]+)\s+EUR\s+([\d]+,[\d]+)\s+EUR",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

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
            if (!rowMatch.Success)
                return new TradeRepublicParserResult.Failure("Instrument data row (POSIZIONE table) not found", "instrument_row");

            var instrumentName = rowMatch.Groups[1].Value.Trim();

            if (!TryParseDecimal(rowMatch.Groups[2].Value, out var units))
                return new TradeRepublicParserResult.Failure($"Units decimal parse failed: '{rowMatch.Groups[2].Value}'", "units");

            if (!TryParseDecimal(rowMatch.Groups[3].Value, out var pricePerUnit))
                return new TradeRepublicParserResult.Failure($"Price per unit decimal parse failed: '{rowMatch.Groups[3].Value}'", "price_per_unit");

            var grossMatch = GrossAmountPattern.Match(normalized);
            if (!grossMatch.Success)
                return new TradeRepublicParserResult.Failure("TOTALE field not found", "gross_amount");

            if (!TryParseDecimal(grossMatch.Groups[1].Value, out var grossAmount))
                return new TradeRepublicParserResult.Failure($"Gross amount decimal parse failed: '{grossMatch.Groups[1].Value}'", "gross_amount");

            var currency = grossMatch.Groups[2].Value.ToUpperInvariant();

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

            var brokerReference = execMatch.Success ? execMatch.Groups[1].Value : null;

            var planMatch = PlanRefPattern.Match(normalized);
            var brokerSecondaryReference = planMatch.Success ? planMatch.Groups[1].Value : null;

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

        private static bool TryParseDate(string raw, string format, out DateOnly result)
        {
            return DateOnly.TryParseExact(raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
    }
}