using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Typedown.WinUI.Services.Export;
using Typedown.WinUI.Utilities;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // New since the fork: File > Export > Word (docs/word-export-design.md). The text of the document on screen, with every edit the
    // editor has made, is written as a .docx by Services/Export/WordExporter.cs: in this process, with no Word, no network.
    public sealed partial class MainWindow
    {
        private async void ExportWordMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (startPageShown) return;
                var doc = activeDoc;
                // the text the editor holds, not the last saved one; when it does not answer nothing is written (it could be an old text)
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("WordExportFailedTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;

                var picker = new FileSavePicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeChoices.Add(Locale.GetString("WordDocument"), new List<string> { ".docx" });
                picker.SuggestedFileName = Path.GetFileNameWithoutExtension(doc.DisplayName);
                var picked = await picker.PickSaveFileAsync();
                if (picked == null) return;

                var markdown = doc.File.Markdown ?? "";
                var options = new WordExportOptions
                {
                    BaseFolder = doc.File.ImageBasePath,
                    Language = WordLanguage(),
                    Title = Path.GetFileNameWithoutExtension(doc.DisplayName),
                    PageSize = RegionInfo.CurrentRegion.IsMetric ? WordPageSize.A4 : WordPageSize.Letter,
                };
                var result = await Task.Run(() => WordExporter.ExportToFile(markdown, picked.Path, options));
                Log($"ExportWord: {picked.Path}, pictures={result.Pictures}, skipped={result.SkippedPictures.Count}");
                if (result.SkippedPictures.Count == 0)
                {
                    ShowToast(Locale.Format("WordExportDone", picked.Path));
                    return;
                }
                var list = string.Join("\n", result.SkippedPictures);
                await ShowErrorDialog(Locale.GetString("WordExportSkippedTitle"), Locale.Format("WordExportSkippedMessage", picked.Path) + "\n\n" + list);
            }
            catch (Exception ex)
            {
                Log($"ExportWord: failed: {ex.Message}");
                await ShowErrorDialog(Locale.GetString("WordExportFailedTitle"), ex.Message);
            }
        }

        // The language Word proofs the text in: that of the interface, with the region of the PC when it is of that language.
        private static string WordLanguage()
        {
            var lang = Locale.CurrentLang;
            var system = CultureInfo.CurrentUICulture.Name;
            if (system.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase)) return system;
            return lang switch { "fr" => "fr-FR", "es" => "es-ES", "pl" => "pl-PL", "pt" => "pt-BR", _ => "en-US" };
        }
    }
}
