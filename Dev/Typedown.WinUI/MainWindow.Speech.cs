using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Typedown.WinUI.Services;
using Typedown.WinUI.Utilities;
using Windows.Foundation;

namespace Typedown.WinUI
{
    // New since the fork: speech marks (docs/speech-marks-design.md), delivery marks for a talk written in the text
    // itself: {pause 2s}, {slow}...{/slow}, {tone: dry irony}...{/tone}. The editor reads and draws them
    // (Typedown.Editor, Muya/lib/parser/speech.js) only while Speech mode is on. This file is the switch (View >
    // Speech mode, a setting like Split, off by default, never switched on by opening a file), the Speech card in the
    // sidebar (the list of marks, shown in Speech mode), and Edit > Remove all speech marks. The card only asks the
    // page for a mark (services/speechPage.ts); what is written, and where, is decided there by the rules in
    // speechEdit.js, which are tested alone.
    public sealed partial class MainWindow
    {
        private bool speechCardCollapsed;
        private TextBlock speechDefinedHeader;
        private Panel speechDefinedPanel;
        private DispatcherTimer speechStatusTimer;

        // Words of the built-in set: the card lists the others, the ones the document defines for itself.
        private static readonly HashSet<string> SpeechBuiltInWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "beat", "pause", "wait", "cue", "wpm", "budget", "slow", "fast", "loud", "soft", "emphasis", "tone", "define",
        };

