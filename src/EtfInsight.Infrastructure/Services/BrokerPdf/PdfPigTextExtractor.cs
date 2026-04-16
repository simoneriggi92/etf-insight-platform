using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Interfaces;
using UglyToad.PdfPig;

namespace EtfInsight.Infrastructure.Services.BrokerPdf
{
    public class PdfPigTextExtractor : IPdfTextExtractor
    {
        public Task<PdfExtractionResult> ExtractTextAsync(string filePath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // PdfDocument.Open() is synchronous, Task,.Run offloads it to a background thread to avoid blocking the caller.
                using var document = PdfDocument.Open(filePath);
                var title = document.Information.Title;

                var sb = new System.Text.StringBuilder();
                foreach (var page in document.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    sb.AppendLine(page.Text);
                }

                return new PdfExtractionResult(
                    string.IsNullOrWhiteSpace(title) ? null : title,
                    sb.ToString()
                );

            }, ct);
        }
    }
}