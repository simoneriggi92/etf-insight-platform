using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;
using EtfInsight.Infrastructure.Services.BrokerPdf;
using Xunit;

namespace EtfInsight.Tests.BrokerPdf;

public class TradeRepublicParserTests
{
    private readonly ITradeRepublicParser _parser = new TradeRepublicParser();

    // Fixture text matching the confirmed sample PDF structure (Italian locale, savings plan)
    private const string SavingsPlanText =
        "Savings Plan Execution\n" +
        "\n" +
        "PIANO DI ACCUMULO c57a-f6c2\n" +
        "ESECUZIONE 100c-2d49\n" +
        "\n" +
        "DATA 02.03.2026\n" +
        "\n" +
        "POSIZIONE QUANTITÀ PREZZO MEDIO IMPORTO\n" +
        "Core MSCI World USD (Acc) 7,378349 113,8466 EUR 840,00 EUR\n" +
        "\n" +
        "ISIN: IE00B4L5Y983\n" +
        "\n" +
        "TOTALE 840,00 EUR\n" +
        "\n" +
        "DATA VALUTA IMPORTO IBAN\n" +
        "2026-03-04 840,00 EUR IT60X0542811101000000123456";

    private const string BuyConfirmationText =
        "Order Confirmation\n" +
        "\n" +
        "ACQUISTO\n" +
        "ESECUZIONE buy-ref-001\n" +
        "\n" +
        "DATA 15.01.2026\n" +
        "\n" +
        "POSIZIONE QUANTITÀ PREZZO MEDIO IMPORTO\n" +
        "iShares Core MSCI World 10,000000 80,5000 EUR 805,00 EUR\n" +
        "\n" +
        "ISIN: IE00B4L5Y983\n" +
        "\n" +
        "TOTALE 805,00 EUR\n" +
        "\n" +
        "DATA VALUTA IMPORTO IBAN\n" +
        "2026-01-17 805,00 EUR IT60X0542811101000000123456";

    private const string SellConfirmationText =
        "Order Confirmation\n" +
        "\n" +
        "VENDITA\n" +
        "ESECUZIONE sell-ref-001\n" +
        "\n" +
        "DATA 20.02.2026\n" +
        "\n" +
        "POSIZIONE QUANTITÀ PREZZO MEDIO IMPORTO\n" +
        "iShares Core MSCI World 5,500000 90,5000 EUR 497,75 EUR\n" +
        "\n" +
        "ISIN: IE00B4L5Y983\n" +
        "\n" +
        "TOTALE 497,75 EUR\n" +
        "\n" +
        "DATA VALUTA IMPORTO IBAN\n" +
        "2026-02-22 497,75 EUR IT60X0542811101000000123456";

    private const string DividendText =
        "Dividend\n" +
        "\n" +
        "DIVIDENDO\n" +
        "\n" +
        "DATA 10.03.2026";

    private const string FlattenedSavingsPlanText =
        "TRADE REPUBLIC BANK GMBH, BRANCH ITALY SPACES GAE AULENTI, PIAZZA GAE AULENTI 1, TORRE B20154 MILANO (MI)" +
        "PAGINA1 da 1DATA02.03.2026ESECUZIONE100c-2d49PIANO DI ACCUMULOc57a-f6c2CONTO TITOLI3079293601" +
        "PIANO D'INVESTIMENTO PER IL REGOLAMENTO DEI TITOLIPANORAMICAEsecuzione del piano d'accumulo il 02.03.2026 " +
        "su Lang und Schwarz Exchange.La controparte dell'operazione è Lang & Schwarz TradeCenter AG & Co. KG." +
        "POSIZIONEQUANTITÀPREZZO MEDIOIMPORTOCore MSCI World USD (Acc)ISIN: IE00B4L5Y9837,378349113,8466 EUR840,00 EUR" +
        "TOTALE840,00 EURPRENOTAZIONECONTO DI TRANSITODATA VALUTAIMPORTOIT83A03674016000030792936112026-03-04-840,00 EUR";

