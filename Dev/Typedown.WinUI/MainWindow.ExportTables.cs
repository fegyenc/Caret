using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // New since the fork: File > Export > Tables (Excel, CSV). The tables of the note on screen, with every edit the editor has made, into a
    // spreadsheet (Services/TableExport.cs): Excel has every table, a sheet each; a CSV file has one, the table at the cursor (or the first).
    // The note is not touched.
    public sealed partial class MainWindow
    {
        private async void ExportTablesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // from the command palette: the pane that had the keyboard when the palette opened (read before the first await)
            var paletteHint = paletteCodePaneHint;
            string target = null;
            string partial = null;
            try
            {
                if (startPageShown || file.IsXml) { ShowToast(Locale.GetString("ExportTablesNone")); return; }
                var doc = activeDoc;
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("ExportTablesFailedTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;
                var markdown = doc.File.Markdown ?? "";
                var word = Locale.GetString("ExportTablesTableWord");
                var tables = TableExport.Tables(markdown, word);
                if (tables.Count == 0) { ShowToast(Locale.GetString("ExportTablesNone"), 6000); return; }
                if (await MarkdownCursorLine(doc, paletteHint) is not { } line) return;

                var picker = new FileSavePicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeChoices.Add(Locale.GetString("ExportTablesExcelType"), new List<string> { ".xlsx" });
                picker.FileTypeChoices.Add(Locale.GetString("ExportTablesCsvType"), new List<string> { ".csv" });
                picker.SuggestedFileName = Path.GetFileNameWithoutExtension(doc.DisplayName);
                var picked = await picker.PickSaveFileAsync();
                if (picked == null) return;
                target = picked.Path;
                // written beside the destination and put in place only when complete: a failure leaves the chosen file as it was
                partial = picked.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var csv = string.Equals(Path.GetExtension(target), ".csv", StringComparison.OrdinalIgnoreCase);
                var one = csv ? TableExport.AtLine(markdown, line, word) : null;
                await Task.Run(() =>
                {
                    using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write))
                    {
                        if (csv)
                        {
                            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                            writer.Write(TableExport.ToCsv(one!.Value.Table));
                        }
                        else TableExport.WriteXlsx(tables, stream);
                    }
                    File.Move(partial, target, overwrite: true);
                });
                partial = null;
                Log($"ExportTables: {target}, {(csv ? "csv, one table" : $"xlsx, {tables.Count} table(s)")}");
                ShowToast(csv
                    ? Locale.Format(one!.Value.AtCursor || tables.Count == 1 ? "ExportTablesCsvDone" : "ExportTablesCsvFirst", target, one.Value.Table.Name)
                    : Locale.Format("ExportTablesExcelDone", target, tables.Count), 7000);
            }
            catch (Exception ex)
            {
                Log($"ExportTables: failed: {ex}");
                // only what this method made is deleted: the temporary file, and the empty file the picker creates for a new name
                if (partial != null) { try { File.Delete(partial); } catch (Exception) { } }
                if (target != null) { try { if (File.Exists(target) && new FileInfo(target).Length == 0) File.Delete(target); } catch (Exception) { } }
                await ShowErrorDialog(Locale.GetString("ExportTablesFailedTitle"), ex.Message);
            }
        }
    }
}
