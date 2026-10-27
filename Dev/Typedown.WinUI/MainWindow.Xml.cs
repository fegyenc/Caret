using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Models;
using Typedown.WinUI.Services;
using Typedown.WinUI.Services.XmlEditing;
using Typedown.WinUI.Utilities;
using Typedown.WinUI.ViewModels;

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

        private void InitXmlEditing()
        {
            eventCenter.GetObservable<EditorEventArgs>("XmlFormatRequest").Subscribe(_ => FormatXml());
        }

        // The text of an XML document changed: the check and the readable view are made again once typing pauses.
        private void XmlTextChanged()
        {
            if (startPageShown || file == null || !file.IsXml) return;
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
                return;
            }
            var doc = file;
            var text = doc.Markdown;
            var name = doc.DisplayName;
            var language = Locale.CurrentLang;
            var version = ++xmlVersion;
            XmlAnalysis result;
            try
            {
                result = await Task.Run(() => XmlAnalysis.Of(text, language, name));
            }
            catch (Exception ex)
            {
                Log($"XML check failed: {ex.Message}");
                return;
            }
            // the text changed, or another document came to the front, while the check ran: a newer one is on its way
            if (version != xmlVersion || !ReferenceEquals(doc, file)) return;
            xmlAnalysis = result;
            PostMessage("XmlView", new
            {
                ok = result.WellFormed,
                preview = result.Preview,
                line = result.Line,
                position = result.Position,
                signed = result.Signed,
                status = result.WellFormed ? Locale.Format("XmlStatusOk", result.Lines) : Locale.Format("XmlStatusError", result.Line, result.Position, result.Error),
                note = result.TooBigForPreview ? Locale.GetString("XmlTooBigNote") : null,
                signedNote = result.Signed ? Locale.GetString("XmlSignedNote") : null,
                formatButton = Locale.GetString("XmlFormatButton"),
            });
        }

        // Edit > Format XML (and the Format button of the strip): the white space between the elements, nothing else; one step of Undo.
        private async void FormatXml()
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
            if (XmlAnalysis.LooksSigned(text) && !await ConfirmSignedChange(Locale.GetString("XmlSignedFormatBody"), Locale.GetString("XmlSignedFormatButton"))) return;
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

        // Edit > Format XML is for an XML document; the Paragraph and Format menus are for a note in the View mode (an XML document is code).
        private void UpdateXmlMenus()
        {
            var xml = activeDoc != null && !startPageShown && activeDoc.File.IsXml;
            FormatXmlMenuItem.IsEnabled = xml;
            var view = CurrentViewMode == "view" && !xml;
            ParagraphMenu.IsEnabled = view;
            FormatMenu.IsEnabled = view;
            UpdateToolbarVisibility();
        }
    }
}