    private const string SecuritiesSettlementBuyText =
        "Securities Settlement\n" +
        "\n" +
        "ORDINA d74c-69c3\n" +
        "ESECUZIONE 3754-af14\n" +
        "CONTO TITOLI 3079293601\n" +
        "\n" +
        "Market-Order Acquisto su 12.12.2025\n" +
        "\n" +
        "DATA 12.12.2025\n" +
        "\n" +
        "POSIZIONE QUANTITÀ PREZZO IMPORTO\n" +
        "Global Aggregate Bond EUR (Acc) 40,660323 4,9188 EUR 200,00 EUR\n" +
        "\n" +
        "ISIN: IE00BDBRDM35\n" +
        "\n" +
        "TOTALE 200,00 EUR\n" +
        "\n" +
        "FATTURAZIONE\n" +
        "POSIZIONE IMPORTO\n" +
        "Supplemento spese di terzi -1,00 EUR\n" +
        "TOTALE -201,00 EUR\n" +
        "\n" +
        "DATA DI VALUTA IMPORTO\n" +
        "IT83A0367401600003079293611 2025-12-16 -201,00 EUR";

    private const string SecuritiesSettlementSellText =
        "Securities Settlement\n" +
        "\n" +
        "ORDINA s9k1-2xw8\n" +
        "ESECUZIONE a8b2-cd34\n" +
        "CONTO TITOLI 3079293601\n" +
        "\n" +
        "Market-Order Vendita su 15.01.2026\n" +
        "\n" +
        "DATA 15.01.2026\n" +
        "\n" +
        "POSIZIONE QUANTITÀ PREZZO IMPORTO\n" +
        "Global Aggregate Bond EUR (Acc) 20,330162 4,9188 EUR 100,00 EUR\n" +
        "\n" +
        "ISIN: IE00BDBRDM35\n" +
        "\n" +
        "TOTALE 100,00 EUR\n" +
        "\n" +
        "DATA DI VALUTA IMPORTO\n" +
        "IT83A0367401600003079293611 2026-01-17 100,00 EUR";
    [Fact]
    public void parses_savings_plan_execution_successfully()
    {
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", SavingsPlanText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        var tx = success.Transaction;
        Assert.Equal("IE00B4L5Y983", tx.Isin);
        Assert.Equal("BUY", tx.TransactionType);
        Assert.Equal(new DateOnly(2026, 3, 2), tx.TransactionDate);
        Assert.Equal(new DateOnly(2026, 3, 4), tx.SettlementDate);
        Assert.Equal(7.378349m, tx.Units);
        Assert.Equal(113.8466m, tx.PricePerUnit);
        Assert.Equal(840.00m, tx.GrossAmount);
        Assert.Equal("EUR", tx.Currency);
        Assert.Equal("100c-2d49", tx.BrokerReference);
        Assert.Equal("c57a-f6c2", tx.BrokerSecondaryReference);
        Assert.Equal("Core MSCI World USD (Acc)", tx.InstrumentName);
        Assert.Null(tx.Fees);
    }

    [Fact]
    public void parses_flattened_savings_plan_text_with_merged_numeric_fields()
    {
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", FlattenedSavingsPlanText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        var tx = success.Transaction;
        Assert.Equal("IE00B4L5Y983", tx.Isin);
        Assert.Equal("BUY", tx.TransactionType);
        Assert.Equal(new DateOnly(2026, 3, 2), tx.TransactionDate);
        Assert.Equal(new DateOnly(2026, 3, 4), tx.SettlementDate);
        Assert.Equal(7.378349m, tx.Units);
        Assert.Equal(113.8466m, tx.PricePerUnit);
        Assert.Equal(840.00m, tx.GrossAmount);
        Assert.Equal("EUR", tx.Currency);
        Assert.Equal("100c-2d49", tx.BrokerReference);
        Assert.Equal("c57a-f6c2", tx.BrokerSecondaryReference);
        Assert.Equal("Core MSCI World USD (Acc)", tx.InstrumentName);
    }

