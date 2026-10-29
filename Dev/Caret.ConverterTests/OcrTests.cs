using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Reading scans with the OCR of the host (Services/Conversion/OcrText.cs, DocumentConverter): the engine of Windows is replaced by a fake that gives
    // lines of text with their places on the page.
    public class OcrTests
    {
        private static OcrTextLine Line(string text, double top, double height = 10, double left = 0) => new(text, top, top + height, left);

        private sealed class FakeOcr : IOcrProvider
        {
            public bool IsAvailable { get; init; } = true;
            public IReadOnlyList<IReadOnlyList<OcrTextLine>> Pages { get; init; }
            public IReadOnlyList<OcrTextLine> Picture { get; init; }
            public int Calls { get; private set; }
            public PdfPageRange AskedPages { get; private set; }

            public Task<IReadOnlyList<IReadOnlyList<OcrTextLine>>> RecognizePdfAsync(string path, PdfPageRange pages, CancellationToken cancellationToken)
            {
                Calls++;
                AskedPages = pages;
                return Task.FromResult(Pages);
            }

            public Task<IReadOnlyList<OcrTextLine>> RecognizeImageAsync(string path, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Picture);
            }
        }

        [Fact]
        public void Lines_close_together_are_one_paragraph_and_a_gap_starts_another()
        {
            var markdown = OcrText.ToMarkdown(new[]
            {
                Line("The invoice is due", 100), Line("in thirty days.", 112),
                Line("Pay to the account below.", 150),
            });
            Assert.Equal("The invoice is due in thirty days.\n\nPay to the account below.", markdown);
        }

        [Fact]
        public void What_the_engine_read_is_text_and_not_markdown()
        {
            var markdown = OcrText.ToMarkdown(new[] { Line("1. Terms of the *old* deal", 100), Line("File a_b and <tax> and `x`", 112), Line("• item with * star", 150) });
            Assert.StartsWith("1\\. Terms of the \\*old\\* deal", markdown);
            Assert.Contains("\\`x\\`", markdown);
            Assert.DoesNotContain("<tax>", markdown.Replace("\\<tax>", ""));   // never as an HTML tag (it is escaped or written as an entity)
            Assert.Contains("\n\n- item with \\* star", markdown);   // the "- " of a list item is ours, the star in it is text
        }

        [Fact]
        public void The_lines_are_read_from_the_top_of_the_page_down_whatever_order_the_engine_gives_them()
        {
            var markdown = OcrText.ToMarkdown(new[] { Line("second", 150), Line("first", 100) });
            Assert.Equal("first\n\nsecond", markdown);
        }

        [Fact]
        public void A_word_cut_by_a_hyphen_at_the_end_of_a_line_is_joined_and_a_dash_before_a_capital_is_not()
        {
            Assert.Equal("The conversion works.", OcrText.ToMarkdown(new[] { Line("The conver-", 100), Line("sion works.", 112) }));
            Assert.Equal("Item A- B", OcrText.ToMarkdown(new[] { Line("Item A-", 100), Line("B", 112) }));
        }

        [Fact]
        public void Bullets_become_a_list_with_the_wrapped_line_kept_in_its_item()
        {
            var markdown = OcrText.ToMarkdown(new[]
            {
                Line("Terms:", 100),
                Line("• first item that is long", 130), Line("and goes on", 142),
                Line("• second item", 154),
                Line("Thank you.", 200),
            });
            Assert.Equal("Terms:\n\n- first item that is long and goes on\n- second item\n\nThank you.", markdown);
        }

        [Fact]
        public void Pages_follow_each_other_and_a_page_without_text_is_left_out()
        {
            var markdown = OcrText.ToMarkdown(new IReadOnlyList<OcrTextLine>[] { new[] { Line("One", 10) }, Array.Empty<OcrTextLine>(), new[] { Line("Three", 10) } });
            Assert.Equal("One\n\nThree", markdown);
            Assert.Equal("", OcrText.ToMarkdown(Array.Empty<IReadOnlyList<OcrTextLine>>()));
            Assert.Equal("", OcrText.ToMarkdown(new[] { Line("   ", 10) }));
        }

        private static string BlankPdf(string folder)
        {
            var path = Path.Combine(folder, "scan.pdf");
            File.WriteAllBytes(path, SampleFactory.BlankPdf());
            return path;
        }

        [Fact]
        public async Task A_pdf_without_a_text_layer_is_read_with_the_ocr_of_the_host_and_says_so()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                var ocr = new FakeOcr { Pages = new IReadOnlyList<OcrTextLine>[] { new[] { Line("Scanned invoice 456", 10) } } };
                var result = await DocumentConverter.ConvertAsync(BlankPdf(folder), new ConversionOptions { Ocr = ocr, PdfPages = "1" });
                Assert.Equal("Scanned invoice 456\n", result.Markdown);
                Assert.Contains(ConversionWarning.ReadByOcr, result.Warnings);
                Assert.Equal(1, ocr.Calls);
                Assert.True(ocr.AskedPages.Contains(1) && !ocr.AskedPages.Contains(2));   // the pages of the box are the pages that are read
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public async Task Without_an_ocr_the_scan_is_reported_as_before_and_with_no_language_it_is_said_to_be_unavailable()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                var path = BlankPdf(folder);
                var none = await DocumentConverter.ConvertAsync(path, new ConversionOptions());
                Assert.Equal("", none.Markdown);
                Assert.Contains(ConversionWarning.PdfHasNoText, none.Warnings);
                Assert.DoesNotContain(ConversionWarning.ReadByOcr, none.Warnings);
                var unavailable = await DocumentConverter.ConvertAsync(path, new ConversionOptions { Ocr = new FakeOcr { IsAvailable = false } });
                Assert.Equal("", unavailable.Markdown);
                Assert.Contains(ConversionWarning.OcrUnavailable, unavailable.Warnings);
                var nothing = await DocumentConverter.ConvertAsync(path, new ConversionOptions { Ocr = new FakeOcr { Pages = Array.Empty<IReadOnlyList<OcrTextLine>>() } });
                Assert.Contains(ConversionWarning.OcrFoundNoText, nothing.Warnings);
                var failed = await DocumentConverter.ConvertAsync(path, new ConversionOptions { Ocr = new FakeOcr { Pages = null } });
                Assert.Contains(ConversionWarning.OcrUnavailable, failed.Warnings);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public async Task A_pdf_with_text_is_never_sent_to_the_ocr()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(folder, "text.pdf");
                File.WriteAllBytes(path, PdfLayoutSample.Build());
                var ocr = new FakeOcr { Pages = new IReadOnlyList<OcrTextLine>[] { new[] { Line("should not be used", 10) } } };
                var result = await DocumentConverter.ConvertAsync(path, new ConversionOptions { Ocr = ocr });
                Assert.NotEmpty(result.Markdown);
                Assert.Equal(0, ocr.Calls);
                Assert.DoesNotContain(ConversionWarning.ReadByOcr, result.Warnings);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public async Task A_picture_is_read_with_the_ocr_but_is_not_a_supported_document()
        {
            Assert.False(DocumentConverter.IsSupported("photo.png"));   // dropping a picture on the window still inserts it in the note
            Assert.True(DocumentConverter.IsOcrPicture("Photo.JPG"));
            Assert.False(DocumentConverter.IsOcrPicture("report.pdf"));
            var ocr = new FakeOcr { Picture = new[] { Line("Receipt total 12.50", 10) } };
            var result = await DocumentConverter.ConvertAsync("photo.png", new ConversionOptions { Ocr = ocr });
            Assert.Equal("Receipt total 12.50\n", result.Markdown);
            Assert.Contains(ConversionWarning.ReadByOcr, result.Warnings);
            var without = await DocumentConverter.ConvertAsync("photo.png", new ConversionOptions());
            Assert.Contains(ConversionWarning.OcrUnavailable, without.Warnings);
        }
    }
}
