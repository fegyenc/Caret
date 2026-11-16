using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    // Edit > Copy as text figure (Services/TextFigure.cs): a table drawn with lines, and a bar chart drawn with blocks.
    public class TextFigureTests
    {
        private static string TableAt(string markdown, int line) => TextFigure.Table(markdown, ChartFromTable.FindTable(markdown, line));

        [Fact]
        public void A_table_is_drawn_with_lines_and_its_numbers_line_up_on_the_right()
        {
            const string md = "Intro\n\n| Month | Sales |\n|---|---|\n| January | 1,055 |\n| **February** | 870 |\n";
            Assert.Equal(
                "┌──────────┬───────┐\n" +
                "│  Month   │ Sales │\n" +
                "├──────────┼───────┤\n" +
                "│ January  │ 1,055 │\n" +
                "│ February │   870 │\n" +
                "└──────────┴───────┘", TableAt(md, 4));
        }

        [Fact]
        public void A_table_follows_the_alignment_of_its_delimiter_row_and_fills_short_rows()
        {
            const string md = "| A | B | C |\n|:-:|--:|:--|\n| x | y |\n| long | 2 | 3 |";
            Assert.Equal(
                "┌──────┬───┬───┐\n" +
                "│  A   │ B │ C │\n" +
                "├──────┼───┼───┤\n" +
                "│  x   │ y │   │\n" +
                "│ long │ 2 │ 3 │\n" +
                "└──────┴───┴───┘", TableAt(md, 0));
        }

        [Fact]
        public void Cells_lose_their_markdown_and_wide_characters_take_two_cells()
        {
            Assert.Equal("site and code a|b", TextFigure.Plain("[site](https://x.y) and `code` a|b"));
            Assert.Equal(4, TextFigure.Width("東京"));
            Assert.Equal(4, TextFigure.Width("café"));
        }

        [Fact]
        public void A_table_with_a_column_of_numbers_is_a_bar_chart_with_the_column_as_its_title()
        {
            const string md = "| Region | Note | Sales |\n|---|---|---|\n| North | a | 100 |\n| South | b | 50 |\n| East | c | 0 |";
            var series = TextFigure.FindSeries(md, 3);
            Assert.Equal(
                "Sales\n" +
                "North  ████████████████████████  100\n" +
                "South  ████████████               50\n" +
                "East                               0", TextFigure.BarChart(series));
        }

        [Fact]
        public void A_list_of_labels_and_numbers_is_a_bar_chart_with_eighths_of_a_block()
        {
            const string md = "Fruit:\n\n- Apples: 12\n- Pears — 3\n- Plums 1,5\n- No number here\n\nAfter.";
            var series = TextFigure.FindSeries(md, 3);
            Assert.Equal(4, series.Bars.Count);
            // label column 14 wide ("No number here"), two spaces, a bar column 24 wide, two spaces, the number right-aligned in 3
            static string Row(string label, int blocks, string value) =>
                label.PadRight(14) + "  " + new string('█', blocks).PadRight(24) + "  " + value.PadLeft(3);
            Assert.Equal(
                Row("Apples", 24, "12") + "\n" +
                Row("Pears", 6, "3") + "\n" +
                Row("Plums", 3, "1,5") + "\n" +
                "No number here", TextFigure.BarChart(series));
        }

        [Fact]
        public void A_negative_number_keeps_its_sign_and_a_label_with_a_digit_is_not_split()
        {
            var series = TextFigure.FindSeries("- Change: -5\n- Q1 2024 10", 0);
            Assert.Equal(new[] { "Change", "Q1 2024" }, series.Bars.ConvertAll(b => b.Label));
            Assert.Equal(-5, series.Bars[0].Value);
            // the bar is half of the longest (5 of 10), and the number keeps its minus
            Assert.Equal("Change".PadRight(7) + "  " + new string('█', 12).PadRight(24) + "  " + "-5", TextFigure.BarChart(series).Split('\n')[0]);
        }

        [Fact]
        public void No_chart_without_two_numbers_or_outside_a_table_or_list()
        {
            Assert.Null(TextFigure.FindSeries("- Apples: 12\n- Pears", 0));
            Assert.Null(TextFigure.FindSeries("Just a paragraph 12", 0));
            Assert.Null(TextFigure.FindSeries("| A | B |\n|---|---|\n| x | y |", 2));
        }

        [Fact]
        public void The_html_keeps_the_figure_in_a_fixed_width_font_and_escapes_it()
        {
            var html = TextFigure.Html("a < b");
            Assert.StartsWith("<pre style=\"font-family:Consolas", html);
            Assert.Contains("a &lt; b", html);
        }
    }
}