    [Fact]
    public void parses_buy_confirmation_with_buy_transaction_type()
    {
        var result = _parser.Parse(new PdfExtractionResult("Order Confirmation", BuyConfirmationText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal("BUY", success.Transaction.TransactionType);
        Assert.Equal("IE00B4L5Y983", success.Transaction.Isin);
        Assert.Equal(new DateOnly(2026, 1, 15), success.Transaction.TransactionDate);
        Assert.Equal(10.000000m, success.Transaction.Units);
    }

    [Fact]
    public void parses_sell_confirmation_with_sell_transaction_type()
    {
        var result = _parser.Parse(new PdfExtractionResult("Order Confirmation", SellConfirmationText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal("SELL", success.Transaction.TransactionType);
        Assert.Equal("IE00B4L5Y983", success.Transaction.Isin);
        Assert.Equal(new DateOnly(2026, 2, 20), success.Transaction.TransactionDate);
    }

    [Fact]
    public void returns_unsupported_for_dividend_document()
    {
        var result = _parser.Parse(new PdfExtractionResult("Dividend", DividendText));

        Assert.IsType<TradeRepublicParserResult.Unsupported>(result);
    }

    [Fact]
    public void returns_failure_when_isin_is_missing()
    {
        var textWithoutIsin = SavingsPlanText.Replace("ISIN: IE00B4L5Y983", string.Empty);
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutIsin));

        var failure = Assert.IsType<TradeRepublicParserResult.Failure>(result);
        Assert.Equal("isin", failure.Stage);
    }

    [Fact]
    public void returns_failure_when_instrument_data_row_is_missing()
    {
        var textWithoutRow = SavingsPlanText
            .Replace("POSIZIONE QUANTITÀ PREZZO MEDIO IMPORTO\n", string.Empty)
            .Replace("Core MSCI World USD (Acc) 7,378349 113,8466 EUR 840,00 EUR\n", string.Empty);

        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutRow));

        var failure = Assert.IsType<TradeRepublicParserResult.Failure>(result);
        Assert.Equal("instrument_row", failure.Stage);
    }

