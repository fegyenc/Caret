using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Export;
using Xunit;

namespace Caret.ConverterTests
{
    // File > Export > Markdown with pictures (.zip) (Services/Export/NotePackager.cs): the note and its pictures in one folder, the links relative.
    public class NotePackagerTests
    {
        private static string Folder() => TestPaths.NewTempFolder();

        private static string Picture(string folder, string relative, string content = "PNGDATA")
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void A_picture_from_another_folder_goes_to_images_and_its_link_becomes_relative()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var pasted = Picture(elsewhere, "pasted-2026.png");
                var markdown = "# Title\n\nText before ![a screenshot](" + pasted.Replace('\\', '/') + " \"the title\") text after.\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal("# Title\n\nText before ![a screenshot](images/pasted-2026.png \"the title\") text after.\n", result.Markdown);
                var packed = Assert.Single(result.Pictures);
                Assert.Equal("images/pasted-2026.png", packed.PackagePath);
                Assert.Equal(pasted, packed.SourcePath);
                Assert.Empty(result.Missing);
                Assert.Equal(1, result.Rewritten);
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void A_picture_already_under_the_note_keeps_its_place_and_its_link_text()
        {
            var note = Folder();
            try
            {
                Picture(note, "images/chart.png");
                Picture(note, "media/deep/photo one.jpg");
                var markdown = "![c](images/chart.png)\n![p](media/deep/photo%20one.jpg)\n![q](<media/deep/photo one.jpg>)\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal(markdown, result.Markdown);
                Assert.Equal(new[] { "images/chart.png", "media/deep/photo one.jpg" }, result.Pictures.Select(p => p.PackagePath).OrderBy(p => p, StringComparer.Ordinal));
                Assert.Equal(0, result.Rewritten);
            }
            finally { Directory.Delete(note, true); }
        }

        [Fact]
        public void Two_different_pictures_with_the_same_name_do_not_overwrite_each_other_and_one_picture_used_twice_is_packed_once()
        {
            var note = Folder();
            var a = Folder();
            var b = Folder();
            try
            {
                var one = Picture(a, "shot.png", "ONE");
                var two = Picture(b, "shot.png", "TWO");
                var markdown = $"![1]({one.Replace('\\', '/')})\n![2]({two.Replace('\\', '/')})\n![again]({one.Replace('\\', '/')})\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal("![1](images/shot.png)\n![2](images/shot-2.png)\n![again](images/shot.png)\n", result.Markdown);
                Assert.Equal(2, result.Pictures.Count);
            }
            finally { Directory.Delete(note, true); Directory.Delete(a, true); Directory.Delete(b, true); }
        }

        [Fact]
        public void A_name_with_spaces_and_a_file_link_are_written_as_a_valid_relative_link()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var path = Picture(elsewhere, "my holiday (1).png");
                var markdown = "![x](" + new Uri(path).AbsoluteUri + ")\n![y](<" + path + ">)\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal("![x](images/my%20holiday%20%281%29.png)\n![y](<images/my%20holiday%20%281%29.png>)\n", result.Markdown);
                Assert.Single(result.Pictures);
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void An_html_image_and_a_reference_definition_used_by_a_picture_are_packed_too()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var a = Picture(elsewhere, "a.png");
                var b = Picture(elsewhere, "b.png");
                var c = Picture(elsewhere, "c.png");
                var markdown = $"<img src=\"{a.Replace('\\', '/')}\" width=\"200\">\n\n![the b][b]\n\n[b]: {b.Replace('\\', '/')} \"B\"\n[unused]: {c.Replace('\\', '/')}\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Contains("<img src=\"images/a.png\" width=\"200\">", result.Markdown);
                Assert.Contains("[b]: images/b.png \"B\"", result.Markdown);
                // a definition that no picture uses is a link, not a picture: it is not touched
                Assert.Contains("[unused]: " + c.Replace('\\', '/'), result.Markdown);
                Assert.Equal(new[] { "images/a.png", "images/b.png" }, result.Pictures.Select(p => p.PackagePath).OrderBy(p => p, StringComparer.Ordinal));
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void Pictures_on_the_web_and_data_pictures_stay_as_they_are_and_a_missing_one_is_listed_not_dropped()
        {
            var note = Folder();
            try
            {
                var markdown = "![w](https://example.com/a.png)\n![d](data:image/png;base64,AAAA)\n![n](//cdn.example.com/b.png)\n![m](images/gone.png)\n![m2](C:\\nowhere\\gone.png)\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal(markdown, result.Markdown);
                Assert.Equal(3, result.Remote);
                Assert.Equal(new[] { "C:\\nowhere\\gone.png", "images/gone.png" }, result.Missing.OrderBy(m => m, StringComparer.Ordinal));
                Assert.Empty(result.Pictures);
            }
            finally { Directory.Delete(note, true); }
        }

