using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    // The watermark, header, footer and page numbers of an exported PDF (Services/PdfExportMarks.cs).
    public class PdfExportMarksTests
    {
        [Fact]
        public void Nothing_asked_for_adds_nothing()
        {
            Assert.False(PdfExportMarks.Any(null, "  ", "", false));
            Assert.Equal("", PdfExportMarks.Css(null, " ", null, false));
            Assert.False(PdfExportMarks.InMargins("", " ", false));
        }

        [Theory]
        [InlineData("131.0.2903.51", true)]
        [InlineData("142.0.1.0", true)]
        [InlineData("130.0.2849.80", false)]
        [InlineData("99.0.1", false)]
        [InlineData("", true)]        // a version that cannot be read is taken as new enough
        [InlineData(null, true)]
        [InlineData("beta", true)]
        public void The_margin_boxes_are_printed_from_chromium_131(string version, bool supported) =>
            Assert.Equal(supported, PdfExportMarks.MarginBoxesSupported(version));

        [Fact]
        public void A_watermark_is_an_element_fixed_to_the_page_so_that_each_printed_page_has_it()
        {
            var css = PdfExportMarks.Css("CONFIDENTIAL", null, null, false);
            Assert.Contains("@media print", css);
            Assert.Contains("position: fixed", css);
            Assert.Contains("rotate(-35deg)", css);
            Assert.DoesNotContain("@page", css);   // the margins are left as they are when only the watermark is asked for
            Assert.True(PdfExportMarks.Any("CONFIDENTIAL", null, null, false));
        }

        [Fact]
        public void The_header_the_footer_and_the_page_numbers_are_margin_boxes_of_the_page()
        {
            var css = PdfExportMarks.Css(null, "Contract — draft 3", "Acme Ltd", true);
            Assert.Contains("@top-center { content: \"Contract — draft 3\";", css);
            Assert.Contains("@bottom-left { content: \"Acme Ltd\";", css);
            Assert.Contains("@bottom-right { content: counter(page) \" / \" counter(pages);", css);
            Assert.Contains("margin: 0.7in", css);
            Assert.True(PdfExportMarks.InMargins("x", null, false));
            Assert.True(PdfExportMarks.InMargins(null, null, true));
        }

        [Fact]
        public void A_text_cannot_leave_its_css_string()
        {
            Assert.Equal("\"a\\\"b\\\\c d\"", PdfExportMarks.CssString("a\"b\\c\nd"));
            var css = PdfExportMarks.Css(null, "x\"; } body { display: none } /*", null, false);
            // the quote that would end the string is escaped, so the rest stays inside it
            Assert.Contains("@top-center { content: \"x\\\"; } body { display: none } /*\";", css);
        }

        [Fact]
        public void A_long_watermark_is_smaller_than_a_short_one_and_never_tiny_or_huge()
        {
            Assert.True(PdfExportMarks.WatermarkSize("DRAFT") > PdfExportMarks.WatermarkSize("A text that is rather long for a watermark"));
            Assert.Equal(130, PdfExportMarks.WatermarkSize("A"));
            Assert.Equal(36, PdfExportMarks.WatermarkSize(new string('x', 500)));
        }

        [Fact]
        public void The_scripts_add_and_remove_the_style_and_the_watermark_with_the_text_as_data()
        {
            var add = PdfExportMarks.AddScript("DRAFT \"1\"", "h", null, true);
            Assert.Contains("caret-pdf-marks", add);
            Assert.Contains("caret-pdf-watermark", add);
            Assert.Contains("textContent=\"DRAFT \\u0022", add.Replace("\\\\u0022", "\\u0022"));
            Assert.DoesNotContain("caret-pdf-watermark", PdfExportMarks.AddScript(null, "h", null, false));   // no watermark element without a watermark
            var remove = PdfExportMarks.RemoveScript();
            Assert.Contains("getElementById('caret-pdf-marks')", remove);
            Assert.Contains(".caret-pdf-watermark", remove);
        }
    }
}
