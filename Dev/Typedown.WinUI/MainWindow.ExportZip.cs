using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Typedown.WinUI.Services.Export;
using Typedown.WinUI.Utilities;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // New since the fork: File > Export > Markdown with pictures (.zip). The note on screen, with every edit the editor has made, and the
    // pictures its links point at on this PC go into one .zip (Services/Export/NotePackager.cs), with the links made relative in the copy.
    // The note, its file and the pictures are not touched.
    public sealed partial class MainWindow
    {
        private async void ExportZipMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string target = null;
            try
            {
                if (startPageShown) return;
                var doc = activeDoc;
                // the text the editor holds, not the last saved one; when it does not answer nothing is written (it could be an old text)
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("ExportZipFailedTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;

                var markdown = doc.File.Markdown ?? "";
                var name = Path.GetFileNameWithoutExtension(doc.DisplayName);
                var picker = new FileSavePicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeChoices.Add(Locale.GetString("ExportZipFileType"), new List<string> { ".zip" });
                picker.SuggestedFileName = name;
                var picked = await picker.PickSaveFileAsync();
                if (picked == null) return;
                target = picked.Path;

                var folder = doc.File.ImageBasePath;
                var result = await Task.Run(() =>
                {
                    var packed = NotePackager.Pack(markdown, folder);
                    using var stream = new FileStream(picked.Path, FileMode.Create, FileAccess.Write);
                    NotePackager.WriteZip(packed, name, stream);
                    return packed;
                });
                Log($"ExportZip: {picked.Path}, pictures={result.Pictures.Count}, missing={result.Missing.Count}, remote={result.Remote}");
                if (result.Missing.Count == 0)
                {
                    ShowToast(result.Pictures.Count == 0
                        ? Locale.Format("ExportZipDoneNone", picked.Path)
                        : Locale.Format("ExportZipDone", picked.Path, result.Pictures.Count.ToString()), 6000);
                    return;
                }
                await ShowErrorDialog(Locale.GetString("ExportZipMissingTitle"),
                    Locale.Format("ExportZipMissingMessage", picked.Path) + "\n\n" + string.Join("\n", result.Missing));
            }
            catch (Exception ex)
            {
                Log($"ExportZip: failed: {ex}");
                // a zip that is half written is worse than none
                if (target != null) { try { File.Delete(target); } catch (Exception) { } }
                await ShowErrorDialog(Locale.GetString("ExportZipFailedTitle"), ex.Message);
            }
        }
    }
}
