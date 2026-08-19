using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.WinUI.Interfaces;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI.ViewModels
{
    // Drastically reduced port of Typedown.Core\ViewModels\FileViewModel.cs (560 lines in the
    // original). The original is deeply coupled to infrastructure this scaffold doesn't have yet:
    // AutoBackup, AccessHistory (EF Core), AppContentDialog, multi-window focus stealing, native
    // "recent files"/export menus, and the Command<T> bindings the original XAML used. This slice
    // covers what milestone #3's menu bar actually needs and can exercise for real: New, Open, Save,
    // Save As, the startup command-line file load, and dirty-state tracking for the unsaved-changes
    // prompt. FileStartupAction.OpenLast/AccessHistory, Export, and Print are still TODOs.
    public sealed class FileViewModel
    {
        private readonly SettingsViewModel settings;
        private readonly IMarkdownEditor markdownEditor;

        // The original tracked this via EditorViewModel.FileHash/CurrentHash (hash comparison); we just
        // compare against the last-loaded-or-saved text directly, which is simpler and cheap enough for
        // realistic document sizes.
        private string savedSnapshot = "";

        // The editor normalizes an empty document to "\n" (confirmed in earlier session logs), and its
        // MarkdownChange echo after a LoadFile push turns out to be unreliable — it fires sometimes and
        // not others, apparently depending on React effect timing rather than anything we control. So
        // savedSnapshot is set optimistically to what we just pushed (below), and this flag lets a
        // later echo — if one arrives — correct it to whatever the editor actually normalized it to.
        // Belt and suspenders: neither half alone was reliable enough on its own during testing.
        private bool expectingLoadEcho;

        public string FilePath { get; private set; }

        public string Markdown { get; private set; } = "";

        public bool IsDirty => Markdown != savedSnapshot;

        public string ImageBasePath => string.IsNullOrEmpty(FilePath) ? settings.DefaultImageBasePath : Path.GetDirectoryName(FilePath);

        public string DisplayName => string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath);

        public event Action FileStateChanged;

        public FileViewModel(SettingsViewModel settings, EventCenter eventCenter, IMarkdownEditor markdownEditor)
        {
            this.settings = settings;
            this.markdownEditor = markdownEditor;
            // Mirrors EditorViewModel's constructor in the original: the editor pushes its live text
            // back on every change (see Transport's "diffmsg" handling), so Save always has the
            // current content without a separate "give me the text" round trip.
            eventCenter.GetObservable<EditorEventArgs>("MarkdownChange").Subscribe(x =>
            {
                Markdown = x.Args["text"]?.ToString() ?? Markdown;
                if (expectingLoadEcho)
                {
                    savedSnapshot = Markdown;
                    expectingLoadEcho = false;
                }
                FileStateChanged?.Invoke();
            });
        }

        public async Task LoadStartUpMarkdown()
        {
            var path = CommandLine.GetOpenFilePath(Environment.GetCommandLineArgs());
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    Markdown = await File.ReadAllTextAsync(path);
                    FilePath = path;
                }
                catch
                {
                    Markdown = "";
                }
            }
            savedSnapshot = Markdown;
            // The startup document isn't pushed via LoadFile — it goes out in the GetSettings response
            // instead — but the editor still echoes it back once mounted, same as any other load.
            expectingLoadEcho = true;
        }

        public void NewFile()
        {
            FilePath = null;
            Markdown = "";
            savedSnapshot = Markdown;
            expectingLoadEcho = true;
            PushToEditor();
            FileStateChanged?.Invoke();
        }

        public async Task OpenFile(string path)
        {
            if (!File.Exists(path)) return;
            Markdown = await File.ReadAllTextAsync(path);
            savedSnapshot = Markdown;
            FilePath = path;
            expectingLoadEcho = true;
            PushToEditor();
            FileStateChanged?.Invoke();
        }

        // Returns false when there's no FilePath yet — caller (MainWindow) should fall back to SaveAs.
        public async Task<bool> Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return false;
            await File.WriteAllTextAsync(FilePath, Markdown);
            savedSnapshot = Markdown;
            FileStateChanged?.Invoke();
            return true;
        }

        public async Task SaveAs(string path)
        {
            await File.WriteAllTextAsync(path, Markdown);
            FilePath = path;
            savedSnapshot = Markdown;
            FileStateChanged?.Invoke();
        }

        private void PushToEditor() => markdownEditor?.PostMessage("LoadFile", new { text = Markdown, basePath = ImageBasePath });
    }
}