        private void SpeechModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            settings.SpeechMode = SpeechModeMenuItem.IsChecked;
            UpdateSpeechCard();
            Log($"SpeechMode: {settings.SpeechMode}");
        }

        private void SpeechHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            speechCardCollapsed = !speechCardCollapsed;
            UpdateSpeechCard();
        }

        private void UpdateSpeechCard()
        {
            SpeechSection.Visibility = settings.SpeechMode ? Visibility.Visible : Visibility.Collapsed;
            SpeechBody.Visibility = speechCardCollapsed ? Visibility.Collapsed : Visibility.Visible;
            SpeechChevron.Glyph = speechCardCollapsed ? "" : "";
        }

        // One button: what it says, what it asks the page for, and the text it writes (the tooltip: the same in every
        // language, as it is in the file).
        private Button SpeechButton(string label, object spec, string written)
        {
            var button = new Button { Content = label, Padding = new Thickness(8, 3, 8, 3), MinHeight = 0, FontSize = 12 };
            ToolTipService.SetToolTip(button, written);
            AutomationProperties.SetName(button, label);
            button.Click += async (s, e) => await InsertSpeechMark(spec);
            return button;
        }

        private (TextBlock Header, Panel Buttons) SpeechGroup(string key, IEnumerable<Button> buttons)
        {
            var header = new TextBlock
            {
                Text = Locale.GetString(key),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 2),
                Opacity = 0.75,
            };
            var panel = new SpeechWrapPanel { Spacing = 4 };
            foreach (var button in buttons) panel.Children.Add(button);
            SpeechBody.Children.Add(header);
            SpeechBody.Children.Add(panel);
            return (header, panel);
        }

        // The marks that ship with Caret, and the user's own (Settings > Speech marks), in groups. The words written into the
        // file for a tone or a cue are in the language of the interface (the keywords never are, so a file can be
        // exchanged): the speaker can change them afterwards. Inside a group the order follows what the marks mean: pace
        // from the slowest to the fastest, time from the shortest to the longest, so a mark of the user's own that is
        // slower than "slow" is listed before it.
        private void BuildSpeechCard()
        {
            SpeechBody.Children.Clear();
            string T(string key) => Locale.GetString(key);
            var pause = T("SpeechLabelPause");
            var library = SpeechLibraryMarks;

            // the buttons of a group: the built-in ones with their place, and the library marks that belong to it
            IEnumerable<Button> Listed(string group, params (double Key, Button Button)[] builtIn)
            {
                var entries = builtIn.ToList();
                var number = 0;
                foreach (var mark in library.Where(m => SpeechLibrary.GroupOf(m) == group))
                {
                    // numbers only between marks of the kind the group is about; others after the built-in ones
                    var natural = (group == "pace" && mark.Kind == "pace") || (group == "time" && mark.Kind == "pause");
                    var key = natural ? (mark.Kind == "pace" ? mark.Percent : mark.Seconds) : 1000 + number++;
                    entries.Add((key, SpeechLibraryButton(mark)));
                }
                return entries.OrderBy(e => e.Key).Select(e => e.Button);
            }

            var pinned = library.Where(m => m.Pinned).Select(SpeechLibraryButton).ToList();
            // the recipes (several marks in one click) are listed under Mine too, the ones that still hold with the library
            pinned.AddRange(SpeechRecipeList.Where(r => SpeechLibrary.RecipeProblem(r.Template, library) == null).Select(r => SpeechRecipeButton(r, library)));
            if (pinned.Count > 0) SpeechGroup("SpeechGroupMine", pinned);

            SpeechGroup("SpeechGroupTime", Listed("time",
                (0.5, SpeechButton(T("SpeechLabelBeat"), new { name = "beat" }, "{beat}")),
                (1, SpeechButton(pause, new { name = "pause" }, "{pause}")),
                (3, SpeechButton(pause + " 3 s", new { name = "pause", value = "3s" }, "{pause 3s}")),
                (3.5, SpeechButton(T("SpeechLabelWait"), new { name = "wait" }, "{wait}"))));
            SpeechGroup("SpeechGroupPace", Listed("pace",
                (75, SpeechButton(T("SpeechBtnSlow"), new { name = "slow" }, "{slow}…{/slow}")),
                (125, SpeechButton(T("SpeechBtnFast"), new { name = "fast" }, "{fast}…{/fast}"))));
            SpeechGroup("SpeechGroupVolume", Listed("volume",
                (0, SpeechButton(T("SpeechBtnLoud"), new { name = "loud" }, "{loud}…{/loud}")),
                (1, SpeechButton(T("SpeechBtnSoft"), new { name = "soft" }, "{soft}…{/soft}")),
                (2, SpeechButton(T("SpeechBtnEmphasis"), new { name = "emphasis" }, "{emphasis}…{/emphasis}"))));

            var tones = new[] { "SpeechToneJoke", "SpeechToneIrony", "SpeechToneWarm", "SpeechToneSerious", "SpeechToneUrgent", "SpeechToneHumble" };
            var toneButtons = new List<(double Key, Button Button)>
            {
                // a joke and the room for the laugh after it
                (0, SpeechButton(T("SpeechBtnJoke"),
                    new { name = "tone", text = T("SpeechToneJoke"), after = new { name = "wait", value = "3s", text = T("SpeechNoteLaugh") } },
                    $"{{tone: {T("SpeechToneJoke")}}}…{{/tone}}{{wait 3s: {T("SpeechNoteLaugh")}}}")),
            };
            toneButtons.AddRange(tones.Select((key, i) => ((double)i + 1, SpeechButton(T(key), new { name = "tone", text = T(key) }, $"{{tone: {T(key)}}}…{{/tone}}"))));
            SpeechGroup("SpeechGroupTone", Listed("tone", toneButtons.ToArray()));

            var cues = new[] { "SpeechCueAudience", "SpeechCueSlide", "SpeechCueGesture", "SpeechCueObject", "SpeechCueDrink", "SpeechCueBreathe" };
            SpeechGroup("SpeechGroupCue", Listed("cue", cues.Select((key, i) => ((double)i, SpeechButton(T(key), new { name = "cue", text = T(key) }, $"{{cue: {T(key)}}}"))).ToArray()));

            // The words this document defines for itself (filled in by UpdateSpeechDefinitions).
            (speechDefinedHeader, speechDefinedPanel) = SpeechGroup("SpeechGroupDefined", Array.Empty<Button>());
            speechDefinedHeader.Visibility = speechDefinedPanel.Visibility = Visibility.Collapsed;

            SpeechBody.Children.Add(new TextBlock
            {
                Text = T("SpeechShortcutsHint"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 0.75,
            });
            UpdateSpeechCard();
        }

        // A button for a mark of the user's own. It asks for the mark together with its definition, which the page writes into
        // the document when the document does not have it yet.
        private Button SpeechLibraryButton(SpeechMark mark)
        {
            var pair = mark.Kind is "pace" or "span";
            var written = pair ? $"{{{mark.Name}}}…{{/{mark.Name}}}" : $"{{{mark.Name}}}";
            var label = mark.Icon.Length > 0 ? $"{mark.Icon} {mark.Name}" : mark.Name;
            return SpeechButton(label, new { name = mark.Name, definitions = new[] { SpeechLibrary.DefinitionLine(mark) } },
                $"{written}\n{SpeechLibrary.CleanMeaning(mark.Meaning)}");
        }

        // The page lists the words the document defines ({define very-slow pace 50%: ...}): they are marks there, so they
        // are buttons here.
        private void UpdateSpeechDefinitions(JToken args)
        {
            try
            {
                var buttons = new List<Button>();
                foreach (var def in args?["defs"] as JArray ?? new JArray())
                {
                    var name = def["name"]?.ToString();
                    if (string.IsNullOrEmpty(name) || SpeechBuiltInWords.Contains(name)) continue;
                    var pair = def["kind"]?.ToString() is "pace" or "span";
                    var written = pair ? $"{{{name}}}…{{/{name}}}" : $"{{{name}}}";
                    var meaning = def["meaning"]?.ToString();
                    buttons.Add(SpeechButton(name, new { name }, string.IsNullOrEmpty(meaning) ? written : $"{written}\n{meaning}"));
                }
                speechDefinedPanel.Children.Clear();
                foreach (var button in buttons) speechDefinedPanel.Children.Add(button);
                speechDefinedHeader.Visibility = speechDefinedPanel.Visibility = buttons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log($"Speech: the definitions of the document were not listed: {ex.Message}");
            }
        }

        // A button of the card: asks the page to write the mark where the selection or the caret is.
        private async Task InsertSpeechMark(object spec)
        {
            try
            {
                var literal = JsonConvert.SerializeObject(JsonConvert.SerializeObject(spec));
                var answer = await RunInPage($"window.__caretSpeech?window.__caretSpeech.insert({literal}):null");
                var status = string.IsNullOrEmpty(answer) || answer == "null" ? "nofocus" : JsonConvert.DeserializeObject<string>(answer);
                if (status == "ok")
                {
                    SpeechStatusText.Visibility = Visibility.Collapsed;
                    EditorView.Focus(FocusState.Programmatic);
                    return;
                }
                ShowSpeechStatus(Locale.GetString(status switch
                {
                    "nothing" => "SpeechMsgNothing",
                    "unsafe" => "SpeechMsgUnsafe",
                    "several" => "SpeechMsgSeveral",
                    "unknown" => "SpeechMsgUnknown",
                    _ => "SpeechMsgNoFocus",
                }));
            }
            catch (Exception ex)
            {
                Log($"Speech: inserting a mark failed: {ex.Message}");
            }
        }

        // A short line under the buttons (a screen reader reads it as it appears); gone after a few seconds.
        private void ShowSpeechStatus(string message)
        {
            SpeechStatusText.Text = message;
            SpeechStatusText.Visibility = Visibility.Visible;
            speechStatusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            speechStatusTimer.Tick -= HideSpeechStatus;
            speechStatusTimer.Tick += HideSpeechStatus;
            speechStatusTimer.Stop();
            speechStatusTimer.Start();
        }

        private void HideSpeechStatus(object sender, object e)
        {
            speechStatusTimer.Stop();
            SpeechStatusText.Visibility = Visibility.Collapsed;
        }

        // Edit > Remove all speech marks: the marks and the definitions go, the spoken text stays; one step in Undo.
        // The page does the work (the same rules that draw the marks), on the text of the document as the host has it.
        private async void SpeechRemoveAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await FlushEditor();
                var text = file.Markdown ?? "";
                var answer = await RunInPage($"window.__caretSpeech?window.__caretSpeech.strip({JsonConvert.SerializeObject(text)}):null");
                if (string.IsNullOrEmpty(answer) || answer == "null") return;
                var result = JsonConvert.DeserializeObject<string>(answer);
                if (result == text)
                {
                    await ShowReviewMessage(Locale.GetString("SpeechNoMarks"));
                    return;
                }
                file.ReplaceBuffer(result);
                history.ContentChange(result);
                PostMessage("SetMarkdown", new { text = result, cursor = activeDoc.Cursor, basePath = file.ImageBasePath });
                Log("Speech: all speech marks removed from the document");
            }
            catch (Exception ex)
            {
                Log($"Speech: removing the marks failed: {ex.Message}");
            }
        }
    }

    // Buttons side by side, wrapping to the next row when the sidebar is narrow (WinUI 3 has no such panel of its own).
    internal sealed class SpeechWrapPanel : Panel
    {
        public double Spacing { get; set; }

        protected override Size MeasureOverride(Size available)
        {
            double x = 0, y = 0, row = 0, widest = 0;
            foreach (var child in Children)
            {
                child.Measure(new Size(available.Width, double.PositiveInfinity));
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > available.Width) { y += row + Spacing; x = 0; row = 0; }
                x += size.Width + Spacing;
                row = Math.Max(row, size.Height);
                widest = Math.Max(widest, x - Spacing);
            }
            return new Size(Math.Min(widest, double.IsInfinity(available.Width) ? widest : available.Width), y + row);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double x = 0, y = 0, row = 0;
            foreach (var child in Children)
            {
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > final.Width) { y += row + Spacing; x = 0; row = 0; }
                child.Arrange(new Rect(x, y, Math.Min(size.Width, final.Width), size.Height));
                x += size.Width + Spacing;
                row = Math.Max(row, size.Height);
            }
            return final;
        }
    }
}
