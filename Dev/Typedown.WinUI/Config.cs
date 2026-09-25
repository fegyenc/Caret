using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI;

namespace Typedown.WinUI
{
    // Ported from Typedown.Core\Config.cs. The System.Runtime.CompilerServices.IsExternalInit
    // polyfill the original needed for netcoreapp3.1/UAP is dropped — net8.0 already has it,
    // and redefining it here would collide.
    public static class Config
    {
        public static bool IsMicaSupported { get; } = Environment.OSVersion.Version.Build >= 22000;

        // Single source of truth for the handful of places that need the brand palette as a raw
        // Windows.UI.Color rather than a XAML brush — WebView2.DefaultBackgroundColor (no
        // {ThemeResource} binding support) and the theme payload pushed to the web editor
        // (BuildThemePayload, in MainWindow.xaml.cs). Matches Themes/Caret.xaml's
        // CaretBackgroundColor/CaretPrimaryColor/CaretSecondaryColor exactly — keep them in sync if
        // the design tokens ever change.
        public static Color BrandLightBackground { get; } = Color.FromArgb(0xFF, 0xF8, 0xEB, 0xDD);
        public static Color BrandDarkBackground { get; } = Color.FromArgb(0xFF, 0x0E, 0x12, 0x20);
        public static Color BrandLightAccent { get; } = Color.FromArgb(0xFF, 0xA5, 0x52, 0x2A);
        public static Color BrandDarkAccent { get; } = Color.FromArgb(0xFF, 0x8F, 0x4A, 0x22);

        public static IReadOnlyList<string> WebView2Args { get; } = new List<string>()
        {
            "--disable-web-security",
            "--allow-file-access-from-files",
            "--flag-switches-begin",
            "--enable-features=msOverlayScrollbarWinStyle",
            "--flag-switches-end"
        };

        public static JsonSerializerSettings EditorJsonSerializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy(true, true)
            },
            MaxDepth = 256
        };

        public static string GetLocalFolderPath()
        {
            try
            {
                return ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string AppName => "Caret";

        public static bool IsPackaged { get; private set; }

        // Ported from Typedown.Core\Controls\AboutApp.xaml.cs's GetAppVersion() for the About settings
        // section — packaged builds read the MSIX identity's version, unpackaged builds fall back to
        // the assembly version and say so, since there's no package identity to ask.
        public static string AppVersion { get; private set; }

        static Config()
        {
            try
            {
                IsPackaged = Package.Current != null;
            }
            catch
            {
                IsPackaged = false;
            }
            if (IsPackaged)
            {
                var v = Package.Current.Id.Version;
                AppVersion = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            else
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                AppVersion = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision} (Unpackaged)";
            }
        }
    }
}
