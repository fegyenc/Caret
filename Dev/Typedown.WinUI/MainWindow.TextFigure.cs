using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;
using Windows.ApplicationModel.DataTransfer;

namespace Typedown.WinUI
{
    // New since the fork: Edit > Copy as text figure > Table / Bar chart (also in the right-click menu and the command palette). The table
    // under the cursor drawn with lines, or the numbers of the table or list under the cursor as a bar chart drawn with blocks, on the
    // clipboard as text and as HTML in a fixed-width font (Services/TextFigure.cs). Nothing in the note changes.
    public sealed partial class MainWindow
    {
        private async void CopyTextTableMenuItem_Click(object sender, RoutedEventArgs e) => await CopyTextFigure(chart: false);

        private async void CopyTextBarChartMenuItem_Click(object sender, RoutedEventArgs e) => await CopyTextFigure(chart: true);

        private async Task CopyTextFigure(bool chart)
        {
            // from the command palette: the pane that had the keyboard when the palette opened (read before the first await)
            var paletteHint = paletteCodePaneHint;
            try
            {
                if (startPageShown || SettingsPageShown || ConvertPage.Visibility == Visibility.Visible || file.IsXml)
                {
                    ShowToast(Locale.GetString(chart ? "TextFigureNoSeries" : "ChartNoTable"));
                    return;
                }
                var doc = activeDoc;
                if (!await FlushEditor())
                {
                    ShowToast(Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;
                var text = file.Markdown ?? "";
                // the line of the cursor: in the source pane (Code, Split) its own line, which is the line of the Markdown; else the editor's
                var line = doc.Cursor?.Focus.Line ?? -1;
                if (CurrentViewMode != "view" && (paletteHint ?? await CodePaneHasKeyboard()))
                {
                    var answer = await RunInPage($"(function(){{var c={CodePane};return c?c.getCursor().line:-1;}})()");
                    if (int.TryParse(answer, out var codeLine) && codeLine >= 0) line = codeLine;
                }
                string figure, done;
                if (chart)
                {
                    var series = TextFigure.FindSeries(text, line);
                    if (series == null)
                    {
                        ShowToast(Locale.GetString("TextFigureNoSeries"), 6000);
                        return;
                    }
                    figure = TextFigure.BarChart(series);
                    done = Locale.Format("TextFigureCopiedChart", series.Bars.Count);
                }
                else
                {
                    var table = ChartFromTable.FindTable(text, line);
                    if (table == null)
                    {
                        ShowToast(Locale.GetString("ChartNoTable"));
                        return;
                    }
                    figure = TextFigure.Table(text, table);
                    done = Locale.Format("TextFigureCopiedTable", table.Rows.Count);
                }
                var package = new DataPackage();
                package.SetText(figure.Replace("\n", "\r\n"));
                package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(TextFigure.Html(figure)));
                Clipboard.SetContent(package);
                // Stays on the clipboard after Caret closes; the figure is already there if this can't be done.
                try { Clipboard.Flush(); } catch { }
                ShowToast(done);
                Log($"TextFigure: {(chart ? "bar chart" : "table")} copied, {figure.Length} characters");
            }
            catch (Exception ex)
            {
                Log($"TextFigure: not copied: {ex.Message}");
                ShowToast(Locale.GetString("TextFigureFailed"));
            }
        }
    }
}
