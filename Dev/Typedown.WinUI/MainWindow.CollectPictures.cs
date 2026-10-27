using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Typedown.WinUI.Services.Export;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI
{
    // New since the fork: File > Collect pictures next to this note. The pictures a saved note links to from other places (Pictures\Caret, a
    // download, another project) are copied into "images" beside it and the links made relative (Services/Export/NotePackager.cs), so that the
    // note and its pictures are one folder that can be copied, synced or zipped. The originals stay where they are. The new text goes into the
    // editor like Undo's does (SetMarkdown): one step in the undo history, and the file itself is written only when the note is saved.
    public sealed partial class MainWindow
    {
        private async void FileCollectPicturesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (startPageShown || string.IsNullOrEmpty(file.FilePath)) return;
                var doc = activeDoc;
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("CollectPicturesTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;

                var text = file.Markdown ?? "";
                var folder = Path.GetDirectoryName(file.FilePath);
                var result = await Task.Run(() => NotePackager.Collect(text, folder));
                // the note could have changed while the pictures were copied: its text is not replaced by an older one
                if (!ReferenceEquals(doc, activeDoc) || (file.Markdown ?? "") != text)
                {
                    await ShowErrorDialog(Locale.GetString("CollectPicturesTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (result.Markdown != text)
                {
                    file.ReplaceBuffer(result.Markdown);
                    history.ContentChange(result.Markdown);
                    PostMessage("SetMarkdown", new { text = result.Markdown, cursor = activeDoc.Cursor, basePath = file.ImageBasePath });
                }
                Log($"CollectPictures: {file.FilePath}, copied={result.Copied}, rewritten={result.Rewritten}, missing={result.Missing.Count}");
                if (result.Missing.Count > 0)
                    await ShowErrorDialog(Locale.GetString("CollectPicturesTitle"),
                        (result.Rewritten > 0 ? Locale.Format("CollectPicturesDone", result.Copied.ToString()) + "\n\n" : "")
                        + Locale.Format("CollectPicturesMissing", result.Missing.Count.ToString()) + "\n\n" + string.Join("\n", result.Missing));
                else if (result.Rewritten == 0)
                    ShowToast(Locale.GetString("CollectPicturesNone"), 5000);
                else
                    ShowToast(Locale.Format("CollectPicturesDone", result.Copied.ToString()), 9000);
            }
            catch (Exception ex)
            {
                Log($"CollectPictures: failed: {ex}");
                await ShowErrorDialog(Locale.GetString("CollectPicturesTitle"), Locale.Format("CollectPicturesFailed", ex.Message));
            }
        }
    }
}
