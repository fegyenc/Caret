using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services
{
    // New since the fork: live review (docs/live-review-design.md). Tracking remembers the document as it was when it was
    // switched on (the baseline); as the user edits, the baseline and the document are compared again (ReviewDiff) and what
    // differs is shown: as a list, and as the text of a review that the editor's preview draws in colour. Nothing is written
    // into the document by this: the review text exists only to be shown, until the user chooses to write it down.
    //
    // Plain .NET (no WinUI), so the tests in Caret.ConverterTests compile it as it is.
    internal static class LiveReview
    {
        // Added, deleted or replaced text; `Text` is what the list shows (for a replacement, "old -> new").
        internal sealed record Change(ChangeKind Kind, string Text);

        internal enum ChangeKind { Added, Deleted, Replaced }

        // `Marked`: the baseline compared with the document, as a review in CriticMarkup. `Changes`: the number of marks written.
        // `Unmarked`: differences that could not be marked (ReviewDiff). `List`: the changes in the order of the text.
        internal sealed record Result(string Marked, int Changes, int Unmarked, IReadOnlyList<Change> List);

        // The most characters of a change shown in the list.
        private const int ListText = 80;

        public static Result Compare(string baseline, string current, string author, DateTime when, string codeNote)
        {
            var marked = ReviewDiff.Mark(baseline, current, author, when, codeNote);
            return new Result(marked.Text, marked.Changes, marked.Unmarked, List(marked.Text, ReviewMarks.Stamp(author, when)));
        }

        // The marks that are followed by this review's own stamp ({>>@Name day<<}); marks that were in the text before are not
        // changes of this review.
        internal static IReadOnlyList<Change> List(string marked, string stamp)
        {
            var found = new List<Change>();
            if (string.IsNullOrEmpty(marked)) return found;
            var after = Regex.Escape(stamp);
            var pattern = new Regex(
                @"\{\+\+(?<add>[\s\S]*?)\+\+\}(?=" + after + @")" +
                @"|\{--(?<del>[\s\S]*?)--\}(?=" + after + @")" +
                @"|\{~~(?<old>[\s\S]*?)~>(?<new>[\s\S]*?)~~\}(?=" + after + @")",
                RegexOptions.CultureInvariant);
            foreach (Match m in pattern.Matches(marked))
            {
                if (m.Groups["add"].Success) found.Add(new Change(ChangeKind.Added, Short(m.Groups["add"].Value)));
                else if (m.Groups["del"].Success) found.Add(new Change(ChangeKind.Deleted, Short(m.Groups["del"].Value)));
                else found.Add(new Change(ChangeKind.Replaced, Short(m.Groups["old"].Value) + " → " + Short(m.Groups["new"].Value)));
            }
            return found;
        }

        private static string Short(string text)
        {
            var one = Regex.Replace(text, @"\s+", " ").Trim();
            return one.Length <= ListText ? one : one.Substring(0, ListText - 1) + "…";
        }
    }
}
