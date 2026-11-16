using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Typedown.WinUI.Services;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Caret.ConverterTests
{
    public class TableExportTests
    {
        private const string Note =
            "# Report\n\n" +
            "## Sales 2026\n\n" +
            "| Month | **Amount** | Share | Code |\n" +
            "|---|--:|--:|---|\n" +
            "| January | 1.234,50 | 12 % | 007 |\n" +
            "| February | $5.20 | 0,5 % | [12.345.678/0001-95](https://example.com) |\n" +
            "| March | -3 | | 35260312345678000195550010000001231000000019 |\n\n" +
            "Some text.\n\n" +
            "```\n| not | a table |\n|---|---|\n| 1 | 2 |\n```\n\n" +
            "| Name | Note |\n" +
            "|---|---|\n" +
            "| Ana | first<br>second |\n" +
            "| =SUM(A1) | \"quoted\", with comma |\n";

        [Fact]
        public void Every_table_of_the_note_is_found_with_a_name()
        {
            var tables = TableExport.Tables(Note, "Tabla");
            Assert.Equal(2, tables.Count);   // the one in the code block is not a table
            Assert.Equal("Sales 2026", tables[0].Name);
            Assert.Equal(new[] { "Month", "Amount", "Share", "Code" }, tables[0].Header);
            Assert.Equal("12.345.678/0001-95", tables[0].Rows[1][3]);   // the link keeps its text
            Assert.Equal("", tables[0].Rows[2][2]);
            // the heading above the first table is not the second table's: it has none of its own
            Assert.Equal("Tabla 2", tables[1].Name);
            Assert.Equal("first\nsecond", tables[1].Rows[0][1]);
        }

        [Fact]
        public void The_table_at_the_cursor_or_else_the_first()
        {
            var lines = Note.Split('\n').ToList();
            var second = lines.FindIndex(l => l.StartsWith("| Ana"));
            var atCursor = TableExport.AtLine(Note, second)!.Value;
            Assert.True(atCursor.AtCursor);
            Assert.Equal("Name", atCursor.Table.Header[0]);

            var elsewhere = TableExport.AtLine(Note, 0)!.Value;
            Assert.False(elsewhere.AtCursor);
            Assert.Equal("Sales 2026", elsewhere.Table.Name);

            Assert.Null(TableExport.AtLine("No table here.", 0));
        }

        [Theory]
        [InlineData("1.234,50", 1234.5, false)]
        [InlineData("1,234.50", 1234.5, false)]
        [InlineData("$5.20", 5.2, false)]
        [InlineData("-3", -3, false)]
        [InlineData("0,5", 0.5, false)]
        [InlineData("12 %", 0.12, true)]
        public void A_number_in_a_cell_is_a_number(string text, double value, bool percent)
        {
            var n = TableExport.NumberOf(text);
            Assert.NotNull(n);
            Assert.Equal(value, n!.Value.Value, 10);
            Assert.Equal(percent, n.Value.Percent);
        }

        [Theory]
        [InlineData("007")]
        [InlineData("12.345.678/0001-95")]
        [InlineData("2026-03-04")]
        [InlineData("35260312345678000195550010000001231000000019")]
        [InlineData("January")]
        [InlineData("")]
        public void What_only_looks_like_a_number_stays_text(string text) => Assert.Null(TableExport.NumberOf(text));

        [Fact]
        public void The_csv_keeps_the_text_and_quotes_what_needs_it()
        {
            var csv = TableExport.ToCsv(TableExport.Tables(Note)[1]);
            Assert.Equal(
                "Name,Note\r\n" +
                "Ana,\"first\nsecond\"\r\n" +
                "'=SUM(A1),\"\"\"quoted\"\", with comma\"\r\n", csv);
            // a negative number is not a formula
            Assert.Contains("\r\nMarch,-3,", TableExport.ToCsv(TableExport.Tables(Note)[0]));
        }

        [Fact]
        public void The_workbook_has_a_sheet_per_table_with_numbers_as_numbers()
        {
            var tables = TableExport.Tables(Note);
            using var stream = new MemoryStream();
            TableExport.WriteXlsx(tables, stream);
            stream.Position = 0;
            using var document = SpreadsheetDocument.Open(stream, false);
            var workbook = document.WorkbookPart!;
            var sheets = workbook.Workbook.Sheets!.Elements<S.Sheet>().ToList();
            Assert.Equal(new[] { "Sales 2026", "Table 2" }, sheets.Select(s => s.Name!.Value));

            var data = ((WorksheetPart)workbook.GetPartById(sheets[0].Id!)).Worksheet.Descendants<S.Cell>().ToDictionary(c => c.CellReference!.Value!);
            Assert.Equal(1u, data["A1"].StyleIndex!.Value);                       // the header in bold
            Assert.Equal("1234.5", data["B2"].CellValue!.Text);                    // a number
            Assert.Equal("0.12", data["C2"].CellValue!.Text);                      // a percent, as a fraction
            Assert.Equal(2u, data["C2"].StyleIndex!.Value);
            Assert.Equal("007", data["D2"].InlineString!.InnerText);               // a code stays text
            Assert.Equal("35260312345678000195550010000001231000000019", data["D4"].InlineString!.InnerText);
            Assert.False(data.ContainsKey("C4"));                                  // an empty cell is not written

            var second = ((WorksheetPart)workbook.GetPartById(sheets[1].Id!)).Worksheet.Descendants<S.Cell>().ToDictionary(c => c.CellReference!.Value!);
            Assert.Equal("=SUM(A1)", second["A3"].InlineString!.InnerText);       // text, never a formula
            Assert.Null(second["A3"].CellFormula);
            Assert.Equal(3u, second["B2"].StyleIndex!.Value);                      // two lines: wrapped
        }

        [Fact]
        public void A_row_without_a_pipe_belongs_to_the_table_until_another_block()
        {
            var tables = TableExport.Tables("| A | B |\n|---|---|\nfirst\n| second | value |\n# Next\n| not | a row |\n");
            var table = Assert.Single(tables);
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(new[] { "first", "" }, table.Rows[0]);   // one cell, the other empty
            Assert.Equal(new[] { "second", "value" }, table.Rows[1]);
        }

        [Theory]
        [InlineData("11222333000181")]   // a CNPJ without its dots: Excel would show 1,12E+13
        [InlineData("0 007")]            // a leading zero once the space is gone
        public void Identifiers_written_as_digits_stay_text(string text) => Assert.Null(TableExport.NumberOf(text));

        [Fact]
        public void Sheet_names_keep_clear_of_what_excel_refuses()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal("History (2)", TableExport.SheetName("History", used, 1));
            var cut = TableExport.SheetName(new string('x', 30) + "'z", used, 2);
            Assert.False(cut.EndsWith('\''));
            Assert.True(cut.Length <= 31);
        }

        [Fact]
        public void Sheet_names_are_valid_and_different()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal("Sales Q1 2026", TableExport.SheetName("Sales: Q1/2026", used, 1));
            Assert.Equal("Sheet2", TableExport.SheetName("[]*?", used, 2));
            Assert.Equal("Data", TableExport.SheetName("Data", used, 3));
            Assert.Equal("data (2)", TableExport.SheetName("data", used, 4));   // its own spelling, made different (Excel ignores case)
            var longName = TableExport.SheetName(new string('x', 40), used, 5);
            Assert.Equal(31, longName.Length);
            Assert.Equal(31, TableExport.SheetName(new string('x', 40), used, 6).Length);   // "xxx… (2)", still 31
        }
    }
}
