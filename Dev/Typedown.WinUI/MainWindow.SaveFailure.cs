using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI
{
    // New since the fork: a file that could not be written says so. A save that failed used to end in the app's last-chance handler, which only
    // logs: the note looked saved and was not. Now the user is told, with Windows' own reason, and when Windows Security's Controlled folder access
    // protects the folder (Documents, Desktop...), that it may have blocked Caret and what to do (Services/ControlledFolderAccess.cs).
    public sealed partial class MainWindow
    {
        private static bool IsWriteFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;

        // Windows' message, and the hint about Controlled folder access when it protects the folder of this path.
        private static string WriteFailureText(Exception ex, string path) =>
            ControlledFolderAccess.CouldBeBlockedWrite(ex) && ControlledFolderAccess.ProtectingFolder(path) is { } folder
                ? ex.Message + "\n\n" + Locale.Format("WriteBlockedByWindows", folder)
                : ex.Message;

        private async Task ReportSaveFailure(Exception ex, string path)
        {
            Log($"Save failed: {path}: {ex.Message}");
            await ShowErrorDialog(Locale.GetString("SaveFailedTitle"), Locale.Format("SaveFailedMessage", Path.GetFileName(path)) + "\n\n" + WriteFailureText(ex, path));
        }

        // A save of the document on screen: true when it was written, false when it has no file yet (the caller asks for a name), null when it could
        // not be written (the reason was shown, and the document stays unsaved).
        private async Task<bool?> TrySave()
        {
            try
            {
                var saved = await file.Save();
                if (saved) autoSaveFailed.Remove(file);
                return saved;
            }
            catch (Exception ex) when (IsWriteFailure(ex))
            {
                await ReportSaveFailure(ex, file.FilePath);
                return null;
            }
        }

        // An automatic save that failed: the text is kept in the recovery copy instead, and the user is told once (not every five seconds) until a save
        // of that document works again.
        private readonly System.Collections.Generic.HashSet<ViewModels.FileViewModel> autoSaveFailed = new();

        private async Task AutoSaveOne(ViewModels.FileViewModel f)
        {
            try
            {
                await f.Save(automatic: true);
                autoSaveFailed.Remove(f);
                Log($"AutoSave: {f.FilePath}");
            }
            catch (Exception ex) when (IsWriteFailure(ex))
            {
                Log($"AutoSave: failed '{f.FilePath}': {ex.Message}");
                try { await f.BackupTick(); }
                catch (Exception backup) when (IsWriteFailure(backup)) { Log($"AutoBackup: failed too: {backup.Message}"); }
                if (!autoSaveFailed.Add(f)) return;
                var hint = ControlledFolderAccess.CouldBeBlockedWrite(ex) && ControlledFolderAccess.ProtectingFolder(f.FilePath) != null
                    ? " " + Locale.GetString("AutoSaveBlockedHint") : "";
                ShowToast(Locale.Format("AutoSaveFailed", Path.GetFileName(f.FilePath)) + hint, 12000);
            }
        }
    }
}
