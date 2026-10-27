using System;
using System.Linq;
using System.Text.Json;
using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    // Paragraph > Diagram > Chart from table (Services/ChartFromTable.cs): the table under the cursor as a vega-lite block.
    public class ChartFromTableTests
    {
        private const string Note = "# Sales\n\nSome text.\n\n| Month | Sales | Costs |\n| --- | ---: | --- |\n| Jan | 28 | 10 |\n| Feb | 1,055 | 20 |\n| Mar | 43.5 | 30 |\n\nAfter the table.\n";

        private static JsonDocument Spec(string block)
        {
            Assert.StartsWith("```vega-lite\n", block);
            Assert.EndsWith("\n```", block);
            return JsonDocument.Parse(block.Substring("```vega-lite\n".Length, block.Length - "```vega-lite\n".Length - "\n```".Length));
        }

        [Fact]
        public void The_table_that_holds_the_cursor_is_found_with_its_header_and_rows()
        {
            var table = ChartFromTable.FindTable(Note, 7);   // the Feb row
            Assert.NotNull(table);
            Assert.Equal(4, table.StartLine);
            Assert.Equal(8, table.EndLine);
            Assert.Equal(new[] { "Month", "Sales", "Costs" }, table.Header);
            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(new[] { "Feb", "1,055", "20" }, table.Rows[1]);
            // the header row and the delimiter row are part of it; a line outside is not
            Assert.NotNull(ChartFromTable.FindTable(Note, 4));
            Assert.NotNull(ChartFromTable.FindTable(Note, 5));
            Assert.Null(ChartFromTable.FindTable(Note, 2));
            Assert.Null(ChartFromTable.FindTable(Note, 10));
        }

        [Fact]
        public void A_table_in_a_code_block_is_not_a_table_and_two_tables_are_told_apart()
        {
            var text = "```\n| a | b |\n|---|---|\n| 1 | 2 |\n```\n\n| x | y |\n|---|---|\n| 3 | 4 |\n\n| p | q |\n|---|---|\n| 5 | 6 |\n";
            Assert.Null(ChartFromTable.FindTable(text, 2));
            Assert.Equal("x", ChartFromTable.FindTable(text, 8).Header[0]);
            Assert.Equal("p", ChartFromTable.FindTable(text, 12).Header[0]);
        }

        [Fact]
        public void A_line_indented_by_four_spaces_is_code_and_not_a_fence_and_a_fence_closes_only_like_in_CommonMark()
        {
            // four spaces and three backticks is indented code: the table after it is still a table
            var indented = "    ```\n\n| x | y |\n|---|---|\n| a | 1 |\n";
            Assert.NotNull(ChartFromTable.FindTable(indented, 4));
            // a four-backtick fence is not closed by a three-backtick line, nor by a ```js line: the table inside it is code
            var nested = "````\n```\n```js\n| x | y |\n|---|---|\n| a | 1 |\n````\n\n| p | q |\n|---|---|\n| b | 2 |\n";
            Assert.Null(ChartFromTable.FindTable(nested, 5));
            Assert.Equal("p", ChartFromTable.FindTable(nested, 9).Header[0]);
        }

        [Fact]
        public void A_table_with_windows_line_endings_and_an_escaped_pipe_in_a_cell_is_read()
        {
            var table = ChartFromTable.FindTable("| a | b |\r\n|---|---|\r\n| x \\| y | 5 |\r\n", 2);
            Assert.Equal(new[] { "x | y", "5" }, table.Rows[0]);
        }

        [Theory]
        [InlineData("28", 28)]
        [InlineData("1,055", 1055)]
        [InlineData("1,234.50", 1234.5)]
        [InlineData("1.234,50", 1234.5)]
        [InlineData("1.234.567", 1234567)]
        [InlineData("12,5", 12.5)]
        [InlineData("3.141", 3.141)]
        [InlineData("12 %", 12)]
        [InlineData("$5.20", 5.2)]
        [InlineData("-3", -3)]
        [InlineData("**7**", 7)]
        [InlineData("0,123", 0.123)]
        public void Numbers_are_read_as_people_write_them_in_a_table(string cell, double expected)
        {
            Assert.Equal(expected, ChartFromTable.ParseNumber(cell));
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("(3)")]
        [InlineData("1,2,3")]
        [InlineData("12 kg")]
        [InlineData("--")]
        public void Text_that_is_not_a_number_is_not_one(string cell)
        {
            Assert.Null(ChartFromTable.ParseNumber(cell));
        }

        [Fact]
        public void The_numeric_columns_are_the_ones_where_every_cell_is_a_number_or_empty()
        {
            var table = ChartFromTable.FindTable("| name | n | mixed | empty |\n|---|---|---|---|\n| a | 1 | 5 | |\n| b |  | x | |\n", 3);
            Assert.Equal(new[] { 1 }, table.NumericColumns);
        }

        [Fact]
        public void A_bar_chart_keeps_the_order_of_the_table_and_the_numbers_and_the_original_headers_as_titles()
        {
            var table = ChartFromTable.FindTable(Note, 7);
            var block = ChartFromTable.Build(table, ChartKind.Bar, 0, new[] { 1 });
            using var spec = Spec(block);
            var root = spec.RootElement;
            Assert.Equal("bar", root.GetProperty("mark").GetString());
            var values = root.GetProperty("data").GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(new[] { "Jan", "Feb", "Mar" }, values.Select(v => v.GetProperty("Month").GetString()));
            Assert.Equal(new[] { 28d, 1055d, 43.5d }, values.Select(v => v.GetProperty("Sales").GetDouble()));
            var x = root.GetProperty("encoding").GetProperty("x");
            Assert.Equal(JsonValueKind.Null, x.GetProperty("sort").ValueKind);     // the table's order, not the alphabet's
            Assert.Equal("Month", x.GetProperty("title").GetString());
            Assert.Equal("Sales", root.GetProperty("encoding").GetProperty("y").GetProperty("title").GetString());
        }

        [Fact]
        public void Several_columns_become_series_with_a_fold_and_a_line_has_points()
        {
            var table = ChartFromTable.FindTable(Note, 7);
            using var spec = Spec(ChartFromTable.Build(table, ChartKind.Line, 0, new[] { 1, 2 }));
            var root = spec.RootElement;
            Assert.Equal("line", root.GetProperty("mark").GetProperty("type").GetString());
            Assert.True(root.GetProperty("mark").GetProperty("point").GetBoolean());
            var fold = root.GetProperty("transform")[0].GetProperty("fold").EnumerateArray().Select(e => e.GetString());
            Assert.Equal(new[] { "Sales", "Costs" }, fold);
            Assert.Equal("series", root.GetProperty("encoding").GetProperty("color").GetProperty("field").GetString());
        }

        [Fact]
        public void A_pie_takes_the_first_value_column_and_a_cell_that_is_not_a_number_is_null_not_zero()
        {
            var table = ChartFromTable.FindTable("| Region | Share |\n|---|---|\n| North | 40 |\n| South | n/a |\n", 3);
            using var spec = Spec(ChartFromTable.Build(table, ChartKind.Pie, 0, new[] { 1, 0 }));
            var root = spec.RootElement;
            Assert.Equal("arc", root.GetProperty("mark").GetString());
            Assert.Equal("Share", root.GetProperty("encoding").GetProperty("theta").GetProperty("field").GetString());
            var values = root.GetProperty("data").GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(40d, values[0].GetProperty("Share").GetDouble());
            Assert.Equal(JsonValueKind.Null, values[1].GetProperty("Share").ValueKind);
        }

        [Fact]
        public void Headers_with_spaces_dots_and_accents_become_safe_field_names_and_two_equal_ones_stay_apart()
        {
            var table = ChartFromTable.FindTable("| Año (2026) | Ventas.total | Ventas total |\n|---|---|---|\n| 2025 | 5 | 6 |\n", 2);
            using var spec = Spec(ChartFromTable.Build(table, ChartKind.Bar, 0, new[] { 1, 2 }));
            var first = spec.RootElement.GetProperty("data").GetProperty("values")[0];
            var names = first.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Equal(3, names.Distinct().Count());
            Assert.All(names, n => Assert.Matches(@"^[\p{L}\p{N}_]+$", n));
            Assert.Contains("Año_2026", names);
            // the accents are written as they are, not as ñ escapes, so the block can be read and edited
            Assert.Contains("Año", ChartFromTable.Build(table, ChartKind.Bar, 0, new[] { 1 }));
        }

        [Fact]
        public void A_label_column_called_series_or_value_does_not_collide_with_what_the_fold_writes()
        {
            var table = ChartFromTable.FindTable("| value | a | b |\n|---|---|---|\n| x | 1 | 2 |\n| y | 3 | 4 |\n", 2);
            using var spec = Spec(ChartFromTable.Build(table, ChartKind.Bar, 0, new[] { 1, 2 }));
            var first = spec.RootElement.GetProperty("data").GetProperty("values")[0];
            // the labels are in their own field, not in "value" (which the fold fills with the numbers)
            Assert.Equal("x", first.GetProperty("value_2").GetString());
            Assert.Equal("value_2", spec.RootElement.GetProperty("encoding").GetProperty("x").GetProperty("field").GetString());
            Assert.Equal("value", spec.RootElement.GetProperty("encoding").GetProperty("y").GetProperty("field").GetString());
        }

        [Fact]
        public void The_chart_goes_after_the_table_with_a_blank_line_around_it_and_the_rest_of_the_note_is_unchanged()
        {
            var table = ChartFromTable.FindTable(Note, 7);
            var block = ChartFromTable.Build(table, ChartKind.Bar, 0, new[] { 1 });
            var result = ChartFromTable.InsertAfter(Note, table, block);
            Assert.StartsWith(Note.Substring(0, Note.IndexOf("\n\nAfter the table.", StringComparison.Ordinal)) + "\n\n```vega-lite\n", result);
            Assert.EndsWith("\n```\n\nAfter the table.\n", result);
            Assert.Equal(Note, result.Replace("\n\n" + block, ""));
            // a table at the very end of the note
            var atEnd = "| a | b |\n|---|---|\n| x | 1 |";
            var t = ChartFromTable.FindTable(atEnd, 2);
            Assert.Equal(atEnd + "\n\n" + ChartFromTable.Build(t, ChartKind.Bar, 0, new[] { 1 }), ChartFromTable.InsertAfter(atEnd, t, ChartFromTable.Build(t, ChartKind.Bar, 0, new[] { 1 })));
        }

        [Fact]
        public void No_column_of_numbers_is_refused_with_a_clear_exception()
        {
            var table = ChartFromTable.FindTable("| a | b |\n|---|---|\n| x | y |\n", 2);
            Assert.Empty(table.NumericColumns);
            Assert.Throws<ArgumentException>(() => ChartFromTable.Build(table, ChartKind.Bar, 0, Array.Empty<int>()));
        }
    }
}
