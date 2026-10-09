using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI
{
    // New since the fork: live review (docs/live-review-design.md). Review > Track changes remembers the document as it is
    // (the baseline); as the user edits, a moment after the typing stops, the baseline and the document are compared (Services/
    // LiveReview.cs) and what differs is listed in the Review panel, counted in the status bar and drawn in colour in the right-hand
    // pane of the Split view (the host sends the review text to the page as TrackedView). Nothing is written into the document by
    // tracking: the comparison exists only to be shown.
    //
    // Each change in the list can be shown (a click: the source pane selects it and the preview scrolls to it), accepted (the
    // baseline takes it, so it stops being a difference; Undo accept brings it back) or rejected (the document gets the old text
    // back, as one step of Undo). The baseline is kept for the open tab.
    public sealed partial class MainWindow
    {
        // What is kept for a tracked document.
        private sealed class TrackState
        {
            public string Baseline { get; set; }

            public string Author { get; init; }

            public DateTime Started { get; init; }

            // The last comparison, and the number of the comparison that is running: an older answer is dropped.
            public LiveReview.Result Last { get; set; }

            public int Version { get; set; }

            // The baselines before each Accept, newest last, for Undo accept.
            public List<string> Previous { get; } = new();

            // The change last shown, for Next and Previous.
            public int Cursor { get; set; } = -1;
        }

        // How many Accepts Undo accept can take back.
        private const int UndoAcceptDepth = 20;

        private DispatcherQueueTimer trackTimer;

        // The documents edited since the last comparison: when the timer fires each one gets its own, whichever tab is on screen by then.
        private readonly HashSet<DocumentTab> trackPending = new();

        private async void TrackChangesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // The toggle in the menu flips itself; the real state is the document's.
            try
            {
                if (activeDoc?.Track != null) await StopTracking();
                else await StartTracking();
            }
            catch (Exception ex)
            {
                Log($"LiveReview: toggle failed: {ex}");
            }
            UpdateTrackUi();
        }

        private async Task StartTracking()
        {
            if (await ReviewNeedsADocument()) return;
            var name = new TextBox { Header = Locale.GetString("ReviewTrackAuthor"), Text = Environment.UserName };
            var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
            panel.Children.Add(new TextBlock { Text = Locale.GetString("ReviewTrackExplain"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(name);
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = Locale.GetString("ReviewTrackToggle"),
                Content = panel,
                PrimaryButtonText = Locale.GetString("OK"),
                CloseButtonText = Locale.GetString("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await FlushEditor();
            var doc = activeDoc;
            doc.Track = new TrackState { Baseline = file.Markdown ?? "", Author = string.IsNullOrWhiteSpace(name.Text) ? Environment.UserName : name.Text.Trim(), Started = DateTime.Now };
            Log($"LiveReview: tracking started ({doc.Track.Baseline.Length} characters)");
            // The changes are drawn in the preview pane of the Split view.
            if (CurrentViewMode != "split") SetViewMode("split");
            await RefreshTrack(doc);
        }

        private async Task StopTracking()
        {
            var doc = activeDoc;
            var track = doc?.Track;
            if (track == null) return;
            // Asked against the text as it is now: the last comparison may be a moment behind the typing.
            await FlushEditor();
            if (doc.Track != track) return;
            if (TrackDiffers(track.Baseline, doc.File.Markdown))
            {
                var ask = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = Locale.GetString("ReviewTrackStop"),
                    Content = new TextBlock { Text = Locale.GetString("ReviewTrackStopAsk"), TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = Locale.GetString("ReviewTrackStop"),
                    CloseButtonText = Locale.GetString("Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
            }
            if (doc.Track != track) return;
            doc.Track = null;
            Log("LiveReview: tracking stopped");
            ShowTrackOf(doc);
        }

        // The text changed: compare again when the typing has stopped for a moment.
        private void TrackTextChanged()
        {
            var edited = activeDoc;
            if (edited?.Track == null) return;
            trackPending.Add(edited);
            trackTimer ??= DispatcherQueue.CreateTimer();
            trackTimer.Stop();
            trackTimer.Interval = TimeSpan.FromMilliseconds(400);
            trackTimer.IsRepeating = false;
            trackTimer.Tick -= TrackTimer_Tick;
            trackTimer.Tick += TrackTimer_Tick;
            trackTimer.Start();
        }

        private async void TrackTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var docs = trackPending.ToList();
            trackPending.Clear();
            foreach (var doc in docs)
            {
                if (doc.Track != null) await RefreshTrack(doc);
            }
        }

        // Whether the document differs from the baseline in more than line endings and the newlines at the end.
        private static bool TrackDiffers(string baseline, string current) =>
            !string.Equals(Plain(baseline), Plain(current), StringComparison.Ordinal);

        private static string Plain(string text) => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

        // Compares the baseline with the document, off the UI thread; a newer text or Stop drops an older answer.
        private async Task RefreshTrack(DocumentTab doc)
        {
            var track = doc.Track;
            if (track == null) return;
            var version = ++track.Version;
            var baseline = track.Baseline;
            var current = doc.File.Markdown ?? "";
            var codeNote = Locale.GetString("ReviewCodeChanged");
            var when = DateTime.Now;
            LiveReview.Result result;
            try { result = await Task.Run(() => LiveReview.Compare(baseline, current, track.Author, when, codeNote)); }
            catch (Exception ex)
            {
                Log($"LiveReview: compare failed: {ex.Message}");
                return;
            }
            if (doc.Track != track || track.Version != version) return;
            track.Last = result;
            if (track.Cursor >= result.List.Count) track.Cursor = result.List.Count - 1;
            if (doc == activeDoc) ShowTrackOf(doc);
        }

        // Shows the state of a document: the panel, the status bar and the preview pane (nothing, when it is not tracked).
        private void ShowTrackOf(DocumentTab doc)
        {
            if (doc != activeDoc) return;
            var track = doc.Track;
            PostMessage("TrackedView", new { text = track?.Last?.Review, stamp = track?.Last?.Own, shown = track?.Last?.Stamp });
            UpdateTrackUi();
        }

        private void UpdateTrackUi()
        {
            var track = activeDoc?.Track;
            TrackChangesMenuItem.IsChecked = track != null;
            TrackChangesButton.Content = Locale.GetString(track != null ? "ReviewTrackStop" : "ReviewTrackToggle");
            TrackChangesInfo.Visibility = track != null ? Visibility.Visible : Visibility.Collapsed;
            StatusBarTrackText.Visibility = track != null ? Visibility.Visible : Visibility.Collapsed;
            if (track == null) return;
            var result = track.Last;
            TrackSinceText.Text = Locale.Format("ReviewTrackSince", ReviewMarks.Day(track.Started));
            var count = result?.Changes ?? 0;
            var unmarked = result?.Unmarked ?? 0;
            TrackCountText.Text = count == 0 && unmarked > 0 ? "" : count == 0 ? Locale.GetString("ReviewTrackNone") : count == 1 ? Locale.GetString("ReviewTrackCountOne") : Locale.Format("ReviewTrackCount", count);
            StatusBarTrackText.Text = count == 1 ? Locale.GetString("ReviewTrackStatusOne") : Locale.Format("ReviewTrackStatus", count);
            var exact = result?.Exact ?? true;
            var accept = Locale.GetString("ReviewAcceptChange");
            var reject = Locale.GetString("ReviewRejectChange");
            TrackChangesList.ItemsSource = (result?.List ?? new List<LiveReview.Change>()).Select(c => new TrackItem
            {
                Index = c.Index,
                Glyph = c.Kind switch { LiveReview.ChangeKind.Added => "+", LiveReview.ChangeKind.Deleted => "\u2212", _ => "\u2194" },
                Text = c.Text,
                CanAct = exact,
                AcceptTip = accept,
                RejectTip = reject,
            }).ToList();
            TrackNotExactText.Text = Locale.GetString(unmarked > 0 ? "ReviewTrackUnmarked" : "ReviewTrackNotExact");
            TrackNotExactText.Visibility = unmarked > 0 || (!exact && count > 0) ? Visibility.Visible : Visibility.Collapsed;
            TrackUndoAcceptButton.Visibility = track.Previous.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TrackPreviousButton.IsEnabled = TrackNextButton.IsEnabled = (result?.List.Count ?? 0) > 0;
        }

        // --- Showing, accepting and rejecting one change, or all of them ---

        private async void TrackChangesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TrackItem item) await TrackJump(item.Index);
        }

        private async void TrackAccept_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int index }) await ApplyTracked(accept: true, index);
        }

        private async void TrackReject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int index }) await ApplyTracked(accept: false, index);
        }

        private async void TrackNext_Click(object sender, RoutedEventArgs e) => await TrackStep(+1);

        private async void TrackPrevious_Click(object sender, RoutedEventArgs e) => await TrackStep(-1);

        private async Task TrackStep(int direction)
        {
            var track = activeDoc?.Track;
            var count = track?.Last?.List.Count ?? 0;
            if (count == 0) return;
            var next = track.Cursor < 0 ? (direction > 0 ? 0 : count - 1) : (track.Cursor + direction + count) % count;
            await TrackJump(next);
        }

        // The source pane selects the change and the preview scrolls to it. In the Visual view there is no source pane yet (a later
        // step), so the Split view opens first.
        private async Task TrackJump(int index)
        {
            var track = activeDoc?.Track;
            var result = track?.Last;
            if (result == null || index < 0 || index >= result.List.Count) return;
            if (CurrentViewMode == "view")
            {
                SetViewMode("split");
                await Task.Delay(600);
            }
            track.Cursor = index;
            var change = result.List[index];
            await RunInPage($"window.__caretTrack&&window.__caretTrack.jump({index},{change.Offset},{change.Length})");
        }

        private async void TrackUndoAccept_Click(object sender, RoutedEventArgs e)
        {
            var doc = activeDoc;
            var track = doc?.Track;
            if (track == null || track.Previous.Count == 0) return;
            track.Baseline = track.Previous[track.Previous.Count - 1];
            track.Previous.RemoveAt(track.Previous.Count - 1);
            await RefreshTrack(doc);
            UpdateTrackUi();
        }

        // Accept (the baseline takes the change) or reject (the document gets the old text back) change `index`, or all of them
        // when `index` is -1. The comparison is made again first, so that it is the text on screen that is dealt with.
        private async Task ApplyTracked(bool accept, int index)
        {
            var doc = activeDoc;
            var track = doc?.Track;
            if (track == null) return;
            try
            {
                await FlushEditor();
                await RefreshTrack(doc);
                var result = track.Last;
                if (result == null || doc.Track != track) return;
                if (index == -1 && result.List.Count == 0 && result.Changes == 0)
                {
                    await ShowReviewMessage(Locale.GetString("ReviewNoChanges"));
                    return;
                }
                if (index >= 0 && (!result.Exact || index >= result.List.Count))
                {
                    await ShowReviewMessage(Locale.GetString("ReviewTrackNotExact"));
                    return;
                }
                if (accept)
                {
                    var baseline = index >= 0 ? LiveReview.AcceptOne(result, index) : result.Current;
                    track.Previous.Add(track.Baseline);
                    if (track.Previous.Count > UndoAcceptDepth) track.Previous.RemoveAt(0);
                    track.Baseline = LineEndsLike(baseline, track.Baseline);
                    Log($"LiveReview: accepted {(index >= 0 ? "change " + index : "all changes")}");
                }
                else
                {
                    var text = LineEndsLike(index >= 0 ? LiveReview.RejectOne(result, index) : result.Baseline, file.Markdown ?? "");
                    // The new text goes in like Undo's does (SetMarkdown) and is one step in the undo history.
                    file.ReplaceBuffer(text);
                    history.ContentChange(text);
                    PostMessage("SetMarkdown", new { text, cursor = activeDoc.Cursor, basePath = file.ImageBasePath });
                    Log($"LiveReview: rejected {(index >= 0 ? "change " + index : "all changes")}");
                }
                await RefreshTrack(doc);
            }
            catch (Exception ex)
            {
                Log($"LiveReview: apply failed: {ex}");
            }
            UpdateTrackUi();
        }

        // The text with the lines ended the way `like` ends them (the comparison works with "\n").
        private static string LineEndsLike(string text, string like) =>
            like.Contains("\r\n", StringComparison.Ordinal) ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) : text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}