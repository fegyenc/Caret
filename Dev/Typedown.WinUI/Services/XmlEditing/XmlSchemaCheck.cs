using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Typedown.WinUI.Services.XmlEditing
{
    // What kind of problem a schema check found; the words for each kind are in XmlSchemaWords, in the language of the person.
    internal enum XmlIssueKind
    {
        NotExpected,            // an element that the schema does not allow at this place (and what it allows there)
        Incomplete,             // an element that ends before what the schema requires
        TextNotAllowed,         // text where the schema allows only elements
        MissingAttribute,       // a required attribute that is not there
        AttributeNotDeclared,   // an attribute that the schema does not know on this element
        ElementNotDeclared,     // an element that the schema does not declare at all (the root, mostly)
        InvalidValue,           // a value that does not fit its type (a number that is not one, a date...), with the constraint that failed
        Other,                  // anything else: the message of the parser, as it is
    }

    internal sealed class XmlSchemaIssue
    {
        public XmlIssueKind Kind { get; init; }
        public int Line { get; init; }
        public int Position { get; init; }
        // the message of the parser (English), kept for the tooltip and the log
        public string Raw { get; init; }
        // what the message names: the element, its parent, the attribute, the value, the type, what is expected (a list), the constraint
        public string Element { get; init; }
        public string Parent { get; init; }
        public string Attribute { get; init; }
        public string Value { get; init; }
        public string Type { get; init; }
        public string Expected { get; init; }
        public string Constraint { get; init; }
        // an invalid value of an attribute (and not of an element)
        public bool OfAttribute { get; init; }

        public string Plain(string language) => XmlSchemaWords.For(language).Say(this);
    }

    internal enum XmlSchemaState { None, Valid, Invalid, Skipped, Unusable, TooBig }

    internal sealed class XmlSchemaOutcome
    {
        public XmlSchemaState State { get; init; }
        // the schema files, for the strip's tooltip
        public IReadOnlyList<string> Schemas { get; init; } = Array.Empty<string>();
        public IReadOnlyList<XmlSchemaIssue> Issues { get; init; } = Array.Empty<XmlSchemaIssue>();
        // there were more problems than are listed
        public bool Truncated { get; init; }
        // when the schema could not be used: why (the message of the parser)
        public string Problem { get; init; }
    }

    // Checking a text against a schema (XSD) and saying the problems in plain words. Plain .NET, offline, no AI. Only local files are read: a
    // schema file, and the schemas it includes or imports, are opened from the disk (nothing from http, https, ftp, or a network share), a
    // schemaLocation in the text is only a name to look for next to the file (never fetched), and a DTD is never followed.
    internal static class XmlSchemaCheck
    {
        // as many problems as the strip and the gutter can use; more are said ("and more")
        public const int MaxIssues = 200;
        // a text bigger than this is not checked (the same size as the readable view: XmlAnalysis.MaxPreviewCharacters)
        public const int MaxCharacters = XmlAnalysis.MaxPreviewCharacters;
        private const long MaxSchemaBytes = 20L * 1024 * 1024;

        // ---- finding the schema the text names ----------------------------------------------------------------------------------------

        // The schema files that the root element names (xsi:noNamespaceSchemaLocation, or the second word of each pair of
        // xsi:schemaLocation) and that exist as local files, resolved from the folder of the file the text is from. Nothing is fetched.
        public static IReadOnlyList<string> Detect(string text, string xmlPath)
        {
            var names = new List<string>();
            try
            {
                using var reader = XmlReader.Create(new StringReader(text ?? ""), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                if (!reader.Read() || reader.MoveToContent() != XmlNodeType.Element) return Array.Empty<string>();
                const string xsi = "http://www.w3.org/2001/XMLSchema-instance";
                var none = reader.GetAttribute("noNamespaceSchemaLocation", xsi);
                if (!string.IsNullOrWhiteSpace(none)) names.Add(none.Trim());
                var pairs = reader.GetAttribute("schemaLocation", xsi)?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (pairs != null)
                    for (var i = 1; i < pairs.Length; i += 2) names.Add(pairs[i]);
            }
            catch (XmlException)
            {
                return Array.Empty<string>();
            }
            var folder = string.IsNullOrEmpty(xmlPath) ? null : Path.GetDirectoryName(xmlPath);
            var found = new List<string>();
            foreach (var name in names)
            {
                var path = LocalPath(name, folder);
                if (path != null && File.Exists(path) && !found.Contains(path, StringComparer.OrdinalIgnoreCase)) found.Add(path);
            }
            return found;
        }

        // A name from a schemaLocation as a local file path, or null: a web address, a network share, or a name with nothing to resolve it from.
        private static string LocalPath(string name, string folder)
        {
            try
            {
                if (Uri.TryCreate(name, UriKind.Absolute, out var uri))
                {
                    if (!uri.IsFile || uri.IsUnc) return null;
                    return uri.LocalPath;
                }
                if (Path.IsPathRooted(name)) return name.StartsWith(@"\\", StringComparison.Ordinal) ? null : name;
                return folder == null ? null : Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(name)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
            {
                return null;
            }
        }

        // ---- the check ----------------------------------------------------------------------------------------------------------------

        // `wellFormed`: whether the text is well-formed XML (the caller knows already); a text that is not is not checked.
        public static XmlSchemaOutcome Run(string text, IReadOnlyList<string> schemaPaths, bool wellFormed = true)
        {
            schemaPaths ??= Array.Empty<string>();
            if (schemaPaths.Count == 0) return new XmlSchemaOutcome { State = XmlSchemaState.None };
            if (!wellFormed) return new XmlSchemaOutcome { State = XmlSchemaState.Skipped, Schemas = schemaPaths };
            if ((text?.Length ?? 0) > MaxCharacters) return new XmlSchemaOutcome { State = XmlSchemaState.TooBig, Schemas = schemaPaths };

            // the messages of the parser are matched in English: whatever language the thread has, they are made in the invariant one
            var culture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                XmlSchemaSet set;
                try
                {
                    set = Load(schemaPaths);
                }
                catch (Exception ex) when (ex is XmlException or XmlSchemaException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
                {
                    return new XmlSchemaOutcome { State = XmlSchemaState.Unusable, Schemas = schemaPaths, Problem = ex.Message };
                }
                return Validate(text ?? "", set, schemaPaths);
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = culture;
            }
        }

        // The compiled schemas of the last few sets of files, as long as the files are not changed: a big schema (an invoice standard) takes a
        // moment to compile and the check runs after every pause in typing.
        private static readonly object CacheLock = new();
        // each entry has every file the set was made from (the schemas that were included or imported too) with its time: an edit of any of them
        // makes the entry out of date
        private static readonly List<(string Key, XmlSchemaSet Set, (string Path, long Ticks)[] Files)> Cache = new();

        private static XmlSchemaSet Load(IReadOnlyList<string> paths)
        {
            var key = string.Join("|", paths);
            lock (CacheLock)
            {
                var hit = Cache.FirstOrDefault(c => c.Key == key);
                if (hit.Set != null)
                {
                    if (hit.Files.All(f => File.GetLastWriteTimeUtc(f.Path).Ticks == f.Ticks)) return hit.Set;
                    Cache.RemoveAll(c => c.Key == key);
                }
            }
            var problems = new List<string>();
            var resolver = new LocalFilesOnly();
            var set = new XmlSchemaSet { XmlResolver = resolver };
            set.ValidationEventHandler += (s, e) => { if (e.Severity == XmlSeverityType.Error) problems.Add(e.Message); };
            foreach (var path in paths)
            {
                var info = new FileInfo(path);
                if (info.Length > MaxSchemaBytes) throw new InvalidOperationException("The schema file is too big.");
                using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, MaxCharactersFromEntities = 100_000, XmlResolver = null });
                set.Add(null, reader);
            }
            set.Compile();
            // a schema that includes or imports a file that could not be read (it is on the web, or not there) is not used as if it were whole
            if (resolver.Failures.Count > 0) throw new XmlException(resolver.Failures[0]);
            if (problems.Count > 0) throw new XmlSchemaException(problems[0]);
            // the files of the set: the schemas that were read, which the includes and imports are among
            var read = new List<string>(paths);
            var seen = new HashSet<XmlSchema>();
            foreach (XmlSchema schema in set.Schemas()) CollectFiles(schema, seen, read);
            var files = read.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => (p, File.GetLastWriteTimeUtc(p).Ticks)).ToArray();
            lock (CacheLock)
            {
                Cache.Insert(0, (key, set, files));
                if (Cache.Count > 4) Cache.RemoveRange(4, Cache.Count - 4);
            }
            return set;
        }

        // The files a compiled schema was read from: itself, and the schemas it includes or imports (and what those include and import).
        private static void CollectFiles(XmlSchema schema, HashSet<XmlSchema> seen, List<string> files)
        {
            if (schema == null || !seen.Add(schema)) return;
            if (!string.IsNullOrEmpty(schema.SourceUri) && Uri.TryCreate(schema.SourceUri, UriKind.Absolute, out var uri) && uri.IsFile) files.Add(uri.LocalPath);
            foreach (XmlSchemaObject external in schema.Includes)
                if (external is XmlSchemaExternal e) CollectFiles(e.Schema, seen, files);
        }

        private static XmlSchemaOutcome Validate(string text, XmlSchemaSet set, IReadOnlyList<string> schemaPaths)
        {
            var issues = new List<XmlSchemaIssue>();
            var truncated = false;
            XmlReader reader = null;
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = set,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                // a schemaLocation in the text is not followed, and warnings (a schema that is not found for a part) are not problems
                ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints | XmlSchemaValidationFlags.AllowXmlAttributes,
            };
            settings.ValidationEventHandler += (s, e) =>
            {
                if (e.Severity != XmlSeverityType.Error) return;
                if (issues.Count >= MaxIssues) { truncated = true; return; }
                var line = e.Exception?.LineNumber ?? 0;
                var position = e.Exception?.LinePosition ?? 0;
                if (line == 0 && reader is IXmlLineInfo info) { line = info.LineNumber; position = info.LinePosition; }
                // the element the problem is at: the parser is on it, or on one of its attributes (then it is the tag before that attribute)
                var current = reader?.NodeType == XmlNodeType.Element ? reader.Name : reader?.NodeType == XmlNodeType.Attribute ? ElementBefore(text, line, position) : null;
                issues.Add(XmlSchemaWords.Classify(e.Message, line, position, current));
            };
            try
            {
                using var xml = XmlReader.Create(new StringReader(text), settings);
                reader = xml;
                while (xml.Read()) { }
            }
            catch (XmlException ex)
            {
                // not well-formed after all (the caller said it was): the same as not checked
                return new XmlSchemaOutcome { State = XmlSchemaState.Skipped, Schemas = schemaPaths, Problem = ex.Message };
            }
            catch (XmlSchemaException ex)
            {
                return new XmlSchemaOutcome { State = XmlSchemaState.Unusable, Schemas = schemaPaths, Problem = ex.Message };
            }
            return new XmlSchemaOutcome
            {
                State = issues.Count == 0 ? XmlSchemaState.Valid : XmlSchemaState.Invalid,
                Schemas = schemaPaths,
                Issues = issues,
                Truncated = truncated,
            };
        }

        private static readonly Regex NameAtStart = new(@"\G[^\s/>=]+", RegexOptions.CultureInvariant);

        // The name of the start tag that an attribute at (line, position) is in: the last "<" before it (a "<" cannot be in an attribute value).
        private static string ElementBefore(string text, int line, int position)
        {
            if (line < 1) return null;
            var offset = 0;
            for (var current = 1; current < line; current++)
            {
                var next = text.IndexOfAny(new[] { '\r', '\n' }, offset);
                if (next < 0) return null;
                offset = next + (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? 2 : 1);
            }
            offset += Math.Max(0, position - 1);
            if (offset > text.Length) offset = text.Length;
            var open = text.LastIndexOf('<', Math.Max(0, offset - 1));
            if (open < 0) return null;
            var m = NameAtStart.Match(text, open + 1);
            return m.Success ? m.Value : null;
        }

        // Opens local files only (a schema, and what it includes or imports): a web address, a network share, or anything that is not a file
        // is refused, so a schema from outside cannot make Caret go online or read from another machine.
        // A memory stream that throws before a write would take it past its limit.
        private sealed class LimitedStream : MemoryStream
        {
            private readonly long limit;

            public LimitedStream(long limit) => this.limit = limit;

            private void Check(long more)
            {
                if (Length + more > limit) throw new XmlException("The schema file is too big.");
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Check(count);
                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                Check(buffer.Length);
                base.Write(buffer);
            }

            public override void WriteByte(byte value)
            {
                Check(1);
                base.WriteByte(value);
            }
        }

        private sealed class LocalFilesOnly : XmlResolver
        {
            // what could not be opened: the schema parser turns a failure into a warning, so it is kept here to be said
            public List<string> Failures { get; } = new();

            // The standard schema of XML signatures (xmldsig-core-schema.xsd, imported by the schemas of the NFS-e, the NF-e, the DTE...) starts with a DOCTYPE that names the
            // DTD of XML Schema by a public identifier and a web address. Neither is read: both resolve to an empty DTD, and the internal subset of the file is read.
            private static readonly Uri EmptyDtd = new("urn:caret:empty-dtd");

            public override Uri ResolveUri(Uri baseUri, string relativeUri) =>
                relativeUri != null && (relativeUri.StartsWith("-//", StringComparison.Ordinal) || relativeUri.StartsWith("+//", StringComparison.Ordinal) || relativeUri.EndsWith(".dtd", StringComparison.OrdinalIgnoreCase))
                    ? EmptyDtd
                    : base.ResolveUri(baseUri, relativeUri);

            public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
            {
                try
                {
                    if (absoluteUri == EmptyDtd) return new MemoryStream(Array.Empty<byte>());
                    if (absoluteUri == null || !absoluteUri.IsFile || absoluteUri.IsUnc)
                        throw new XmlException($"Only a schema file on this computer can be read, not {absoluteUri}.");
                    var path = absoluteUri.LocalPath;
                    // only a schema document: an external entity that a crafted schema names (a text file, a key) is not opened
                    if (!path.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase)) throw new XmlException($"Only a schema file (.xsd) can be read, not {Path.GetFileName(path)}.");
                    if (new FileInfo(path).Length > MaxSchemaBytes) throw new XmlException("The schema file is too big.");
                    // The schema document is read here, with a limit on what its entities can expand to, and handed on without its DOCTYPE: the reader that XmlSchemaSet makes
                    // for an included or imported file has no such limit (an expanding entity in an imported file could use all the memory).
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, MaxCharactersFromEntities = 100_000, XmlResolver = null }, absoluteUri.AbsoluteUri);
                    var document = XDocument.Load(reader);
                    document.DocumentType?.Remove();
                    var bounded = new LimitedStream(MaxSchemaBytes);   // refuses to grow past the size a schema file may have, while it is being written
                    document.Save(bounded, SaveOptions.DisableFormatting);   // no indentation added
                    bounded.Position = 0;
                    return bounded;
                }
                catch (Exception ex)
                {
                    Failures.Add(ex.Message);
                    throw;
                }
            }
        }
    }
}
