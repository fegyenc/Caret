using System.IO;
using System.Linq;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // The files of a folder that the Convert page converts, with the pictures read with OCR only when the person asked (Services/Conversion/FolderFiles.cs).
    public class FolderFilesTests
    {
        private static string Folder()
        {
            var root = TestPaths.NewTempFolder();
            void Make(string relative, int bytes)
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, new byte[bytes]);
            }
            Make("report.docx", 100);
            Make("scans\\letter.PDF", 100);
            Make("~$report.docx", 100);
            Make("photos\\receipt.png", 20_000);
            Make("photos\\sub\\invoice.JPG", 20_000);
            Make("photos\\icon.png", 300);             // too small to be a page
            Make("photos\\note.txt", 100);              // not a document nor a picture
            Make("report.md", 100);
            Make("report_images\\image1.png", 50_000);   // what Caret extracted from report.docx, written as report.md
            Make("deep\\old.md", 100);
            Make("deep\\old_images\\x.jpg", 50_000);
            Make("deep\\holiday_images\\beach.jpg", 50_000);   // ends in _images but no holiday.md beside it: the person's own pictures
            Make("deep\\scan.tiff", 50_000);
            return root;
        }

        private static bool IsDocument(string path) => DocumentConverter.IsSupported(path);

        [Fact]
        public void Without_the_switch_a_folder_gives_its_documents_and_no_picture()
        {
            var root = Folder();
            try
            {
                var scan = FolderFiles.Scan(root, IsDocument, includePictures: false);
                Assert.Equal(new[] { "report.docx", "letter.PDF" }.OrderBy(x => x).ToArray(), scan.Documents.Select(Path.GetFileName).OrderBy(x => x).ToArray());
                Assert.Empty(scan.Pictures);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void With_the_switch_the_pictures_of_the_folder_and_its_subfolders_are_read_but_not_icons_or_the_pictures_extracted_from_documents()
        {
            var root = Folder();
            try
            {
                var scan = FolderFiles.Scan(root, IsDocument, includePictures: true);
                Assert.Equal(new[] { "beach.jpg", "invoice.JPG", "receipt.png", "scan.tiff" }, scan.Pictures.Select(Path.GetFileName).OrderBy(x => x).ToArray());
                // sorted by path, so the batch has a stable order
                Assert.Equal(scan.Pictures.OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase).ToList(), scan.Pictures.ToList());
                Assert.DoesNotContain(scan.Pictures, p => p.Contains("report_images") || p.Contains("old_images"));
                Assert.DoesNotContain(scan.Documents, d => Path.GetFileName(d).StartsWith("~$"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void The_old_office_formats_the_caller_reports_are_documents_too()
        {
            var root = TestPaths.NewTempFolder();
            try
            {
                File.WriteAllBytes(Path.Combine(root, "old.doc"), new byte[10]);
                var scan = FolderFiles.Scan(root, p => DocumentConverter.IsSupported(p) || Path.GetExtension(p) == ".doc", includePictures: true);
                Assert.Single(scan.Documents);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
