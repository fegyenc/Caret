using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Typedown.WinUI.Services.Export;
using Xunit;

namespace Caret.ConverterTests
{
    // export-samples/showcase.md holds every construct of the Word export. It must export without a skipped picture and pass the
    // validator. CARET_WORD_EXPORT_OUT=<folder> keeps the .docx there, to be opened in the real Word and looked at.
    public class WordExportShowcaseTests
    {
        [Fact]
        public void The_showcase_exports_to_a_valid_file()
        {
            var repository = Path.GetFullPath(Path.Combine(TestPaths.ProjectFolder, "..", ".."));
            var markdown = File.ReadAllText(Path.Combine(TestPaths.ProjectFolder, "export-samples", "showcase.md"));
            var kept = Environment.GetEnvironmentVariable("CARET_WORD_EXPORT_OUT");
            var folder = string.IsNullOrEmpty(kept) ? TestPaths.NewTempFolder() : kept;
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "showcase.docx");
                var result = WordExporter.ExportToFile(markdown, path, new WordExportOptions { BaseFolder = repository, Language = "en-US" });
                Assert.Equal(2, result.Pictures);
                Assert.Empty(result.SkippedPictures);
                using var doc = WordprocessingDocument.Open(path, false);
                var errors = new OpenXmlValidator().Validate(doc).Select(e => e.Description).ToList();
                Assert.True(errors.Count == 0, string.Join("\n", errors));
            }
            finally
            {
                if (string.IsNullOrEmpty(kept)) Directory.Delete(folder, true);
            }
        }
    }
}
