using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Typedown.WinUI.Services
{
    // A table of the note as it goes into a spreadsheet: its name (the heading above it, or "Table 2"), the header and the rows as plain text
    // (without the Markdown marks; a <br> in a cell is a line break in it).
    public sealed record ExportTable(string Name, List<string> Header, List<List<string>> Rows);

    // New since the fork: File > Export > Tables (Excel, CSV). Every table of the note into an Excel workbook, a sheet each, or the table at the
    // cursor into a CSV file. A cell that is a number in the note is a number in the workbook (1.234,50, 12 %, $5.20 as Chart from table reads
    // them), except what only looks like one: a code with a leading zero (007), more than 15 digits (an access key, a long ID, which Excel would
    // round), or anything with other characters (12.345.678/0001-95, 2026-03-04). The CSV keeps the text as it is written. Offline, plain .NET,
    // tested without the app (Dev/Caret.ConverterTests/TableExportTests.cs).
    public static class TableExport
    {
        private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$", RegexOptions.CultureInvariant);

        // Every table of the note, named. `tableWord` is "Table" in the language of the person ("Table 2" for a table with no heading above it).
        public static List<ExportTable> Tables(string markdown, string tableWord = "Table")
        {
            var tables = ChartFromTable.AllTables(markdown);
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            var code = ChartFromTable.CodeLinesOf(lines);
            var result = new List<ExportTable>();
            for (var t = 0; t < tables.Count; t++)
            {
                var table = tables[t];
                string name = null;
                // the nearest heading above the table, after the table before it
                var stop = t > 0 ? tables[t - 1].EndLine : -1;
                for (var i = table.StartLine - 1; i > stop && name == null; i--)
                {
                    if (code[i]) continue;
                    var m = Heading.Match(lines[i]);
                    if (m.Success) name = Cell(m.Groups[2].Value);
                }
                if (string.IsNullOrWhiteSpace(name)) name = $"{tableWord} {t + 1}";
                result.Add(Of(table, name));
            }
            return result;
        }

        // The table that holds the line (0-based), or the first table when the line is in none; null when the note has no table.
        public static (ExportTable Table, bool AtCursor)? AtLine(string markdown, int line, string tableWord = "Table")
        {
            var all = ChartFromTable.AllTables(markdown);
            if (all.Count == 0) return null;
            var named = Tables(markdown, tableWord);
            var index = all.FindIndex(t => line >= t.StartLine && line <= t.EndLine);
            return index >= 0 ? (named[index], true) : (named[0], false);
        }

        private static ExportTable Of(MarkdownTable table, string name)
        {
            var columns = table.Header.Count;
            return new ExportTable(name, table.Header.Select(Cell).ToList(),
                table.Rows.Select(row => Enumerable.Range(0, columns).Select(c => c < row.Count ? Cell(row[c]) : "").ToList()).ToList());
        }

        // The text of a cell: a <br> is a line break, the rest as Copy as text figure has it (links keep their text, bold and code lose their marks).
        internal static string Cell(string text) =>
            string.Join("\n", Regex.Split(text ?? "", @"<br\s*/?>", RegexOptions.IgnoreCase).Select(TextFigure.Plain)).Trim();

        // The number in a cell, or null when it is text. A percent is a fraction (12 % is 0.12) and says so.
        internal static (double Value, bool Percent)? NumberOf(string text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0 || t.Contains('\n')) return null;
            if (t.Count(char.IsAsciiDigit) > 15) return null;
            // the number as ParseNumber reads it (spaces, currency and percent signs dropped): a leading zero there ("0 007") is a code
            var compact = Regex.Replace(t, @"[\s %€$£¥]", "");
            if (Regex.IsMatch(compact, @"^[+-]?0\d")) return null;
            // only digits as written (spaces aside), more than 11 of them: an identifier (a CNPJ written without its dots, an account), which Excel
            // would show as 1,12E+13; with a currency or percent sign it is an amount ($123456789012)
            var digits = Regex.Replace(t, @"\s", "");
            if (digits.Length > 11 && digits.All(char.IsAsciiDigit)) return null;
            if (ChartFromTable.ParseNumber(t) is not { } value) return null;
            return t.Contains('%') ? (value / 100, true) : (value, false);
        }

        // ---- CSV ----------------------------------------------------------------------------------------------------------------------

        // Comma-separated with the text of each cell as written, a line break inside a cell kept in quotes. A text a spreadsheet would run as a
        // formula (it starts with = + - @ and is not a number) gets an apostrophe in front. The file is written as UTF-8 with a byte order mark.
        public static string ToCsv(ExportTable table)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", table.Header.Select(CsvCell))).Append("\r\n");
            foreach (var row in table.Rows) sb.Append(string.Join(",", row.Select(CsvCell))).Append("\r\n");
            return sb.ToString();
        }

        private static string CsvCell(string text)
        {
            text ??= "";
            if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0 && ChartFromTable.ParseNumber(text) == null) text = "'" + text;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

        // ---- Excel --------------------------------------------------------------------------------------------------------------------

        // A sheet per table: the header in bold and frozen, the numbers as numbers, the text as text (never a formula), the columns as wide as
        // their longest cell. The sheet names are the tables' names, made valid for Excel (31 characters, no []:*?/\) and different from each other.
        public static void WriteXlsx(IReadOnlyList<ExportTable> tables, Stream stream)
        {
            // the Open XML library needs a stream it can read back: the workbook is made in memory
            using var memory = new MemoryStream();
            using (var document = SpreadsheetDocument.Create(memory, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new S.Workbook();
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = Styles();
                var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var number = 1u;
                foreach (var table in tables)
                {
                    var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    sheetPart.Worksheet = Sheet(table);
                    sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = number, Name = SheetName(table.Name, used, number) });
                    number++;
                }
                workbookPart.Workbook.Save();
            }
            memory.WriteTo(stream);
        }

        private static S.Worksheet Sheet(ExportTable table)
        {
            var data = new S.SheetData();
            data.Append(Row(1, table.Header.Select(h => Cell(h, header: true)).ToList()));
            var index = 2u;
            foreach (var row in table.Rows) data.Append(Row(index++, row.Select(c => Cell(c, header: false)).ToList()));
            var count = table.Header.Count;
            var widths = Enumerable.Range(0, count).Select(c =>
                Math.Clamp(table.Rows.Select(r => Longest(r[c])).Append(Longest(table.Header[c])).Max() + 2, 8, 60)).ToList();
            var columns = new S.Columns(widths.Select((w, i) => new S.Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = w, CustomWidth = true }));
            var pane = new S.Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen };
            var view = new S.SheetView(pane, new S.Selection { Pane = S.PaneValues.BottomLeft }) { WorkbookViewId = 0U };
            return count == 0 ? new S.Worksheet(data) : new S.Worksheet(new S.SheetViews(view), columns, data);
        }

        private static int Longest(string text) => (text ?? "").Split('\n').Max(l => TextFigure.Width(l));

        // (text, number, style): style 0 plain, 1 bold (header), 2 a percent, 3 text on several lines (wrapped)
        private static (string Text, double? Number, uint Style) Cell(string text, bool header)
        {
            if (header) return (text, null, 1u);
            if (NumberOf(text) is { } n) return ("", n.Value, n.Percent ? 2u : 0u);
            return (text, null, text.Contains('\n') ? 3u : 0u);
        }

        private static S.Row Row(uint index, List<(string Text, double? Number, uint Style)> cells)
        {
            var row = new S.Row { RowIndex = index };
            for (var i = 0; i < cells.Count; i++)
            {
                var (text, number, style) = cells[i];
                var reference = ColumnName(i) + index.ToString(CultureInfo.InvariantCulture);
                if (number != null)
                    row.Append(new S.Cell { CellReference = reference, StyleIndex = style, CellValue = new S.CellValue(number.Value.ToString("R", CultureInfo.InvariantCulture)) });
                else if (text.Length > 0)
                    row.Append(new S.Cell
                    {
                        CellReference = reference, StyleIndex = style, DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve }),
                    });
            }
            return row;
        }

        internal static string ColumnName(int index)
        {
            var name = "";
            for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        // A name Excel takes: at most 31 characters, none of []:*?/\, not empty, not starting or ending with an apostrophe, not "History" (Excel
        // keeps that name for itself), and not one already used (in any case): "Sales (2)".
        internal static string SheetName(string name, HashSet<string> used, uint number)
        {
            // a character Excel refuses is a space ("Sales: Q1/2026" is "Sales Q1 2026")
            var spaced = new string((name ?? "").Select(c => "[]:*?/\\".IndexOf(c) >= 0 || char.IsControl(c) ? ' ' : c).ToArray());
            var clean = Edges(Regex.Replace(spaced, @"\s+", " "));
            if (clean.Length > 31) clean = Edges(clean.Substring(0, 31));
            if (clean.Length == 0) clean = "Sheet" + number.ToString(CultureInfo.InvariantCulture);
            // the reserved name counts as taken: "History (2)"
            used.Add("History");
            var candidate = clean;
            for (var n = 2; !used.Add(candidate); n++)
            {
                var suffix = $" ({n})";
                candidate = (clean.Length + suffix.Length > 31 ? Edges(clean.Substring(0, 31 - suffix.Length)) : clean) + suffix;
            }
            return candidate;
        }

        // No space and no apostrophe at either end (the apostrophe again after a cut: "xxx'z" cut before the z).
        private static string Edges(string text)
        {
            var t = text;
            string before;
            do { before = t; t = t.Trim().Trim('\''); } while (t != before);
            return t;
        }

        private static S.Stylesheet Styles() => new(
            new S.Fonts(
                new S.Font(new S.FontSize { Val = 11D }, new S.FontName { Val = "Calibri" }),
                new S.Font(new S.Bold(), new S.FontSize { Val = 11D }, new S.FontName { Val = "Calibri" })),
            new S.Fills(
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
            new S.Borders(new S.Border()),
            new S.CellFormats(
                new S.CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 0 },
                new S.CellFormat { FontId = 1, FillId = 0, BorderId = 0, NumberFormatId = 0, ApplyFont = true },
                // 0.00% (a built-in format of Excel)
                new S.CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 10, ApplyNumberFormat = true },
                new S.CellFormat(new S.Alignment { WrapText = true, Vertical = S.VerticalAlignmentValues.Top }) { FontId = 0, FillId = 0, BorderId = 0, ApplyAlignment = true }));
    }
}
