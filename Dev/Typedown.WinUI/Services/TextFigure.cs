using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services
{
    // New since the fork: Edit > Copy as text figure. The table under the cursor as a table drawn with lines (┌─┬─┐), or the numbers of a table
    // or of a list ("- Apples: 12") as a bar chart drawn with blocks (████▌), put on the clipboard as text: for an e-mail, a chat, a ticket or a
    // comment in code, where a Markdown table or a picture does not go. Both line up in a fixed-width font; the clipboard has them as HTML in
    // such a font too, so Outlook and Word keep them straight.
    //
    // Plain .NET, no WinUI, tested without the app (Dev/Caret.ConverterTests/TextFigureTests.cs). Tables are found as Chart from table finds
    // them (ChartFromTable.FindTable), and numbers are read the same way (ChartFromTable.ParseNumber: 1,055 or 1.234,50 or 12 %).
    public sealed record TextBar(string Label, double? Value, string ValueText);

    public sealed record TextBarSeries(string Title, List<TextBar> Bars);

    public static class TextFigure
    {
        // The widest bar, in characters; eighths of a block make the rest.
        public const int BarWidth = 24;

        private static readonly string Eighths = "▏▎▍▌▋▊▉";

        // ---- the table ---------------------------------------------------------------------------------------------------------------

        // The table as text with lines around its cells. Each column is aligned as its delimiter row says (:--, :-:, --:); a column without
        // a colon is aligned right when it holds numbers, left otherwise. The cells lose their Markdown marks (bold, links, code).
        public static string Table(string markdown, MarkdownTable table)
        {
            var columns = table.Header.Count;
            var header = table.Header.Select(Plain).ToList();
            var rows = table.Rows.Select(row => Enumerable.Range(0, columns).Select(c => c < row.Count ? Plain(row[c]) : "").ToList()).ToList();
            var align = Alignments(markdown, table);
            for (var c = 0; c < columns; c++)
                if (align[c] == 'n') align[c] = table.IsNumeric(c) ? 'r' : 'l';
            var widths = Enumerable.Range(0, columns)
                .Select(c => Math.Max(1, rows.Select(r => Width(r[c])).Append(Width(header[c])).Max())).ToList();

            var sb = new StringBuilder();
            string Rule(char left, char middle, char right) => left + string.Join(middle, widths.Select(w => new string('─', w + 2))) + right;
            string Line(List<string> cells, bool head) =>
                "│" + string.Join("│", Enumerable.Range(0, columns).Select(c => " " + Pad(cells[c], widths[c], head ? 'c' : align[c]) + " ")) + "│";
            sb.Append(Rule('┌', '┬', '┐')).Append('\n');
            sb.Append(Line(header, true)).Append('\n');
            sb.Append(Rule('├', '┼', '┤')).Append('\n');
            foreach (var row in rows) sb.Append(Line(row, false)).Append('\n');
            sb.Append(Rule('└', '┴', '┘'));
            return sb.ToString();
        }

        // 'l', 'c', 'r' as the delimiter row of the table says, 'n' where it says nothing.
        private static List<char> Alignments(string markdown, MarkdownTable table)
        {
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            var delimiter = table.StartLine + 1 < lines.Length ? ChartFromTable.Cells(lines[table.StartLine + 1]) : new List<string>();
            return Enumerable.Range(0, table.Header.Count).Select(c =>
            {
                var cell = c < delimiter.Count ? delimiter[c].Trim() : "";
                var left = cell.StartsWith(':');
                var right = cell.EndsWith(':');
                return left && right ? 'c' : right ? 'r' : left ? 'l' : 'n';
            }).ToList();
        }

        // ---- the bar chart -----------------------------------------------------------------------------------------------------------

        private static readonly Regex ListItem = new(@"^\s*(?:[-*+]|\d{1,9}[.)])\s+(?:\[[ xX]\]\s+)?(.*)$", RegexOptions.CultureInvariant);

        // The numbers under the cursor: those of the table it is in (the labels are its first column that is not numbers, the values its first
        // column of numbers), or those of the list it is in, an item being a label and a number at its end ("Apples: 12", "North — 1.234,5").
        // Null when there are fewer than two numbers.
        public static TextBarSeries FindSeries(string markdown, int line)
        {
            var table = ChartFromTable.FindTable(markdown, line);
            if (table != null) return SeriesOf(table);
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            if (line < 0 || line >= lines.Length || !ListItem.IsMatch(lines[line])) return null;
            var start = line;
            while (start > 0 && ListItem.IsMatch(lines[start - 1])) start--;
            var end = line;
            while (end + 1 < lines.Length && ListItem.IsMatch(lines[end + 1])) end++;
            var bars = new List<TextBar>();
            for (var i = start; i <= end; i++) bars.Add(ItemBar(ListItem.Match(lines[i]).Groups[1].Value));
            return bars.Count(b => b.Value != null) >= 2 ? new TextBarSeries("", bars) : null;
        }

        private static TextBarSeries SeriesOf(MarkdownTable table)
        {
            var numeric = table.NumericColumns;
            if (numeric.Count == 0) return null;
            var labelColumn = Enumerable.Range(0, table.Header.Count).Where(c => !numeric.Contains(c)).DefaultIfEmpty(-1).First();
            var valueColumn = numeric[0];
            string Cell(List<string> row, int c) => c >= 0 && c < row.Count ? row[c] : "";
            var bars = table.Rows.Select((row, i) =>
            {
                var text = Plain(Cell(row, valueColumn));
                return new TextBar(labelColumn < 0 ? (i + 1).ToString(CultureInfo.InvariantCulture) : Plain(Cell(row, labelColumn)), ChartFromTable.ParseNumber(text), text);
            }).ToList();
            return bars.Count(b => b.Value != null) >= 2 ? new TextBarSeries(Plain(table.Header[valueColumn]), bars) : null;
        }

        // A list item as a bar: the number is the last word (with a "%" after it, if there is one), the label is the rest, without the ":" or
        // dash between them. An item with no number at its end is a label without a bar.
        private static TextBar ItemBar(string item)
        {
            var text = Plain(item).Trim();
            // the label and the number are apart: a ":" or "=", a dash between spaces, or a space ("Q1" is not Q and 1; "-5" keeps its sign)
            var match = Regex.Match(text, @"^(.*?)(?:\s*[:=]\s*|\s+[–—-]\s+|\s+)([+-]?[$€£¥]?\d[\d.,]*(?:\s?%)?)$");
            if (!match.Success || match.Groups[1].Value.Length == 0) return new TextBar(text, null, "");
            var value = ChartFromTable.ParseNumber(match.Groups[2].Value);
            if (value == null) return new TextBar(text, null, "");
            return new TextBar(match.Groups[1].Value, value, match.Groups[2].Value);
        }

        // The chart: a line for each label, its bar, and its number as the note writes it. The longest bar is the largest number (the
        // largest size, when there are negative numbers: the number keeps its sign). A title above when the numbers have one (the column's).
        public static string BarChart(TextBarSeries series)
        {
            var max = series.Bars.Where(b => b.Value != null).Select(b => Math.Abs(b.Value.Value)).DefaultIfEmpty(0).Max();
            var labelWidth = series.Bars.Select(b => Width(b.Label)).DefaultIfEmpty(0).Max();
            var valueWidth = series.Bars.Select(b => Width(b.ValueText)).DefaultIfEmpty(0).Max();
            var sb = new StringBuilder();
            if (series.Title.Length > 0) sb.Append(series.Title).Append('\n');
            foreach (var bar in series.Bars)
            {
                var line = Pad(bar.Label, labelWidth, 'l') + "  " + Pad(Bar(bar.Value, max), BarWidth, 'l') + "  " + Pad(bar.ValueText, valueWidth, 'r');
                sb.Append(line.TrimEnd()).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        private static string Bar(double? value, double max)
        {
            if (value == null || max <= 0 || value.Value == 0) return "";
            var eighths = (int)Math.Round(Math.Abs(value.Value) / max * BarWidth * 8, MidpointRounding.AwayFromZero);
            if (eighths == 0) return Eighths[0].ToString(); // a number that is not zero gets a bar you can see
            var bar = new string('█', eighths / 8);
            return eighths % 8 == 0 ? bar : bar + Eighths[eighths % 8 - 1];
        }

        // ---- text --------------------------------------------------------------------------------------------------------------------

        // The figure as HTML for the clipboard: in a fixed-width font, so the lines stay straight when pasted into a mail or a document.
        public static string Html(string figure) =>
            "<pre style=\"font-family:Consolas,'Cascadia Mono','Courier New',monospace\">" + WebUtility.HtmlEncode(figure) + "</pre>";

        // A cell or an item without its Markdown: links and pictures keep their text, the marks of bold, italics, strikethrough and code go,
        // "\|" is a pipe and <br> a space. What is inside code stays as it is written (`**x**` is **x**).
        internal static string Plain(string text)
        {
            var code = new List<string>();
            var t = Regex.Replace(text ?? "", @"(`+)(.*?)\1", m =>
            {
                code.Add(m.Groups[2].Value);
                return "\u0001" + (code.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
            });
            t = Regex.Replace(t, @"!\[([^\]]*)\]\([^)]*\)", "$1");
            t = Regex.Replace(t, @"\[([^\]]*)\]\([^)]*\)", "$1");
            t = Regex.Replace(t, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"(\*\*|__|~~)(.+?)\1", "$2");
            t = Regex.Replace(t, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "$1");
            t = Regex.Replace(t, @"(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)", "$1");
            t = Regex.Replace(t, @"\\([\\`*_{}\[\]()#+\-.!|~<>])", "$1");
            t = Regex.Replace(t, "\u0001(\\d+)\u0002", m => code[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
            return t.Trim();
        }

        // The width of a text in a fixed-width font: a wide character (Chinese, Japanese, Korean, most emoji) takes two cells, a combining
        // mark none.
        internal static int Width(string text)
        {
            var width = 0;
            var e = StringInfo.GetTextElementEnumerator(text ?? "");
            while (e.MoveNext())
            {
                var element = e.GetTextElement();
                var code = char.ConvertToUtf32(element, 0);
                // U+FE0F asks for the emoji look (❤️, ☀️): drawn as an emoji, two cells wide
                width += IsWide(code) || element.Contains('️') ? 2 : 1;
            }
            return width;
        }

        private static bool IsWide(int c) =>
            (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) ||
            (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6) || (c >= 0x1F300 && c <= 0x1FAFF) ||
            (c >= 0x20000 && c <= 0x3FFFD);

        private static string Pad(string text, int width, char align)
        {
            var space = Math.Max(0, width - Width(text));
            return align switch
            {
                'r' => new string(' ', space) + text,
                'c' => new string(' ', space / 2) + text + new string(' ', space - space / 2),
                _ => text + new string(' ', space),
            };
        }
    }
}
