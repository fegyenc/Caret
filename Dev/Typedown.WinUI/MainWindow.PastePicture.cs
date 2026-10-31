using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Typedown.WinUI.Services;
using Typedown.WinUI.Services.Conversion;
using Typedown.WinUI.Utilities;
using Windows.ApplicationModel.DataTransfer;

namespace Typedown.WinUI
{
    public sealed partial class MainWindow
    {
        // New since the fork: Edit > Paste text from picture (OCR). A picture on the clipboard (a screenshot made with Win+Shift+S, a picture copied from a page)
        // is read with the OCR of Windows, on this PC, and its text goes in at the caret like a paste. With no note on screen it opens as a new note.
        // `inCode`: whether the source pane has the focus (not known from the menu: the page is asked).
        private async void PastePictureTextMenuItem_Click(object sender, RoutedEventArgs e) => await PastePictureText();

        private async Task PastePictureText(bool? inCode = null)
        {
            var title = Locale.GetString("PastePictureTextMenuItem");
            try
            {
                var view = await GetClipboardContent();
                if (!Offers(view, StandardDataFormats.Bitmap))
                {
                    await ShowXmlDialog(title, Locale.GetString("PastePictureNoPicture"));
                    return;
                }
                var ocr = new WindowsOcr(settings.OcrLanguage);
                if (!ocr.IsAvailable)
                {
                    await ShowXmlDialog(title, Locale.GetString("ConvertOcrUnavailable"));
                    return;
                }
                var reference = await view.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var lines = await ocr.RecognizeStreamAsync(stream);
                if (lines == null)
                {
                    await ShowXmlDialog(title, Locale.GetString("ConvertOcrUnavailable"));
                    return;
                }
                var text = OcrText.ToMarkdown(lines);
                if (text.Length == 0)
                {
                    await ShowXmlDialog(title, Locale.GetString("PastePictureNoText"));
                    return;
                }
                if (startPageShown || activeDoc == null)
                {
                    if (!await MakeRoomForDocument()) return;
                    file.NewFile(text + "\n");
                    UpdateTitle();
                }
                else if (file.IsXml || (inCode ?? await RunInPage("!!(document.activeElement&&document.activeElement.closest&&document.activeElement.closest('.CodeMirror'))") == "true"))
                    await RunInPage($"(function(){{var c={CodePane};if(c)c.replaceSelection({JsonConvert.SerializeObject(text)});}})()");
                else
                {
                    EditorView.Focus(FocusState.Programmatic);
                    PostMessage("Paste", new { type = "normal", text, html = (string)null });
                }
                ShowToast(Locale.GetString("PastePictureDone"));
                Log("OCR: pasted the text of a picture from the clipboard");
            }
            catch (Exception ex)
            {
                Log($"OCR paste failed: {ex.Message}");
            }
        }
    }
}
