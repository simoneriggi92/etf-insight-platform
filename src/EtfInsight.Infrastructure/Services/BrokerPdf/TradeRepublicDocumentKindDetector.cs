using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Infrastructure.Services.BrokerPdf
{
    public enum TradeRepublicDocumentKind
    {
        Unknown,
        BuyConfirmation,
        SellConfirmation,
        SavingsPlanExecution,
        Dividend,
        Tax,
        CashMovement
    }

    internal static class TradeRepublicDocumentKindDetector
    {
        public static TradeRepublicDocumentKind Detect(string? pdfTitle, string normalizedBody)
        {
            if (pdfTitle is not null)
            {
                var title = pdfTitle.Trim().ToLowerInvariant();

                if (title.Contains("savings plan execution", StringComparison.OrdinalIgnoreCase))
                {
                    return TradeRepublicDocumentKind.SavingsPlanExecution;
                }

                if (title.Contains("order confirmation", StringComparison.OrdinalIgnoreCase))
                {
                    if (normalizedBody.Contains("ACQUISTO", StringComparison.OrdinalIgnoreCase))
                    {
                        return TradeRepublicDocumentKind.BuyConfirmation;
                    }

                    if (normalizedBody.Contains("VENDITA", StringComparison.OrdinalIgnoreCase))
                    {
                        return TradeRepublicDocumentKind.SellConfirmation;
                    }
                }

                if (title.Contains("securities settlement", StringComparison.OrdinalIgnoreCase))
                {
                    if (normalizedBody.Contains("ACQUISTO", StringComparison.OrdinalIgnoreCase))
                    {
                        return TradeRepublicDocumentKind.BuyConfirmation;
                    }
                    if (normalizedBody.Contains("VENDITA", StringComparison.OrdinalIgnoreCase))
                    {
                        return TradeRepublicDocumentKind.SellConfirmation;
                    }
                }

                if (title.Contains("dividend", StringComparison.OrdinalIgnoreCase))
                {
                    return TradeRepublicDocumentKind.Dividend;
                }
            }

            if (normalizedBody.Contains("PIANO DI ACCUMULO", StringComparison.OrdinalIgnoreCase))
                return TradeRepublicDocumentKind.SavingsPlanExecution;
            if (normalizedBody.Contains("ACQUISTO", StringComparison.OrdinalIgnoreCase))
                return TradeRepublicDocumentKind.BuyConfirmation;
            if (normalizedBody.Contains("VENDITA", StringComparison.OrdinalIgnoreCase))
                return TradeRepublicDocumentKind.SellConfirmation;
            if (normalizedBody.Contains("DIVIDENDO", StringComparison.OrdinalIgnoreCase))
                return TradeRepublicDocumentKind.Dividend;

            return TradeRepublicDocumentKind.Unknown;
        }
    }
}