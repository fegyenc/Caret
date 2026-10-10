using System;
using System.Collections.Generic;
using System.IO;

namespace Typedown.WinUI.Services.Export
{
    // New since the fork: File > Export > Word. Markdown is read with Markdig and written as a .docx with the Open XML SDK, in the
    // process, with no Word, no network and no AI (docs/word-export-design.md). Plain .NET (no WinUI), so the tests in
    // Caret.ConverterTests compile it as it is and read the result back with the Word converter.
    public enum WordPageSize
    {
        A4,
        Letter,
    }

    public sealed class WordExportOptions
    {
        // The folder of the Markdown file: the pictures with a relative path are read from there. null: only pictures with a full path.
        public string BaseFolder { get; set; }

        // The language Word proofs the text in (a BCP 47 tag such as fr-FR).
        public string Language { get; set; } = "en-US";

        // The title in the properties of the file when the text has no heading to take it from (the name of the file).
        public string Title { get; set; }

        public WordPageSize PageSize { get; set; } = WordPageSize.A4;

        // The number of the page, centered at the foot of every page.
        public bool PageNumbers { get; set; }

        // A single line break in the Markdown is a space, as in the HTML export; true makes it a line break in Word.
        public bool SoftBreaksAsLineBreaks { get; set; }
    }

    public sealed class WordExportResult
    {
        public int Pictures { get; init; }

        // The pictures that could not be put in (a missing file, a remote address, a format Word cannot show): the text is there, the
        // picture is not. What is said is the path or address as written in the Markdown.
        public IReadOnlyList<string> SkippedPictures { get; init; } = Array.Empty<string>();
    }

    public static class WordExporter
    {
        public static WordExportResult Export(string markdown, Stream output, WordExportOptions options = null)
        {
            options ??= new WordExportOptions();
            using var buffer = new MemoryStream();
            var result = new WordBuilder(options).Build(markdown ?? "", buffer);
            buffer.Position = 0;
            buffer.CopyTo(output);
            return result;
        }

        // Written under another name beside the target and moved into place: a failure never leaves a half file where a good one was.
        public static WordExportResult ExportToFile(string markdown, string path, WordExportOptions options = null)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                WordExportResult result;
                using (var file = File.Create(temporary)) result = Export(markdown, file, options);
                File.Move(temporary, path, true);
                return result;
            }
            catch
            {
                try { File.Delete(temporary); } catch { }
                throw;
            }
        }
    }
}
