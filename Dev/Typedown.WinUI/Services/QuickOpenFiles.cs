using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI.Services
{
    // New since the fork: the list behind "Go to File" (Ctrl+K). Every file under the open folder that Caret opens as a document: the
    // notes, and since XML editing the XML files too. Plain .NET (no WinUI), so the tests in Caret.ConverterTests compile it as it is.
    internal static class QuickOpenFiles
    {
        // Plain recursion with a per-directory try/catch, not Directory.EnumerateFiles(..., AllDirectories) - that throws (and abandons the
        // whole walk) on the first access-denied subfolder instead of just skipping it. Starts from ExplorerItem.PassesFilter's own
        // hidden/system/dotfolder/node_modules exclusions; bin/obj is Quick Open's own addition on top of that - FileTypeHelper.Markdown
        // includes .txt (the same set the folder tree itself uses), which otherwise buries real notes under build-artifact
        // LICENSE.txt/FileListAbsolute.txt noise whenever a dev repo is the opened folder. The folder tree still shows them; this only
        // trims Quick Open's own candidate list.
        internal static List<string> Collect(string folder, int depth = 0)
        {
            var results = new List<string>();
            if (depth > 16) return results; // guards against a pathological symlink loop
            List<FileSystemInfo> entries;
            try
            {
                // Materialized inside the try, not just the EnumerateFileSystemInfos() call: that call itself can't fail (it is lazy), but a
                // foreach over its result can throw mid-walk (a folder that becomes inaccessible partway through), and an uncaught
                // exception would escape the Task.Run in ShowQuickOpen's async void and could crash the app.
                entries = new DirectoryInfo(folder).EnumerateFileSystemInfos().ToList();
            }
            catch { return results; }
            foreach (var info in entries)
            {
                if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System)) continue;
                if (info.Name.StartsWith(".") || info.Name == "node_modules" || info.Name == "bin" || info.Name == "obj") continue;
                if (info.Attributes.HasFlag(FileAttributes.Directory))
                    results.AddRange(Collect(info.FullName, depth + 1));
                else if (FileTypeHelper.IsEditableFile(info.Name))
                    results.Add(info.FullName);
            }
            return results;
        }

        // The files whose name holds what was typed (any case), by name, at most `limit` of them: filtered as you type rather than a real
        // fuzzy match, which is enough for a project-sized file list.
        internal static List<string> Filter(IEnumerable<string> files, string typed, int limit = 50)
        {
            var matches = string.IsNullOrWhiteSpace(typed)
                ? files
                : files.Where(p => Path.GetFileName(p).Contains(typed, StringComparison.OrdinalIgnoreCase));
            return matches.OrderBy(Path.GetFileName).Take(limit).ToList();
        }
    }
}
