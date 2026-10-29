using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typedown.WinUI.Services.Conversion;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Typedown.WinUI.Services
{
    // New since the fork: the OCR that Windows has built in (Windows.Media.Ocr), for scans: a PDF without a text layer (its pages are drawn by Windows' own PDF
    // renderer, Windows.Data.Pdf) and pictures of a page. It runs on this PC with the OCR language of Windows (Settings > Time & language > Language & region:
    // each language has an optional OCR component); nothing is uploaded, no key, no model of ours. The engine is made for text in a language: the languages that
    // are installed are the ones it reads, and the person can choose which for a document (the Convert page).
    internal sealed class WindowsOcr : IOcrProvider
    {
        // The pages of a scan are drawn about 280 dpi high (an A4 page is 1123 device-independent pixels high): the engine reads text of normal size best there.
        private const double PageHeightPixels = 3300;

        private readonly string languageTag;

        // `languageTag`: a BCP-47 tag ("pt-BR"), or empty for the languages of the person's Windows profile.
        public WindowsOcr(string languageTag = "") => this.languageTag = languageTag ?? "";

        // The OCR languages that are installed: what the engine can read.
        public static IReadOnlyList<(string Tag, string Name)> AvailableLanguages()
        {
            try
            {
                return OcrEngine.AvailableRecognizerLanguages.Select(l => (l.LanguageTag, l.DisplayName)).ToList();
            }
            catch
            {
                return Array.Empty<(string, string)>();
            }
        }

        public bool IsAvailable => AvailableLanguages().Count > 0;

        // The engine for the language asked for, else for the languages of the profile, else for any language that is installed.
        private OcrEngine Engine()
        {
            try
            {
                if (languageTag.Length > 0)
                {
                    var asked = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                    if (asked != null) return asked;
                }
                var profile = OcrEngine.TryCreateFromUserProfileLanguages();
                if (profile != null) return profile;
                var any = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                return any == null ? null : OcrEngine.TryCreateFromLanguage(any);
            }
            catch
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<IReadOnlyList<OcrTextLine>>> RecognizePdfAsync(string path, PdfPageRange pages, CancellationToken cancellationToken)
        {
            var engine = Engine();
            if (engine == null) return null;
            PdfDocument pdf;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                pdf = await PdfDocument.LoadFromFileAsync(file);
            }
            catch
            {
                // a PDF that Windows cannot open (damaged, or with a password)
                return null;
            }
            var result = new List<IReadOnlyList<OcrTextLine>>();
            for (uint index = 0; index < pdf.PageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!pages.Contains((int)index + 1)) continue;
                using var page = pdf.GetPage(index);
                var scale = Math.Min(PageHeightPixels / Math.Max(1, page.Size.Height), (double)OcrEngine.MaxImageDimension / Math.Max(1, Math.Max(page.Size.Width, page.Size.Height)));
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale),
                    DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),   // a page is drawn on white: transparent would read as black
                };
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, options);
                stream.Seek(0);
                result.Add(await ReadAsync(engine, await BitmapDecoder.CreateAsync(stream)));
            }
            return result;
        }

        public async Task<IReadOnlyList<OcrTextLine>> RecognizeImageAsync(string path, CancellationToken cancellationToken)
        {
            var engine = Engine();
            if (engine == null) return null;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                using var stream = await file.OpenReadAsync();
                return await ReadAsync(engine, await BitmapDecoder.CreateAsync(stream));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // a picture that Windows cannot decode
                return null;
            }
        }

        // The lines of a picture, with where each is: the top and bottom of its words, and its left edge.
        private static async Task<IReadOnlyList<OcrTextLine>> ReadAsync(OcrEngine engine, BitmapDecoder decoder)
        {
            var transform = new BitmapTransform();
            var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            var limit = OcrEngine.MaxImageDimension;
            if (longest > limit)
            {
                var factor = (double)limit / longest;
                transform.ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * factor);
                transform.ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * factor);
            }
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var recognised = await engine.RecognizeAsync(bitmap);
            return recognised.Lines.Select(line =>
            {
                var words = line.Words;
                return words.Count == 0
                    ? new OcrTextLine(line.Text, 0, 0)
                    : new OcrTextLine(line.Text, words.Min(w => w.BoundingRect.Top), words.Max(w => w.BoundingRect.Bottom), words.Min(w => w.BoundingRect.Left));
            }).ToList();
        }
    }
}
