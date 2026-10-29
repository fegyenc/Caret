using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Services.Conversion;
using Typedown.WinUI.Utilities;

namespace Typedown.WinUI
{
    // New since the fork: Edit > Mask personal data. Email addresses, phone numbers, IBANs, card numbers and ID numbers, and the names the person lists,
    // are replaced by placeholders (Services/Conversion/PersonalDataMask.cs: the masking of the emails, for any note or XML file). The dialog says what
    // it finds as the names change, and then either opens a masked copy in a new document (the safe default: the original is not touched; an XML file
    // opens as XML) or masks this note, as one step of the undo history (not for an XML file: a signed invoice would be changed in place).
    public sealed partial class MainWindow
    {
        // Above this size the count is made when the person acts, not at each letter of a name.
        private const int MaskLiveLimit = 100_000;

        private async void MaskPersonalDataMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (startPageShown || activeDoc == null) return;
                var doc = activeDoc;
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("MaskDialogTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;
                var text = file.Markdown ?? "";
                var xml = file.IsXml;
                var live = text.Length <= MaskLiveLimit;

                var names = new TextBox
                {
                    Header = Locale.GetString("MaskNamesHeader"),
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 80,
                    MaxHeight = 160,
                    Text = settings.MaskNames ?? "",
                };
                var found = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
                var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
                panel.Children.Add(new TextBlock { Text = Locale.GetString("MaskExplain"), TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(names);
                panel.Children.Add(found);
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = Locale.GetString("MaskDialogTitle"),
                    Content = panel,
                    PrimaryButtonText = Locale.GetString("MaskOpenCopy"),
                    SecondaryButtonText = Locale.GetString("MaskInPlace"),
                    CloseButtonText = Locale.GetString("Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                };
                void Update()
                {
                    if (!live)
                    {
                        found.Text = Locale.Format("MaskTooBig", text.Length / 1000);
                        dialog.IsSecondaryButtonEnabled = !xml;
                        return;
                    }
                    var r = PersonalDataMask.Mask(text, PersonalDataMask.ParseNames(names.Text));
                    found.Text = MaskSummary(r);
                    dialog.IsPrimaryButtonEnabled = r.Total > 0;
                    dialog.IsSecondaryButtonEnabled = r.Total > 0 && !xml;
                }
                names.TextChanged += (s, args) => Update();
                Update();
                var answer = await dialog.ShowAsync();
                if (answer == ContentDialogResult.None) return;

                settings.MaskNames = names.Text;
                // the note could have changed while the dialog was open: it is masked as it is now
                if (!ReferenceEquals(doc, activeDoc)) return;
                if (!await FlushEditor())
                {
                    await ShowErrorDialog(Locale.GetString("MaskDialogTitle"), Locale.GetString("WordExportEditorBusy"));
                    return;
                }
                if (!ReferenceEquals(doc, activeDoc)) return;
                text = file.Markdown ?? "";
                var result = PersonalDataMask.Mask(text, PersonalDataMask.ParseNames(names.Text));
                if (result.Total == 0)
                {
                    ShowToast(Locale.GetString("MaskNothing"), 5000);
                    return;
                }
                if (answer == ContentDialogResult.Primary)
                {
                    if (!await MakeRoomForDocument()) return;
                    file.NewFile(result.Text, xml);
                    UpdateTitle();
                    Log($"Mask: masked copy, {result.Total} values");
                    return;
                }
                file.ReplaceBuffer(result.Text);
                history.ContentChange(result.Text);
                PostMessage("SetMarkdown", new { text = result.Text, cursor = doc.Cursor, basePath = file.ImageBasePath });
                ShowToast(Locale.Format("MaskDone", result.Total), 6000);
                Log($"Mask: masked in place, {result.Total} values");
            }
            catch (Exception ex)
            {
                Log($"Mask: failed: {ex}");
                await ShowErrorDialog(Locale.GetString("MaskDialogTitle"), ex.Message);
            }
        }

        // "Found: Email addresses: 3 · Phone numbers: 1", or that there is nothing.
        private static string MaskSummary(PersonalDataMask.Result result)
        {
            if (result.Total == 0) return Locale.GetString("MaskNothing");
            var parts = new[] { ("EMAIL", "MaskKindEmail"), ("PHONE", "MaskKindPhone"), ("IBAN", "MaskKindIban"), ("CARD", "MaskKindCard"), ("ID", "MaskKindId"), ("PERSON", "MaskKindPerson") }
                .Where(k => result.Counts.TryGetValue(k.Item1, out var n) && n > 0)
                .Select(k => $"{Locale.GetString(k.Item2)}: {result.Counts[k.Item1]}");
            return Locale.Format("MaskFound", string.Join(" · ", parts));
        }
    }
}
