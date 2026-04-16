using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Infrastructure.Services.BrokerPdf;
using Xunit;

namespace EtfInsight.Tests.BrokerPdf
{
    public class TradeRepublicDocumentKindDetectorTests
    {
        [Fact]
        public void detects_savings_plan_from_pdf_title()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect("Savings Plan Execution", "some body text");
            Assert.Equal(TradeRepublicDocumentKind.SavingsPlanExecution, kind);
        }

        [Fact]
        public void detects_savings_plan_from_pdf_title_case_insensitive()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect("SAVINGS PLAN EXECUTION", "some body text");
            Assert.Equal(TradeRepublicDocumentKind.SavingsPlanExecution, kind);
        }

        [Theory]
        [InlineData("Order Confirmation", "ACQUISTO qualcosa", TradeRepublicDocumentKind.BuyConfirmation)]
        [InlineData("Order Confirmation", "acquisto qualcosa", TradeRepublicDocumentKind.BuyConfirmation)]
        [InlineData("Order Confirmation", "VENDITA qualcosa", TradeRepublicDocumentKind.SellConfirmation)]
        [InlineData("Order Confirmation", "vendita qualcosa", TradeRepublicDocumentKind.SellConfirmation)]
        public void detects_buy_or_sell_from_order_confirmation_title_and_body(
            string title, string body, TradeRepublicDocumentKind expected)
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(title, body);
            Assert.Equal(expected, kind);
        }

        [Fact]
        public void detects_dividend_from_pdf_title()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect("Dividend", "DIVIDENDO qualcosa");
            Assert.Equal(TradeRepublicDocumentKind.Dividend, kind);
        }

        [Fact]
        public void falls_back_to_body_keyword_piano_di_accumulo_when_title_is_null()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(null, "PIANO DI ACCUMULO c57a-f6c2");
            Assert.Equal(TradeRepublicDocumentKind.SavingsPlanExecution, kind);
        }

        [Fact]
        public void falls_back_to_body_keyword_acquisto_when_title_is_null()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(null, "ACQUISTO qualcosa");
            Assert.Equal(TradeRepublicDocumentKind.BuyConfirmation, kind);
        }

        [Fact]
        public void falls_back_to_body_keyword_vendita_when_title_is_null()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(null, "VENDITA qualcosa");
            Assert.Equal(TradeRepublicDocumentKind.SellConfirmation, kind);
        }

        [Fact]
        public void falls_back_to_body_keyword_dividendo_when_title_is_null()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(null, "DIVIDENDO qualcosa");
            Assert.Equal(TradeRepublicDocumentKind.Dividend, kind);
        }

        [Fact]
        public void returns_unknown_when_no_signals_match()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(null, "completely unrecognized content");
            Assert.Equal(TradeRepublicDocumentKind.Unknown, kind);
        }

        [Fact]
        public void returns_unknown_when_title_is_unrecognized_and_body_has_no_keywords()
        {
            var kind = TradeRepublicDocumentKindDetector.Detect("Tax Statement", "some italian text without keywords");
            Assert.Equal(TradeRepublicDocumentKind.Unknown, kind);
        }

        [Fact]
        public void title_check_takes_precedence_over_body_fallback()
        {
            // Title says savings plan but body has ACQUISTO — should be SavingsPlanExecution
            var kind = TradeRepublicDocumentKindDetector.Detect("Savings Plan Execution", "ACQUISTO qualcosa");
            Assert.Equal(TradeRepublicDocumentKind.SavingsPlanExecution, kind);
        }

        [Theory]
        [InlineData("Securities Settlement", "ACQUISTO qualcosa", TradeRepublicDocumentKind.BuyConfirmation)]
        [InlineData("Securities Settlement", "acquisto qualcosa", TradeRepublicDocumentKind.BuyConfirmation)]
        [InlineData("Securities Settlement", "VENDITA qualcosa", TradeRepublicDocumentKind.SellConfirmation)]
        [InlineData("Securities Settlement", "vendita qualcosa", TradeRepublicDocumentKind.SellConfirmation)]
        public void detects_buy_or_sell_from_securities_settlement_title_and_body(
            string title, string body, TradeRepublicDocumentKind expected)
        {
            var kind = TradeRepublicDocumentKindDetector.Detect(title, body);
            Assert.Equal(expected, kind);
        }
    }
}