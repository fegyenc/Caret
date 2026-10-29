using System.IO;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Reading only some pages of a PDF (Services/Conversion/PdfPageRange.cs, ConversionOptions.PdfPages).
    public class PdfPageRangeTests
    {
        [Theory]
        [InlineData("3", new[] { 3 }, new[] { 1, 2, 4 })]
        [InlineData("2-4", new[] { 2, 3, 4 }, new[] { 1, 5 })]
        [InlineData("1-3, 5, 8-", new[] { 1, 3, 5, 8, 99 }, new[] { 4, 6, 7 })]
        [InlineData("-2", new[] { 1, 2 }, new[] { 3 })]
        [InlineData("  2 – 3 ;7 ", new[] { 2, 3, 7 }, new[] { 4 })]
        public void A_range_is_read_the_way_it_is_written(string text, int[] inside, int[] outside)
        {
            var range = PdfPageRange.Parse(text);
            Assert.NotNull(range);
            Assert.False(range.IsAll);
            foreach (var page in inside) Assert.True(range.Contains(page), $"{page} in {text}");
            foreach (var page in outside) Assert.False(range.Contains(page), $"{page} not in {text}");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Nothing_written_is_all_the_pages(string text)
        {
            var range = PdfPageRange.Parse(text);
            Assert.NotNull(range);
            Assert.True(range.IsAll);
            Assert.True(range.Contains(12345));
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("5-2")]
        [InlineData("-")]
        [InlineData("1,,x")]
        [InlineData("1.5")]
        [InlineData("1 2")]          // two pages without a comma, not page 12
        [InlineData("1 - 3 4")]
        [InlineData("99999999999")]
        public void Text_that_is_not_pages_is_not_a_range(string text) => Assert.Null(PdfPageRange.Parse(text));

        [Fact]
        public void The_range_is_said_back_tidied()
        {
            Assert.Equal("1-3, 5, 8-", PdfPageRange.Parse("1 - 3;5, 8-").Text);
            Assert.Equal("2", PdfPageRange.Parse("2-2").Text);
        }

        [Fact]
        public void Only_the_pages_asked_for_are_converted()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(folder, "pages.pdf");
                File.WriteAllBytes(path, PdfLayoutSample.Build());
                string Convert(string pages, out ConversionResult result)
                {
                    result = DocumentConverter.Convert(path, new ConversionOptions { PdfPages = pages });
                    return result.Markdown;
                }
                var all = Convert(null, out _);
                var second = Convert("2", out _);
                Assert.NotEmpty(second);
                Assert.True(second.Length < all.Length);
                var firstPage = Convert("1", out _);
                Assert.NotEqual(firstPage, second);
                Assert.Empty(Convert("40-50", out var outOfRange));
                Assert.Contains(ConversionWarning.PdfPagesOutOfRange, outOfRange.Warnings);
                Assert.Empty(Convert("two", out var notValid));
                Assert.Contains(ConversionWarning.PdfPagesNotValid, notValid.Warnings);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
