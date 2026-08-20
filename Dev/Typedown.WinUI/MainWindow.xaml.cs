using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Typedown.WinUI.Enums;
using Typedown.WinUI.Interfaces;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;
using Typedown.WinUI.ViewModels;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // [DoNotNotify]: MainWindow implements IMarkdownEditor, which extends INotifyPropertyChanged.
    // Fody's PropertyChanged weaver auto-weaves every type that implements that interface — including
    // this XAML-codegen partial class — which corrupts the WinUI3-generated activation IL and crashes
    // the native XAML engine on startup (0xc000027b) with no managed exception to catch. Excluding
    // MainWindow keeps Fody scoped to plain ViewModels like SettingsViewModel, where it belongs.
    [DoNotNotify]
    public sealed partial class MainWindow : Window, IMarkdownEditor
    {
        private readonly string logPath = Path.Combine(Path.GetTempPath(), "caret_winui_probe.log");

        private readonly RemoteInvoke remoteInvoke = new();
        private readonly EventCenter eventCenter = new();
        private readonly Transport transport;
        private readonly SettingsViewModel settings;
        private readonly FileViewModel file;
        private readonly RecentFilesService recentFiles = new();
        private readonly ObservableCollection<TocEntry> tocEntries = new();
        private readonly UISettings uiSettings = new();

        public bool IsEditorLoadFailed { get; private set; }
        public bool IsEditorLoaded { get; private set; }

#pragma warning disable CS0067 // required by INotifyPropertyChanged; unused until real ViewModels replace this scaffold
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        public void Dispose()
        {
            remoteInvoke.Dispose();
            eventCenter.Dispose();
        }

        public MainWindow()
        {
            InitializeComponent();
            transport = new Transport(remoteInvoke, eventCenter);
            settings = new SettingsViewModel(this);
            file = new FileViewModel(settings, eventCenter, this);
            file.FileStateChanged += UpdateTitle;
            file.FileStateChanged += UpdateFolderSelection;
            RegisterHandlers();
            SetUpTitleBar();
            SetUpWindowPlacement();
            SetUpClosingPrompt();
            SetUpAutoSaveTimer();
            // Both apply a persisted appearance setting that, until now, only ever took effect once
            // the user opened Settings and touched the corresponding control — a real startup gap for
            // anyone who'd already set a non-default theme or turned Mica off, not something specific
            // to this Mica pass. Fixing them together since they're the same shape of bug.
            ApplyNativeTheme();
            ApplyBackdrop();
            ApplyEditorBackground();
            SetUpThemePush();
            UpdateTitle();
            RefreshRecentFilesMenu();
            TocListView.ItemsSource = tocEntries;
            eventCenter.GetObservable<EditorEventArgs>("StateChange").Subscribe(x => UpdateToc(x.Args));
            EditorView.Loaded += MainWindow_Loaded;
        }

        // Reimplemented against WinUI 3's own custom-title-bar API (ExtendsContentIntoTitleBar +
        // SetTitleBar) rather than ported from Caption.xaml, which drew its own caption buttons and
        // drag region via the private Typedown.XamlUI NuGet package (no source available) plus a full
        // AppViewModel for back-navigation we haven't built. WinUI 3's native support does the same
        // job — draggable custom region, OS-drawn caption buttons — with far less code.
        private void SetUpTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        // --- Window placement ---
        // Reimplemented against WinUI 3's own AppWindow/OverlappedPresenter APIs rather than a literal
        // port of the original's TrySaveWindowPlacement/ShowWindowWithSavedPlacement (Typedown\Utilities
        // \Common.cs), which used the raw Win32 WINDOWPLACEMENT struct via PInvoke.GetWindowPlacement/
        // SetWindowPlacement on the WPF host window's HWND. AppWindow.MoveAndResize + Presenter.State is
        // the modern WinUI 3-native equivalent of the same "position + size + maximized" triple, and
        // this app already uses AppWindow elsewhere (Closing, above) — no raw struct interop needed.
        //
        // AppWindow.Changed fires continuously during a drag-move/drag-resize (every intermediate frame,
        // not just the final one), so writes are debounced the same way the original throttled its own
        // LocationChanged/SizeChanged handlers with a 100ms delay — otherwise every pixel of a drag would
        // rewrite Settings.json.
        private bool placementSaveScheduled;

        private void SetUpWindowPlacement()
        {
            if (settings.WindowX is int x && settings.WindowY is int y &&
                settings.WindowWidth is int w && settings.WindowHeight is int h)
            {
                AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
            }
            if (settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            AppWindow.Changed += async (s, args) =>
            {
                if (allowClose || placementSaveScheduled) return;
                placementSaveScheduled = true;
                await Task.Delay(300);
                placementSaveScheduled = false;
                if (!allowClose) SavePlacementNow();
            };
        }

        private void SavePlacementNow()
        {
            if (AppWindow?.Presenter is not OverlappedPresenter presenter) return;
            if (presenter.State == OverlappedPresenterState.Maximized)
            {
                settings.WindowMaximized = true;
            }
            else if (presenter.State == OverlappedPresenterState.Restored)
            {
                settings.WindowMaximized = false;
                settings.WindowX = AppWindow.Position.X;
                settings.WindowY = AppWindow.Position.Y;
                settings.WindowWidth = AppWindow.Size.Width;
                settings.WindowHeight = AppWindow.Size.Height;
            }
            // Minimized: leave whatever was last recorded (maximized or restored bounds) alone —
            // there's nothing meaningful to capture about a minimized window's "shape".
        }

        private void UpdateTitle()
        {
            var dirtyMark = file.IsDirty ? "● " : ""; // ● — matches the original's DisplaySaved-driven title dot
            TitleTextBlock.Text = dirtyMark + file.DisplayName + " - Caret";
            Title = TitleTextBlock.Text;
        }

        // --- Unsaved-changes prompt ---
        // The original's AskToSave (FileViewModel.cs) gated every New/Open/window-close behind this;
        // this scaffold didn't have it until now, which meant New and Open silently discarded unsaved
        // work. IsDirty is tracked in FileViewModel by comparing the live text against the
        // last-loaded-or-saved snapshot, same idea as the original's FileHash/CurrentHash comparison.
        //
        // allowClose exists because AppWindow.Closing can't simply be "awaited then let close happen" —
        // it's synchronous-looking but we need to show an async dialog first, so we always cancel the
        // first close attempt, run the check, and re-invoke Close() ourselves once it's confirmed.
        private bool allowClose;

        private void SetUpClosingPrompt()
        {
            AppWindow.Closing += async (s, args) =>
            {
                if (allowClose) return;
                args.Cancel = true;
                if (await ConfirmDiscardChangesIfNeeded())
                {
                    allowClose = true;
                    SavePlacementNow(); // final capture — don't wait for the debounced save below
                    Close();
                }
            };
        }

        // Returns true if it's OK to proceed (no unsaved changes, or the user chose Save/Don't Save);
        // false if the user cancelled, in which case the caller should not continue.
        private async System.Threading.Tasks.Task<bool> ConfirmDiscardChangesIfNeeded()
        {
            if (!file.IsDirty) return true;
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Unsaved changes",
                Content = $"Do you want to save changes to {file.DisplayName}?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Don't Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (!await file.Save())
                    await SaveAsInternal();
                return !file.IsDirty; // still dirty means the Save As picker was cancelled — don't proceed
            }
            return result == ContentDialogResult.Secondary; // Don't Save = proceed; Cancel (or dismissed) = stop
        }

        // --- Auto-save / AutoBackup ---
        // Ported the shape of FileViewModel's saveFileTimer (5-second DispatcherTimer tick in the
        // original): when Auto save is on and the document already has a real path, silently re-save
        // it. Otherwise — untitled documents (no path to autosave to yet) or Auto save turned off —
        // fall back to FileViewModel.BackupTick(), which writes the dirty content to a hash-named
        // recovery file instead. This closes the gap noted here previously: an unsaved untitled
        // document now has a safety net even though there's nowhere to Ctrl+S it to.
        private void SetUpAutoSaveTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += async (s, e) =>
            {
                if (settings.AutoSave && file.IsDirty && !string.IsNullOrEmpty(file.FilePath))
                {
                    await file.Save();
                    Log($"AutoSave: {file.FilePath}");
                }
                else
                {
                    if (await file.BackupTick())
                        Log($"AutoBackup: wrote recovery backup for '{file.FilePath}'");
                }
            };
            timer.Start();
        }

        // --- AutoBackup recovery prompt ---
        // Ported from the original's FileViewModel.CheckBackup(), split so the dialog (which needs
        // XamlRoot) lives here instead of in the dialog-free FileViewModel. Call after any load — New,
        // Open, startup — so a leftover backup from a previous crash gets offered back instead of
        // silently sitting unused. If the backup matches what was just loaded there's nothing to
        // recover, so it's left alone (same as the original, which also doesn't clean up a
        // stale-but-matching backup here).
        private async Task OfferBackupRecoveryIfAny(string path)
        {
            var backup = await file.PeekBackup(path);
            if (backup == null || backup == file.Markdown) return;
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Recover unsaved changes?",
                Content = $"A backup was found for {(string.IsNullOrEmpty(path) ? "an untitled document" : Path.GetFileName(path))} " +
                          "from a previous session that was never saved. Recover it?",
                PrimaryButtonText = "Recover",
                SecondaryButtonText = "Discard",
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                file.ApplyRecoveredBackup(backup);
                UpdateTitle();
                Log($"AutoBackup: recovered backup for '{path}'");
            }
            else
            {
                file.DiscardBackup(path);
                Log($"AutoBackup: discarded backup for '{path}'");
            }
        }

        // GetSettings/GetStringResources/Markdown/BasePath are real now (backed by SettingsViewModel,
        // Locale, and FileViewModel). GetCurrentTheme now returns the real {theme, accentColor,
        // background} shape (see BuildThemePayload) instead of a bare theme-name string — the bare
        // string was a real gap, not a simplification: Typedown.Editor's theme.ts (bundled JS, used
        // as-is) destructures accentColor/background out of whatever GetCurrentTheme resolves to, so
        // a string here meant those two silently came out undefined.
        private void RegisterHandlers()
        {
            remoteInvoke.Handle("GetCurrentTheme", () => BuildThemePayload());
            remoteInvoke.Handle("GetStringResources", (JToken args) =>
            {
                var names = args["names"]?.ToObject<string[]>() ?? Array.Empty<string>();
                var result = new JObject();
                foreach (var name in names) result[name] = Locale.GetString(name);
                return result;
            });
            remoteInvoke.Handle("GetSettings", () => new
            {
                settings.FocusMode,
                settings.Typewriter,
                settings.SourceCode,
                settings.FontSize,
                settings.LineHeight,
                settings.AutoPairBracket,
                settings.AutoPairQuote,
                settings.TrimUnnecessaryCodeBlockEmptyLines,
                settings.PreferLooseListItem,
                settings.AutoPairMarkdownSyntax,
                settings.EditorAreaWidth,
                settings.TabSize,
                file.Markdown,
                BasePath = file.ImageBasePath,
            });
            // Traced from FileViewModel.cs's Export()/ExportCallback() and Editor/index.tsx's Export
            // listener: we push {type, context, basePath, title} via PostMessage("Export", ...), the
            // editor's own ExportHtml (JS) renders it to a clean HTML string, and calls this back with
            // {html, context}. context is opaque to the editor — just handed back verbatim — so a
            // single in-flight TaskCompletionSource is enough since only one export runs at a time.
            remoteInvoke.Handle("ExportCallback", (JToken args) =>
            {
                pendingExportHtml?.TrySetResult(args["html"]?.ToString());
                return true;
            });
            // Not used by anything we send (Print uses WebView2's own ShowPrintUI instead of routing
            // through the editor's HTML export), but registered for API completeness — an unexpected
            // call would otherwise throw "function does not exist" back at the editor.
            remoteInvoke.Handle("PrintHTML", (JToken args) => true);
        }

        private void Log(string message) => File.AppendAllText(logPath, $"{DateTime.Now:O} {message}\n");

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await file.LoadStartUpMarkdown();
                if (!string.IsNullOrEmpty(file.FilePath))
                {
                    recentFiles.Record(file.FilePath);
                    RefreshRecentFilesMenu();
                }
                await OfferBackupRecoveryIfAny(file.FilePath);
                UpdateTitle();
                Log($"LoadStartUpMarkdown: FilePath={file.FilePath}, chars={file.Markdown.Length}");
                // Config.WebView2Args (ported back in #2) is still applied via this documented
                // environment-variable path — verified by inspecting the spawned msedgewebview2.exe
                // command line, --disable-web-security and --allow-file-access-from-files really do
                // reach the browser process. It's harmless (this WebView2 only ever shows our own
                // bundled editor, never arbitrary web content) but it turned out NOT to be what fixes
                // local image rendering below: Chromium's file:// subresource block for non-file
                // origins isn't a web-security-policy check --disable-web-security lifts, it's a lower
                // level "not allowed to load local resource" restriction that these flags don't touch.
                // Kept for parity with the original's args list and because some of the other flags
                // (msOverlayScrollbarWinStyle) are still meaningful.
                //
                // CoreWebView2Environment.CreateAsync's overloads didn't match what either the base
                // Microsoft.Web.WebView2.Core.dll or its WinUI3-specific .Projection.dll counterpart
                // actually expose here (tried 1-arg and 3-arg forms, both rejected by the compiler) —
                // rather than keep guessing at an API surface that clearly differs from the plain .NET
                // docs in this WinUI3+projection combination, the well-documented environment-variable
                // configuration path sidesteps the ambiguity entirely and needs no API call at all.
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", string.Join(" ", Config.WebView2Args));
                await EditorView.EnsureCoreWebView2Async();
                Log("CoreWebView2 initialized OK");
                var staticsPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Statics");
                EditorView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "typedown.editor.local", staticsPath, CoreWebView2HostResourceAccessKind.Allow);
                // WebView2 has its own built-in accelerator keys (Ctrl+F opens its native find-on-page
                // bar, Ctrl+P prints, F5 refreshes, F12 opens DevTools...) that would otherwise compete
                // with our shortcuts and the app's own menu actions. Turning this off makes WebView2
                // behave like a plain content host instead of a mini-browser.
                EditorView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                // The actual fix for local images (see the WebView2Args comment above for the dead
                // end): Muya's renderer.js/getImageInfo.js build a plain file:/// URI for any local
                // image path and hand it to the DOM as-is (traced in Typedown.Editor's Muya lib) — we
                // can't change what URI scheme the editor emits without forking that JS. Rather than
                // fight Chromium's local-resource block, we intercept every file:/// request ourselves
                // and serve the bytes directly through WebView2's response pipeline, so the request
                // never reaches Chromium's own file loader (and its origin restriction) at all. Scoped
                // to CoreWebView2WebResourceContext.Image since that's the only local-file scheme this
                // app needs to serve — anything else falls through to the (still blocked) default.
                EditorView.CoreWebView2.AddWebResourceRequestedFilter("file:///*", CoreWebView2WebResourceContext.Image);
                EditorView.CoreWebView2.WebResourceRequested += EditorView_WebResourceRequested;
                EditorView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    var raw = args.TryGetWebMessageAsString();
                    Log($"WebMessage: {raw}");
                    transport.EmitWebViewMessage(this, raw);
                };
                EditorView.CoreWebView2.NavigationCompleted += (s, args) =>
                    Log($"NavigationCompleted: IsSuccess={args.IsSuccess}, WebErrorStatus={args.WebErrorStatus}");
                await EditorView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(HostShortcutScript);
                eventCenter.GetObservable<EditorEventArgs>("HostShortcut").Subscribe(x => HandleHostShortcut(x.Args));
                EditorView.Source = new Uri("https://typedown.editor.local/index.html");
                IsEditorLoaded = true;
            }
            catch (Exception ex)
            {
                IsEditorLoadFailed = true;
                Log($"EXCEPTION: {ex}");
            }
        }

        private static readonly Dictionary<string, string> ImageMimeTypes = new()
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".jfif"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".svg"] = "image/svg+xml",
            [".webp"] = "image/webp",
        };

        // Serves file:/// requests for <img> tags ourselves instead of letting Chromium's own file
        // loader handle them — see the registration comment in MainWindow_Loaded for why. Synchronous
        // and fast (local disk read), so no CoreWebView2Deferral is needed; per the WebView2 docs a
        // deferral is only required when the response is produced asynchronously.
        private void EditorView_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            try
            {
                var localPath = new Uri(args.Request.Uri).LocalPath;
                if (!File.Exists(localPath))
                {
                    Log($"WebResourceRequested: not found, {localPath}");
                    return;
                }
                var ext = Path.GetExtension(localPath).ToLowerInvariant();
                var contentType = ImageMimeTypes.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
                var stream = File.OpenRead(localPath);
                args.Response = EditorView.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream.AsRandomAccessStream(), 200, "OK", $"Content-Type: {contentType}");
            }
            catch (Exception ex)
            {
                Log($"WebResourceRequested EXCEPTION: {ex}");
            }
        }

        // Wire contract per Typedown.Editor/src/services/transport.ts: the web side does
        // `JSON.parse(event.data)` itself, so the host must post a STRING (PostWebMessageAsString),
        // not PostWebMessageAsJson (which would hand the web side an already-parsed object and break
        // its JSON.parse call). The envelope is flat: { name, args }.
        public bool PostMessage(string name, object arg)
        {
            try
            {
                var payload = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new { name, args = arg }, Config.EditorJsonSerializerSettings);
                Log($"PostMessage: {payload}");
                EditorView.CoreWebView2.PostWebMessageAsString(payload);
                return true;
            }
            catch (Exception ex)
            {
                Log($"PostMessage EXCEPTION: {ex}");
                return false;
            }
        }

        // --- Keyboard shortcuts while focus is inside the editor ---
        // XAML KeyboardAccelerator (see the MenuFlyoutItems in MainWindow.xaml) only fires when
        // keyboard focus is somewhere in the native XAML tree. WebView2 hosts an actual separate child
        // HWND for its Chromium content — when that HWND has focus (i.e. whenever you're actually
        // typing in the document, the overwhelmingly common case), the OS delivers key presses
        // straight to it, and XAML-declared accelerators never fire. This is a documented WebView2
        // limitation across every XAML host (WPF/UWP/WinUI3 alike), not specific to our setup, and
        // it's exactly what made Ctrl+S silently do nothing.
        //
        // The natural fix — CoreWebView2Controller.AcceleratorKeyPressed — turned out to be a dead
        // end: WinUI 3's XAML WebView2 control deliberately does not expose CoreWebView2Controller
        // ("WinUI takes care of the environment and window creation behind the scenes," per
        // Microsoft's own guidance). So instead we catch the keys in the web content itself, in the
        // capture phase (before the page's own handlers can see or stop them), and post them back to
        // the host over the same message channel Transport already listens on — no different from how
        // GetCurrentTheme/GetSettings/etc. work. AreBrowserAcceleratorKeysEnabled=false (set right
        // after EnsureCoreWebView2Async, above) stops WebView2's own built-in shortcuts (its native
        // Ctrl+F find-on-page bar in particular) from grabbing the key first.
        private const string HostShortcutScript = @"
            window.addEventListener('keydown', function (e) {
                if (!e.ctrlKey) return;
                var key = e.key.toLowerCase();
                if (key !== 's' && key !== 'o' && key !== 'n' && key !== 'w' && key !== 'f' && key !== 'p') return;
                e.preventDefault();
                e.stopPropagation();
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'message', name: 'HostShortcut', args: { key: key, shift: e.shiftKey }
                }));
            }, true);
        ";

        private void HandleHostShortcut(JToken args)
        {
            var key = args["key"]?.ToString();
            var shift = args["shift"]?.ToObject<bool>() ?? false;
            Log($"HostShortcut: key={key}, shift={shift}");
            switch (key)
            {
                case "n": NewMenuItem_Click(this, null); break;
                case "o": OpenMenuItem_Click(this, null); break;
                case "s" when shift: SaveAsMenuItem_Click(this, null); break;
                case "s": SaveMenuItem_Click(this, null); break;
                case "f": ShowFindReplace(); break;
                case "w": Close(); break;
                case "p": PrintMenuItem_Click(this, null); break;
            }
        }

        // --- Menu bar handlers ---
        // Unpackaged WinUI 3 apps must initialize file pickers with the owning window's HWND
        // (WinRT.Interop.InitializeWithWindow) — there's no implicit window context like there is
        // for a packaged/UWP app.

        private async void NewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmDiscardChangesIfNeeded()) return;
            file.NewFile();
            await OfferBackupRecoveryIfAny(null);
            UpdateTitle();
        }

        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmDiscardChangesIfNeeded()) return;
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            foreach (var ext in Utilities.FileTypeHelper.Markdown) picker.FileTypeFilter.Add(ext);
            var pickedFile = await picker.PickSingleFileAsync();
            if (pickedFile == null) return;
            await file.OpenFile(pickedFile.Path);
            await OfferBackupRecoveryIfAny(pickedFile.Path);
            recentFiles.Record(pickedFile.Path);
            RefreshRecentFilesMenu();
            UpdateTitle();
            Log($"OpenFile: {pickedFile.Path}");
        }

        private async void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (await file.Save())
            {
                recentFiles.Record(file.FilePath);
                RefreshRecentFilesMenu();
            }
            else
            {
                await SaveAsInternal();
            }
            Log($"Save: {file.FilePath}");
        }

        private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e) => await SaveAsInternal();

        private async System.Threading.Tasks.Task SaveAsInternal()
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Markdown", new System.Collections.Generic.List<string> { ".md" });
            picker.SuggestedFileName = file.DisplayName == "Untitled" ? "Untitled" : Path.GetFileNameWithoutExtension(file.DisplayName);
            var pickedFile = await picker.PickSaveFileAsync();
            if (pickedFile == null) return;
            await file.SaveAs(pickedFile.Path);
            recentFiles.Record(pickedFile.Path);
            RefreshRecentFilesMenu();
            UpdateTitle();
            Log($"SaveAs: {pickedFile.Path}");
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

        // --- Open Recent ---
        // Reimplemented against RecentFilesService (see Services/RecentFilesService.cs) rather than
        // AccessHistory/EF Core. MenuFlyoutSubItem has no Opening event to hook (confirmed by the XAML
        // compiler — WMC0011: Unknown member 'Opening'), so instead of rebuilding lazily on open, this
        // is kept in sync eagerly: RefreshRecentFilesMenu runs once at startup and again after every
        // Record/Remove/Clear call.
        private void RefreshRecentFilesMenu()
        {
            OpenRecentMenu.Items.Clear();
            if (recentFiles.Files.Count == 0)
            {
                OpenRecentMenu.Items.Add(new MenuFlyoutItem { Text = "No Recent Files", IsEnabled = false });
                return;
            }
            foreach (var path in recentFiles.Files)
            {
                var item = new MenuFlyoutItem { Text = path };
                item.Click += async (s, args) => await OpenRecentFile(path);
                OpenRecentMenu.Items.Add(item);
            }
            OpenRecentMenu.Items.Add(new MenuFlyoutSeparator());
            var clearItem = new MenuFlyoutItem { Text = "Clear Recent Files" };
            clearItem.Click += (s, args) => { recentFiles.Clear(); RefreshRecentFilesMenu(); };
            OpenRecentMenu.Items.Add(clearItem);
        }

        private async System.Threading.Tasks.Task OpenRecentFile(string path)
        {
            if (!await ConfirmDiscardChangesIfNeeded()) return;
            if (!File.Exists(path))
            {
                // Matches the original's behavior in LoadFile's not-found branch: a stale entry gets
                // dropped from history instead of leaving a dead link around.
                recentFiles.Remove(path);
                RefreshRecentFilesMenu();
                Log($"OpenRecentFile: missing {path}, removed from history");
                return;
            }
            await file.OpenFile(path);
            await OfferBackupRecoveryIfAny(path);
            recentFiles.Record(path);
            RefreshRecentFilesMenu();
            UpdateTitle();
            Log($"OpenRecentFile: {path}");
        }

        // --- Export & Print ---
        // Reimplemented, not ported: the original's Export()/ExportCallback() went through a whole
        // ExportConfig/IFileExport/PdfiumViewer pipeline (Controls/DialogControls/AddExportConfigDialog,
        // Enums/ExportType, per-format config models) that isn't ported. PDF and Print use WebView2's
        // own native PrintToPdfAsync/ShowPrintUI instead — genuinely simpler than replicating PDF
        // conversion by hand, and it's the current document as actually rendered, not a re-parse.
        // HTML export is the one case that still goes through the editor's own clean HTML generator
        // (ExportHtml, JS-side) via the real Export/ExportCallback wire messages, since WebView2 has no
        // "give me clean semantic HTML" API of its own to substitute.
        private TaskCompletionSource<string> pendingExportHtml;

        private Task<string> RequestExportHtml()
        {
            pendingExportHtml = new TaskCompletionSource<string>();
            PostMessage("Export", new { type = "export", context = (object)null, basePath = file.ImageBasePath, title = file.DisplayName });
            return pendingExportHtml.Task;
        }

        private async void ExportHtmlMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("HTML", new System.Collections.Generic.List<string> { ".html" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(file.DisplayName);
            var pickedFile = await picker.PickSaveFileAsync();
            if (pickedFile == null) return;
            var html = await RequestExportHtml();
            if (html == null)
            {
                Log("ExportHtml: editor returned no html");
                return;
            }
            await File.WriteAllTextAsync(pickedFile.Path, html);
            Log($"ExportHtml: {pickedFile.Path}");
        }

        private async void ExportPdfMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("PDF", new System.Collections.Generic.List<string> { ".pdf" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(file.DisplayName);
            var pickedFile = await picker.PickSaveFileAsync();
            if (pickedFile == null) return;
            var ok = await EditorView.CoreWebView2.PrintToPdfAsync(pickedFile.Path, null);
            Log($"ExportPdf: {pickedFile.Path}, success={ok}");
        }

        private async void ExportTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Plain Text", new System.Collections.Generic.List<string> { ".txt" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(file.DisplayName);
            var pickedFile = await picker.PickSaveFileAsync();
            if (pickedFile == null) return;
            await File.WriteAllTextAsync(pickedFile.Path, file.Markdown);
            Log($"ExportText: {pickedFile.Path}");
        }

        private void PrintMenuItem_Click(object sender, RoutedEventArgs e)
        {
            EditorView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System);
            Log("Print: ShowPrintUI invoked");
        }

        // --- Image handling ---
        // Reimplemented, not ported: the original's ImageToolbar/ImageSelector floating controls and
        // drag-drop-onto-EditorContainer path aren't built — this is the same PostMessage("InsertImage",
        // { src }) the original's drag-drop handler sent (EditorContainer.xaml.cs), just triggered from
        // a menu item instead of a drop event. src is the raw absolute filesystem path, unmodified —
        // that's what the original sent too, not a file:// URI or data URI.
        private async void InsertImageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            foreach (var ext in FileTypeHelper.Image) picker.FileTypeFilter.Add(ext);
            var pickedFile = await picker.PickSingleFileAsync();
            if (pickedFile == null) return;
            PostMessage("InsertImage", new { src = pickedFile.Path });
            Log($"InsertImage: {pickedFile.Path}");
        }

        // --- Settings dialog ---
        // Reads/writes the real SettingsViewModel directly (no x:Bind — we don't have the
        // AppViewModel-as-DataContext infrastructure the original pages relied on). suppressSettingsEvents
        // stops LoadSettingsIntoDialog's programmatic control updates from bouncing back into the
        // ViewModel as if the user had changed them.

        private bool suppressSettingsEvents;

        private async void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SettingsDialog.XamlRoot = Content.XamlRoot;
            LoadSettingsIntoDialog();
            await SettingsDialog.ShowAsync();
        }

        private void LoadSettingsIntoDialog()
        {
            suppressSettingsEvents = true;
            ThemeComboBox.SelectedIndex = settings.AppTheme switch { AppTheme.Light => 1, AppTheme.Dark => 2, _ => 0 };
            AutoSaveToggle.IsOn = settings.AutoSave;
            AnimationToggle.IsOn = settings.AnimationEnable;
            UseMicaToggle.IsOn = settings.UseMicaEffect;
            UseMicaToggle.IsEnabled = Config.IsMicaSupported;
            UseEditorMicaToggle.IsOn = settings.UseEditorMicaEffect;
            UseEditorMicaToggle.IsEnabled = Config.IsMicaSupported && settings.UseMicaEffect;
            FontSizeBox.Value = settings.FontSize;
            LineHeightBox.Value = settings.LineHeight;
            TabSizeBox.Value = settings.TabSize;
            EditorAreaWidthBox.Text = settings.EditorAreaWidth;
            suppressSettingsEvents = false;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSettingsEvents) return;
            var tag = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            settings.AppTheme = tag switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Default };
            ApplyNativeTheme();
            ApplyEditorBackground();
            PushThemeToEditor();
        }

        // Applies to our own chrome (title bar/menu/dialogs) immediately. Pushing the choice into the
        // editor's own live theme is UIViewModel territory (reactive system-theme + AppTheme tracking)
        // — still deferred, same as GetCurrentTheme's note in RegisterHandlers.
        private void ApplyNativeTheme()
        {
            var theme = settings.AppTheme switch { AppTheme.Light => ElementTheme.Light, AppTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
            ((FrameworkElement)Content).RequestedTheme = theme;
        }

        // Reimplemented against WinUI 3's own Window.SystemBackdrop property rather than a literal
        // port of whatever manual Mica setup the original's WPF+XAML-Islands host used — WinAppSDK
        // 1.6's SystemBackdrop API (MicaBackdrop/DesktopAcrylicBackdrop) replaced the older
        // WindowsSystemDispatcherQueueHelper + MicaController compositor dance entirely, so there's no
        // controller lifecycle to manage here. Falls back to no backdrop (plain solid chrome) on
        // Windows versions that don't support Mica (Config.IsMicaSupported, build < 22000) or when the
        // Use Mica setting is off. This only affects the window's own chrome (title bar, TocPane) — see
        // ApplyEditorBackground below for making the WebView2 editor area itself show Mica through.
        private void ApplyBackdrop()
        {
            SystemBackdrop = settings.UseMicaEffect && Config.IsMicaSupported ? new MicaBackdrop { Kind = MicaKind.Base } : null;
        }

        // The window-level Mica backdrop above doesn't reach through WebView2 on its own — Chromium's
        // surface is opaque by default regardless of what CSS the document sets. WebView2's XAML
        // control exposes DefaultBackgroundColor for exactly this (it forwards to the underlying
        // CoreWebView2Controller); pairing it with the document's own transparent body background
        // (theme.ts, driven by BuildThemePayload's `background` field below) is what actually lets
        // Mica show through the editor content, matching the original's UseMicaEffect &&
        // UseEditorMicaEffect condition in Common.cs's GetCurrentTheme.
        private void ApplyEditorBackground()
        {
            var editorMica = settings.UseMicaEffect && Config.IsMicaSupported && settings.UseEditorMicaEffect;
            EditorView.DefaultBackgroundColor = editorMica ? Color.FromArgb(0, 0, 0, 0)
                : ((FrameworkElement)Content).ActualTheme == ElementTheme.Dark ? Color.FromArgb(0xFF, 0x28, 0x28, 0x28)
                : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);
        }

        // Ported from Typedown\Utilities\Common.cs's GetCurrentTheme (used by both the original's
        // GetCurrentTheme wire handler and its live ThemeChanged push in MarkdownEditor.cs) — same
        // theme/accentColor/background shape, same colors. One deliberate departure: theme.ts (bundled
        // JS, used as-is) destructures accentColor as {r,g,b,a} but background as {R,G,B,A} — verified
        // by inspecting the actual wire payload, a plain anonymous object serialized through this
        // project's camelCase Config.EditorJsonSerializerSettings comes out {a,r,g,b} for BOTH, which
        // would leave background's rgba() built from four undefined values and silently no-op. A
        // JObject's keys pass through the serializer untouched (the naming strategy only reshapes
        // reflected POCO property names, not JToken trees already holding string keys), so background
        // is built that way here — the only way to actually match what theme.ts reads, not a guess.
        private object BuildThemePayload()
        {
            var isDarkMode = ((FrameworkElement)Content).ActualTheme == ElementTheme.Dark;
            var accentColor = uiSettings.GetColorValue(UIColorType.Accent);
            var solidBackground = isDarkMode ? Color.FromArgb(0xFF, 0x28, 0x28, 0x28) : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);
            var bg = settings.UseMicaEffect && settings.UseEditorMicaEffect ? Color.FromArgb(0, 0, 0, 0) : solidBackground;
            var background = new JObject { ["R"] = bg.R, ["G"] = bg.G, ["B"] = bg.B, ["A"] = bg.A };
            return new { theme = isDarkMode ? "Dark" : "Light", accentColor, background };
        }

        private void PushThemeToEditor() => PostMessage("ThemeChanged", BuildThemePayload());

        // Reimplemented against a plain PropertyChanged subscription rather than the original's
        // Reactive Extensions Merge() chain (UIViewModel.cs / MarkdownEditor.cs) — same three triggers
        // (system theme/accent change, AppTheme setting, the two Mica settings), just without pulling
        // in an Rx observable chain for three property names. uiSettings.ColorValuesChanged fires off
        // the UI thread, so it's marshalled back via DispatcherQueue before touching Content/WebView2.
        // Only wires the system theme/accent-color half (a genuine WinRT event, unrelated to
        // SettingsViewModel). The AppTheme/UseMicaEffect/UseEditorMicaEffect half is NOT wired through
        // settings.PropertyChanged — SettingsViewModel defines its own OnPropertyChanged(name, before,
        // after) hook for the original's notifySet-driven SettingsChanged push (FontSize etc.), and
        // Fody.PropertyChanged uses a class-supplied hook like that as the sole notification path
        // instead of also raising the plain INotifyPropertyChanged event — confirmed by instrumenting
        // it: an external `settings.PropertyChanged +=` subscriber here never fired even across a
        // genuine Dark→Light change. So each of those three settings pushes the theme directly from
        // its own Toggled/SelectionChanged handler below instead, same as ApplyBackdrop already did.
        private void SetUpThemePush()
        {
            uiSettings.ColorValuesChanged += (s, e) => DispatcherQueue.TryEnqueue(() =>
            {
                ApplyNativeTheme();
                ApplyEditorBackground();
                PushThemeToEditor();
            });
        }

        private void AutoSaveToggle_Toggled(object sender, RoutedEventArgs e) { if (!suppressSettingsEvents) settings.AutoSave = AutoSaveToggle.IsOn; }

        private void UseMicaToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (suppressSettingsEvents) return;
            settings.UseMicaEffect = UseMicaToggle.IsOn;
            ApplyBackdrop();
            ApplyEditorBackground();
            PushThemeToEditor();
            UseEditorMicaToggle.IsEnabled = Config.IsMicaSupported && settings.UseMicaEffect;
        }

        private void UseEditorMicaToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (suppressSettingsEvents) return;
            settings.UseEditorMicaEffect = UseEditorMicaToggle.IsOn;
            ApplyEditorBackground();
            PushThemeToEditor();
        }

        private void AnimationToggle_Toggled(object sender, RoutedEventArgs e) { if (!suppressSettingsEvents) settings.AnimationEnable = AnimationToggle.IsOn; }

        private void FontSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { if (!suppressSettingsEvents && !double.IsNaN(args.NewValue)) settings.FontSize = args.NewValue; }

        private void LineHeightBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { if (!suppressSettingsEvents && !double.IsNaN(args.NewValue)) settings.LineHeight = args.NewValue; }

        private void TabSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { if (!suppressSettingsEvents && !double.IsNaN(args.NewValue)) settings.TabSize = (int)args.NewValue; }

        private void EditorAreaWidthBox_TextChanged(object sender, TextChangedEventArgs e) { if (!suppressSettingsEvents) settings.EditorAreaWidth = EditorAreaWidthBox.Text; }

        // --- Find & Replace ---
        // Wire contract traced from Typedown.Editor/src/components/Muya/index.tsx: SearchOpenChange
        // toggles the in-editor highlight overlay, Search sets the term + options, Find moves between
        // matches, Replace does the actual text substitution. All the matching/highlighting logic
        // lives in the web editor — this panel only sends what it's told to.

        private void FindMenuItem_Click(object sender, RoutedEventArgs e) => ShowFindReplace();

        private void ShowFindReplace()
        {
            Log("ShowFindReplace called");
            FindReplacePanel.Visibility = Visibility.Visible;
            FindTextBox.Focus(FocusState.Programmatic);
            FindTextBox.SelectAll();
            PostMessage("SearchOpenChange", new { open = 1 });
            PushSearch();
        }

        private void HideFindReplace()
        {
            FindReplacePanel.Visibility = Visibility.Collapsed;
            PostMessage("SearchOpenChange", new { open = 0 });
        }

        private void CloseFind_Click(object sender, RoutedEventArgs e) => HideFindReplace();

        private void PushSearch() => PostMessage("Search", new
        {
            value = string.IsNullOrEmpty(FindTextBox.Text) ? null : FindTextBox.Text,
            opt = new
            {
                searchIsCaseSensitive = CaseSensitiveCheck.IsChecked == true,
                searchIsWholeWord = WholeWordCheck.IsChecked == true,
                searchIsRegexp = RegexCheck.IsChecked == true,
            },
        });

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) => PushSearch();

        private void SearchOption_Changed(object sender, RoutedEventArgs e) => PushSearch();

        private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
                PostMessage("Find", new { action = "next" });
            else if (e.Key == VirtualKey.Escape)
                HideFindReplace();
        }

        private void FindNext_Click(object sender, RoutedEventArgs e) => PostMessage("Find", new { action = "next" });

        private void FindPrev_Click(object sender, RoutedEventArgs e) => PostMessage("Find", new { action = "prev" });

        private void Replace_Click(object sender, RoutedEventArgs e) => PushReplace(true);

        private void ReplaceAll_Click(object sender, RoutedEventArgs e) => PushReplace(false);

        private void PushReplace(bool isSingle) => PostMessage("Replace", new
        {
            value = ReplaceTextBox.Text,
            opt = new
            {
                isSingle,
                searchIsCaseSensitive = CaseSensitiveCheck.IsChecked == true,
                searchIsWholeWord = WholeWordCheck.IsChecked == true,
                searchIsRegexp = RegexCheck.IsChecked == true,
            },
        });

        // --- Table of contents pane ---
        // Traced from EditorViewModel.cs (Toc built from ContentState.Toc on every StateChange) and
        // JumpBySlug (PostMessage("ScrollTo", { slug })), confirmed against the ScrollTo listener in
        // Typedown.Editor/src/components/Muya/index.tsx. StateChange always carries the full
        // reconstructed state by the time it reaches EventCenter — Transport already resolves the
        // diff/no-diff distinction before emitting, so there's no partial-JSON handling needed here.

        private void UpdateToc(JToken args)
        {
            try
            {
                var toc = args["state"]?["toc"];
                if (toc == null) return;
                tocEntries.Clear();
                foreach (var item in toc)
                {
                    tocEntries.Add(new TocEntry
                    {
                        Content = item["content"]?.ToString(),
                        Slug = item["slug"]?.ToString(),
                        Lvl = item["lvl"]?.ToObject<int>() ?? 1,
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"UpdateToc EXCEPTION: {ex}");
            }
        }

        private void TocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocEntry entry)
                PostMessage("ScrollTo", new { slug = entry.Slug });
        }

        private void TocMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var visible = TocMenuItem.IsChecked;
            TocPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            TocColumn.Width = new GridLength(visible ? 220 : 0);
        }

        // --- Folder browsing ---
        // Ported (structurally) from Typedown.Core\Controls\SidePaneControls\Pages\FolderPage.xaml.cs
        // against the ExplorerItem tree (Models\ExplorerItem.cs) — see the XAML comment above
        // FolderTreeView for what's still cut versus the original (drag-drop, clipboard cut/copy/
        // paste). rootExplorerItem's Children is what FolderTreeView is bound to; the root item itself
        // is never shown, matching the original's WorkFolderExplorerItem.
        private readonly HashSet<string> expandedFolderPaths = new();
        private ExplorerItem rootExplorerItem;

        // Windows.Storage.Pickers.FolderPicker (the WinRT picker used everywhere else in this file)
        // throws COMException 0x80004005 (E_FAIL) reliably here — confirmed reproducible, not a
        // one-off. FileOpenPicker/FileSavePicker (this project's Open/Save/SaveAs) don't hit it; the
        // difference is StorageFolder vs. StorageFile, and StorageFolder marshalling back to an
        // unpackaged process is a documented limitation, not something a picker property fixes
        // (SuggestedStartLocation didn't help). Win32FolderPicker talks to the same native dialog
        // through the plain IFileOpenDialog COM interface instead — see its own file for why
        // System.Windows.Forms.FolderBrowserDialog isn't used either (UseWindowsForms breaks the
        // WinUI 3 XAML compiler's resource resolution in this SDK version).
        private async void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var picked = await Win32FolderPicker.PickFolderAsync(WindowNative.GetWindowHandle(this));
            if (picked == null) return;
            if (rootExplorerItem == null)
            {
                rootExplorerItem = new ExplorerItem(expandedFolderPaths, DispatcherQueue);
                FolderTreeView.ItemsSource = rootExplorerItem.Children;
            }
            rootExplorerItem.FullPath = picked;
            rootExplorerItem.IsExpanded = true;
            FolderHeaderText.Text = rootExplorerItem.Name;
            FolderSection.Visibility = Visibility.Visible;
            UpdateFolderSelection();
            Log($"OpenFolder: {picked}");
        }

        // Keeps the tree's selection highlight on whatever file is currently open, including when it
        // changed via Open/Open Recent/New rather than a click inside the tree itself. Hooked onto
        // FileViewModel.FileStateChanged (see the constructor), which already fires on every load/save.
        private void UpdateFolderSelection()
        {
            if (rootExplorerItem == null) return;
            void Walk(ExplorerItem item)
            {
                item.IsSelected = item.FullPath == file.FilePath;
                foreach (var child in item.Children) Walk(child);
            }
            foreach (var child in rootExplorerItem.Children) Walk(child);
        }

        private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is ExplorerItem item && item.Type == ExplorerItem.ExplorerItemType.File)
                await OpenFolderTreeFile(item);
        }

        private async Task OpenFolderTreeFile(ExplorerItem item)
        {
            if (item.FullPath == file.FilePath) return;
            if (!await ConfirmDiscardChangesIfNeeded()) return;
            await file.OpenFile(item.FullPath);
            await OfferBackupRecoveryIfAny(item.FullPath);
            recentFiles.Record(item.FullPath);
            RefreshRecentFilesMenu();
            UpdateTitle();
            Log($"FolderTree open: {item.FullPath}");
        }

        // --- Folder tree context menu ---
        // A ContextFlyout's items inherit DataContext from whatever element it was opened on (the
        // TreeViewItem in FolderItemTemplate/FileItemTemplate) — standard WinUI 3/UWP flyout behavior,
        // and the same mechanism the original relied on for its GetExplorerItemFromMenuFlyoutItem.
        private static ExplorerItem GetContextItem(object sender) => (sender as FrameworkElement)?.DataContext as ExplorerItem;

        private async void OpenContext_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is ExplorerItem item && item.Type == ExplorerItem.ExplorerItemType.File)
                await OpenFolderTreeFile(item);
        }

        private async void NewFileContext_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is not ExplorerItem item || item.Type != ExplorerItem.ExplorerItemType.Folder) return;
            var name = await PromptForName("New File", "Untitled.md");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var path = Path.Combine(item.FullPath, name);
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"'{name}' already exists.");
                File.Create(path).Dispose();
                item.IsExpanded = true;
                Log($"NewFile: {path}");
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Couldn't create file", ex.Message);
            }
        }

        private async void NewFolderContext_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is not ExplorerItem item || item.Type != ExplorerItem.ExplorerItemType.Folder) return;
            var name = await PromptForName("New Folder", "New Folder");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var path = Path.Combine(item.FullPath, name);
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"'{name}' already exists.");
                Directory.CreateDirectory(path);
                item.IsExpanded = true;
                Log($"NewFolder: {path}");
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Couldn't create folder", ex.Message);
            }
        }

        private async void RenameContext_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is not ExplorerItem item || item == rootExplorerItem) return;
            var newName = await PromptForName("Rename", item.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
            try
            {
                var newPath = Path.Combine(Path.GetDirectoryName(item.FullPath), newName);
                if (File.Exists(newPath) || Directory.Exists(newPath)) throw new IOException($"'{newName}' already exists.");
                if (item.Type == ExplorerItem.ExplorerItemType.Folder)
                    Directory.Move(item.FullPath, newPath);
                else
                    File.Move(item.FullPath, newPath);
                if (item.FullPath == file.FilePath)
                {
                    file.RenamePathOnly(newPath);
                    recentFiles.Remove(item.FullPath);
                    recentFiles.Record(newPath);
                    RefreshRecentFilesMenu();
                    UpdateTitle();
                }
                Log($"Rename: {item.FullPath} -> {newPath}");
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Couldn't rename", ex.Message);
            }
        }

        private async void DeleteContext_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is not ExplorerItem item || item == rootExplorerItem) return;
            var isFolder = item.Type == ExplorerItem.ExplorerItemType.Folder;
            var confirm = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = $"Delete {(isFolder ? "folder" : "file")}?",
                Content = $"'{item.Name}' will be moved to the Recycle Bin.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                // Recycle Bin, not a permanent delete. Microsoft.VisualBasic.FileIO.FileSystem is the
                // simplest way to get that from a plain .NET app — despite the namespace, it's just a
                // small framework-provided assembly with no relation to VB the language.
                if (isFolder)
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                Log($"Delete: {item.FullPath}");
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Couldn't delete", ex.Message);
            }
        }

        private void RevealContext_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is not ExplorerItem item) return;
            // /select opens Explorer with the item highlighted — the simple well-known equivalent of
            // the original's Common.OpenFileLocation (Windows Shell OpenFolderAndSelectItems API) for
            // a single path.
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }

        private async Task<string> PromptForName(string title, string startingText)
        {
            TextInputDialog.Title = title;
            TextInputDialog.XamlRoot = Content.XamlRoot;
            TextInputBox.Text = startingText;
            TextInputBox.SelectAll();
            var result = await TextInputDialog.ShowAsync();
            return result == ContentDialogResult.Primary ? TextInputBox.Text.Trim() : null;
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "OK" };
            await dialog.ShowAsync();
        }
    }
}
