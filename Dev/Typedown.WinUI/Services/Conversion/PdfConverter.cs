using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Typedown.WinUI.Services.Conversion
{
    // .pdf → Markdown from the text layer. A PDF has no paragraphs, headings, lists or tables — only
    // positioned glyphs — so structure is rebuilt from layout:
    //   - words → lines by vertical overlap (a bullet glyph or superscript stays on its own line);
    //   - two-column pages are read column by column when there is a clear gutter;
    //   - a new paragraph starts at a font-size change, a larger gap, or a list marker;
    //   - headings come from font size relative to the body text (bold short lines are minor headings);
    //   - rows of cells separated by wide gaps and aligned across lines become a table;
    //   - running headers/footers and page numbers are dropped; "exam-\nple" is rejoined.
    // Scanned PDFs have no text layer (there is no OCR) and are reported as such.
    internal static class PdfConverter
    {
        private sealed class WordBox
        {
            public string Text;
            public double Left, Right, Top, Bottom, Size;
            public bool Bold;
            public double Center => (Top + Bottom) / 2;
            public double Height => Math.Max(Top - Bottom, 0.1);
        }

        private sealed class Line
        {
            public int Page;
            public List<WordBox> Words = new();
            public double Top => Words.Max(w => w.Top);
            public double Bottom => Words.Min(w => w.Bottom);
            public double Left => Words.Min(w => w.Left);
            public double Size => Median(Words.SelectMany(w => Enumerable.Repeat(w.Size, Math.Max(1, w.Text.Length))));
            public bool Bold => Words.Sum(w => w.Bold ? w.Text.Length : 0) > Words.Sum(w => w.Text.Length) / 2;
            public bool IsEdge;
            public string Text => string.Join(" ", Words.Select(w => w.Text));
        }

        private static readonly Regex NumberedMarker = new(@"^\(?\d{1,3}[.)]$", RegexOptions.Compiled);

        public static string Convert(Stream stream, ConversionContext context)
        {
            using var pdf = PdfDocument.Open(stream);
            var lines = new List<Line>();
            foreach (var page in pdf.GetPages())
            {
                var words = page.GetWords()
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .Select(w => new WordBox
                    {
                        Text = w.Text.Trim(),
                        Left = w.BoundingBox.Left,
                        Right = w.BoundingBox.Right,
                        Top = w.BoundingBox.Top,
                        Bottom = w.BoundingBox.Bottom,
                        Size = w.Letters.Count > 0 ? Median(w.Letters.Select(l => l.PointSize)) : 10,
                        Bold = w.Letters.Count > 0 && w.Letters.Count(l => IsBoldFont(l.FontName)) * 2 > w.Letters.Count,
                    }).ToList();
                if (words.Count == 0) continue;
                lines.AddRange(PageLines(words, page.Number, page.Width, page.Height));
            }
            if (lines.Count == 0)
            {
                context.Warnings.Add(ConversionWarning.PdfHasNoText);
                return "";
            }

            var bodySize = Mode(lines.Where(l => !l.IsEdge).SelectMany(l => l.Words).SelectMany(w => Enumerable.Repeat(Math.Round(w.Size, 1), w.Text.Length)));
            var pageCount = lines.Max(l => l.Page);
            var repeated = RepeatedEdgeText(lines, pageCount);
            lines = lines.Where(l => !(l.IsEdge && (repeated.Contains(Normalize(l.Text)) || Regex.IsMatch(l.Text, @"^(page\s*)?\d+(\s*(/|of|sur|de)\s*\d+)?$", RegexOptions.IgnoreCase)))).ToList();

            var bulletLefts = lines.Where(l => IsBulletMarker(l.Words[0])).Select(l => l.Words[0].Left).DefaultIfEmpty(0).ToList();
            var bulletBase = bulletLefts.Min();
            var lineStep = Median(lines.Zip(lines.Skip(1), (a, b) => (a, b))
                .Where(p => p.a.Page == p.b.Page && Math.Abs(p.a.Size - bodySize) < 0.6 && Math.Abs(p.b.Size - bodySize) < 0.6 && p.a.Bottom > p.b.Bottom)
                .Select(p => p.a.Bottom - p.b.Bottom).Where(d => d > 0 && d < bodySize * 3).DefaultIfEmpty(bodySize * 1.2));

            // Heading levels by rank of font size: the largest heading size is "#", the next "##"...
            // (so a document title and its section headings don't both become "#").
            var headingSizes = lines.Where(l => l.Size / bodySize >= 1.12).Select(l => Math.Round(l.Size * 2) / 2)
                .Distinct().OrderByDescending(s => s).ToList();
            int HeadingRank(double size) => Math.Clamp(headingSizes.IndexOf(Math.Round(size * 2) / 2) + 1, 1, 3);

            var blocks = new List<string>();
            var paragraph = new StringBuilder();
            string paragraphKind = null; // "p", "h" (by size), "h3" (bold line), "li"
            double paragraphSize = 0;
            Line previous = null;

            void Flush()
            {
                if (paragraph.Length == 0) { paragraphKind = null; return; }
                // List items keep their leading indentation (nesting).
                var text = paragraphKind == "li" ? paragraph.ToString().TrimEnd() : paragraph.ToString().Trim();
                blocks.Add(paragraphKind switch
                {
                    "h" => new string('#', HeadingRank(paragraphSize)) + " " + Inline(text),
                    "h3" => "### " + Inline(text),
                    "li" => text, // already formatted
                    _ => MarkdownText.EscapeBlockStart(Inline(text)),
                });
                paragraph.Clear();
                paragraphKind = null;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // A table: two or more consecutive lines split into the same number of aligned cells.
                var table = TryTable(lines, i, out var consumed);
                if (table != null)
                {
                    Flush();
                    blocks.Add(table);
                    i += consumed - 1;
                    previous = null;
                    continue;
                }

                var ratio = line.Size / bodySize;
                var kind = ratio >= 1.12 ? "h" : "p";
                if (kind == "p" && line.Bold && line.Text.Length < 90 && !line.Text.EndsWith('.') && !IsBulletMarker(line.Words[0])
                    && (previous == null || !previous.Bold || previous.Page != line.Page))
                    kind = "h3";

                var first = line.Words[0];
                var isBullet = IsBulletMarker(first) && line.Words.Count > 1;
                var isNumbered = NumberedMarker.IsMatch(first.Text) && line.Words.Count > 1 && kind == "p";
                // A flow break: a new page, or a jump back up to the top of the next column.
                var flowBreak = previous != null && (previous.Page != line.Page || line.Bottom > previous.Bottom);
                var gap = previous != null && !flowBreak ? previous.Bottom - line.Bottom : double.MaxValue;
                // A body paragraph that runs over a page or column break: the last line had no closing
                // punctuation and this one starts in lower case.
                var continuesOverPage = flowBreak && kind == "p" && (paragraphKind == "p" || paragraphKind == "li")
                    && !Regex.IsMatch(paragraph.ToString().TrimEnd(), @"[.!?:;»""”)]$") && char.IsLower(line.Text[0]);
                var newParagraph = previous == null || paragraphKind == null
                    || isBullet || isNumbered
                    || kind != (paragraphKind == "li" ? "p" : paragraphKind)
                    || Math.Abs(line.Size - previous.Size) > bodySize * 0.12
                    || (gap > lineStep * 1.45 && !continuesOverPage);

                if (newParagraph)
                {
                    Flush();
                    if (isBullet || isNumbered)
                    {
                        var level = isBullet ? (int)Math.Clamp(Math.Round((first.Left - bulletBase) / 18.0), 0, 5) : 0;
                        var marker = isBullet ? "-" : "1.";
                        var rest = string.Join(" ", line.Words.Skip(1).Select(w => w.Text));
                        paragraph.Append(new string(' ', 4 * level)).Append(marker).Append(' ').Append(Inline(rest));
                        paragraphKind = "li";
                    }
                    else
                    {
                        paragraph.Append(line.Text);
                        paragraphKind = kind;
                        paragraphSize = line.Size;
                    }
                }
                else
                {
                    var text = paragraphKind == "li" ? Inline(line.Text) : line.Text;
                    // "exam-" + "ple" → "example"
                    if (paragraph.Length > 1 && paragraph[^1] == '-' && char.IsLetter(paragraph[^2]) && char.IsLower(text[0]))
                        paragraph.Length--;
                    else
                        paragraph.Append(' ');
                    paragraph.Append(text);
                }
                previous = line;
            }
            Flush();

            var sb = new StringBuilder();
            for (var i = 0; i < blocks.Count; i++)
            {
                if (i > 0) sb.Append(IsListItem(blocks[i - 1]) && IsListItem(blocks[i]) ? "\n" : "\n\n");
                sb.Append(blocks[i]);
            }
            return sb.ToString();
        }

        private static bool IsListItem(string block) => Regex.IsMatch(block, @"^\s*(- |1\. )");

        // Words of one page → lines in reading order; a page with a clear vertical gutter is read as
        // two columns, with full-width lines (titles spanning both) kept in place.
        private static IEnumerable<Line> PageLines(List<WordBox> words, int pageNumber, double width, double height)
        {
            var gutter = FindGutter(words, width);
            var all = GroupLines(words, pageNumber);
            foreach (var line in all)
                line.IsEdge = line.Top > height * 0.93 || line.Bottom < height * 0.07;
            if (gutter == null)
                return all;

            var g = gutter.Value;
            var ordered = new List<Line>();
            var left = new List<Line>();
            var right = new List<Line>();
            void FlushColumns()
            {
                ordered.AddRange(left);
                ordered.AddRange(right);
                left.Clear();
                right.Clear();
            }
            foreach (var line in all)
            {
                if (line.Words.Any(w => w.Left < g && w.Right > g))
                {
                    FlushColumns();
                    ordered.Add(line);
                    continue;
                }
                var l = line.Words.Where(w => w.Right <= g).ToList();
                var r = line.Words.Where(w => w.Left >= g).ToList();
                if (l.Count > 0) left.Add(new Line { Page = pageNumber, Words = l, IsEdge = line.IsEdge });
                if (r.Count > 0) right.Add(new Line { Page = pageNumber, Words = r, IsEdge = line.IsEdge });
            }
            FlushColumns();
            return ordered;
        }

        private static List<Line> GroupLines(List<WordBox> words, int pageNumber)
        {
            // Each line is matched against its "core" word — the largest one so far — rather than the
            // first: a small superscript (a footnote number) would otherwise anchor the line and push
            // short words like "non" onto a line of their own.
            var lines = new List<(WordBox Core, Line Line)>();
            foreach (var word in words.OrderByDescending(w => w.Size).ThenByDescending(w => w.Center))
            {
                var index = lines.FindIndex(x =>
                {
                    var overlap = Math.Min(x.Core.Top, word.Top) - Math.Max(x.Core.Bottom, word.Bottom);
                    return overlap >= 0.5 * Math.Min(x.Core.Height, word.Height);
                });
                if (index >= 0) lines[index].Line.Words.Add(word);
                else lines.Add((word, new Line { Page = pageNumber, Words = { word } }));
            }
            foreach (var (_, line) in lines) line.Words.Sort((a, b) => a.Left.CompareTo(b.Left));
            return lines.Select(x => x.Line).OrderByDescending(l => l.Top).ToList();
        }

        // A vertical strip in the middle of the page that almost no word crosses, with plenty of text
        // on both sides.
        private static double? FindGutter(List<WordBox> words, double width)
        {
            if (words.Count < 40) return null;
            double? best = null;
            var bestCrossing = int.MaxValue;
            for (var x = width * 0.35; x <= width * 0.65; x += 2)
            {
                var crossing = words.Count(w => w.Left < x && w.Right > x);
                if (crossing < bestCrossing) { bestCrossing = crossing; best = x; }
            }
            if (best == null || bestCrossing > words.Count * 0.03) return null;
            var g = best.Value;
            var leftCount = words.Count(w => w.Right <= g);
            var rightCount = words.Count(w => w.Left >= g);
            if (leftCount < words.Count * 0.25 || rightCount < words.Count * 0.25) return null;
            // A real gutter is empty for a while on both sides, not just a lucky line between words.
            var nearGutter = words.Count(w => w.Right > g - 6 && w.Left < g + 6);
            return nearGutter <= words.Count * 0.03 ? g : null;
        }

        private static string TryTable(List<Line> lines, int start, out int consumed)
        {
            consumed = 0;
            var rows = new List<List<(double Left, string Text)>>();
            for (var i = start; i < lines.Count; i++)
            {
                if (rows.Count > 0 && lines[i].Page != lines[i - 1].Page) break;
                var cells = Cells(lines[i]);
                if (cells.Count < 2) break;
                if (rows.Count > 0 && (cells.Count != rows[0].Count || !Aligned(cells, rows[0]))) break;
                rows.Add(cells);
            }
            if (rows.Count < 2) return null;
            consumed = rows.Count;
            return MarkdownText.Table(rows.Select(r => (IReadOnlyList<string>)r.Select(c => MarkdownText.EscapeInline(c.Text)).ToList()).ToList());
        }

        // A line split into cells where the space between words is much wider than a normal space.
        private static List<(double Left, string Text)> Cells(Line line)
        {
            var cells = new List<(double Left, string Text)>();
            var gapLimit = Math.Max(10, line.Size * 1.8);
            var current = new List<WordBox> { line.Words[0] };
            for (var i = 1; i < line.Words.Count; i++)
            {
                if (line.Words[i].Left - line.Words[i - 1].Right > gapLimit)
                {
                    cells.Add((current[0].Left, string.Join(" ", current.Select(w => w.Text))));
                    current = new List<WordBox>();
                }
                current.Add(line.Words[i]);
            }
            cells.Add((current[0].Left, string.Join(" ", current.Select(w => w.Text))));
            return cells;
        }

        private static bool Aligned(List<(double Left, string Text)> a, List<(double Left, string Text)> b) =>
            a.Zip(b, (x, y) => Math.Abs(x.Left - y.Left) < 25).All(ok => ok);

        private static bool IsBulletMarker(WordBox word)
        {
            if (word.Text.Length != 1) return false;
            var c = word.Text[0];
            return "•●○◦▪▫■□‣⁃–-·*".Contains(c) || (c >= '' && c <= '') || (c == 'o' && word.Size <= 14 && word.Right - word.Left < word.Size);
        }

        private static bool IsBoldFont(string name) =>
            name != null && (name.Contains("Bold", StringComparison.OrdinalIgnoreCase) || name.Contains("Black", StringComparison.OrdinalIgnoreCase) || name.Contains("Heavy", StringComparison.OrdinalIgnoreCase) || name.Contains("Semibold", StringComparison.OrdinalIgnoreCase));

        private static string Inline(string text) => MarkdownText.EscapeInline(Regex.Replace(text, @"\s+", " ").Trim());

        private static string Normalize(string text) => Regex.Replace(text, @"\d+", "#").Trim().ToLowerInvariant();

        private static HashSet<string> RepeatedEdgeText(List<Line> lines, int pageCount)
        {
            if (pageCount < 3) return new HashSet<string>();
            return lines.Where(l => l.IsEdge)
                .GroupBy(l => Normalize(l.Text))
                .Where(g => g.Select(l => l.Page).Distinct().Count() >= Math.Max(3, pageCount / 2))
                .Select(g => g.Key)
                .ToHashSet();
        }

        private static double Median(IEnumerable<double> values)
        {
            var list = values.OrderBy(v => v).ToList();
            return list.Count == 0 ? 0 : list[list.Count / 2];
        }

        private static double Mode(IEnumerable<double> values)
        {
            var groups = values.GroupBy(v => v).OrderByDescending(g => g.Count()).ToList();
            return groups.Count == 0 ? 10 : groups[0].Key;
        }
    }
}
