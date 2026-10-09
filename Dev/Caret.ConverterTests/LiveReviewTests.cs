using System;
using System.Linq;
using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    // Live review (Services/LiveReview.cs): the baseline compared with the document, as a review text and a list of changes.
    public class LiveReviewTests
    {
        private static readonly DateTime Day = new(2026, 10, 9);

        private static LiveReview.Result Compare(string baseline, string current) => LiveReview.Compare(baseline, current, "Ann", Day, "code changed");

        private static string Unix(string text) => text.Replace("\r\n", "\n");

        [Fact]
        public void The_same_text_has_no_changes()
        {
            var result = Compare("# Title\n\nA line.\n", "# Title\n\nA line.\n");
            Assert.Equal(0, result.Changes);
            Assert.Empty(result.List);
        }

        [Fact]
        public void An_added_line_a_deleted_line_and_a_changed_word_are_listed_in_the_order_of_the_text()
        {
            var baseline = "# Report\n\nSales were low.\n\nThe old paragraph goes.\n";
            var current = "# Report\n\nSales were high.\n\nA brand new paragraph.\n";
            var result = Compare(baseline, current);
            Assert.True(result.Changes >= 2, result.Marked);
            Assert.Equal(result.Changes, result.List.Count);
            Assert.Contains(result.List, c => c.Kind == LiveReview.ChangeKind.Replaced && c.Text.Contains("low") && c.Text.Contains("high"));
            // accepting every mark gives the document, rejecting every mark gives the baseline
            Assert.Equal(current, Unix(ReviewMarks.Resolve(result.Marked, ReviewAction.Accept)));
            Assert.Equal(baseline, Unix(ReviewMarks.Resolve(result.Marked, ReviewAction.Reject)));
        }

        [Fact]
        public void A_mark_that_was_already_in_the_text_is_not_a_change_of_this_review()
        {
            var text = "A {++mark++}{>>@Bob 2026-01-01<<} that is there already.\n";
            var result = Compare(text, text + "\nNew line.\n");
            var change = Assert.Single(result.List);
            Assert.Equal(LiveReview.ChangeKind.Added, change.Kind);
        }

        [Fact]
        public void A_long_change_is_cut_for_the_list()
        {
            var result = Compare("Start.\n", "Start.\n\n" + new string('x', 300) + "\n");
            var change = Assert.Single(result.List);
            Assert.True(change.Text.Length <= 80, change.Text.Length.ToString());
            Assert.EndsWith("\u2026", change.Text);
        }
    }
}