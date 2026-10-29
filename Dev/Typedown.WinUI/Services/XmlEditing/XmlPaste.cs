using System;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.XmlEditing
{
    // New since the fork: Edit > Paste as XML. The text on the clipboard goes into the note on screen as a fenced xml block and opens in a new XML
    // document, so that it can be kept in the note and also checked, formatted and read. It does not have to be well-formed (a person pastes a broken
    // message to see where it breaks): it has to be XML in kind, which is a text that starts with `<`. Plain .NET, offline, no AI.
    internal static class XmlPaste
    {
        private static readonly Regex Backticks = new("`+", RegexOptions.CultureInvariant);

        // Starts with `<` once the white space and a byte-order mark before it are left out.
        public static bool LooksLikeXml(string text) =>
            !string.IsNullOrEmpty(text) && text.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('<');

        // The text as the editor holds it: LF line ends, no byte-order mark, no blank lines around it.
        public static string Normalize(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('﻿', ' ', '\t', '\n');

        // A fenced block with the language xml. The fence is longer than any run of backticks inside the text (a CDATA section may hold a code fence),
        // as Markdown asks.
        public static string Fenced(string xml)
        {
            var longest = 0;
            foreach (Match run in Backticks.Matches(xml)) longest = Math.Max(longest, run.Length);
            var fence = new string('`', Math.Max(3, longest + 1));
            return fence + "xml\n" + xml + "\n" + fence + "\n";
        }
    }
}
