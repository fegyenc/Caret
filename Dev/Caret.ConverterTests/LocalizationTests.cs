using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Caret.ConverterTests
{
    // The translations (Dev/Typedown.WinUI/Strings/<lang>/AppResources.resw) must stay in step with the English file: every key,
    // the same {placeholders}, the same line breaks. A string missing from a language shows in English (the app falls back),
    // but one with a lost {0} breaks a sentence.
    public class LocalizationTests
    {
        private static string StringsFolder => Path.GetFullPath(Path.Combine(TestPaths.ProjectFolder, "..", "Typedown.WinUI", "Strings"));

        public static IEnumerable<object[]> Languages => new[] { "fr", "es", "pl" }.Select(l => new object[] { l });

        private static Dictionary<string, string> Read(string language) =>
            XDocument.Load(Path.Combine(StringsFolder, language, "AppResources.resw")).Root.Elements("data")
                .ToDictionary(x => x.Attribute("name").Value, x => x.Element("value")?.Value ?? "");

        // The editor's own strings are in the translations only: English has them in the files inherited from Typedown.
        private static readonly string[] EditorStrings =
        {
            "InputFootnoteDefine", "InputYAMLFrontMatter", "InputMathFormula", "InputLanguageIdentifier", "ClickToAddAnImage", "LoadImageFail",
            "Footnote", "FootnoteNotFound", "Create", "GoTo", "AlignLeft", "AlignCenter", "AlignRight", "DeleteTable", "CopyContent", "CtrlAndClickOpenLink",
        };

        // Fails with the keys that broke the rule, so the message says which strings to fix.
        private static void None(IEnumerable<string> keys)
        {
            var list = keys.ToList();
            Assert.True(list.Count == 0, "strings to fix: " + string.Join(", ", list));
        }

        private static string[] Placeholders(string text) => Regex.Matches(text, @"\{[A-Za-z0-9]+\}").Select(m => m.Value).OrderBy(x => x).ToArray();

        [Theory]
        [MemberData(nameof(Languages))]
        public void EveryEnglishStringHasATranslation(string language)
        {
            var english = Read("en");
            var translated = Read(language);
            None(english.Keys.Where(k => !translated.ContainsKey(k)));
            None(EditorStrings.Where(k => !translated.ContainsKey(k)));
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void ATranslationHasNoStringEnglishLacks(string language)
        {
            var english = Read("en");
            None(Read(language).Keys.Where(k => !english.ContainsKey(k) && !EditorStrings.Contains(k)));
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void PlaceholdersAndLineBreaksAreKept(string language)
        {
            var english = Read("en");
            var translated = Read(language);
            var broken = english.Where(e => translated.TryGetValue(e.Key, out var t)
                    && (!Placeholders(e.Value).SequenceEqual(Placeholders(t)) || e.Value.Count(c => c == '\n') != t.Count(c => c == '\n')))
                .Select(e => e.Key).ToList();
            None(broken);
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void NoTranslationIsEmptyOrHoldsAReplacementCharacter(string language)
        {
            var translated = Read(language);
            None(translated.Where(t => string.IsNullOrWhiteSpace(t.Value) && t.Key != "PicturesCaret").Select(t => t.Key));
            None(translated.Where(t => t.Value.Contains('\uFFFD')).Select(t => t.Key));
        }

        /// <summary>
        /// Verifies that Polish menu access keys are single characters and unique regardless of case.
        /// </summary>
        [Fact]
        public void ThePolishAccessKeysAreDistinctSingleLetters()
        {
            var polish = Read("pl");
            var keys = new[] { "AccessKeyFile", "AccessKeyEdit", "AccessKeyParagraph", "AccessKeyFormat", "AccessKeyView" }.Select(k => polish[k]).ToList();
            Assert.All(keys, k => Assert.Equal(1, k.Length));
            Assert.Equal(keys.Count, keys.Select(k => k.ToUpperInvariant()).Distinct().Count());
        }

        /// <summary>
        /// Verifies that the shared Spanish translation avoids selected Spain-specific terms and formal "usted" forms,
        /// preserving neutral Latin American wording and informal "tú" address. Reports resource keys that violate the rule.
        /// </summary>
        [Fact]
        public void SpanishKeepsToNeutralLatinAmericanWording()
        {
            var banned = new[]
            {
                @"\b[Aa]ñad", @"\beste PC\b", @"\bPC\b.*\bPC\b", @"[Cc]omprob", @"[Cc]omprueb", @"en directo", @"\bIntro\b", @"\bordenador", @"\bratón\b",
                @"ningún sitio", @"hay sitio", @"\bcada vez\b", @"\busted\b", @"\bSu ritmo\b", @"\b(Revise|Vuelva|elija|ensayó)\b", @"\blea el\b",
            };
            None(Read("es").Where(p => banned.Any(b => Regex.IsMatch(p.Value, b))).Select(p => p.Key));
        }
    }
}
