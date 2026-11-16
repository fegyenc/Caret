using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.Export
{
    // New since the fork: callouts, the alerts of GitHub and the callouts of Obsidian. A quote whose first line is a marker such as [!NOTE] is a box of
    // its kind; the file stays plain Markdown. The same rule as the editor's (Typedown.Editor/src/components/Muya/lib/utils/callout.js), kept in step
    // by the tests on both sides. Plain .NET.
    internal sealed record Callout(string Kind, string Title);

    internal static class Callouts
    {
        // The five kinds of GitHub, and the names of Obsidian that mean the same.
        private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["note"] = "note", ["info"] = "note", ["todo"] = "note", ["abstract"] = "note", ["summary"] = "note", ["tldr"] = "note",
            ["example"] = "note", ["quote"] = "note", ["cite"] = "note",
            ["tip"] = "tip", ["hint"] = "tip", ["success"] = "tip", ["check"] = "tip", ["done"] = "tip", ["question"] = "tip", ["help"] = "tip",
            ["faq"] = "tip",
            ["important"] = "important",
            ["warning"] = "warning", ["attention"] = "warning",
            ["caution"] = "caution", ["danger"] = "caution", ["error"] = "caution", ["failure"] = "caution", ["fail"] = "caution",
            ["missing"] = "caution", ["bug"] = "caution",
        };

        // The title when the marker has none: the words GitHub shows, whatever the language (a title after the marker replaces it).
        public static string DefaultTitle(string kind) => kind switch
        {
            "tip" => "Tip",
            "important" => "Important",
            "warning" => "Warning",
            "caution" => "Caution",
            _ => "Note",
        };

        // The colour of the bar and the title, and the tint of the box (GitHub's), as Word wants them: RRGGBB.
        public static (string Line, string Fill) Colors(string kind) => kind switch
        {
            "tip" => ("1A7F37", "DAFBE1"),
            "important" => ("8250DF", "FBEFFF"),
            "warning" => ("9A6700", "FFF8C5"),
            "caution" => ("CF222E", "FFEBE9"),
            _ => ("0969DA", "DDF4FF"),
        };

        private static readonly Regex Marker = new(@"^[ \t]*\[!([A-Za-z]+)\]([+-]?)[ \t]*(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex MarkerPrefix = new(@"^[ \t]*\[![A-Za-z]+\][+-]?[ \t]*", RegexOptions.CultureInvariant);

        // How many characters of a first line are the marker (with the fold sign and the spaces after it); 0 when it opens with none.
        public static int MarkerLength(string line) => line == null ? 0 : MarkerPrefix.Match(line).Length;

        // The callout that a first line opens, or null when the line is not a marker of a known kind. `[!note]-` and `[!note]+` (Obsidian's
        // folding) are callouts too; the fold itself is not drawn.
        public static Callout Parse(string line)
        {
            if (line == null) return null;
            var match = Marker.Match(line.Split('\n')[0].TrimEnd('\r'));
            if (!match.Success || !Kinds.TryGetValue(match.Groups[1].Value, out var kind)) return null;
            return new Callout(kind, match.Groups[3].Value.Trim());
        }
    }
}