    [Fact]
    public void returns_failure_when_totale_is_missing()
    {
        var textWithoutTotale = SavingsPlanText.Replace("TOTALE 840,00 EUR", string.Empty);
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutTotale));

        var failure = Assert.IsType<TradeRepublicParserResult.Failure>(result);
        Assert.Equal("gross_amount", failure.Stage);
    }

    [Fact]
    public void returns_failure_when_transaction_date_is_missing()
    {
        var textWithoutDate = SavingsPlanText.Replace("DATA 02.03.2026", string.Empty);
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutDate));

        var failure = Assert.IsType<TradeRepublicParserResult.Failure>(result);
        Assert.Equal("transaction_date", failure.Stage);
    }

    [Fact]
    public void returns_failure_when_document_kind_is_unknown()
    {
        var result = _parser.Parse(new PdfExtractionResult(null, "unrecognized document content"));

        var failure = Assert.IsType<TradeRepublicParserResult.Failure>(result);
        Assert.Equal("detection", failure.Stage);
    }

    [Fact]
    public void handles_6_decimal_fractional_quantity()
    {
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", SavingsPlanText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal(7.378349m, success.Transaction.Units);
    }

    [Fact]
    public void fees_are_null_when_no_fee_line_is_present()
    {
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", SavingsPlanText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Null(success.Transaction.Fees);
    }

    [Fact]
    public void settlement_date_is_parsed_when_present()
    {
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", SavingsPlanText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal(new DateOnly(2026, 3, 4), success.Transaction.SettlementDate);
    }

    [Fact]
    public void settlement_date_is_null_when_data_valuta_is_absent()
    {
        var textWithoutSettlement = SavingsPlanText
            .Replace("DATA VALUTA IMPORTO IBAN\n", string.Empty)
            .Replace("2026-03-04 840,00 EUR IT60X0542811101000000123456", string.Empty);

        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutSettlement));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Null(success.Transaction.SettlementDate);
    }

    [Fact]
    public void transaction_date_uses_execution_date_not_settlement_date()
    {
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", SavingsPlanText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        // DATA 02.03.2026 is the execution date; DATA VALUTA 2026-03-04 is settlement
        Assert.Equal(new DateOnly(2026, 3, 2), success.Transaction.TransactionDate);
        Assert.NotEqual(success.Transaction.TransactionDate, success.Transaction.SettlementDate!.Value);
    }

    [Fact]
    public void broker_reference_is_null_when_esecuzione_is_absent()
    {
        var textWithoutRef = SavingsPlanText.Replace("ESECUZIONE 100c-2d49\n", string.Empty);
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutRef));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Null(success.Transaction.BrokerReference);
    }

    [Fact]
    public void broker_reference_does_not_fall_back_to_sentence_text_when_flattened_reference_is_missing()
    {
        var textWithoutRef = FlattenedSavingsPlanText.Replace("ESECUZIONE100c-2d49", string.Empty);
        var result = _parser.Parse(new PdfExtractionResult("Savings Plan Execution", textWithoutRef));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Null(success.Transaction.BrokerReference);
    }

    [Fact]
    public void body_keyword_detection_works_when_pdf_title_is_null()
    {
        // Savings plan detected via PIANO DI ACCUMULO body keyword alone
        var result = _parser.Parse(new PdfExtractionResult(null, SavingsPlanText));

        Assert.IsType<TradeRepublicParserResult.Success>(result);
    }

    [Fact]
    public void parses_securities_settlement_buy_successfully()
    {
        var result = _parser.Parse(new PdfExtractionResult("Securities Settlement", SecuritiesSettlementBuyText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        var tx = success.Transaction;
        Assert.Equal("IE00BDBRDM35", tx.Isin);
        Assert.Equal("BUY", tx.TransactionType);
        Assert.Equal(new DateOnly(2025, 12, 12), tx.TransactionDate);
        Assert.Equal(new DateOnly(2025, 12, 16), tx.SettlementDate);
        Assert.Equal(40.660323m, tx.Units);
        Assert.Equal(4.9188m, tx.PricePerUnit);
        Assert.Equal(200.00m, tx.GrossAmount);
        Assert.Equal(1.00m, tx.Fees);
        Assert.Equal("EUR", tx.Currency);
        Assert.Equal("3754-af14", tx.BrokerReference);
        Assert.Equal("d74c-69c3", tx.BrokerSecondaryReference);
        Assert.Equal("Global Aggregate Bond EUR (Acc)", tx.InstrumentName);
    }

    [Fact]
    public void parses_securities_settlement_sell_with_sell_transaction_type()
    {
        var result = _parser.Parse(new PdfExtractionResult("Securities Settlement", SecuritiesSettlementSellText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal("SELL", success.Transaction.TransactionType);
        Assert.Equal("IE00BDBRDM35", success.Transaction.Isin);
        Assert.Equal(new DateOnly(2026, 1, 15), success.Transaction.TransactionDate);
    }

    [Fact]
    public void settlement_date_parsed_with_data_di_valuta_label()
    {
        var result = _parser.Parse(new PdfExtractionResult("Securities Settlement", SecuritiesSettlementBuyText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal(new DateOnly(2025, 12, 16), success.Transaction.SettlementDate);
    }

    [Fact]
    public void ordina_captured_as_broker_secondary_reference()
    {
        var result = _parser.Parse(new PdfExtractionResult("Securities Settlement", SecuritiesSettlementBuyText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal("d74c-69c3", success.Transaction.BrokerSecondaryReference);
    }

    [Fact]
    public void fee_extracted_from_supplemento_spese_di_terzi()
    {
        var result = _parser.Parse(new PdfExtractionResult("Securities Settlement", SecuritiesSettlementBuyText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal(1.00m, success.Transaction.Fees);
    }

    [Fact]
    public void two_totale_layout_uses_first_match_as_gross_amount()
    {
        var result = _parser.Parse(new PdfExtractionResult("Securities Settlement", SecuritiesSettlementBuyText));

        var success = Assert.IsType<TradeRepublicParserResult.Success>(result);
        Assert.Equal(200.00m, success.Transaction.GrossAmount);
    }
}
