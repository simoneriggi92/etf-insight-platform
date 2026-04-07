using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EtfInsight.Infrastructure.Services.BrokerPdf;

namespace EtfInsight.Tests.BrokerPdf
{
    public class TradeRepublicTextNormalizerTests
    {
        [Fact]
        public void replaces_crlf_with_lf()
        {
            var result = TradeRepublicTextNormalizer.Normalize("line1\r\nline2");
            Assert.Equal("line1\nline2", result);
        }

        [Fact]
        public void replaces_bare_cr_with_lf()
        {
            var result = TradeRepublicTextNormalizer.Normalize("line1\rline2");
            Assert.Equal("line1\nline2", result);
        }

        [Fact]
        public void removes_zero_width_space()
        {
            var result = TradeRepublicTextNormalizer.Normalize("a\u200Bb");
            Assert.Equal("a b", result);
        }

        [Fact]
        public void removes_non_breaking_space()
        {
            var result = TradeRepublicTextNormalizer.Normalize("a\u00A0b");
            Assert.Equal("a b", result);
        }

        [Fact]
        public void removes_byte_order_mark()
        {
            var result = TradeRepublicTextNormalizer.Normalize("a\uFEFFb");
            Assert.Equal("a b", result);
        }

        [Fact]
        public void collapses_intra_line_whitespace_runs_to_single_space()
        {
            var result = TradeRepublicTextNormalizer.Normalize("word1   word2\t\tword3");
            Assert.Equal("word1 word2 word3", result);
        }

        [Fact]
        public void trims_leading_and_trailing_whitespace_per_line()
        {
            var result = TradeRepublicTextNormalizer.Normalize("  line1  \n  line2  ");
            Assert.Equal("line1\nline2", result);
        }

        [Fact]
        public void preserves_up_to_two_consecutive_blank_lines()
        {
            // 3 newlines = 2 blank lines; plan says "more than two" should collapse
            var input = "line1\n\n\nline2";
            var result = TradeRepublicTextNormalizer.Normalize(input);
            Assert.Equal("line1\n\n\nline2", result);
        }

        [Fact]
        public void collapses_three_or_more_consecutive_blank_lines_to_one_blank_line()
        {
            // 4 newlines = 3 blank lines → collapse to 2 newlines = 1 blank line
            var input = "line1\n\n\n\nline2";
            var result = TradeRepublicTextNormalizer.Normalize(input);
            Assert.Equal("line1\n\nline2", result);
        }

        [Fact]
        public void does_not_convert_decimal_commas()
        {
            var result = TradeRepublicTextNormalizer.Normalize("7,378349");
            Assert.Equal("7,378349", result);
        }

        [Fact]
        public void does_not_convert_date_strings()
        {
            var result = TradeRepublicTextNormalizer.Normalize("02.03.2026");
            Assert.Equal("02.03.2026", result);
        }

        [Fact]
        public void preserves_newlines_between_lines()
        {
            var result = TradeRepublicTextNormalizer.Normalize("line1\nline2\nline3");
            Assert.Equal("line1\nline2\nline3", result);
        }
    }
}