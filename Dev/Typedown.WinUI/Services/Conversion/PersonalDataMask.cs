using System;
using System.Collections.Generic;
using System.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: Edit > Mask personal data. The masking of the emails (Email/Redactor.cs) for any note or XML file: email addresses, phone numbers,
    // IBANs, card numbers and ID numbers (each checked with its check digits where the format has them) and the names the person lists are replaced by
    // placeholders ([EMAIL-1], [PERSON-2]), the same value always by the same one. Names are not guessed from greetings, as they are in a mail: in a note
    // a word after "Hello" is as likely to be "World" as a person. Pattern matching only, offline, no AI.
    internal static class PersonalDataMask
    {
        internal sealed record Result(string Text, IReadOnlyDictionary<string, int> Counts)
        {
            public int Total => Counts.Values.Sum();
        }

        // The names of the box, one per line (a name of one letter would mask every word that holds it). The text box of WinUI ends a line with \r alone.
        public static IReadOnlyList<string> ParseNames(string lines) =>
            (lines ?? "").Split(new[] { '\r', '\n' }).Select(l => l.Trim()).Where(l => l.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public static Result Mask(string text, IEnumerable<string> names = null)
        {
            text ??= "";
            var redactor = new Redactor(null, names);
            var masked = redactor.Redact(text);
            return new Result(masked, redactor.Counts);
        }
    }
}
