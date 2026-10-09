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

        // The three changes: a replaced word, a deleted paragraph and an added line.
        private const string Before = "# Report\n\nSales were low.\n\nThe old paragraph goes.\n\nThe end.\n";
        private const string After = "# Report\n\nSales were high.\n\nThe end.\n\nA new last line.\n";

        [Fact]
        public void Every_change_has_its_place_in_the_document()
        {
            var result = Compare(Before, After);
            Assert.True(result.Exact);
            var added = result.List.Single(c => c.Kind == LiveReview.ChangeKind.Added);
            Assert.Equal("A new last line.", After.Substring(added.Offset, added.Length).TrimEnd('\n'));
            var replaced = result.List.Single(c => c.Kind == LiveReview.ChangeKind.Replaced);
            Assert.Equal("high", After.Substring(replaced.Offset, replaced.Length));
        }

        [Fact]
        public void Accepting_one_change_leaves_the_others_as_changes()
        {
            var result = Compare(Before, After);
            Assert.True(result.List.Count >= 3, result.Marked);
            for (var k = 0; k < result.List.Count; k++)
            {
                var baseline = LiveReview.AcceptOne(result, k);
                var again = Compare(baseline, After);
                Assert.Equal(result.List.Count - 1, again.List.Count);
                Assert.True(again.Exact);
                // the baseline got that change and nothing else: the other changes are still the same ones
                Assert.Equal(result.List.Where(c => c.Index != k).Select(c => c.Text), again.List.Select(c => c.Text));
            }
        }

        [Fact]
        public void Rejecting_one_change_takes_it_back_in_the_document_and_leaves_the_others()
        {
            var result = Compare(Before, After);
            for (var k = 0; k < result.List.Count; k++)
            {
                var document = LiveReview.RejectOne(result, k);
                var again = Compare(Before, document);
                Assert.Equal(result.List.Count - 1, again.List.Count);
                Assert.True(again.Exact);
                Assert.Equal(result.List.Where(c => c.Index != k).Select(c => c.Text), again.List.Select(c => c.Text));
            }
        }

        [Fact]
        public void Accepting_every_change_one_by_one_ends_with_no_changes()
        {
            var result = Compare(Before, After);
            var baseline = Before;
            for (var n = result.List.Count; n > 0; n--)
            {
                var step = Compare(baseline, After);
                baseline = LiveReview.AcceptOne(step, 0);
            }
            Assert.Equal(After, baseline);
            Assert.Empty(Compare(baseline, After).List);
        }

        [Fact]
        public void Changed_code_cannot_be_marked_so_one_change_is_not_exact()
        {
            var result = Compare("Text.\n\n```\nold code\n```\n", "Text, changed.\n\n```\nnew code\n```\n");
            Assert.False(result.Exact);
        }

        [Fact]
        public void Windows_line_ends_are_understood()
        {
            var result = Compare(Before.Replace("\n", "\r\n"), After.Replace("\n", "\r\n"));
            Assert.True(result.Exact);
            Assert.Equal(3, result.List.Count);
        }
        [Fact]
        public void A_mark_that_was_there_before_is_not_swallowed_into_the_next_change()
        {
            var before = "An old {--mark--}{>>@Bob 2026-01-01<<} here.\n\nTail.\n";
            var after = "An old {--mark--}{>>@Bob 2026-01-01<<} here.\n\nTail. More.\n";
            var result = Compare(before, after);
            var change = Assert.Single(result.List);
            Assert.True(result.Exact);
            Assert.Equal(LiveReview.ChangeKind.Added, change.Kind);
            // accepting it gives the document, and the older mark is still in it
            var baseline = LiveReview.AcceptOne(result, 0);
            Assert.Equal(after, baseline);
            Assert.Contains("{--mark--}", baseline);
        }
        [Fact]
        public void Clearing_a_paragraph_and_leaving_blank_lines_is_still_exact()
        {
            var result = Compare("# T\n\nKeep.\n\nGo away.\n\nEnd.\n", "# T\n\nKeep.\n\n\n\nEnd.\n");
            Assert.True(result.Exact);
            Assert.Equal(LiveReview.ChangeKind.Deleted, Assert.Single(result.List).Kind);
        }
    }
}