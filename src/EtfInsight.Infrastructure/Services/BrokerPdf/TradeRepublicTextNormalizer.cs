using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EtfInsight.Infrastructure.Services.BrokerPdf
{
    internal static class TradeRepublicTextNormalizer
    {
        private static readonly Regex MultipleBlankLines = new(@"\n{4,}", RegexOptions.Compiled);

        private static readonly Regex IntraLineWhitespace = new(@"[^\S\n]+", RegexOptions.Compiled);

        public static string Normalize(string rawText)
        {
            var text = rawText
           .Replace("\r\n", "\n")
           .Replace("\r", "\n");

            text = text
                .Replace("\u200B", " ")
                .Replace("\u00A0", " ")
                .Replace("\uFEFF", " ");


            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = IntraLineWhitespace.Replace(lines[i], " ").Trim();
            }

            text = string.Join("\n", lines);
            text = MultipleBlankLines.Replace(text, "\n\n");

            return text;
        }
    }
}