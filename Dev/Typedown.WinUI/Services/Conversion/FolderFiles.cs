using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: the files of a folder that the Convert page gives to the conversion. The documents (Word, Excel, PowerPoint, PDF, CSV, XML, emails)
    // are always taken; the pictures (png, jpg, bmp, tif), which are read with OCR (Services/WindowsOcr.cs), only when the person has switched on
    // "Read pictures in folders too": a folder of photos is not read by itself. Plain .NET, so the tests compile it as it is.
    internal sealed record FolderScan(IReadOnlyList<string> Documents, IReadOnlyList<string> Pictures);

    internal static class FolderFiles
    {
        // A picture smaller than this is an icon, a bullet or a separator, not a page.
        public const long MinPictureBytes = 4096;

        // The Convert page asks before reading this many pictures (each takes a second or two).
        public const int ConfirmPicturesFrom = 30;

        // `isDocument`: what the conversion takes as a document (the caller knows the formats it handles, also the old Office ones it only reports).
        public static FolderScan Scan(string folder, Func<string, bool> isDocument, bool includePictures)
        {
            var documents = new List<string>();
            var pictures = new List<string>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)) continue;
                if (isDocument(file))
                {
                    documents.Add(file);
                    continue;
                }
                if (!includePictures || !DocumentConverter.IsOcrPicture(file) || InExtractedImages(folder, file)) continue;
                try
                {
                    if (new FileInfo(file).Length < MinPictureBytes) continue;
                }
                catch (IOException) { continue; }
                pictures.Add(file);
            }
            documents.Sort(StringComparer.OrdinalIgnoreCase);
            pictures.Sort(StringComparer.OrdinalIgnoreCase);
            return new FolderScan(documents, pictures);
        }

        // The pictures that Caret itself extracted from a document are in a folder named <document>_images, next to <document>.md: they are not scans.
        // A folder that only ends in _images (holiday_images) with no such Markdown beside it is the person's own.
        private static bool InExtractedImages(string folder, string file)
        {
            const string suffix = "_images";
            // from the picture's folder up to the folder that was chosen, which is checked too: the person can choose report_images itself
            var rootLength = folder.TrimEnd(Path.DirectorySeparatorChar).Length;
            for (var dir = Path.GetDirectoryName(file); !string.IsNullOrEmpty(dir) && dir.Length >= rootLength; dir = Path.GetDirectoryName(dir))
            {
                var name = Path.GetFileName(dir);
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(Path.GetDirectoryName(dir), name[..^suffix.Length] + ".md"))) return true;
            }
            return false;
        }
    }
}
