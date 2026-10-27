using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            if (stream.CanSeek && stream.Length > MaxBytes) throw new InvalidDataException("The XML file is too big to be read here (more than 60 MB).");
            var document = Load(stream);
            return Render(document, context);
        }

        // Text that is itself XML (the invoice inside a DIAN container is stored as text) is read with the same rules.
        internal static XDocument Parse(string xml)
        {
            using var reader = XmlReader.Create(new StringReader(xml), Settings());
            return XDocument.Load(reader);
        }

        private static XDocument Load(Stream stream)
        {
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
            AppendOutline(sb, root, labels);
            return sb.ToString();
        }

        // The generic view: the fields of the file in order, as nested lists. Text that stands between child elements is kept in its place (mixed
        // content), empty elements are kept, and a run of three or more adjacent siblings with the same name and simple contents (rows of data)
        // becomes a table at the place of the first of them, so a list of 300 lines reads like one. Digital signatures are named, not printed
        // (they are long and unreadable). Nesting deeper than 150 levels is cut off with a note: a file can be made to nest far deeper than
        // this code can follow without running out of stack.
        internal static void AppendOutline(StringBuilder sb, XElement root, XmlLabels labels)
        {
            WriteElement(sb, root, 0, labels, 0);
        }

        private const int MaxDepth = 150;
        private const int MaxStoredText = 2000;

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
            var nodes = element.Nodes().Where(n => !(n is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))).ToList();
            var hasChildren = nodes.Any(n => n is XElement);

            sb.Append(indent).Append("- **").Append(MarkdownText.EscapeInline(name)).Append("**");
            if (attributes.Count > 0)
                sb.Append(" (").Append(string.Join(", ", attributes.Select(a => MarkdownText.EscapeInline(a.Name.LocalName) + "=" + MarkdownText.EscapeInline(a.Value)))).Append(')');
            XElement stored = null;
            if (!hasChildren)
            {
                var text = element.Value.Trim();
                if (text.Length > 0)
                {
                    // XML stored as text (a CDATA part holding a document) is read and its fields follow, indented, so that nothing in it is hidden;
                    // when it cannot be read, its text is kept (up to a bound) and not replaced by a count
                    if (text.StartsWith("<") && nesting < 3) { try { stored = Parse(text).Root; } catch (Exception ex) when (ex is XmlException or InvalidOperationException) { } }
                    if (stored != null) sb.Append(": ").Append(labels.EmbeddedXml);
                    else sb.Append(": ").Append(Bounded(text, labels));
                }
            }
            sb.Append('\n');
            if (stored != null) WriteElement(sb, stored, depth + 1, labels, nesting + 1);
            if (!hasChildren) return;

            var childIndent = new string(' ', (depth + 1) * 2);
            var i = 0;
            while (i < nodes.Count)
            {
                if (nodes[i] is XElement first)
                {
                    var j = i;
                    while (j < nodes.Count && nodes[j] is XElement next && next.Name == first.Name) j++;
                    var run = nodes.Skip(i).Take(j - i).Cast<XElement>().ToList();
                    if (run.Count >= 3 && run.All(IsRecord))
                    {
                        AppendRecordTable(sb, run, depth + 1);
                        i = j;
                        continue;
                    }
                    WriteElement(sb, first, depth + 1, labels, nesting);
                }
                else if (nodes[i] is XText mixed)
                {
                    // text between the elements: kept where it stands
                    sb.Append(childIndent).Append("- ").Append(Bounded(mixed.Value.Trim(), labels)).Append('\n');
                }
                i++;
            }
        }

        // The text of a field written as one line and escaped; a very long one is cut with the number of characters left out.
        private static string Bounded(string text, XmlLabels labels)
        {
            var line = OneLine(text);
            if (line.Length <= MaxStoredText) return MarkdownText.EscapeInline(line);
            var left = line.Length - MaxStoredText;
            return MarkdownText.EscapeInline(line.Substring(0, MaxStoredText)) + "… (" + left.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + labels.MoreCharacters + ")";
        }

        // A row of data: child elements that all have text and no children of their own (the row's own attributes become columns), up to 12
        // columns, and no two children with the same name.
        private static bool IsRecord(XElement element)
        {
            var kids = element.Elements().ToList();
            var columns = kids.Count + element.Attributes().Count(a => !a.IsNamespaceDeclaration);
            return kids.Count >= 1 && columns <= 12 && kids.All(k => !k.HasElements && k.Value.Trim().Length > 0) && kids.Select(k => k.Name).Distinct().Count() == kids.Count
                && !element.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value));
        }

        private static void AppendRecordTable(StringBuilder sb, List<XElement> rows, int depth)
        {
            var indent = new string(' ', depth * 2);
            var attributeColumns = rows.SelectMany(r => r.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName)).Distinct().ToList();
            var columns = rows.SelectMany(r => r.Elements().Select(e => e.Name.LocalName)).Distinct().ToList();
            sb.Append('\n');
            var table = new List<IReadOnlyList<string>> { attributeColumns.Select(a => "@" + a).Concat(columns).ToList() };
            foreach (var row in rows)
                table.Add(attributeColumns.Select(a => row.Attributes().FirstOrDefault(x => !x.IsNamespaceDeclaration && x.Name.LocalName == a)?.Value ?? "")
                    .Concat(columns.Select(c => CellText(row.Elements().FirstOrDefault(e => e.Name.LocalName == c)))).ToList());
            foreach (var line in MarkdownText.Table(table.Select(r => (IReadOnlyList<string>)r.Select(c => MarkdownText.EscapeInline(OneLine(c))).ToList()).ToList()).TrimEnd('\n').Split('\n')) sb.Append(indent).Append(line).Append('\n');
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
