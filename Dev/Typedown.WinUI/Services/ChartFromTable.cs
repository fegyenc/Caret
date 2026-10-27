using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services
{
    // New since the fork: Paragraph > Diagram > Chart from table. The Markdown table under the cursor becomes a chart written as text, a
    // ```vega-lite block that the editor draws and that stays editable in the note (change a number, the type of chart, the colours...). The
    // chart keeps its OWN copy of the numbers (Markdown has no way to link a chart to a table), which the dialog says: a table changed later
    // needs the chart made again, or its data edited.
    //
    // Plain .NET, no WinUI, tested without the app (Dev/Caret.ConverterTests/ChartFromTableTests.cs).
    public enum ChartKind { Bar, Line, Pie }

    public sealed class MarkdownTable
    {
        // The lines of the table in the note (0-based, the header and the last row included).
        public int StartLine { get; init; }
        public int EndLine { get; init; }
        public List<string> Header { get; init; } = new();
        public List<List<string>> Rows { get; init; } = new();

        // The columns whose cells are all numbers (or empty), and that have at least one number.
        public List<int> NumericColumns => Enumerable.Range(0, Header.Count).Where(IsNumeric).ToList();

        public bool IsNumeric(int column)
        {
            var any = false;
            foreach (var row in Rows)
            {
                var cell = column < row.Count ? row[column] : "";
                if (cell.Trim().Length == 0) continue;
                if (ChartFromTable.ParseNumber(cell) == null) return false;
                any = true;
            }
            return any;
        }
    }

    public static class ChartFromTable
    {
        private static readonly Regex Delimiter = new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.CultureInvariant);

        // The table that contains the line `line` (0-based), or null. A table is a header row, a delimiter row (|---|---|) and the rows after
        // them that have a pipe; lines in code blocks are not tables.
        public static MarkdownTable FindTable(string markdown, int line)
        {
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            var code = CodeLines(lines);
            for (var i = 0; i + 1 < lines.Length; i++)
            {
                if (code[i] || code[i + 1]) continue;
                if (!lines[i].Contains('|') || !lines[i + 1].Contains('-') || !Delimiter.IsMatch(lines[i + 1])) continue;
                var header = Cells(lines[i]);
                if (header.Count == 0 || header.Count != Cells(lines[i + 1]).Count) continue;
                var end = i + 1;
                var rows = new List<List<string>>();
                while (end + 1 < lines.Length && !code[end + 1] && lines[end + 1].Trim().Length > 0 && lines[end + 1].Contains('|'))
                {
                    end++;
                    rows.Add(Cells(lines[end]));
                }
                if (line >= i && line <= end) return new MarkdownTable { StartLine = i, EndLine = end, Header = header, Rows = rows };
                i = end;
            }
            return null;
        }

        // The cells of a row of a table: the leading and trailing pipe are not cells, and "\|" is a pipe in a cell.
        internal static List<string> Cells(string row)
        {
            var text = row.Trim();
            if (text.StartsWith("|")) text = text.Substring(1);
            if (text.EndsWith("|") && !text.EndsWith("\\|")) text = text.Substring(0, text.Length - 1);
            var cells = new List<string>();
            var current = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|') { current.Append('|'); i++; }
                else if (text[i] == '|') { cells.Add(current.ToString().Trim()); current.Clear(); }
                else current.Append(text[i]);
            }
            cells.Add(current.ToString().Trim());
            return cells;
        }

        private static bool[] CodeLines(string[] lines)
        {
            // fences as CommonMark has them: a fence line is indented by at most three spaces more than its container (the page, or the text of a
            // list item: a fence inside a list item has the item's indentation too), opens with three or more backticks or tildes (a backtick fence
            // has no backtick in its info string, so ```one``` is a code span), and closes only with a run of the same character that is at least
            // as long and has nothing after it
            var code = new bool[lines.Length];
            var containers = new List<int>();   // the text column of each open list item, outermost first
            string fence = null;
            var fenceBase = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                // a tab goes to the next multiple of four, as in CommonMark (after two spaces it adds two columns, not four)
                var indent = 0;
                foreach (var c in lines[i].Substring(0, lines[i].Length - trimmed.Length)) indent += c == '\t' ? 4 - indent % 4 : 1;
                if (fence != null)
                {
                    code[i] = true;
                    var closing = trimmed.TrimEnd();
                    if (indent - fenceBase <= 3 && closing.Length >= fence.Length && closing.All(c => c == fence[0])) fence = null;
                    continue;
                }
                if (trimmed.Length == 0) continue;
                while (containers.Count > 0 && containers[containers.Count - 1] > indent) containers.RemoveAt(containers.Count - 1);
                var text = trimmed;
                var column = indent;
                var marker = Regex.Match(trimmed, @"^([-+*]|\d{1,9}[.)])( +|$)");
                if (marker.Success && !Regex.IsMatch(trimmed, @"^([-*_])( *\1){2,} *$"))
                {
                    var width = marker.Groups[1].Length + (marker.Groups[2].Length is >= 1 and <= 4 ? marker.Groups[2].Length : 1);
                    column = indent + width;
                    text = trimmed.Substring(Math.Min(trimmed.Length, width));
                    containers.Add(column);
                }
                var container = containers.Count > 0 ? containers[containers.Count - 1] : 0;
                var open = Regex.Match(text, @"^(`{3,}|~{3,})(.*)$");
                if (open.Success && column >= container && column - container <= 3 && !(open.Groups[1].Value[0] == '`' && open.Groups[2].Value.Contains('`')))
                {
                    fence = open.Groups[1].Value;
                    fenceBase = container;
                    code[i] = true;
                }
            }
            return code;
        }

        // A number as people write it in a table: 1234.5, 1,234.50, 1.234,50, 12,5, 12 %, $5.20; (3) is not one. Null when the text is not a number.
        // Rules: a currency sign, a percent sign and spaces are ignored; a "," and a "." together: the last one is the decimal mark; one of them
        // alone is a thousands mark as Thousands says, otherwise the decimal mark.
        public static double? ParseNumber(string cell)
        {
            var text = Regex.Replace((cell ?? "").Trim(), @"[\s %€$£¥]", "");
            text = text.Replace("*", "").Replace("`", "");
            if (text.Length == 0 || !Regex.IsMatch(text, @"^[+-]?[\d.,]+$") || !Regex.IsMatch(text, @"\d")) return null;
            var comma = text.LastIndexOf(',');
            var dot = text.LastIndexOf('.');
            string normalized;
            if (comma >= 0 && dot >= 0) normalized = comma > dot ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
            else if (comma >= 0) normalized = Thousands(text, ',') ? text.Replace(",", "") : text.Replace(',', '.');
            else if (dot >= 0) normalized = Thousands(text, '.') ? text.Replace(".", "") : text;
            else normalized = text;
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsInfinity(value) ? value : null;
        }

        // A thousands mark: groups of exactly three digits after a first group of one to three. Said twice or more it is one ("1.234.567");
        // said once, a comma before exactly three digits is one ("1,234") and a dot is not ("3.141" is a decimal, as in English).
        private static bool Thousands(string text, char mark)
        {
            var body = text.TrimStart('+', '-');
            if (!Regex.IsMatch(body, @"^\d{1,3}(\" + mark + @"\d{3})+$")) return false;
            return body.Count(c => c == mark) > 1 || (mark == ',' && !body.StartsWith("0,"));
        }

        // The ```vega-lite block of a chart of the table: the labels of the rows are the column `labelColumn`, the values the columns of
        // `valueColumns` (more than one: bars stacked, lines one per column; a pie takes the first). The order of the rows is the order of the table.
        public static string Build(MarkdownTable table, ChartKind kind, int labelColumn, IReadOnlyList<int> valueColumns)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (valueColumns == null || valueColumns.Count == 0) throw new ArgumentException("Choose at least one column of numbers.", nameof(valueColumns));
            if (kind == ChartKind.Pie) valueColumns = new[] { valueColumns[0] };

            // field names that are safe in a chart (a header can have spaces, dots, accents); the original header is the title of the axis
            var keys = new Dictionary<int, string>();
            string Key(int column)
            {
                if (keys.TryGetValue(column, out var known)) return known;
                var raw = Regex.Replace(table.Header[column], @"[^\p{L}\p{N}]+", "_").Trim('_');
                if (raw.Length == 0) raw = "column" + (column + 1);
                var key = raw;
                // "series" and "value" are what the fold transform writes on every row: a column of that name must not share them
                for (var n = 2; keys.Values.Contains(key) || key is "series" or "value"; n++) key = raw + "_" + n;
                keys[column] = key;
                return key;
            }

            var label = Key(labelColumn);
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                json.WriteStartObject();
                json.WriteString("$schema", "https://vega.github.io/schema/vega-lite/v5.json");
                json.WriteString("description", "Chart made from a table. It has its own copy of the numbers: edit them here.");
                json.WriteStartObject("data");
                json.WriteStartArray("values");
                foreach (var row in table.Rows)
                {
                    json.WriteStartObject();
                    json.WriteString(label, labelColumn < row.Count ? row[labelColumn] : "");
                    foreach (var column in valueColumns)
                    {
                        var number = column < row.Count ? ParseNumber(row[column]) : null;
                        if (number == null) json.WriteNull(Key(column)); else json.WriteNumber(Key(column), number.Value);
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();

                var several = valueColumns.Count > 1;
                if (several)
                {
                    json.WriteStartArray("transform");
                    json.WriteStartObject();
                    json.WriteStartArray("fold");
                    foreach (var column in valueColumns) json.WriteStringValue(Key(column));
                    json.WriteEndArray();
                    json.WriteStartArray("as");
                    json.WriteStringValue("series");
                    json.WriteStringValue("value");
                    json.WriteEndArray();
                    json.WriteEndObject();
                    json.WriteEndArray();
                }

                if (kind != ChartKind.Pie)
                {
                    // vega-lite's own default is a 20-pixel step per bar, which draws three bars as a thin strip
                    json.WriteStartObject("width");
                    json.WriteNumber("step", 48);
                    json.WriteEndObject();
                    json.WriteNumber("height", 260);
                }
                var valueField = several ? "value" : Key(valueColumns[0]);
                var valueTitle = several ? "" : table.Header[valueColumns[0]];
                if (kind == ChartKind.Pie)
                {
                    json.WriteString("mark", "arc");
                    json.WriteStartObject("encoding");
                    WriteField(json, "theta", valueField, "quantitative", valueTitle, sort: false);
                    WriteField(json, "color", label, "nominal", table.Header[labelColumn], sort: true);
                    json.WriteEndObject();
                }
                else
                {
                    if (kind == ChartKind.Line)
                    {
                        json.WriteStartObject("mark");
                        json.WriteString("type", "line");
                        json.WriteBoolean("point", true);
                        json.WriteEndObject();
                    }
                    else json.WriteString("mark", "bar");
                    json.WriteStartObject("encoding");
                    WriteField(json, "x", label, "nominal", table.Header[labelColumn], sort: true);
                    WriteField(json, "y", valueField, "quantitative", valueTitle, sort: false);
                    if (several) WriteField(json, "color", "series", "nominal", "", sort: false);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            return "```vega-lite\n" + Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n```";
        }

        // A field of the encoding. `sort` false: nothing; true: "sort": null, which keeps the rows in the order of the table (a chart sorts
        // its labels alphabetically unless told not to: January, February, March would come as February, January, March).
        private static void WriteField(Utf8JsonWriter json, string channel, string field, string type, string title, bool sort)
        {
            json.WriteStartObject(channel);
            json.WriteString("field", field);
            json.WriteString("type", type);
            if (sort) json.WriteNull("sort");
            if (title.Length > 0) json.WriteString("title", title);
            json.WriteEndObject();
        }

        // The note with the chart after the table (a blank line between them and after), as one text; the cursor line does not change.
        public static string InsertAfter(string markdown, MarkdownTable table, string block)
        {
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            var at = Math.Min(table.EndLine + 1, lines.Count);
            var insert = new List<string> { "" };
            insert.AddRange(block.Split('\n'));
            // a blank line after the block unless the table was the last thing in the note or a blank line follows
            if (at < lines.Count && lines[at].Trim().Length > 0) insert.Add("");
            lines.InsertRange(at, insert);
            return string.Join("\n", lines);
        }
    }
}
