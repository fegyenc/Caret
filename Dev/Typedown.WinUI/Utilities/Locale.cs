using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Typedown.WinUI.Utilities
{
    // Reimplemented, not ported: the original Typedown.Core\Utilities\Locale.cs reads strings via
    // UWP's compiled PRI resource system (Windows.ApplicationModel.Resources.Core.ResourceManager),
    // which is tied to app package identity and doesn't carry over cleanly to an unpackaged WinUI 3
    // app. Rather than fight the PRI/MRT migration as part of this pass, this reads the same .resw
    // files directly as plain XML at startup. Same public GetString/GetDialogString surface as the
    // original, so callers (and the LocaleAttribute/LocaleString markup extension once ported) don't
    // need to change. Only "en" is shipped for now — the other ~69 languages under
    // Typedown.Core\Resources\Strings are a mechanical copy-over once this holds up.
    public static class Locale
    {
        public enum ResourceSource
        {
            All,
            CommonResources,
            DialogResources,
            SettingsResources,
            Resources
        }

        private static readonly Dictionary<ResourceSource, Dictionary<string, string>> resourcesDictionary = new();

        public static string CurrentLang { get; private set; } = "en";

        static Locale()
        {
            Load(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }

        public static void Load(string lang)
        {
            var stringsRoot = Path.Combine(AppContext.BaseDirectory, "Strings");
            var langRoot = Path.Combine(stringsRoot, lang);
            if (!Directory.Exists(langRoot))
                langRoot = Path.Combine(stringsRoot, "en"); // fallback for any language we haven't copied over yet
            CurrentLang = Path.GetFileName(langRoot);

            resourcesDictionary.Clear();
            foreach (ResourceSource source in Enum.GetValues(typeof(ResourceSource)))
            {
                if (source == ResourceSource.All) continue;
                var path = Path.Combine(langRoot, $"{source}.resw");
                resourcesDictionary[source] = File.Exists(path) ? ParseResw(path) : new Dictionary<string, string>();
            }
        }

        private static Dictionary<string, string> ParseResw(string path)
        {
            var doc = XDocument.Load(path);
            return doc.Root
                .Elements("data")
                .Select(x => new { Name = x.Attribute("name")?.Value, Value = x.Element("value")?.Value })
                .Where(x => x.Name != null && x.Value != null)
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.First().Value);
        }

        public static string GetString(string key, ResourceSource source = ResourceSource.All)
        {
            key = key.Replace('.', '/');
            if (source == ResourceSource.All)
                return resourcesDictionary.Values
                    .Select(dict => dict.TryGetValue(key, out var value) ? value : null)
                    .FirstOrDefault(x => !string.IsNullOrEmpty(x));
            return resourcesDictionary.TryGetValue(source, out var dict) && dict.TryGetValue(key, out var v) ? v : null;
        }

        public static string GetDialogString(string key) => GetString(key, ResourceSource.DialogResources);
    }

    // Ported verbatim from Typedown.Core\Utilities\Locale.cs — pure reflection/attribute code, no UWP dependency.
    public class LocaleAttribute : Attribute
    {
        public string[] Keys { get; }

        public string Text => Texts.FirstOrDefault();

        public IEnumerable<string> Texts => Keys.Select(x => Locale.GetString(x));

        public LocaleAttribute(params string[] keys)
        {
            Keys = keys;
        }
    }
}