        [Fact]
        public void A_link_in_a_code_block_or_a_code_span_is_not_a_picture()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var pic = Picture(elsewhere, "p.png").Replace('\\', '/');
                var markdown = "```\n![in a block](" + pic + ")\n```\n\nSay `![in a span](" + pic + ")` and ![real](" + pic + ").\n~~~\n![tilde](" + pic + ")\n~~~\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal("```\n![in a block](" + pic + ")\n```\n\nSay `![in a span](" + pic + ")` and ![real](images/p.png).\n~~~\n![tilde](" + pic + ")\n~~~\n", result.Markdown);
                Assert.Equal(1, result.Rewritten);
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void A_fence_closes_only_like_in_CommonMark_so_a_nested_or_longer_fence_line_does_not_end_the_block_early()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var pic = Picture(elsewhere, "p.png").Replace('\\', '/');
                var markdown = "````\n```\n![inside 1](" + pic + ")\n```js\n![inside 2](" + pic + ")\n````\n\n![outside](" + pic + ")\n"
                    + "~~~~\n~~~ not a closing line\n![inside 3](" + pic + ")\n~~~~\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Contains("![inside 1](" + pic + ")", result.Markdown);
                Assert.Contains("![inside 2](" + pic + ")", result.Markdown);
                Assert.Contains("![inside 3](" + pic + ")", result.Markdown);
                Assert.Contains("![outside](images/p.png)", result.Markdown);
                Assert.Equal(1, result.Rewritten);
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void A_link_to_a_file_that_is_not_a_picture_is_not_packed_not_copied_and_not_rewritten()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var secret = Picture(elsewhere, "secret.docx", "SECRET").Replace('\\', '/');
                var keys = Picture(note, "keys.txt", "KEYS");
                var markdown = "![x](" + secret + ")\n<img src=\"keys.txt\">\n![ok](" + Picture(elsewhere, "ok.PNG").Replace('\\', '/') + ")\n";
                var packed = NotePackager.Pack(markdown, note);
                Assert.Equal(new[] { "images/ok.PNG" }, packed.Pictures.Select(p => p.PackagePath));
                Assert.Contains("![x](" + secret + ")", packed.Markdown);
                Assert.Contains("<img src=\"keys.txt\">", packed.Markdown);
                var collected = NotePackager.Collect(markdown, note);
                Assert.Equal(1, collected.Copied);
                Assert.False(File.Exists(Path.Combine(note, "images", "secret.docx")));
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void A_picture_from_elsewhere_linked_first_does_not_take_the_name_of_a_picture_under_the_note_linked_later()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var outside = Picture(elsewhere, "shot.png", "OUTSIDE").Replace('\\', '/');
                Picture(note, "images/shot.png", "UNDER-THE-NOTE");
                var markdown = "![first](" + outside + ")\n![second](images/shot.png)\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal("![first](images/shot-2.png)\n![second](images/shot.png)\n", result.Markdown);
                Assert.Equal(2, result.Pictures.Select(p => p.PackagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                using var zipStream = new MemoryStream();
                NotePackager.WriteZip(result, "n", zipStream);
                zipStream.Position = 0;
                using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
                using (var reader = new StreamReader(zip.GetEntry("n/images/shot.png").Open())) Assert.Equal("UNDER-THE-NOTE", reader.ReadToEnd());
                using (var reader = new StreamReader(zip.GetEntry("n/images/shot-2.png").Open())) Assert.Equal("OUTSIDE", reader.ReadToEnd());
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void The_rest_of_the_note_is_written_exactly_as_it_is_with_its_line_endings()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var pic = Picture(elsewhere, "p.png").Replace('\\', '/');
                var markdown = "Line one  \r\n\r\n| a | b |\r\n|---|---|\r\n| ![x](" + pic + ") | **bold** |\r\n\r\n    indented code ![y](" + pic + ")\r\n";
                var result = NotePackager.Pack(markdown, note);
                Assert.Equal(markdown.Replace(pic, "images/p.png"), result.Markdown);
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void The_zip_has_one_folder_with_the_note_and_its_pictures_and_the_original_is_untouched()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var pic = Picture(elsewhere, "pasted.png", "PICTURE-BYTES");
                var original = "# Notes ñ ü\n\n![p](" + pic.Replace('\\', '/') + ")\n";
                var result = NotePackager.Pack(original, note);
                using var output = new MemoryStream();
                NotePackager.WriteZip(result, "My: notes?", output);
                output.Position = 0;
                using var zip = new ZipArchive(output, ZipArchiveMode.Read);
                Assert.Equal(new[] { "My_ notes_/My_ notes_.md", "My_ notes_/images/pasted.png" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
                using (var reader = new StreamReader(zip.GetEntry("My_ notes_/My_ notes_.md").Open(), Encoding.UTF8))
                    Assert.Equal("# Notes ñ ü\n\n![p](images/pasted.png)\n", reader.ReadToEnd());
                using (var reader = new StreamReader(zip.GetEntry("My_ notes_/images/pasted.png").Open()))
                    Assert.Equal("PICTURE-BYTES", reader.ReadToEnd());
                // the original text and the picture on this PC are as they were
                Assert.Contains(pic.Replace('\\', '/'), original);
                Assert.True(File.Exists(pic));
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void Collect_copies_the_pictures_next_to_the_note_keeps_the_originals_and_never_overwrites_a_file()
        {
            var note = Folder();
            var elsewhere = Folder();
            try
            {
                var one = Picture(elsewhere, "shot.png", "FROM-ELSEWHERE");
                var kept = Picture(note, "images/shot.png", "ALREADY-HERE");
                var markdown = "![a](" + one.Replace('\\', '/') + ")\n";
                var result = NotePackager.Collect(markdown, note);
                Assert.Equal("![a](images/shot-2.png)\n", result.Markdown);
                Assert.Equal(1, result.Copied);
                Assert.Equal("FROM-ELSEWHERE", File.ReadAllText(Path.Combine(note, "images", "shot-2.png")));
                Assert.Equal("ALREADY-HERE", File.ReadAllText(kept));
                Assert.Equal("FROM-ELSEWHERE", File.ReadAllText(one));

                // done again on the new text there is nothing left to do
                var again = NotePackager.Collect(result.Markdown, note);
                Assert.Equal(result.Markdown, again.Markdown);
                Assert.Equal(0, again.Copied);
                Assert.Equal(0, again.Rewritten);
            }
            finally { Directory.Delete(note, true); Directory.Delete(elsewhere, true); }
        }

        [Fact]
        public void Collect_needs_a_folder()
        {
            Assert.Throws<ArgumentException>(() => NotePackager.Collect("![a](x.png)", null));
        }

        [Fact]
        public void A_note_with_no_folder_yet_packs_its_absolute_pictures_and_lists_the_relative_ones_as_missing()
        {
            var elsewhere = Folder();
            try
            {
                var pic = Picture(elsewhere, "p.png").Replace('\\', '/');
                var result = NotePackager.Pack("![a](" + pic + ")\n![b](images/x.png)\n", null);
                Assert.Equal("![a](images/p.png)\n![b](images/x.png)\n", result.Markdown);
                Assert.Equal(new[] { "images/x.png" }, result.Missing);
            }
            finally { Directory.Delete(elsewhere, true); }
        }
    }
}
