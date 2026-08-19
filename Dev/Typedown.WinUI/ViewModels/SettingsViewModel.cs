using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using Typedown.WinUI.Enums;
using Typedown.WinUI.Interfaces;

namespace Typedown.WinUI.ViewModels
{
    // Ported from Typedown.Core\ViewModels\SettingsViewModel.cs. The property list, JSON-file-backed
    // store (GetSettingValue/SetSettingValue/LoadAllSettings/SaveAllSettings), and change notification
    // are all unchanged in shape — this was already framework-agnostic despite living in the UWP
    // project. What's deferred to later milestones:
    //   - StartupPlacement (needs PInvoke.WINDOWPLACEMENT, not ported yet — milestone #3)
    //   - ResetSettingsCommand / ResetSetting() (needs AppContentDialog + XamlRoot — milestone #5)
    //   - Full IServiceProvider-based DI (this takes a plain IMarkdownEditor reference instead, since
    //     that's the only service this class actually needs)
    // PropertyChanged.Fody (see FodyWeavers.xml) weaves in the INotifyPropertyChanged raises and the
    // OnPropertyChanged(name, before, after) calls automatically, exactly like the original — no
    // change needed to how the properties themselves are declared.
    public sealed partial class SettingsViewModel : INotifyPropertyChanged, IDisposable
    {
        public bool SidePaneOpen { get => GetSettingValue(false); set => SetSettingValue(value); }
        public double SidePaneWidth { get => GetSettingValue(300d); set => SetSettingValue(value); }
        public bool StatusBarOpen { get => GetSettingValue(true); set => SetSettingValue(value); }
        public double FindReplaceDialogWidth { get => GetSettingValue(600d); set => SetSettingValue(value); }
        public bool SourceCode { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool Typewriter { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool FocusMode { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool SearchIsCaseSensitive { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool SearchIsRegexp { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool SearchIsWholeWord { get => GetSettingValue(false); set => SetSettingValue(value); }
        public int SidePaneIndex { get => GetSettingValue(0); set => SetSettingValue(value); }
        public double FontSize { get => GetSettingValue(16d); set => SetSettingValue(value); }
        public double LineHeight { get => GetSettingValue(1.6d); set => SetSettingValue(value); }
        public bool AutoPairBracket { get => GetSettingValue(true); set => SetSettingValue(value); }
        public bool AutoPairQuote { get => GetSettingValue(true); set => SetSettingValue(value); }
        public bool TrimUnnecessaryCodeBlockEmptyLines { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool PreferLooseListItem { get => GetSettingValue(true); set => SetSettingValue(value); }
        public bool AutoPairMarkdownSyntax { get => GetSettingValue(true); set => SetSettingValue(value); }
        public string EditorAreaWidth { get => GetSettingValue("1200px"); set => SetSettingValue(value); }
        public bool AutoSave { get => GetSettingValue(false); set => SetSettingValue(value); }
        public AppTheme AppTheme { get => GetSettingValue(AppTheme.Default); set => SetSettingValue(value); }
        public string Language { get => GetSettingValue("default"); set => SetSettingValue(value); }
        public int WordCountMethod { get => GetSettingValue(0); set => SetSettingValue(value); }
        public int TabSize { get => GetSettingValue(4); set => SetSettingValue(value); }
        public bool SpellcheckEnabled { get => GetSettingValue(false); set => SetSettingValue(value); }
        public string SpellcheckLang { get => GetSettingValue(""); set => SetSettingValue(value); }
        public bool KeepRun { get => GetSettingValue(Config.IsPackaged); set => SetSettingValue(value); }
        public bool AnimationEnable { get => GetSettingValue(true); set => SetSettingValue(value); }
        public bool UseMicaEffect { get => GetSettingValue(Config.IsMicaSupported); set => SetSettingValue(value); }
        public bool UseEditorMicaEffect { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool Topmost { get => GetSettingValue(false); set => SetSettingValue(value); }
        public FileStartupAction FileStartupAction { get => GetSettingValue(FileStartupAction.None); set => SetSettingValue(value); }
        public FolderStartupAction FolderStartupAction { get => GetSettingValue(FolderStartupAction.OpenLast); set => SetSettingValue(value); }
        public string StartupOpenFolder { get => GetSettingValue(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); set => SetSettingValue(value); }
        public bool AppCompactMode { get => GetSettingValue(false); set => SetSettingValue(value); }
        public InsertImageAction InsertClipboardImageAction { get => GetSettingValue(InsertImageAction.None); set => SetSettingValue(value); }
        public string InsertClipboardImageCopyPath { get => GetSettingValue("./images"); set => SetSettingValue(value); }
        public int? InsertClipboardImageUseUploadConfigId { get => GetSettingValue<int?>(null); set => SetSettingValue(value); }
        public InsertImageAction InsertLocalImageAction { get => GetSettingValue(InsertImageAction.None); set => SetSettingValue(value); }
        public string InsertLocalImageCopyPath { get => GetSettingValue("./images"); set => SetSettingValue(value); }
        public int? InsertLocalImageUseUploadConfigId { get => GetSettingValue<int?>(null); set => SetSettingValue(value); }
        public InsertImageAction InsertWebImageAction { get => GetSettingValue(InsertImageAction.None); set => SetSettingValue(value); }
        public string InsertWebImageCopyPath { get => GetSettingValue("./images"); set => SetSettingValue(value); }
        public int? InsertWebImageUseUploadConfigId { get => GetSettingValue<int?>(null); set => SetSettingValue(value); }
        public string DefaultImageBasePath { get => GetSettingValue(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Config.AppName)); set => SetSettingValue(value); }
        public bool AutoCopyRelativePathImage { get => GetSettingValue(true); set => SetSettingValue(value); }
        public bool PreferRelativeImagePaths { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool AddSymbolBeforeRelativePath { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool AutoEncodeImageURL { get => GetSettingValue(true); set => SetSettingValue(value); }
        public bool OpenFolderAfterExport { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool FileExportDatabaseInitialized { get => GetSettingValue(false); set => SetSettingValue(value); }
        public bool ImageUploadDatabaseInitialized { get => GetSettingValue(false); set => SetSettingValue(value); }

        private readonly IMarkdownEditor markdownEditor;

        private readonly CompositeDisposable disposables = new();

        private readonly string settingsFile = Path.Combine(Config.GetLocalFolderPath(), "Settings.json");

        private JToken store;

        private readonly HashSet<string> notifySet = new()
        {
            "SourceCode", "Typewriter", "FocusMode", "SearchIsCaseSensitive", "SearchIsRegexp",
            "SearchIsWholeWord", "FontSize", "LineHeight", "AutoPairBracket", "AutoPairQuote",
            "TrimUnnecessaryCodeBlockEmptyLines", "PreferLooseListItem", "AutoPairMarkdownSyntax", "EditorAreaWidth"
        };

        public SettingsViewModel(IMarkdownEditor markdownEditor = null)
        {
            this.markdownEditor = markdownEditor;
            LoadAllSettings();
        }

        private void LoadAllSettings()
        {
            try
            {
                store = JToken.Parse(File.ReadAllText(settingsFile));
            }
            catch
            {
                store = new JObject();
            }
        }

        private async void SaveAllSettings()
        {
            try
            {
                await File.WriteAllTextAsync(settingsFile, store.ToString());
            }
            catch
            {
                // Ignore
            }
        }

        public T GetSettingValue<T>(T defaultValue = default, [CallerMemberName] string propertyName = null)
        {
            return (T)(store[propertyName]?.ToObject(typeof(T)) ?? defaultValue);
        }

        public void SetSettingValue<T>(T value, [CallerMemberName] string propertyName = null)
        {
            if (value is null || value is string || value is long || value is int || value is short || value is sbyte || value is ulong ||
                value is uint || value is ushort || value is byte || value is Enum || value is double || value is float || value is decimal ||
                value is DateTime || value is byte[] || value is bool || value is Guid || value is Uri || value is TimeSpan)
                store[propertyName] = new JValue(value);
            else
                store[propertyName] = JObject.FromObject(value);
            SaveAllSettings();
        }

        public void OnPropertyChanged(string propertyName, object before, object after)
        {
            if (notifySet.Contains(propertyName))
                markdownEditor?.PostMessage("SettingsChanged", new Dictionary<string, object>() { { propertyName, after } });
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
