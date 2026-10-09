using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI
{
    // New since the fork: live review (docs/live-review-design.md), step L1. Review > Track changes remembers the document as it is
    // (the baseline); as the user edits, a moment after the typing stops, the baseline and the document are compared (Services/
    // LiveReview.cs) and what differs is listed in the Review panel, counted in the status bar and drawn in colour in the right-hand
    // pane of the Split view (the host sends the review text to the page as TrackedView). Nothing is written into the document: the
    // baseline is kept for the open tab, and the comparison exists only to be shown.
    public sealed partial class MainWindow
    {
        // What is kept for a tracked document.
        private sealed class TrackState
        {
            public string Baseline { get; init; }

            public string Author { get; init; }

            public DateTime Started { get; init; }

            // The last comparison, and the number of the comparison that is running: an older answer is dropped.
            public LiveReview.Result Last { get; set; }

            public int Version { get; set; }
        }

        private DispatcherQueueTimer trackTimer;

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
            if (track.Last?.Changes > 0)
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
            if (activeDoc?.Track == null) return;
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
            var doc = activeDoc;
            if (doc?.Track != null) await RefreshTrack(doc);
        }

        // Compares the baseline with the document, off the UI thread; a newer text or Stop drops an older answer.
        private async Task RefreshTrack(DocumentTab doc)
        {
            var track = doc.Track;
            if (track == null) return;
            var version = ++track.Version;
            var current = doc.File.Markdown ?? "";
            var codeNote = Locale.GetString("ReviewCodeChanged");
            var when = DateTime.Now;
            LiveReview.Result result;
            try { result = await Task.Run(() => LiveReview.Compare(track.Baseline, current, track.Author, when, codeNote)); }
            catch (Exception ex)
            {
                Log($"LiveReview: compare failed: {ex.Message}");
                return;
            }
            if (doc.Track != track || track.Version != version) return;
            track.Last = result;
            if (doc == activeDoc) ShowTrackOf(doc);
        }

        // Shows the state of a document: the panel, the status bar and the preview pane (nothing, when it is not tracked).
        private void ShowTrackOf(DocumentTab doc)
        {
            if (doc != activeDoc) return;
            var track = doc.Track;
            PostMessage("TrackedView", new { text = track?.Last?.Marked });
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
            TrackCountText.Text = count == 0 ? Locale.GetString("ReviewTrackNone") : count == 1 ? Locale.GetString("ReviewTrackCountOne") : Locale.Format("ReviewTrackCount", count);
            StatusBarTrackText.Text = count == 1 ? Locale.GetString("ReviewTrackStatusOne") : Locale.Format("ReviewTrackStatus", count);
            TrackChangesList.ItemsSource = (result?.List ?? new List<LiveReview.Change>()).Select(c =>
                (c.Kind switch { LiveReview.ChangeKind.Added => "+  ", LiveReview.ChangeKind.Deleted => "\u2212  ", _ => "\u2194  " }) + c.Text).ToList();
        }
    }
}