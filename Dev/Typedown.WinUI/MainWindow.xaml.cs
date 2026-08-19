using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Typedown.WinUI.Enums;
using Typedown.WinUI.Interfaces;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;
using Typedown.WinUI.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
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
            RegisterHandlers();
            SetUpTitleBar();
            SetUpClosingPrompt();
            SetUpAutoSaveTimer();
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

        // --- Auto-save ---
        // Ported the shape, not the code, of FileViewModel's saveFileTimer (5-second DispatcherTimer
        // tick in the original). Only the AutoSaveFile half is here — silently re-save a dirty file
        // that already has a path when the Auto save setting is on. The AutoBackupFile fallback (backs
        // up untitled/AutoSave-off documents to a recovery location) needs the AutoBackup service,
        // which isn't ported — that's a real gap: unsaved untitled documents still have no safety net
        // if the app crashes, only Ctrl+S/the unsaved-changes prompt protect against losing work.
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
            };
            timer.Start();
        }

        // GetSettings/GetStringResources/Markdown/BasePath are real now (backed by SettingsViewModel,
        // Locale, and FileViewModel). GetCurrentTheme is still a stand-in for UIViewModel's actual
        // theme-tracking logic (system theme + AppTheme setting reactively kept in sync) — deferred.
        private void RegisterHandlers()
        {
            remoteInvoke.Handle("GetCurrentTheme", () => settings.AppTheme switch
            {
                AppTheme.Light => "Light",
                AppTheme.Dark => "Dark",
                _ => ((FrameworkElement)Content).ActualTheme.ToString(),
            });
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
                UpdateTitle();
                Log($"LoadStartUpMarkdown: FilePath={file.FilePath}, chars={file.Markdown.Length}");
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
                if (key !== 's' && key !== 'o' && key !== 'n' && key !== 'w' && key !== 'f') return;
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
            recentFiles.Record(path);
            RefreshRecentFilesMenu();
            UpdateTitle();
            Log($"OpenRecentFile: {path}");
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
        }

        // Applies to our own chrome (title bar/menu/dialogs) immediately. Pushing the choice into the
        // editor's own live theme is UIViewModel territory (reactive system-theme + AppTheme tracking)
        // — still deferred, same as GetCurrentTheme's note in RegisterHandlers.
        private void ApplyNativeTheme()
        {
            var theme = settings.AppTheme switch { AppTheme.Light => ElementTheme.Light, AppTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
            ((FrameworkElement)Content).RequestedTheme = theme;
        }

        private void AutoSaveToggle_Toggled(object sender, RoutedEventArgs e) { if (!suppressSettingsEvents) settings.AutoSave = AutoSaveToggle.IsOn; }

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
    }
}
