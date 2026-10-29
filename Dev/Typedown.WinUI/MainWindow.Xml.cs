using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Services.XmlEditing;
using Typedown.WinUI.Utilities;
using Typedown.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Typedown.WinUI
{
    // New since the fork: editing an XML file. An XML document opens in the Code view with its readable view beside it (the same view as File >
    // Convert, made again a moment after the text stops changing), and a strip under the code says whether the text is well-formed and where it is
    // not. The file is saved the way it was written (encoding, byte-order mark, line ends: Services/XmlEditing/XmlTextFile.cs); a digitally signed
    // file is not saved or formatted without asking, because any change makes the signature invalid. Auto save does not touch an XML file: a data
    // file other programs read is written when the person says so (the recovery backup still protects the text).
    public sealed partial class MainWindow
    {
        private DispatcherQueueTimer xmlTimer;
        private int xmlVersion;
        // the check of the document on screen, as the editor was last told
        private XmlAnalysis xmlAnalysis;
        // the XML documents whose signature the person has agreed to invalidate (asked once for each)
        private readonly HashSet<FileViewModel> xmlSignedAgreed = new();

        // The schema (XSD) an XML document is checked against: the one the person chose for it (kept until the document is replaced), or else the one
        // its text names next to the file; `Off` once they stopped the check (the text may still name one).
        private sealed class XmlSchemaChoice
        {
            public string[] Paths;
            public bool Off;
        }

        private readonly ConditionalWeakTable<FileViewModel, XmlSchemaChoice> xmlSchemaChoices = new();

        private void InitXmlEditing()
        {
            eventCenter.GetObservable<EditorEventArgs>("XmlFormatRequest").Subscribe(_ => FormatXml());
            eventCenter.GetObservable<EditorEventArgs>("XmlSchemaChoose").Subscribe(_ => ChooseXmlSchema());
            eventCenter.GetObservable<EditorEventArgs>("XmlSchemaClear").Subscribe(_ => ClearXmlSchema());
        }

        // The text of an XML document changed: the check and the readable view are made again once typing pauses.
        private void XmlTextChanged()
        {
            if (startPageShown || file == null || !file.IsXml) return;
            xmlVersion++;   // a check that is running for the text before this change is out of date: its result is thrown away
            if (xmlTimer == null)
            {
                xmlTimer = DispatcherQueue.CreateTimer();
                xmlTimer.Interval = TimeSpan.FromMilliseconds(350);
                xmlTimer.IsRepeating = false;
                xmlTimer.Tick += (s, e) => RunXmlAnalysis();
            }
            xmlTimer.Stop();
            xmlTimer.Start();
        }

        // A document was loaded or came to the front: the strip and the readable view are made now.
        private void XmlRefreshNow()
        {
            xmlTimer?.Stop();
            RunXmlAnalysis();
        }

        private async void RunXmlAnalysis()
        {
            if (startPageShown || file == null || !file.IsXml)
            {
                xmlAnalysis = null;
                xmlVersion++;   // a check that is still running for a document that was XML is out of date now
                ClearXmlOutline();
                return;
            }
            var doc = file;
            // another XML document came to the front: the rows of the one before go now, not when the new check is done (a click would send its line to this document)
            if (xmlOutlineOwner != null && xmlOutlineOwner != doc) ClearXmlOutline();
            var text = doc.Markdown;
            var name = doc.DisplayName;
            var language = Locale.CurrentLang;
            var version = ++xmlVersion;
            xmlSchemaChoices.TryGetValue(doc, out var choice);
            var chosen = choice?.Paths;
            var schemaOff = choice?.Off == true;
            var path = doc.FilePath;
            XmlAnalysis result;
            XmlSchemaOutcome schema;
            try
            {
                // the check of the text, and its check against the schema (the one chosen, or the one the text names), off the UI thread
                (result, schema) = await Task.Run(() =>
                {
                    var analysis = XmlAnalysis.Of(text, language, name);
                    // (a schema describes a document: several elements side by side are not checked against one)
                    var paths = schemaOff || analysis.Fragment ? Array.Empty<string>() : chosen ?? XmlSchemaCheck.Detect(text, path);
                    return (analysis, XmlSchemaCheck.Run(text, paths, analysis.WellFormed));
                });
            }
            catch (Exception ex)
            {
                Log($"XML check failed: {ex.Message}");
                return;
            }
            // the text changed, or another document came to the front, while the check ran: a newer one is on its way
            if (version != xmlVersion || !ReferenceEquals(doc, file) || !doc.IsXml) return;
            xmlAnalysis = result;
            ApplyXmlOutline(doc, result);
            PostMessage("XmlView", new
            {
                ok = result.WellFormed,
                preview = result.Preview,
                line = result.Line,
                position = result.Position,
                signed = result.Signed,
                status = result.Fragment ? Locale.Format("XmlStatusFragment", result.Elements, result.Lines)
                    : result.WellFormed ? Locale.Format("XmlStatusOk", result.Lines) : Locale.Format("XmlStatusError", result.Line, result.Position, result.Error),
                note = result.TooBigForPreview ? Locale.GetString("XmlTooBigNote") : result.Fragment ? Locale.GetString("XmlFragmentNote") : null,
                noteWarn = result.Fragment && !string.IsNullOrEmpty(path),
                signedNote = result.Signed ? Locale.GetString("XmlSignedNote") : null,
                formatButton = Locale.GetString("XmlFormatButton"),
                jumpTip = Locale.GetString("XmlJumpTip"),
                schema = SchemaPayload(schema, language, chosen != null),
                // the ticks of the document map; text that is not well-formed keeps the outline of the last good text (the editor keeps what it had)
                outline = result.WellFormed ? result.Outline.Select(e => new { level = e.Level, label = OutlineLabel(e), line = e.Line }).ToList() : null,
            });
        }

        // The outline of the XML document on screen fills the Outline list of the sidebar and of the inspector (a row goes to the line of its element) and the
        // ticks of the document map. While the person types a text that is not well-formed the last outline stays; another document never shows it.
        private FileViewModel xmlOutlineOwner;

        private static string OutlineLabel(XmlOutlineEntry e) =>
            e.Limit ? Locale.Format("XmlOutlineLimit", XmlOutline.MaxEntries)
            : e.More > 0 ? Locale.Format("XmlOutlineMore", e.More, e.Name)
            : e.Hint != null ? e.Name + " · " + e.Hint : e.Name;

        private void ApplyXmlOutline(FileViewModel doc, XmlAnalysis result)
        {
            if (!result.WellFormed && xmlOutlineOwner == doc) return;
            xmlOutlineOwner = doc;
            SyncToc(result.Outline.Select(e => new TocEntry { Content = OutlineLabel(e), Lvl = e.Level, Line = e.Line }).ToList());
            UpdateOutlineHeader();
            UpdateInspectorOutlineEmpty();
        }

        // Another kind of document came to the front: the rows of the XML outline go (a note brings its own headings).
        private void ClearXmlOutline()
        {
            if (xmlOutlineOwner == null) return;
            xmlOutlineOwner = null;
            tocEntries.Clear();
            UpdateOutlineHeader();
            UpdateInspectorOutlineEmpty();
        }

        // What the editor shows of the schema check: the state, the words of the strip, and each problem in plain words (and as the parser said it).
        private static object SchemaPayload(XmlSchemaOutcome outcome, string language, bool chosen)
        {
            var button = Locale.GetString("XmlSchemaButton");
            var pickTip = Locale.GetString("XmlSchemaPickTip");
            if (outcome == null || outcome.State == XmlSchemaState.None) return new { state = "none", button, pickTip };
            var status = outcome.State switch
            {
                XmlSchemaState.Valid => Locale.GetString("XmlSchemaStatusValid"),
                XmlSchemaState.Invalid => Locale.Format("XmlSchemaStatusInvalid", outcome.Issues.Count + (outcome.Truncated ? "+" : "")),
                XmlSchemaState.Skipped => Locale.GetString("XmlSchemaStatusSkipped"),
                XmlSchemaState.TooBig => Locale.GetString("XmlSchemaStatusTooBig"),
                _ => Locale.Format("XmlSchemaStatusUnusable", outcome.Problem),
            };
            return new
            {
                state = outcome.State.ToString().ToLowerInvariant(),
                status,
                names = string.Join(", ", outcome.Schemas.Select(Path.GetFileName)),
                chosen,
                button,
                pickTip,
                clearTip = Locale.GetString("XmlSchemaClearTip"),
                issues = outcome.Issues.Select(i => new { line = i.Line, position = i.Position, text = i.Plain(language), raw = i.Raw }).ToList(),
            };
        }

        // Edit > Check against schema... (and the Schema button of the strip): an .xsd is picked and the text is checked against it; the includes
        // and imports of the schema are read from its folder.
        private async void ChooseXmlSchema()
        {
            if (startPageShown || file == null || !file.IsXml) return;
            var doc = file;
            try
            {
                var picker = new FileOpenPicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".xsd");
                var picked = await picker.PickSingleFileAsync();
                if (picked == null || !ReferenceEquals(doc, file)) return;
                var choice = xmlSchemaChoices.GetOrCreateValue(doc);
                choice.Paths = new[] { picked.Path };
                choice.Off = false;
                Log($"XML: schema {picked.Path}");
                XmlRefreshNow();
            }
            catch (Exception ex)
            {
                Log($"XML schema choice failed: {ex.Message}");
            }
        }

        // The Clear button of the strip: the text is not checked against a schema any more (not the one chosen, nor the one the text names).
        private void ClearXmlSchema()
        {
            if (startPageShown || file == null || !file.IsXml) return;
            var choice = xmlSchemaChoices.GetOrCreateValue(file);
            choice.Paths = null;
            choice.Off = true;
            XmlRefreshNow();
        }

        private void XmlSchemaMenuItem_Click(object sender, RoutedEventArgs e) => ChooseXmlSchema();

        // Edit > Format XML (and the Format button of the strip): the white space between the elements, nothing else; one step of Undo.
        // A second request while the first is waiting (a double click) is ignored: two dialogs cannot be open at once.
        private bool xmlFormatting;

        private async void FormatXml()
        {
            if (xmlFormatting) return;
            xmlFormatting = true;
            try
            {
                await FormatXmlCore();
            }
            catch (Exception ex)
            {
                Log($"XML format failed: {ex.Message}");
            }
            finally
            {
                xmlFormatting = false;
            }
        }

        private async Task FormatXmlCore()
        {
            if (startPageShown || file == null || !file.IsXml) return;
            await FlushEditor();
            var text = file.Markdown;
            string formatted;
            try
            {
                formatted = XmlFormatter.Format(text);
            }
            catch (XmlFormatException ex)
            {
                await ShowXmlDialog(Locale.GetString("XmlFormatTitle"), ex.Problem switch
                {
                    XmlFormatProblem.Empty => Locale.GetString("XmlFormatEmpty"),
                    XmlFormatProblem.Doctype => Locale.GetString("XmlFormatDoctype"),
                    XmlFormatProblem.NoRoot => Locale.GetString("XmlFormatNoRoot"),
                    XmlFormatProblem.TooDeep => Locale.GetString("XmlFormatTooDeep"),
                    _ => Locale.Format("XmlFormatNotWellFormed", ex.Line, ex.Position),
                });
                return;
            }
            // "\n" at the end of the last line is the one the formatter adds; a text that has none is not changed by it
            if (formatted == text || formatted == text + "\n") return;
            var signed = XmlAnalysis.LooksSigned(text);
            if (signed && !xmlSignedAgreed.Contains(file) && !await ConfirmSignedChange(Locale.GetString("XmlSignedFormatBody"), Locale.GetString("XmlSignedFormatButton"))) return;
            if (signed) xmlSignedAgreed.Add(file);   // asked once for each document: the save that follows does not ask again
            PostMessage("ReplaceAllText", new { text = formatted });
            Log("XML: formatted");
        }

        // Before a save of a signed XML document that has been changed: asked once for each document.
        private async Task<bool> ConfirmSavingSignedXml()
        {
            if (startPageShown || file == null || !file.IsXml || !file.IsDirty) return true;
            if (xmlSignedAgreed.Contains(file) || !XmlAnalysis.LooksSigned(file.Markdown)) return true;
            if (!await ConfirmSignedChange(Locale.GetString("XmlSignedSaveBody"), Locale.GetString("XmlSignedSaveButton"))) return false;
            xmlSignedAgreed.Add(file);
            return true;
        }

        private async Task<bool> ConfirmSignedChange(string body, string button)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = Locale.GetString("XmlSignedTitle"),
                Content = body,
                PrimaryButtonText = button,
                CloseButtonText = Locale.GetString("Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ShowXmlDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = Locale.GetString("OK"),
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }

        // The save did not write the file because its encoding cannot hold what was typed (or is not known): nothing was changed on disk.
        private Task ShowXmlSaveProblem(XmlEncodingException problem) =>
            ShowXmlDialog(Locale.GetString("XmlSaveProblemTitle"), problem.UnknownEncoding != null
                ? Locale.Format("XmlEncodingUnknown", problem.UnknownEncoding)
                : Locale.Format("XmlEncodingCannot", problem.Character, problem.Line, problem.Position, problem.EncodingName));

        private void FormatXmlMenuItem_Click(object sender, RoutedEventArgs e) => FormatXml();

        // Edit > Paste as XML (and the right-click menu): the text on the clipboard goes into the note on screen as a fenced xml block, and into a new XML
        // document of its own. An empty note does not get the block: it becomes the XML document. With tabs off a note keeps the window (no document
        // is replaced) and only gets the block; an XML document, Home or no document on screen can only open the XML, which asks about unsaved changes
        // as any open does. `inCode`: whether the source pane has the focus (not known from the menu: the page is asked).
        private async void PasteAsXmlMenuItem_Click(object sender, RoutedEventArgs e) => await PasteAsXml();

        private async Task PasteAsXml(bool? inCode = null)
        {
            try
            {
                var view = await GetClipboardContent();
                var raw = Offers(view, Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) ? await view.GetTextAsync() : null;
                if (!XmlPaste.LooksLikeXml(raw))
                {
                    await ShowXmlDialog(Locale.GetString("PasteAsXmlMenuItem"), Locale.GetString("PasteAsXmlNotXml"));
                    return;
                }
                var xml = XmlPaste.Normalize(raw);
                var note = !startPageShown && activeDoc != null && !file.IsXml;
                var blank = note && activeDoc.IsBlank;
                if (note && !blank)
                {
                    var fenced = XmlPaste.Fenced(xml);
                    var code = inCode ?? await RunInPage("!!(document.activeElement&&document.activeElement.closest&&document.activeElement.closest('.CodeMirror'))") == "true";
                    // (in the source pane the fence must start a line: after text on the line it would be `text```xml`, not a block)
                    if (code) await RunInPage($"(function(){{var c={CodePane};if(c)c.replaceSelection((c.getCursor('from').ch>0?'\\n':'')+{Newtonsoft.Json.JsonConvert.SerializeObject(fenced)});}})()");
                    else
                    {
                        EditorView.Focus(FocusState.Programmatic);
                        PostMessage("Paste", new { type = "normal", text = fenced, html = (string)null });
                    }
                    // the editor reports its change a moment later; the new document must not come to the front before the block is in this one
                    await Task.Delay(250);
                }
                if (!note || blank || TabsEnabled)
                {
                    if (!await MakeRoomForDocument()) return;
                    file.NewFile(xml + "\n", xml: true);
                    UpdateTitle();
                }
                Log("XML: pasted from the clipboard");
            }
            catch (Exception ex)
            {
                Log($"XML paste failed: {ex.Message}");
            }
        }

        // Edit > Format XML is for an XML document; the Paragraph and Format menus are for a note in the View mode (an XML document is code).
        private void UpdateXmlMenus()
        {
            var xml = activeDoc != null && !startPageShown && activeDoc.File.IsXml;
            FormatXmlMenuItem.IsEnabled = xml;
            XmlSchemaMenuItem.IsEnabled = xml;
            MaskPersonalDataMenuItem.IsEnabled = activeDoc != null && !startPageShown;
            var view = CurrentViewMode == "view" && !xml;
            ParagraphMenu.IsEnabled = view;
            FormatMenu.IsEnabled = view;
            UpdateToolbarVisibility();
        }
    }
}
