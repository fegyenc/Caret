using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Typedown.WinUI.Services.Conversion
{
    // .xlsx → one "## Sheet" section with a pipe table per visible sheet. Values are what Excel shows
    // for the common cases (dates as dates, percentages as percentages, formulas as their last computed
    // result), and empty rows and columns are dropped — they carry no meaning and only cost tokens.
    internal static class ExcelConverter
    {
        public static string Convert(Stream stream, ConversionContext context)
        {
            using var doc = SpreadsheetDocument.Open(stream, false);
            var workbook = doc.WorkbookPart;
            if (workbook?.Workbook?.Sheets == null) return "";
            var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>()
                .Select(i => i.InnerText).ToArray() ?? Array.Empty<string>();
            var formats = new NumberFormats(workbook.WorkbookStylesPart?.Stylesheet);

            var sb = new StringBuilder();
            var visibleSheets = workbook.Workbook.Sheets.Elements<S.Sheet>()
                .Where(s => s.State == null || s.State.Value == S.SheetStateValues.Visible).ToList();
            foreach (var sheet in visibleSheets)
            {
                if (sheet.Id?.Value == null || workbook.GetPartById(sheet.Id.Value) is not WorksheetPart part) continue;
                var rows = ReadRows(part, sharedStrings, formats);
                if (rows.Count == 0) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("## ").Append(MarkdownText.EscapeInline(sheet.Name?.Value ?? "")).Append("\n\n");
                sb.Append(MarkdownText.Table(rows));
            }
            if (sb.Length == 0) context.Warnings.Add(ConversionWarning.NoData);
            return sb.ToString();
        }

        private static List<IReadOnlyList<string>> ReadRows(WorksheetPart part, string[] sharedStrings, NumberFormats formats)
        {
            var grid = new List<Dictionary<int, string>>();
            var sheetData = part.Worksheet?.GetFirstChild<S.SheetData>();
            if (sheetData == null) return new List<IReadOnlyList<string>>();
            foreach (var row in sheetData.Elements<S.Row>())
            {
                var cells = new Dictionary<int, string>();
                var nextColumn = 0;
                foreach (var cell in row.Elements<S.Cell>())
                {
                    var column = cell.CellReference?.Value is string reference ? ColumnIndex(reference) : nextColumn;
                    nextColumn = column + 1;
                    var value = CellText(cell, sharedStrings, formats);
                    if (!string.IsNullOrWhiteSpace(value)) cells[column] = value.Trim();
                }
                if (cells.Count > 0) grid.Add(cells);
            }
            if (grid.Count == 0) return new List<IReadOnlyList<string>>();
            var usedColumns = grid.SelectMany(r => r.Keys).Distinct().OrderBy(c => c).ToList();
            return grid.Select(r => (IReadOnlyList<string>)usedColumns.Select(c => r.TryGetValue(c, out var v) ? v : "").ToList()).ToList();
        }

        private static int ColumnIndex(string reference)
        {
            var index = 0;
            foreach (var c in reference)
            {
                if (!char.IsLetter(c)) break;
                index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            }
            return index - 1;
        }

        private static string CellText(S.Cell cell, string[] sharedStrings, NumberFormats formats)
        {
            var raw = cell.CellValue?.Text;
            var type = cell.DataType?.Value;
            if (type == S.CellValues.SharedString)
                return int.TryParse(raw, out var i) && i >= 0 && i < sharedStrings.Length ? sharedStrings[i] : "";
            if (type == S.CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
            if (type == S.CellValues.Boolean) return raw == "1" ? "TRUE" : "FALSE";
            if (type == S.CellValues.String || type == S.CellValues.Error) return raw ?? "";
            if (string.IsNullOrEmpty(raw)) return "";
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return raw;
            return formats.Format(number, cell.StyleIndex?.Value ?? 0);
        }

        private sealed class NumberFormats
        {
            private readonly List<uint> cellFormatIds = new();
            private readonly Dictionary<uint, string> customCodes = new();

            public NumberFormats(S.Stylesheet stylesheet)
            {
                if (stylesheet?.CellFormats != null)
                    cellFormatIds.AddRange(stylesheet.CellFormats.Elements<S.CellFormat>().Select(f => f.NumberFormatId?.Value ?? 0));
                if (stylesheet?.NumberingFormats != null)
                    foreach (var f in stylesheet.NumberingFormats.Elements<S.NumberingFormat>())
                        if (f.NumberFormatId?.Value is uint id) customCodes[id] = f.FormatCode?.Value ?? "";
            }

            public string Format(double value, uint styleIndex)
            {
                var formatId = styleIndex < cellFormatIds.Count ? cellFormatIds[(int)styleIndex] : 0u;
                customCodes.TryGetValue(formatId, out var code);
                if (IsDate(formatId, code) && value > -657435 && value < 2958466)
                {
                    var date = DateTime.FromOADate(value);
                    var hasTime = Math.Abs(value % 1) > 1e-9 || (code != null && Regex.IsMatch(StripLiterals(code), "[hs]", RegexOptions.IgnoreCase));
                    var hasDate = value >= 1 || (code != null && Regex.IsMatch(StripLiterals(code), "[dy]", RegexOptions.IgnoreCase)) || formatId is >= 14 and <= 17 or 22;
                    if (!hasDate) return date.ToString("HH:mm", CultureInfo.InvariantCulture);
                    return date.ToString(hasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                if (formatId is 9 or 10 || (code != null && StripLiterals(code).Contains('%')))
                    return (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
                return value.ToString("G15", CultureInfo.InvariantCulture);
            }

            private static bool IsDate(uint id, string code)
            {
                if (id is >= 14 and <= 22 or >= 45 and <= 47) return true;
                if (string.IsNullOrEmpty(code)) return false;
                var bare = StripLiterals(code);
                return Regex.IsMatch(bare, "[dmyhs]", RegexOptions.IgnoreCase) && !Regex.IsMatch(bare, "[0#?]");
            }

            // Removes quoted text, escaped characters and [colour]/[locale] sections from a format code.
            private static string StripLiterals(string code) => Regex.Replace(code, "\"[^\"]*\"|\\\\.|\\[[^\\]]*\\]|_.|\\*.", "");
        }
    }

    // .csv → a pipe table. The delimiter is detected (French and Spanish Excel save with ";").
    internal static class CsvConverter
    {
        public static string Convert(Stream stream, ConversionContext context)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (text.Contains('�'))
            {
                // Not UTF-8: fall back to the Windows code page Excel uses for "CSV (delimited)".
                stream.Position = 0;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                using var legacy = new StreamReader(stream, Encoding.GetEncoding(1252), false);
                text = legacy.ReadToEnd();
            }
            var delimiter = DetectDelimiter(text);
            var rows = Parse(text, delimiter).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
            if (rows.Count == 0)
            {
                context.Warnings.Add(ConversionWarning.NoData);
                return "";
            }
            return MarkdownText.Table(rows.Select(r => (IReadOnlyList<string>)r.Select(c => c.Trim()).ToList()).ToList());
        }

        private static char DetectDelimiter(string text)
        {
            var firstLine = text.Split('\n').FirstOrDefault() ?? "";
            var candidates = new[] { ',', ';', '\t', '|' };
            return candidates.OrderByDescending(c => CountOutsideQuotes(firstLine, c)).First();
        }

        private static int CountOutsideQuotes(string line, char c)
        {
            var count = 0;
            var quoted = false;
            foreach (var ch in line)
            {
                if (ch == '"') quoted = !quoted;
                else if (ch == c && !quoted) count++;
            }
            return count;
        }

        private static IEnumerable<List<string>> Parse(string text, char delimiter)
        {
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    yield return row;
                    row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return row; }
        }
    }
}
