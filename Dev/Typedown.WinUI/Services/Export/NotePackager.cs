using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.Export
{
    // New since the fork: File > Export > Markdown with pictures (.zip). A note that has pictures cannot be sent as the .md file alone (the
    // links point at files on this PC, often at absolute paths such as Pictures\Caret\pasted-....png that mean nothing on another PC), so the
    // note and its pictures are put in one folder inside a .zip (Windows opens it with no extra program) and the links are made relative
    // ("images/pasted.png") in the copy. The note on screen and its file are not touched.
    //
    // Rules: only links to pictures on this PC are packed (web addresses and data: pictures stay as they are); a picture that is already
    // under the note's folder keeps its place and its link; a picture from anywhere else goes to "images/" (a clash of names gets "-2"); a link
    // to a picture that is not there is listed, not dropped (and left as it is); links in code blocks and code spans are not links; and
    // everything else in the note is written exactly as it is.
    public sealed class PackedPicture
    {
        public string SourcePath { get; init; }
        // Where it is inside the folder of the package, written with "/".
        public string PackagePath { get; init; }
        public long Bytes { get; init; }
    }

    public sealed class NotePackResult
    {
        // The note as it goes in the package.
        public string Markdown { get; set; }
        public List<PackedPicture> Pictures { get; } = new();
        // The links to local pictures that are not there, as they are written in the note.
        public List<string> Missing { get; } = new();
        // Links to pictures on the web (or data: pictures): left as they are.
        public int Remote { get; set; }
        // Links that were changed in the package.
        public int Rewritten { get; set; }
        // Pictures copied into the folder of the note (Collect).
        public int Copied { get; set; }
    }

    public static class NotePackager
    {
        private const string PicturesFolder = "images";

        // Inline image: ![alt](destination "title"); the destination is <...> or a run without spaces (balanced parentheses allowed).
        private static readonly Regex InlineImage = new(
            @"!\[(?<alt>(?:[^\[\]\\]|\\.)*)\]\(\s*(?:<(?<angle>[^>\r\n]*)>|(?<plain>(?:[^\s()\\]|\\.|\((?:[^\s()\\]|\\.)*\))*))(?<tail>(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*)\)",
            RegexOptions.CultureInvariant);

        // <img src="..."> (HTML in a note).
        private static readonly Regex HtmlImage = new(@"<img\b[^>]*?\bsrc\s*=\s*(?:""(?<d>[^""]*)""|'(?<d>[^']*)')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // [label]: destination "title" (a definition for ![alt][label]).
        private static readonly Regex Definition = new(@"^(?<lead> {0,3}\[(?<label>[^\]]+)\]:\s*)(?:<(?<angle>[^>\r\n]*)>|(?<plain>\S+))", RegexOptions.CultureInvariant);

        private static readonly Regex ReferenceUse = new(@"!\[[^\]]*\]\[(?<label>[^\]]*)\]|!\[(?<label2>[^\]]+)\](?![\(\[])", RegexOptions.CultureInvariant);

        // `avoidExistingFiles`: a picture that goes to "images/" does not take the name of a file that is already there on disk (for Collect, which
        // writes into the folder of the note; a package is made new).
        public static NotePackResult Pack(string markdown, string noteFolder, bool avoidExistingFiles = false)
        {
            markdown ??= "";
            var result = new NotePackResult();
            var lines = markdown.Split('\n');

            // pass 1: which labels do pictures use (a definition is a picture only if one does)
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inFence = false;
            string fence = null;
            var codeLine = new bool[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (!inFence && (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")))
                {
                    inFence = true;
                    fence = trimmed.Substring(0, 3);
                    codeLine[i] = true;
                    continue;
                }
                if (inFence)
                {
                    codeLine[i] = true;
                    if (trimmed.StartsWith(fence)) inFence = false;
                    continue;
                }
                var line = lines[i];
                var spans = CodeSpans(line);
                foreach (Match m in ReferenceUse.Matches(line))
                    if (!InSpan(spans, m.Index))
                        labels.Add(m.Groups["label"].Success && m.Groups["label"].Value.Length > 0 ? m.Groups["label"].Value : m.Groups["label2"].Success ? m.Groups["label2"].Value : m.Groups["label"].Value);
            }

            // pass 2: the destinations, changed where the picture goes somewhere else in the package
            var packed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);       // source file -> package path
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);                  // package paths in use
            var missing = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                if (codeLine[i]) continue;
                var line = lines[i];
                var spans = CodeSpans(line);
                var edits = new List<(int Start, int Length, string Text)>();

                void Handle(Group destination)
                {
                    if (!destination.Success || InSpan(spans, destination.Index)) return;
                    var replacement = Decide(destination.Value, noteFolder, result, packed, taken, missing, avoidExistingFiles);
                    if (replacement != null && replacement != destination.Value) edits.Add((destination.Index, destination.Length, replacement));
                }

                foreach (Match m in InlineImage.Matches(line)) Handle(m.Groups["angle"].Success ? m.Groups["angle"] : m.Groups["plain"]);
                foreach (Match m in HtmlImage.Matches(line)) Handle(m.Groups["d"]);
                var definition = Definition.Match(line);
                if (definition.Success && labels.Contains(definition.Groups["label"].Value)) Handle(definition.Groups["angle"].Success ? definition.Groups["angle"] : definition.Groups["plain"]);

                if (edits.Count == 0) continue;
                var sb = new StringBuilder(line);
                foreach (var edit in edits.OrderByDescending(e => e.Start))
                {
                    sb.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
                    result.Rewritten++;
                }
                lines[i] = sb.ToString();
            }
            result.Missing.AddRange(missing);
            result.Markdown = string.Join("\n", lines);
            return result;
        }

        // The new text of a destination, or null when it stays as it is.
        private static string Decide(string destination, string noteFolder, NotePackResult result, Dictionary<string, string> packed, HashSet<string> taken, HashSet<string> missing, bool avoidExistingFiles)
        {
            var text = destination.Trim();
            if (text.Length == 0) return null;
            var source = LocalFile(text, noteFolder, out var isLocal);
            if (!isLocal)
            {
                result.Remote++;
                return null;
            }
            if (source == null)
            {
                missing.Add(text);
                return null;
            }

            var underNote = !string.IsNullOrEmpty(noteFolder) && IsUnder(noteFolder, source);
            if (packed.TryGetValue(source, out var known))
                return underNote && !Path.IsPathRooted(Decode(text)) && !text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? null : Link(known);

            string packagePath;
            if (underNote)
            {
                packagePath = Path.GetRelativePath(noteFolder, source).Replace('\\', '/');
            }
            else
            {
                var name = Path.GetFileName(source);
                packagePath = PicturesFolder + "/" + name;
                for (var n = 2; taken.Contains(packagePath) || (avoidExistingFiles && !string.IsNullOrEmpty(noteFolder) && File.Exists(Path.Combine(noteFolder, packagePath.Replace('/', Path.DirectorySeparatorChar)))); n++)
                    packagePath = PicturesFolder + "/" + Path.GetFileNameWithoutExtension(name) + "-" + n + Path.GetExtension(name);
            }
            taken.Add(packagePath);
            packed[source] = packagePath;
            result.Pictures.Add(new PackedPicture { SourcePath = source, PackagePath = packagePath, Bytes = new FileInfo(source).Length });

            // a link that was already relative and stays where it is is not touched
            if (underNote && !Path.IsPathRooted(Decode(text)) && !text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
            return Link(packagePath);
        }

        private static string Link(string packagePath) => string.Join("/", packagePath.Split('/').Select(Uri.EscapeDataString));

        private static string Decode(string text) => text.Contains('%') ? Uri.UnescapeDataString(text) : text;

        // The file a destination points at on this PC: `isLocal` false for a web address or a data: picture; null (with isLocal true) when the
        // file is not there.
        private static string LocalFile(string destination, string noteFolder, out bool isLocal)
        {
            isLocal = true;
            if (destination.StartsWith("//", StringComparison.Ordinal)) { isLocal = false; return null; }
            string path;
            if (destination.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
                path = uri.LocalPath;
            }
            else
            {
                var scheme = Regex.Match(destination, @"^([A-Za-z][A-Za-z0-9+.\-]*):");
                // "C:\x.png" and "C:/x.png" are drive letters, not schemes
                if (scheme.Success && scheme.Groups[1].Value.Length > 1) { isLocal = false; return null; }
                path = Decode(destination).Replace('/', Path.DirectorySeparatorChar);
            }
            foreach (var candidate in Candidates(path))
            {
                try
                {
                    var full = Path.IsPathRooted(candidate) ? Path.GetFullPath(candidate)
                        : string.IsNullOrEmpty(noteFolder) ? null : Path.GetFullPath(Path.Combine(noteFolder, candidate));
                    if (full != null && File.Exists(full)) return full;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
            }
            return null;
        }

        // The path as written, then without a "?query" or "#fragment" (a link to a picture may have one).
        private static IEnumerable<string> Candidates(string path)
        {
            yield return path;
            var cut = path.IndexOfAny(new[] { '?', '#' });
            if (cut > 0) yield return path.Substring(0, cut);
        }

        private static bool IsUnder(string folder, string file)
        {
            var relative = Path.GetRelativePath(folder, file);
            return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }

        // Backtick code spans of a line: (start, end) of each, the ticks included.
        private static List<(int Start, int End)> CodeSpans(string line)
        {
            var spans = new List<(int, int)>();
            var i = 0;
            while (i < line.Length)
            {
                if (line[i] != '`') { i++; continue; }
                var run = 0;
                while (i + run < line.Length && line[i + run] == '`') run++;
                var close = -1;
                for (var j = i + run; j < line.Length; j++)
                {
                    if (line[j] != '`') continue;
                    var length = 0;
                    while (j + length < line.Length && line[j + length] == '`') length++;
                    if (length == run) { close = j + length; break; }
                    j += length - 1;
                }
                if (close < 0) { i += run; continue; }
                spans.Add((i, close));
                i = close;
            }
            return spans;
        }

        private static bool InSpan(List<(int Start, int End)> spans, int index) => spans.Any(s => index >= s.Start && index < s.End);

        // ---- collecting the pictures next to the note ------------------------------------------------------------------------------

        // The same work as Pack, done on disk: the pictures of the note that are somewhere else are COPIED into "images/" next to the note (the
        // originals stay where they are, another note may use them) and the returned text has the relative links. The caller puts the text in
        // the note; nothing is written to the note's own file here. A copy that cannot be made stops it with an exception, before any text is
        // changed (the copies that were made stay: they are harmless).
        public static NotePackResult Collect(string markdown, string noteFolder)
        {
            if (string.IsNullOrEmpty(noteFolder)) throw new ArgumentException("The note has no folder yet.", nameof(noteFolder));
            var result = Pack(markdown, noteFolder, avoidExistingFiles: true);
            foreach (var picture in result.Pictures.Where(p => !IsUnder(noteFolder, p.SourcePath)))
            {
                var target = Path.Combine(noteFolder, picture.PackagePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(picture.SourcePath, target, overwrite: false);
                result.Copied++;
            }
            return result;
        }

        // ---- the zip ------------------------------------------------------------------------------------------------------------------

        // "<name>/<name>.md" and "<name>/images/..." in one .zip: it unzips into one folder, never into the middle of someone's Downloads.
        public static void WriteZip(NotePackResult result, string noteName, Stream output)
        {
            var folder = SafeName(noteName);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var note = zip.CreateEntry(folder + "/" + folder + ".md", CompressionLevel.Optimal);
            using (var stream = note.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(result.Markdown ?? "");
                stream.Write(bytes, 0, bytes.Length);
            }
            foreach (var picture in result.Pictures)
            {
                // pictures are compressed already: storing them costs less time and about the same size
                var level = Path.GetExtension(picture.SourcePath).ToLowerInvariant() is ".svg" or ".bmp" or ".tif" or ".tiff" ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                var entry = zip.CreateEntry(folder + "/" + picture.PackagePath, level);
                using var to = entry.Open();
                using var from = new FileStream(picture.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                from.CopyTo(to);
            }
        }

        // A name that is good as a folder name in a zip on any PC.
        private static string SafeName(string name)
        {
            var cleaned = string.Concat((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
            return cleaned.Length == 0 ? "note" : cleaned;
        }
    }
}
