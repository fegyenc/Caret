using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Typedown.WinUI.Services
{
    // New since the fork: the user's own speech marks (docs/speech-marks-design.md, 3.7 and 5.5). A mark is a word the
    // user invents (very-slow, word-by-word, whisper) with one of four kinds that Caret can calculate or draw: `pace`
    // (a speed, and seconds added after every word), `pause` (a length), `span` (a style and a meaning) and `note`
    // (a cue). The library lives in the user's Settings; a document never depends on it: using a mark writes its
    // `{define ...}` line into the document (the page does that), so the file always says what its marks mean.
    //
    // The rules here are those of the editor (Typedown.Editor, Muya/lib/parser/speech.js `parseDefine`); the tests check
    // that the lines written here are ones the editor accepts. Plain .NET (no WinUI), so Caret.ConverterTests compiles
    // it as it is.
    internal sealed class SpeechMark
    {
        public string Name { get; set; } = "";
        // pace | pause | span | note
        public string Kind { get; set; } = "span";
        // pace: the speed as a percentage of the speaker's baseline, and the seconds added after every word
        public double Percent { get; set; } = 100;
        public double PerWord { get; set; }
        // pause: the length
        public double Seconds { get; set; } = 1;
        // one line in the user's own words: what an AI reads, and what the legend of "Copy for AI" prints
        public string Meaning { get; set; } = "";
        // where it is listed: time | pace | volume | tone | cue (empty: by kind)
        public string Group { get; set; } = "";
        // how it is drawn (not in the file): one of SpeechLibrary.Colors and one of SpeechLibrary.Icons, or empty
        public string Color { get; set; } = "";
        public string Icon { get; set; } = "";
        // listed in the Mine group
        public bool Pinned { get; set; }

        public SpeechMark Clone() => (SpeechMark)MemberwiseClone();
    }

    internal static class SpeechLibrary
    {
        public const int MaxMarks = 60;
        public const double PaceMin = 10, PaceMax = 300, PerWordMax = 2, PauseMin = 0.1, PauseMax = 600;

        public static readonly string[] Kinds = { "pace", "pause", "span", "note" };
        public static readonly string[] Groups = { "time", "pace", "volume", "tone", "cue" };
        public static readonly string[] Colors = { "red", "orange", "yellow", "green", "teal", "blue", "purple", "pink" };
        // Plain symbols, drawn by the font of the system: a star, notes, a face, a flag, a pencil, a check, sparkles...
        public static readonly string[] Icons =
        {
            "★", "♪", "♫", "☺", "⚑", "✎", "✓", "✦", "❖", "♥", "➜", "◆", "●", "▲", "✚", "☾",
        };

        // The words of the built-in set: a user's word cannot be one of them (a document can only change their numbers).
        public static readonly HashSet<string> BuiltInWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "beat", "pause", "wait", "cue", "wpm", "budget", "slow", "fast", "loud", "soft", "emphasis", "tone", "define",
        };

        private static readonly Regex NameRule = new("^[a-z][a-z0-9-]{1,23}$", RegexOptions.Compiled);

        public static string GroupOf(SpeechMark mark) =>
            Array.IndexOf(Groups, mark.Group) >= 0 ? mark.Group : mark.Kind switch { "pace" => "pace", "pause" => "time", "note" => "cue", _ => "volume" };

        // The meaning as it goes into a document: one line, and none of the characters the markup is made of.
        public static string CleanMeaning(string meaning) =>
            Regex.Replace((meaning ?? "").Replace("{", "").Replace("}", ""), @"\s+", " ").Trim();

        public static string CleanName(string name) => (name ?? "").Trim().ToLowerInvariant();

        // Why a mark cannot be kept, as a short code the page of settings turns into words, or null when it can.
        // `others` are the other marks of the library (a mark being edited is not among them).
        public static string Check(SpeechMark mark, IEnumerable<SpeechMark> others)
        {
            var name = CleanName(mark.Name);
            if (!NameRule.IsMatch(name)) return "name";
            if (BuiltInWords.Contains(name)) return "builtin";
            if (others.Any(o => string.Equals(CleanName(o.Name), name, StringComparison.Ordinal))) return "taken";
            if (CleanMeaning(mark.Meaning).Length == 0) return "meaning";
            if (Array.IndexOf(Kinds, mark.Kind) < 0) return "kind";
            if (mark.Kind == "pace")
            {
                if (!(mark.Percent >= PaceMin && mark.Percent <= PaceMax)) return "pace";
                if (!(mark.PerWord >= 0 && mark.PerWord <= PerWordMax)) return "perword";
            }
            if (mark.Kind == "pause" && !(mark.Seconds >= PauseMin && mark.Seconds <= PauseMax)) return "pause";
            return null;
        }

        // A number as the file has it: a dot, no trailing zeros, whatever the regional settings are.
        public static string Number(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

        // `{define very-slow pace 50%: about half speed}`: the line a document carries for the mark.
        public static string DefinitionLine(SpeechMark mark)
        {
            var value = mark.Kind switch
            {
                "pace" => " " + Number(mark.Percent) + "%" + (mark.PerWord > 0 ? " +" + Number(mark.PerWord) + "s" : ""),
                "pause" => " " + Number(mark.Seconds) + "s",
                _ => "",
            };
            return "{define " + CleanName(mark.Name) + " " + mark.Kind + value + ": " + CleanMeaning(mark.Meaning) + "}";
        }

        // The marks offered as a starting set; `meaning` gives the words of a mark in the language of the interface.
        public static IReadOnlyList<SpeechMark> Starters(Func<string, string> meaning) => new[]
        {
            new SpeechMark { Name = "very-slow", Kind = "pace", Percent = 50, Meaning = meaning("very-slow"), Color = "blue" },
            new SpeechMark { Name = "very-fast", Kind = "pace", Percent = 160, Meaning = meaning("very-fast"), Color = "orange" },
            new SpeechMark { Name = "word-by-word", Kind = "pace", Percent = 60, PerWord = 0.3, Meaning = meaning("word-by-word"), Color = "purple" },
            new SpeechMark { Name = "long-pause", Kind = "pause", Seconds = 5, Meaning = meaning("long-pause"), Color = "teal" },
            new SpeechMark { Name = "whisper", Kind = "span", Meaning = meaning("whisper"), Color = "pink", Icon = "☾" },
            new SpeechMark { Name = "sing-song", Kind = "span", Meaning = meaning("sing-song"), Color = "green", Icon = "♪" },
            new SpeechMark { Name = "wave", Kind = "note", Meaning = meaning("wave"), Color = "yellow", Icon = "⚑" },
        };

        public static string Serialize(IEnumerable<SpeechMark> marks) => JsonConvert.SerializeObject(marks);

        // The library as stored. Anything that cannot be kept (damaged text, a mark that breaks a rule, a name used twice,
        // more than MaxMarks) is left out, so a hand-edited or damaged Settings file never breaks the card.
        public static List<SpeechMark> Parse(string json)
        {
            var result = new List<SpeechMark>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            List<SpeechMark> stored;
            try { stored = JsonConvert.DeserializeObject<List<SpeechMark>>(json); }
            catch (JsonException) { return result; }
            foreach (var mark in stored ?? new List<SpeechMark>())
            {
                if (mark == null || result.Count >= MaxMarks) continue;
                mark.Name = CleanName(mark.Name);
                mark.Meaning = CleanMeaning(mark.Meaning);
                if (Array.IndexOf(Colors, mark.Color) < 0) mark.Color = "";
                if (Array.IndexOf(Icons, mark.Icon) < 0) mark.Icon = "";
                if (Array.IndexOf(Groups, mark.Group) < 0) mark.Group = "";
                if (Check(mark, result) == null) result.Add(mark);
            }
            return result;
        }

        // What the page of the editor needs to draw the marks the way the user chose: { name: { color, icon } }.
        public static Dictionary<string, object> Styles(IEnumerable<SpeechMark> marks) =>
            marks.Where(m => m.Color != "" || m.Icon != "").ToDictionary(m => m.Name, m => (object)new { color = m.Color, icon = m.Icon });
    }
}
