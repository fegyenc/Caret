using Microsoft.UI.Xaml;

namespace Typedown.WinUI
{
    public partial class App : Application
    {
        private Window? window;

        public App()
        {
            Services.DiagLog.Verbose = !Config.IsPackaged || Services.DiagLog.VerboseRequested;
            // An error no code catches. Before, it closed Caret without a word, and Partner Center could only count
            // "Unknown" failures. Now it is written to the local log first (only the error: no document text). One that
            // reaches XAML is marked handled so the window and the unsaved work stay; the notes have their own backups.
            UnhandledException += (s, e) =>
            {
                Services.DiagLog.Error("Unhandled (UI)", e.Exception);
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Services.DiagLog.Error("Unobserved task", e.Exception);
                e.SetObserved();
            };
            System.AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Services.DiagLog.Write($"Unhandled (fatal={e.IsTerminating}): {e.ExceptionObject}");
            // The app starts in the theme chosen in Caret, not only its windows: resources that controls
            // from libraries (the tab strip, the Settings cards) look up are resolved against the app's
            // theme, so with Windows in dark mode and Caret set to light they came out dark. It can only
            // be set here, before anything loads; after a change in View > Theme, those few parts follow
            // on the next start.
            var theme = new ViewModels.SettingsViewModel().AppTheme;
            if (theme == Enums.AppTheme.Light) RequestedTheme = ApplicationTheme.Light;
            else if (theme == Enums.AppTheme.Dark) RequestedTheme = ApplicationTheme.Dark;
            InitializeComponent();
            // Settings > Layout > Density: WinUI's own compact sizing for every control.
            if (new ViewModels.SettingsViewModel().AppCompactMode)
            {
                try { Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new System.Uri("ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml") }); }
                catch { }
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Before any window: XAML resolves its {u:Loc} strings as it loads.
            Utilities.Locale.Load(new ViewModels.SettingsViewModel().Language);
            window = new MainWindow();
            window.Activate();
        }
    }
}
