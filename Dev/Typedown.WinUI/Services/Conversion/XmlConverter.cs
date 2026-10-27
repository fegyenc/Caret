using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: native XML files as readable Markdown. Not Office files (those have their own converters) but the XML that
    // authorities and ERP systems produce: electronic invoices, tax returns, bank files. The first format pack is Colombia's electronic
    // invoice (DIAN, UBL 2.1; XmlUbl.cs). What no pack knows is shown by the generic outline below, so every well-formed XML file gives a
    // readable result and nothing is hidden: a pack's summary is followed by every field of the file.
    //
    // Plain .NET, offline, no AI. The file is read with DTDs prohibited and no external resolver (an XML file from outside must not be able
    // to read other files or call out), and a file that is not well-formed is refused with the place of the error.
    internal static class XmlConverter
    {
        // A file bigger than this is not read into memory as a tree: the generic outline would be unreadable anyway.
        private const long MaxBytes = 60L * 1024 * 1024;

        public static string Convert(Stream stream, ConversionContext context)
        {
            var document = Load(stream);
            return Render(document, context);
        }

        // Text that is itself XML (the invoice inside a DIAN container is stored as text) is read with the same rules.
        internal static XDocument Parse(string xml)
        {
            using var reader = XmlReader.Create(new StringReader(xml), Settings());
            return XDocument.Load(reader);
        }

        internal static XDocument Load(Stream stream)
        {
            if (stream.CanSeek && stream.Length > MaxBytes) // not an InvalidDataException: that one is shown as "damaged or protected", and this file is not damaged, only too big
                throw new NotSupportedException("The XML file is too big to be read here (more than 60 MB).");
            try
            {
                using var reader = XmlReader.Create(stream, Settings());
                return XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"This is not well-formed XML (line {ex.LineNumber}, position {ex.LinePosition}): {ex.Message}", ex);
            }
        }

        private static XmlReaderSettings Settings() => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0,
        };

        private static string Render(XDocument document, ConversionContext context)
        {
            var root = document.Root ?? throw new InvalidDataException("The XML file has no content.");
            var labels = XmlLabels.For(context.Options.Language);
            var sb = new StringBuilder();
            if (XmlUbl.TryRender(root, context, labels, sb))
            {
                sb.Append("\n## ").Append(labels.AllFields).Append("\n\n").Append(labels.AllFieldsNote).Append("\n\n");
                AppendOutline(sb, root, labels);
                return sb.ToString();
            }
            sb.Append("# ").Append(MarkdownText.EscapeInline(root.Name.LocalName)).Append("\n\n");
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(labels.File).Append(": ").Append(MarkdownText.EscapeInline(context.SourceName)).Append('\n');
            if (!string.IsNullOrEmpty(root.Name.NamespaceName)) sb.Append("> ").Append(labels.Namespace).Append(": `").Append(root.Name.NamespaceName).Append("`\n");
            sb.Append('\n');
            XmlStructure.Append(sb, root, labels);
            AppendOutline(sb, root, labels, sections: true);
            return sb.ToString();
        }

        // The generic view: the fields of the file in order, as nested lists. Text that stands between child elements is kept in its place (mixed
        // content), empty elements are kept, and a run of three or more adjacent siblings with the same name and simple contents (rows of data)
        // becomes a table at the place of the first of them, so a list of 300 lines reads like one. A run of two or more adjacent text-only
        // siblings with the same name (a list of instructions, of notes) is one numbered list under the name, said once. Digital signatures are
        // named, not printed (they are long and unreadable). Nesting deeper than 150 levels is cut off with a note: a file can be made to nest
        // far deeper than this code can follow without running out of stack.
        //
        // With `sections` (the generic view of a file that no format pack knows), an element that has a title of its own (an attribute id, name,
        // title or label, or a child element title) and child elements becomes a heading, and so does an element that only holds such elements
        // (the "tasks" around the "task"s), so that a document-like file (a prompt, a manual, a feed) has an outline that can be navigated. A
        // heading's contents follow it; what has no title stays a list. A file with nothing of this kind reads as one list, as before.
        internal static void AppendOutline(StringBuilder sb, XElement root, XmlLabels labels, bool sections = false)
        {
            if (sections && root.Elements().Any(IsSection))
            {
                var attributes = root.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
                if (attributes.Count > 0) sb.Append("- ").Append(AttributesText(attributes, labels)).Append('\n');
                WriteChildren(sb, Content(root), 0, labels, 0, 1, null);
                return;
            }
            WriteElement(sb, root, 0, labels, 0);
        }

        private const int MaxDepth = 150;
        private const int MaxStoredText = 2000;

        private static List<XNode> Content(XElement element) =>
            element.Nodes().Where(n => !(n is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))).ToList();

        private static string AttributesText(IEnumerable<XAttribute> attributes, XmlLabels labels) =>
            "(" + string.Join(", ", attributes.Select(a => MarkdownText.EscapeInline(a.Name.LocalName) + "=" + Bounded(a.Value, labels))) + ")";

        private static void WriteElement(StringBuilder sb, XElement element, int depth, XmlLabels labels, int nesting)
        {
            var indent = new string(' ', depth * 2);
            if (depth > MaxDepth)
            {
                sb.Append(indent).Append("- … ").Append(labels.TooDeep).Append('\n');
                return;
            }
            var name = element.Name.LocalName;
            if (name == "Signature" && element.Name.NamespaceName.Contains("xmldsig"))
            {
                sb.Append(indent).Append("- **").Append(name).Append("**: ").Append(labels.SignaturePresent).Append('\n');
                return;
            }
            var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
            var nodes = Content(element);
            var hasChildren = nodes.Any(n => n is XElement);

            sb.Append(indent).Append("- **").Append(MarkdownText.EscapeInline(name)).Append("**");
            if (attributes.Count > 0) sb.Append(' ').Append(AttributesText(attributes, labels));
            XElement stored = null;
            List<(string Marker, string Text)> enumerated = null;
            if (!hasChildren)
            {
                var text = element.Value.Trim();
                if (text.Length > 0)
                {
                    // XML stored as text (a CDATA part holding a document) is read and its fields follow, indented, so that nothing in it is hidden;
                    // when it cannot be read, its text is kept (up to a bound) and not replaced by a count
                    if (text.StartsWith("<") && nesting < 3) { try { stored = Parse(text).Root; } catch (Exception ex) when (ex is XmlException or InvalidOperationException) { } }
                    if (stored != null) sb.Append(": ").Append(labels.EmbeddedXml);
                    else if (TrySplitItems(text, out var head, out enumerated)) { if (head.Length > 0) sb.Append(": ").Append(Bounded(head, labels)); }
                    else sb.Append(": ").Append(Bounded(text, labels));
                }
            }
            sb.Append('\n');
            if (enumerated != null) AppendItems(sb, enumerated, (depth + 1) * 2, labels);
            if (stored != null) WriteElement(sb, stored, depth + 1, labels, nesting + 1);
            if (!hasChildren) return;
            WriteChildren(sb, nodes, depth + 1, labels, nesting, 0, null);
        }

        // The children of an element, at the list depth `depth`. With a `headingLevel` above 0 the children that are sections become headings of
        // the next level (down to level 6; below that they are lists); `skip` is the title child that the heading already says.
        private static void WriteChildren(StringBuilder sb, List<XNode> nodes, int depth, XmlLabels labels, int nesting, int headingLevel, XElement skip)
        {
            var childIndent = new string(' ', depth * 2);
            var i = 0;
            var sectionSeen = false;
            while (i < nodes.Count)
            {
                if (nodes[i] is XElement first)
                {
                    if (first == skip) { i++; continue; }
                    var j = i;
                    while (j < nodes.Count && nodes[j] is XElement next && next.Name == first.Name) j++;
                    var run = nodes.Skip(i).Take(j - i).Cast<XElement>().ToList();
                    if (run.Count >= 3 && run.All(IsRecord))
                    {
                        AppendRecordTable(sb, run, depth, labels);
                        i = j;
                        continue;
                    }
                    // after the first heading every element that follows is one too: a list that came after a heading would read as part of it
                    if (headingLevel > 0 && headingLevel < 6 && (sectionSeen || IsSection(first)) && !IsSignature(first))
                    {
                        sectionSeen = true;
                        WriteSection(sb, first, headingLevel + 1, labels, nesting);
                        i++;
                        continue;
                    }
                    if (run.Count >= 2 && run.All(IsTextOnly))
                    {
                        AppendTextRun(sb, run, depth, labels);
                        i = j;
                        continue;
                    }
                    WriteElement(sb, first, depth, labels, nesting);
                }
                else if (nodes[i] is XText mixed)
                {
                    // text between the elements: kept where it stands
                    sb.Append(childIndent).Append("- ").Append(Bounded(mixed.Value.Trim(), labels)).Append('\n');
                }
                i++;
            }
        }

        // A heading and what is under it; the lists under a heading start again at depth 0. Every attribute is said: those that make the title in
        // the heading, the others in a line under it.
        private static void WriteSection(StringBuilder sb, XElement element, int level, XmlLabels labels, int nesting)
        {
            var title = SectionTitle(element, out var used, out var titleChild) ?? element.Name.LocalName;
            sb.Append('\n').Append(new string('#', level)).Append(' ').Append(Bounded(title, labels)).Append("\n\n");
            var rest = element.Attributes().Where(a => !a.IsNamespaceDeclaration && !used.Contains(a)).ToList();
            if (rest.Count > 0) sb.Append("- ").Append(AttributesText(rest, labels)).Append('\n');
            // an element that holds no elements (a note after the sections) has its text under its heading
            if (!element.HasElements && element.Value.Trim().Length > 0) sb.Append(Bounded(element.Value, labels)).Append('\n');
            WriteChildren(sb, Content(element), 0, labels, nesting, level, titleChild);
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
        }

        // ---- sections ------------------------------------------------------------------------------------------------------------------

        private static readonly string[] TitleAttributes = { "id", "name", "title", "label" };

        // The title of an element: its id and name attributes ("TW.9 — Verify..."), or else its title or its label when it has no name, or else
        // the text of a child element "title". Null when it has none. `used` are the attributes the title is made of, `titleChild` the child.
        private static string SectionTitle(XElement element, out List<XAttribute> used, out XElement titleChild)
        {
            used = new List<XAttribute>();
            titleChild = null;
            var parts = new List<string>();
            foreach (var key in TitleAttributes)
            {
                var attribute = element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && string.Equals(a.Name.LocalName, key, StringComparison.OrdinalIgnoreCase));
                var value = attribute?.Value.Trim();
                if (string.IsNullOrEmpty(value)) continue;
                used.Add(attribute);
                if (!parts.Contains(value)) parts.Add(value);
                if (key == "name") break;   // id and name together; a title or a label only when there is no name
            }
            if (parts.Count > 0) return string.Join(" — ", parts.Select(OneLine));
            titleChild = element.Elements().FirstOrDefault(e => !e.HasElements && string.Equals(e.Name.LocalName, "title", StringComparison.OrdinalIgnoreCase) && e.Value.Trim().Length > 0);
            return titleChild == null ? null : OneLine(titleChild.Value.Trim());
        }

        private static string SectionTitle(XElement element) => SectionTitle(element, out _, out _);

        // A heading is an element with child elements and a title of its own, or one that holds such elements (the container of the tasks).
        private static bool IsSignature(XElement element) => element.Name.LocalName == "Signature" && element.Name.NamespaceName.Contains("xmldsig");

        private static bool IsSection(XElement element) =>
            element.HasElements && !IsSignature(element) && (SectionTitle(element) != null || element.Elements().Any(c => c.HasElements && SectionTitle(c) != null));

        private static bool IsTextOnly(XElement element) =>
            !element.HasElements && !IsSignature(element) && !element.Value.TrimStart().StartsWith("<");

        // Several text-only siblings with the same name: the name once, then the texts as a numbered list (an item keeps its attributes).
        private static void AppendTextRun(StringBuilder sb, List<XElement> run, int depth, XmlLabels labels)
        {
            var indent = new string(' ', depth * 2);
            sb.Append(indent).Append("- **").Append(MarkdownText.EscapeInline(run[0].Name.LocalName)).Append("** × ").Append(run.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            for (var k = 0; k < run.Count; k++)
            {
                var attributes = run[k].Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
                var text = run[k].Value.Trim();
                var split = TrySplitItems(text, out var head, out var enumerated);
                if (split) text = head;
                var prefix = indent + "  " + (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". ";
                sb.Append(prefix)
                    .Append(text.Length > 0 ? Bounded(text, labels) : attributes.Count == 0 || split ? "—" : "")
                    .Append(attributes.Count > 0 ? (text.Length > 0 ? " " : "") + AttributesText(attributes, labels) : "").Append('\n');
                // the lines of an item that are themselves a list stay a list, under the item
                if (split) AppendItems(sb, enumerated, prefix.Length, labels);
            }
        }

        // ---- text that is a list --------------------------------------------------------------------------------------------------------

        private static readonly Regex Enumerator = new(@"^(\d{1,3}[.)]|[-*•])\s+(\S.*)$", RegexOptions.CultureInvariant);

        // A text of several lines of which at least two start with an enumerator (1. 2) - * •), as instructions are written ("Confirm: / 1. the
        // file exists; / 2. ..."): what comes before the first one is the head, and each enumerated line an item (a line that is not one belongs
        // to the item before it). Other texts, with line breaks or not, are one line, as before.
        private static bool TrySplitItems(string text, out string head, out List<(string Marker, string Text)> items)
        {
            head = null;
            items = null;
            if (text.IndexOf('\n') < 0) return false;
            var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count(l => Enumerator.IsMatch(l)) < 2) return false;
            var first = lines.FindIndex(l => Enumerator.IsMatch(l));
            head = string.Join(" ", lines.Take(first));
            items = new List<(string, string)>();
            foreach (var line in lines.Skip(first))
            {
                var m = Enumerator.Match(line);
                if (m.Success) items.Add((char.IsDigit(m.Groups[1].Value[0]) ? m.Groups[1].Value : "-", m.Groups[2].Value));
                else items[items.Count - 1] = (items[items.Count - 1].Marker, items[items.Count - 1].Text + " " + line);
            }
            return true;
        }

        // The items indented by `spaces` (under the content of the item or element above them): a number keeps the number the file has, a bullet is a dash.
        private static void AppendItems(StringBuilder sb, List<(string Marker, string Text)> items, int spaces, XmlLabels labels)
        {
            var indent = new string(' ', spaces);
            foreach (var (marker, text) in items) sb.Append(indent).Append(marker).Append(' ').Append(Bounded(text, labels)).Append('\n');
        }

        // The text of a field written as one line (runs of white space become one space) and escaped; a very long one is cut with the number of
        // characters left out. One pass over the text, which builds only what is shown and only counts the rest: a value of many megabytes
        // costs no more memory than one of 2000 characters.
        private static string Bounded(string text, XmlLabels labels)
        {
            var shown = new StringBuilder(Math.Min(text.Length, MaxStoredText));
            var total = 0;
            var pendingSpace = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c)) { pendingSpace = total > 0; continue; }
                if (pendingSpace)
                {
                    total++;
                    if (shown.Length < MaxStoredText) shown.Append(' ');
                    pendingSpace = false;
                }
                total++;
                if (shown.Length < MaxStoredText) shown.Append(c);
            }
            if (total <= MaxStoredText) return MarkdownText.EscapeInline(shown.ToString());
            return MarkdownText.EscapeInline(shown.ToString()) + "… (" + (total - MaxStoredText).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + labels.MoreCharacters + ")";
        }

        // A row of data: child elements that all have text and no children of their own (the row's own attributes become columns), up to 12
        // columns, and no two children with the same name; or an element that is only attributes (<add key="a" value="b"/>, the rows of a
        // configuration file or of a list of packages), with no text.
        private static bool IsRecord(XElement element)
        {
            var kids = element.Elements().ToList();
            var attributeCount = element.Attributes().Count(a => !a.IsNamespaceDeclaration);
            if (kids.Count == 0) return attributeCount >= 1 && attributeCount <= 12 && element.Value.Trim().Length == 0;
            var columns = kids.Count + attributeCount;
            return kids.Count >= 1 && columns <= 12 && kids.All(k => !k.HasElements && k.Value.Trim().Length > 0) && kids.Select(k => k.Name).Distinct().Count() == kids.Count
                && !element.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value));
        }

        private static void AppendRecordTable(StringBuilder sb, List<XElement> rows, int depth, XmlLabels labels)
        {
            var indent = new string(' ', depth * 2);
            var attributeColumns = rows.SelectMany(r => r.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName)).Distinct().ToList();
            var columns = rows.SelectMany(r => r.Elements().Select(e => e.Name.LocalName)).Distinct().ToList();
            sb.Append('\n');
            // the attribute columns are marked with @ where there are element columns too (to tell them apart); a table of attributes only has plain names
            var table = new List<IReadOnlyList<string>> { attributeColumns.Select(a => columns.Count == 0 ? a : "@" + a).Concat(columns).ToList() };
            foreach (var row in rows)
                table.Add(attributeColumns.Select(a => row.Attributes().FirstOrDefault(x => !x.IsNamespaceDeclaration && x.Name.LocalName == a)?.Value ?? "")
                    .Concat(columns.Select(c => CellText(row.Elements().FirstOrDefault(e => e.Name.LocalName == c)))).ToList());
            foreach (var line in MarkdownText.Table(table.Select(r => (IReadOnlyList<string>)r.Select(c => Bounded(c, labels)).ToList()).ToList()).TrimEnd('\n').Split('\n')) sb.Append(indent).Append(line).Append('\n');
            sb.Append('\n');
        }

        // The value of a cell, with its attributes when it has some (a currency, a unit): nothing of the file is lost in a table.
        private static string CellText(XElement cell)
        {
            if (cell == null) return "";
            var attributes = cell.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
            var text = cell.Value.Trim();
            return attributes.Count == 0 ? text : text + " [" + string.Join(", ", attributes.Select(a => a.Name.LocalName + "=" + a.Value)) + "]";
        }

        internal static string OneLine(string text) => string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
