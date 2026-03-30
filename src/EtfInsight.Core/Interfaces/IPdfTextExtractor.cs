using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Interfaces
{
    public record PdfExtractionResult(
        string? Title,
        string RawText);
    public interface IPdfTextExtractor
    {
        Task<PdfExtractionResult> ExtractTextAsync(string filePath, CancellationToken ct = default);
    }
}