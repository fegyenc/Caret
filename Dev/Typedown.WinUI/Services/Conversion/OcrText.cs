using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: reading a scan (a PDF without a text layer, or a photo of a page) with the OCR engine that Windows has built in
    // (Services/WindowsOcr.cs: Windows.Media.Ocr; offline, nothing is uploaded; not an LLM and no key). The engine gives lines of text with where each is on
    // the page. This turns them into Markdown paragraphs, by the gaps between the lines. Plain .NET, so the tests compile it as it is.
    internal sealed record OcrTextLine(string Text, double Top, double Bottom, double Left = 0);

    // What the engine of the host offers (the Windows one in the app, a fake in the tests). Null in a conversion that is not asked to read scans.
    internal interface IOcrProvider
    {
        // Whether a scan can be read at all (Windows has an OCR language that can be used).
        bool IsAvailable { get; }

        // The text of the pages of a PDF (those in `pages`; all when it is all), as lines per page; null when it cannot be read (no OCR language, a PDF that
        // Windows cannot open).
        Task<IReadOnlyList<IReadOnlyList<OcrTextLine>>> RecognizePdfAsync(string path, PdfPageRange pages, CancellationToken cancellationToken);

        // The lines of a picture file (png, jpg, bmp, tif); null when it cannot be read.
        Task<IReadOnlyList<OcrTextLine>> RecognizeImageAsync(string path, CancellationToken cancellationToken);
    }

    internal static class OcrText
    {
        // A line that opens a list item: a bullet, or a dash, followed by a space.
        private static readonly Regex Bullet = new(@"^[•·●▪◦‣\-–—*]\s+", RegexOptions.CultureInvariant);

        // The lines of each page as Markdown: a paragraph per block of lines (a gap between lines wider than a part of the height of a line starts a new
        // one), words broken by a hyphen at the end of a line joined, bullets as list items. Pages follow one another as paragraphs. Empty when there is no text.
        public static string ToMarkdown(IEnumerable<IReadOnlyList<OcrTextLine>> pages)
        {
            var blocks = new List<string>();
            foreach (var page in pages ?? Enumerable.Empty<IReadOnlyList<OcrTextLine>>())
                blocks.AddRange(Paragraphs(page));
            return string.Join("\n\n", blocks.Where(b => b.Length > 0)).Trim();
        }

        public static string ToMarkdown(IReadOnlyList<OcrTextLine> page) => ToMarkdown(new[] { page });

        internal static List<string> Paragraphs(IReadOnlyList<OcrTextLine> page)
        {
            var lines = (page ?? Array.Empty<OcrTextLine>()).Where(l => !string.IsNullOrWhiteSpace(l.Text)).OrderBy(l => l.Top).ThenBy(l => l.Left).ToList();
            var blocks = new List<string>();
            if (lines.Count == 0) return blocks;
            var heights = lines.Select(l => l.Bottom - l.Top).Where(h => h > 0).OrderBy(h => h).ToList();
            var height = heights.Count > 0 ? heights[heights.Count / 2] : 10;
            var current = new StringBuilder();
            OcrTextLine previous = null;
            foreach (var line in lines)
            {
                var text = Regex.Replace(line.Text.Trim(), @"\s+", " ");
                var bullet = Bullet.IsMatch(text);
                // what the engine read is text: characters that would be Markdown ("*", "_", "<tax>") are escaped; the "- " of a list item is ours
                text = MarkdownText.EscapeInline(bullet ? Bullet.Replace(text, "") : text);
                if (bullet) text = "- " + text;
                var gap = previous == null ? 0 : line.Top - previous.Bottom;
                // (a line that carries on a list item, with no gap, stays in the item)
                var newBlock = previous != null && (gap > height * 0.6 || bullet);
                if (newBlock && current.Length > 0)
                {
                    // consecutive list items are one list: one line end between them, not a blank line
                    blocks.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) Append(current, text);
                else current.Append(bullet ? text : MarkdownText.EscapeBlockStart(text));
                previous = line;
            }
            if (current.Length > 0) blocks.Add(current.ToString());
            return JoinLists(blocks);
        }

        // A word cut at the end of a line ("conver-" / "sion") is one word again; any other line end is a space.
        private static void Append(StringBuilder block, string text)
        {
            if (block.Length > 1 && block[^1] == '-' && char.IsLetter(block[^2]) && text.Length > 0 && char.IsLower(text[0])) block.Length--;
            else block.Append(' ');
            block.Append(text);
        }

        // Items of a list come out as separate blocks: they are put together again with one line end between them.
        private static List<string> JoinLists(List<string> blocks)
        {
            var joined = new List<string>();
            foreach (var block in blocks)
            {
                if (block.StartsWith("- ", StringComparison.Ordinal) && joined.Count > 0 && joined[^1].StartsWith("- ", StringComparison.Ordinal))
                    joined[^1] += "\n" + block;
                else joined.Add(block);
            }
            return joined;
        }
    }
}
