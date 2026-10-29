using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: the pages of a PDF to read, as a person writes them: "3", "3-7", "9-" (page 9 to the end), "-4" (the first four), several of
    // them separated by commas or semicolons ("1-3, 5, 8-"), with a hyphen or a dash. Page numbers count from 1, as the viewer shows them. An empty text
    // is all the pages (IsAll). Parse gives null when the text is not written like that.
    internal sealed class PdfPageRange
    {
        private readonly List<(int From, int To)> parts = new();

        public bool IsAll => parts.Count == 0;

        public bool Contains(int page) => IsAll || parts.Any(p => page >= p.From && page <= p.To);

        // "3-7, 9" as the person wrote it, tidied (for the line that says which pages were read).
        public string Text => string.Join(", ", parts.Select(p => p.From == p.To ? p.From.ToString(CultureInfo.InvariantCulture) : p.To == int.MaxValue ? p.From + "-" : p.From + "-" + p.To));

        public static PdfPageRange Parse(string text)
        {
            var range = new PdfPageRange();
            if (string.IsNullOrWhiteSpace(text)) return range;
            foreach (var piece in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // spaces around a dash are not part of the range ("1 - 3"), spaces between digits are not allowed ("1 2" is not page 12)
                var p = Regex.Replace(piece.Trim().Replace('–', '-').Replace('—', '-'), @"\s*-\s*", "-");
                if (p.Length == 0) continue;
                if (p.Any(char.IsWhiteSpace)) return null;
                var dash = p.IndexOf('-');
                if (dash < 0)
                {
                    if (!TryPage(p, out var page)) return null;
                    range.parts.Add((page, page));
                    continue;
                }
                var left = p[..dash];
                var right = p[(dash + 1)..];
                if (left.Length == 0 && right.Length == 0) return null;
                var from = 1;
                var to = int.MaxValue;
                if (left.Length > 0 && !TryPage(left, out from)) return null;
                if (right.Length > 0 && !TryPage(right, out to)) return null;
                if (to < from) return null;
                range.parts.Add((from, to));
            }
            return range.parts.Count == 0 ? null : range;
        }

        private static bool TryPage(string text, out int page) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;
    }
}
