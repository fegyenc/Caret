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

            // Where the tracking is kept in the data folder (ReviewStore) under the path of the document; null while it is untitled.
            public string Path { get; set; }

            // The first-seen day of each change (ReviewDates), what the file on disk held when Caret last saved it, and whether it was
            // edited elsewhere since tracking last ran (said in the panel).
            public IReadOnlyList<ReviewDates.Seen> Seen { get; set; } = new List<ReviewDates.Seen>();

            public string SavedHash { get; set; }

            public bool OutsideEdit { get; set; }

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

        // What tracking keeps in the data folder (ReviewStore): read when a document with a saved review opens, written when the
        // baseline, the days or the file change, removed when tracking stops. One write at a time.
        private static readonly ReviewStore reviewStore = new(System.IO.Path.Combine(Config.GetLocalFolderPath(), "Review"));
        private static readonly object reviewStoreLock = new();
        private static bool reviewStoreCleaned;

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
            // The changes are drawn in place in the Visual view and in the preview pane of the Split view; the source view has neither.
            if (CurrentViewMode != "split" && CurrentViewMode != "view") SetViewMode("split");
            await RefreshTrack(doc);
            PersistTrack(doc);
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
            // what was kept for it goes too (the path it was kept under, or where the document is now)
            ForgetStored(track.Path);
            ForgetStored(doc.File.FilePath);
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
            var seen = track.Seen;
            try { result = await Task.Run(() => LiveReview.Compare(baseline, current, track.Author, when, codeNote, seen)); }
            catch (Exception ex)
            {
                Log($"LiveReview: compare failed: {ex.Message}");
                return;
            }
            if (doc.Track != track || track.Version != version) return;
            track.Last = result;
            // the days of the changes are kept, so that they are still the days they were first seen after Caret was closed
            var daysChanged = !track.Seen.SequenceEqual(result.Seen);
            track.Seen = result.Seen;
            if (daysChanged) PersistTrack(doc);
            if (track.Cursor >= result.List.Count) track.Cursor = result.List.Count - 1;
            if (doc == activeDoc) ShowTrackOf(doc);
        }

        // Shows the state of a document: the panel, the status bar and the preview pane (nothing, when it is not tracked).
        private void ShowTrackOf(DocumentTab doc)
        {
            if (doc != activeDoc) return;
            var track = doc.Track;
            PostMessage("TrackedView", new
            {
                text = track?.Last?.Review,
                stamp = track?.Last?.Own,
                shown = track?.Last?.Stamp,
                days = track?.Last?.Days,
                by = track == null ? null : "{>>@" + ReviewMarks.Author(track.Author) + " ",
                // what the Visual view needs to draw each change in place and to offer accept and reject on it
                changes = TrackedChangesForPage(track?.Last),
                author = track?.Author,
                accept = Locale.GetString("ReviewAcceptChange"),
                reject = Locale.GetString("ReviewRejectChange"),
            });
            UpdateTrackUi();
        }

        // The changes as the page needs them to find them in the rendered document: kind, old and new text, a little of the text around each, and its day.
        private static object TrackedChangesForPage(LiveReview.Result result)
        {
            if (result == null || result.Seen.Count != result.List.Count) return null;
            return result.List.Select((c, i) => new
            {
                index = c.Index,
                kind = c.Kind.ToString().ToLowerInvariant(),
                old = result.Seen[i].Old,
                @new = result.Seen[i].New,
                before = result.Seen[i].Before,
                after = result.Seen[i].After,
                day = result.Days[i],
            }).ToList();
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
            TrackOutsideText.Visibility = track.OutsideEdit ? Visibility.Visible : Visibility.Collapsed;
            TrackNotExactText.Text = Locale.GetString(unmarked > 0 ? "ReviewTrackUnmarked" : "ReviewTrackNotExact");
            TrackNotExactText.Visibility = unmarked > 0 || (!exact && count > 0) ? Visibility.Visible : Visibility.Collapsed;
            WriteReviewMenuItem.IsEnabled = WriteReviewButton.IsEnabled = count > 0 || unmarked > 0;
            TrackUndoAcceptButton.Visibility = track.Previous.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TrackPreviousButton.IsEnabled = TrackNextButton.IsEnabled = (result?.List.Count ?? 0) > 0;
        }

        // --- Writing the changes into the document ---

        private async void WriteReview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WriteTrackedReview();
            }
            catch (Exception ex)
            {
                Log($"LiveReview: write failed: {ex}");
            }
            UpdateTrackUi();
        }

        // The changes become the marks of a review in the text of the document (what Compare with another file writes: {++added++},
        // {--deleted--}, {~~old~>new~~}, each with the name and the first-seen day), as one step of Undo, and tracking stops: from then
        // on they are marks like any others, and the review commands accept and reject them. Nothing is written to disk by this; saving
        // is the user's, as for any edit.
        private async Task WriteTrackedReview()
        {
            var doc = activeDoc;
            var track = doc?.Track;
            if (track == null) return;
            await FlushEditor();
            await RefreshTrack(doc);
            var result = track.Last;
            if (result == null || doc.Track != track) return;
            if (result.Changes == 0 && result.Unmarked == 0)
            {
                await ShowReviewMessage(Locale.GetString("ReviewNoChanges"));
                return;
            }
            var ask = Locale.GetString("ReviewTrackWriteAsk");
            // differences that could not be marked stay in the text as plain text; the user is told
            if (result.Unmarked > 0) ask += "\n\n" + Locale.GetString("ReviewTrackUnmarked");
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = Locale.GetString("ReviewTrackWrite"),
                Content = new TextBlock { Text = ask, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Locale.GetString("OK"),
                CloseButtonText = Locale.GetString("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (doc.Track != track || activeDoc != doc) return;
            // the text of the comparison is the text on screen (it was flushed and compared just now)
            await FlushEditor();
            if (!string.Equals((file.Markdown ?? "").Replace("\r\n", "\n", StringComparison.Ordinal), result.Current, StringComparison.Ordinal))
            {
                await RefreshTrack(doc);
                result = track.Last;
                if (result == null || doc.Track != track) return;
            }
            var text = LineEndsLike(result.Marked, file.Markdown ?? "");
            // the new text goes in like Undo's does (SetMarkdown) and is one step in the undo history
            file.ReplaceBuffer(text);
            history.ContentChange(text);
            PostMessage("SetMarkdown", new { text, cursor = activeDoc.Cursor, basePath = file.ImageBasePath });
            doc.Track = null;
            trackPending.Remove(doc);
            ForgetStored(track.Path);
            ForgetStored(doc.File.FilePath);
            Log($"LiveReview: the changes were written into the document ({result.Changes} change(s))");
            ShowTrackOf(doc);
        }

        // --- Keeping the tracking between sessions (ReviewStore) ---

        // Saves what tracking needs to go on after Caret was closed. A document with no path yet (untitled) keeps it with the tab until
        // it is saved (TrackFileState). Written off the UI thread.
        private void PersistTrack(DocumentTab doc)
        {
            var track = doc?.Track;
            var path = doc?.File.FilePath;
            if (track == null || string.IsNullOrEmpty(path)) return;
            track.Path = path;
            var state = new ReviewStore.Saved(path, track.Baseline ?? "", track.Author, ReviewMarks.Day(track.Started), track.SavedHash, track.Previous.ToList(), track.Seen.ToList());
            _ = Task.Run(() =>
            {
                try
                {
                    lock (reviewStoreLock) reviewStore.Save(state);
                }
                catch (Exception ex)
                {
                    Log($"LiveReview: could not keep the review of {path}: {ex.Message}");
                }
            });
        }

        private void ForgetStored(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _ = Task.Run(() =>
            {
                try
                {
                    lock (reviewStoreLock) reviewStore.Remove(path);
                }
                catch (Exception ex)
                {
                    Log($"LiveReview: could not remove the review of {path}: {ex.Message}");
                }
            });
        }

        // Once per run: what was not opened for 90 days goes.
        private void CleanStoredReviews()
        {
            if (reviewStoreCleaned) return;
            reviewStoreCleaned = true;
            _ = Task.Run(() =>
            {
                try
                {
                    int removed;
                    lock (reviewStoreLock) removed = reviewStore.Cleanup(DateTime.Now);
                    if (removed > 0) Log($"LiveReview: {removed} old saved review(s) removed");
                }
                catch (Exception ex)
                {
                    Log($"LiveReview: clean-up failed: {ex.Message}");
                }
            });
        }

        // The tab has opened a document: when a review was kept for it, tracking goes on from there. If the file was edited elsewhere
        // since Caret last saved it, the panel says so (the differences include that edit).
        private void ResumeTracking(DocumentTab doc)
        {
            var path = doc.File.FilePath;
            if (string.IsNullOrEmpty(path)) return;
            ReviewStore.Saved saved;
            lock (reviewStoreLock) saved = reviewStore.Load(path);
            if (saved == null) return;
            var started = DateTime.TryParseExact(saved.Started, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) ? day : DateTime.Now;
            var track = new TrackState
            {
                Baseline = saved.Baseline,
                Author = saved.Author,
                Started = started,
                Path = path,
                Seen = saved.Seen.ToList(),
                SavedHash = saved.SavedHash,
                OutsideEdit = !string.IsNullOrEmpty(saved.SavedHash) && ReviewStore.Hash(doc.File.Markdown) != saved.SavedHash,
            };
            track.Previous.AddRange(saved.Previous);
            doc.Track = track;
            Log($"LiveReview: tracking resumed ({track.Baseline.Length} characters, {track.Seen.Count} known change(s){(track.OutsideEdit ? ", the file was edited elsewhere" : "")})");
            _ = RefreshTrack(doc);
        }

        // The file of a tracked tab was saved, saved under another name or renamed: the review follows it, and what the file holds
        // now is what it held when Caret last saved it.
        private void TrackFileState(DocumentTab doc)
        {
            var track = doc.Track;
            if (track == null) return;
            var path = doc.File.FilePath;
            var changed = false;
            if (!string.IsNullOrEmpty(path) && !string.Equals(track.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                ForgetStored(track.Path);
                track.Path = path;
                changed = true;
            }
            if (!doc.File.IsDirty)
            {
                var hash = ReviewStore.Hash(doc.File.Markdown);
                if (hash != track.SavedHash)
                {
                    track.SavedHash = hash;
                    changed = true;
                }
            }
            if (changed) PersistTrack(doc);
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
            // In the Visual view the change is shown in place; when it is not drawn there, the Split view shows it.
            if (CurrentViewMode == "view")
            {
                track.Cursor = index;
                if (await RunInPage($"!!(window.__caretTrackVisual&&window.__caretTrackVisual.jump({index}))") == "true") return;
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
            PersistTrack(doc);
            UpdateTrackUi();
        }

        // Accept (the baseline takes the change) or reject (the document gets the old text back) change `index`, or all of them
        // when `index` is -1. The comparison is made again first, so that it is the text on screen that is dealt with.
        // Accept or reject on a card in the Visual view: the page says which change it showed (its number and its text), and by the time
        // this runs the document may have changed, so the change is looked for again by its text.
        private async void TrackActionFromPage(Newtonsoft.Json.Linq.JToken args)
        {
            try
            {
                var index = (int?)args?["index"] ?? -1;
                if (index < 0) return;
                await ApplyTracked((bool?)args?["accept"] ?? false, index, args?["old"]?.ToString() ?? "", args?["new"]?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                Log($"LiveReview: action from the page failed: {ex}");
            }
        }

        private async Task ApplyTracked(bool accept, int index, string expectedOld = null, string expectedNew = null)
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
                // from a card: the same change as the one that was shown, or the one with the same text
                if (index >= 0 && expectedOld != null)
                {
                    var same = Enumerable.Range(0, result.Seen.Count).Where(i => result.Seen[i].Old == expectedOld && result.Seen[i].New == expectedNew).ToList();
                    if (same.Count == 0) return;
                    index = same.Contains(index) ? index : same[0];
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
                PersistTrack(doc);
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