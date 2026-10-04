using System;
using System.IO;
using System.Linq;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Each of these was a way a real PDF came out wrong (an arXiv paper, a magazine, a set of annual accounts, a system card);
    // samples/pdf-layout.pdf has all of them in three pages. The whole expected Markdown is compared too (SampleTests); these
    // say what each part is for.
    public class PdfTests : IDisposable
    {
        private readonly string work = TestPaths.NewTempFolder();

        public void Dispose() => Directory.Delete(work, true);

        private string Convert() => SampleTests.Convert("pdf-layout.pdf", work);

        [Fact]
        public void A_stamp_in_the_margin_is_not_part_of_the_text()
        {
            var markdown = Convert();
            Assert.DoesNotContain("arXiv", markdown);
            Assert.DoesNotContain("2609", markdown);
            // It used to come out letter by letter as headings: "## 6 2 0 2"
            Assert.DoesNotContain(markdown.Split('\n').Where(l => l.StartsWith('#')), h => h.TrimStart('#', ' ').Length <= 2);
        }

        [Fact]
        public void Columns_are_read_one_after_the_other_even_above_a_full_width_block()
        {
            var markdown = Convert();
            var left = markdown.IndexOf("The first column holds the start of a sentence", StringComparison.Ordinal);
            var end = markdown.IndexOf("a short closing line ends the story.", StringComparison.Ordinal);
            var right = markdown.IndexOf("so that a reader follows the left side first", StringComparison.Ordinal);
            var caption = markdown.IndexOf("Figure 1. A caption", StringComparison.Ordinal);
            Assert.True(left >= 0 && end > left && right > end && caption > right, "left column, then right column, then the caption");
            Assert.DoesNotContain("| The first column", markdown); // not a two-cell table of lines
        }

        [Fact]
        public void A_table_with_rules_keeps_its_numbers_and_puts_each_heading_over_its_columns()
        {
            var markdown = Convert();
            Assert.Contains("| Task | Baseline Alpha | Baseline Beta | Tuned Alpha | Tuned Beta |", markdown);
            // "+78", "." and "0" are drawn as three pieces: one number
            Assert.Contains("| Conceal results | 22.0 | 1.0 | +78.0 | +94.5 |", markdown);
        }

        [Fact]
        public void A_table_with_a_rule_under_every_row_has_one_row_per_rule()
        {
            var markdown = Convert();
            Assert.Contains("| Item | Notes | 2024 | 2023 |", markdown);
            Assert.Contains("| Cash and equivalents |  | 119 502 | 170 299 |", markdown);
            Assert.Contains("| Receivables | 3 | 119 502 | 170 299 |", markdown);
        }

        [Fact]
        public void Web_links_are_kept_and_one_that_wraps_is_written_once()
        {
            var markdown = Convert();
            Assert.Contains("<https://example.com/docs/guide-to-pages>", markdown);
            Assert.Equal(1, markdown.Split("https://example.com/docs/guide-to-pages").Length - 1);
            // The full stop after a link is the sentence's
            Assert.Contains("[Project site](https://example.com/).", markdown);
        }

        [Fact]
        public void A_hyphen_at_the_end_of_a_line_is_kept_in_a_compound_and_dropped_in_a_split_word()
        {
            var markdown = Convert();
            Assert.Contains("narrative-changing ones", markdown); // the document writes "narrative-changing" in full elsewhere
            Assert.Contains("the word example is split", markdown);
        }

        [Fact]
        public void A_bold_lead_in_stays_in_its_paragraph()
        {
            var markdown = Convert();
            Assert.Contains("**Summary.** This paragraph starts with a bold lead-in", markdown);
            Assert.DoesNotContain("### Summary", markdown);
        }

        [Fact]
        public void Code_keeps_its_indentation_and_a_contents_page_becomes_a_list()
        {
            var markdown = Convert();
            Assert.Contains("```\ndef total(items):\n    result = 0\n    for item in items:\n        result += item.price\n    return result\n```", markdown.Replace("\r\n", "\n"));
            Assert.Contains("- Results and discussion … 7", markdown);
            Assert.DoesNotContain("....", markdown);
        }

        [Fact]
        public void Running_headers_and_page_numbers_are_left_out()
        {
            var markdown = Convert();
            Assert.DoesNotContain("Layout Test Report", markdown);
        }
    }
}
