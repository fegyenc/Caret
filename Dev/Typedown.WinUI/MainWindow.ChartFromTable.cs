using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI
{
    // New since the fork: Paragraph > Diagram > Chart from table. The table under the cursor becomes a chart written as text (a ```vega-lite
    // block) after it (Services/ChartFromTable.cs). The text goes into the editor like Undo's does (SetMarkdown): one step in the undo history.
    public sealed partial class MainWindow
    {
        private async void ChartFromTableMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (startPageShown) return;
                var doc = activeDoc;
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("ChartDialogTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;
                var text = file.Markdown ?? "";
                var table = ChartFromTable.FindTable(text, doc.Cursor?.Focus.Line ?? -1);
                if (table == null)
                {
                    ShowToast(Locale.GetString("ChartNoTable"), 5000);
                    return;
                }
                if (table.NumericColumns.Count == 0)
                {
                    ShowToast(Locale.GetString("ChartNoNumbers"), 5000);
                    return;
                }
                var choice = await AskChart(table);
                if (choice == null) return;
                // the note could have changed while the dialog was open: the chart is made from the table as it is now, or not at all
                if (!ReferenceEquals(doc, activeDoc) || (file.Markdown ?? "") != text)
                {
                    await ShowErrorDialog(Locale.GetString("ChartDialogTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                var block = ChartFromTable.Build(table, choice.Kind, choice.LabelColumn, choice.ValueColumns);
                var result = ChartFromTable.InsertAfter(text, table, block);
                file.ReplaceBuffer(result);
                history.ContentChange(result);
                PostMessage("SetMarkdown", new { text = result, cursor = doc.Cursor, basePath = file.ImageBasePath });
                Log($"ChartFromTable: {choice.Kind}, label column {choice.LabelColumn}, {choice.ValueColumns.Count} value column(s), {table.Rows.Count} rows");
            }
            catch (Exception ex)
            {
                Log($"ChartFromTable: failed: {ex}");
                await ShowErrorDialog(Locale.GetString("ChartDialogTitle"), ex.Message);
            }
        }

        private sealed record ChartChoice(ChartKind Kind, int LabelColumn, List<int> ValueColumns);

        private async Task<ChartChoice> AskChart(MarkdownTable table)
        {
            var numeric = table.NumericColumns;
            var kind = new ComboBox { Header = Locale.GetString("ChartKindLabel"), HorizontalAlignment = HorizontalAlignment.Stretch };
            kind.Items.Add(Locale.GetString("ChartBar"));
            kind.Items.Add(Locale.GetString("ChartLine"));
            kind.Items.Add(Locale.GetString("ChartPie"));
            kind.SelectedIndex = 0;

            var labels = new ComboBox { Header = Locale.GetString("ChartLabelsColumn"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var header in table.Header) labels.Items.Add(header.Length == 0 ? "—" : header);
            // the first column that is not numbers is the labels (months, regions); a table of numbers only starts at its first column
            var firstText = Enumerable.Range(0, table.Header.Count).FirstOrDefault(c => !numeric.Contains(c));
            labels.SelectedIndex = numeric.Count == table.Header.Count ? 0 : firstText;

            var values = new StackPanel { Spacing = 2 };
            var boxes = new List<(int Column, CheckBox Box)>();
            foreach (var column in numeric)
            {
                var box = new CheckBox { Content = table.Header[column].Length == 0 ? "—" : table.Header[column], IsChecked = false };
                boxes.Add((column, box));
                values.Children.Add(box);
            }
            // the first column of numbers that is not the labels is on to begin with
            void Preselect()
            {
                var labelColumn = labels.SelectedIndex;
                foreach (var (column, box) in boxes)
                {
                    box.IsEnabled = column != labelColumn;
                    if (column == labelColumn) box.IsChecked = false;
                }
                if (boxes.All(b => b.Box.IsChecked != true))
                {
                    var first = boxes.FirstOrDefault(b => b.Column != labelColumn);
                    if (first.Box != null) first.Box.IsChecked = true;
                }
            }
            labels.SelectionChanged += (_, _) => Preselect();
            Preselect();

            var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
            panel.Children.Add(new TextBlock { Text = Locale.GetString("ChartNote"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
            panel.Children.Add(kind);
            panel.Children.Add(labels);
            panel.Children.Add(new TextBlock { Text = Locale.GetString("ChartValuesColumns") });
            panel.Children.Add(values);

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = Locale.GetString("ChartDialogTitle"),
                Content = new ScrollViewer { Content = panel },
                PrimaryButtonText = Locale.GetString("ChartMake"),
                CloseButtonText = Locale.GetString("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            // nothing to draw without a column of numbers: the dialog stays open
            dialog.PrimaryButtonClick += (_, args) => { if (boxes.All(b => b.Box.IsChecked != true)) args.Cancel = true; };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            var chosenColumns = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Column).ToList();
            return new ChartChoice((ChartKind)Math.Max(0, kind.SelectedIndex), Math.Max(0, labels.SelectedIndex), chosenColumns);
        }
    }
}
