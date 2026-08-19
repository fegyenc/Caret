using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Typedown.WinUI
{
    // Ported from Typedown.Core\Config.cs. The System.Runtime.CompilerServices.IsExternalInit
    // polyfill the original needed for netcoreapp3.1/UAP is dropped — net8.0 already has it,
    // and redefining it here would collide.
    public static class Config
    {
        public static bool IsMicaSupported { get; } = Environment.OSVersion.Version.Build >= 22000;

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
        }
    }
}
