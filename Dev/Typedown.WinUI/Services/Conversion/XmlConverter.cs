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
                AppendOutline(sb, root, labels, skipEmpty: true);
                return sb.ToString();
            }
            sb.Append("# ").Append(MarkdownText.EscapeInline(root.Name.LocalName)).Append("\n\n");
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(labels.File).Append(": ").Append(MarkdownText.EscapeInline(context.SourceName)).Append('\n');
            if (!string.IsNullOrEmpty(root.Name.NamespaceName)) sb.Append("> ").Append(labels.Namespace).Append(": `").Append(root.Name.NamespaceName).Append("`\n");
            sb.Append('\n');
            AppendOutline(sb, root, labels, skipEmpty: true);
            return sb.ToString();
        }

        // The generic view: the fields of the file in order, as nested lists. A group of equal siblings with simple contents (rows of data)
        // becomes a table, so a list of 300 lines reads like one. Digital signatures are named, not printed (they are long and unreadable).
        internal static void AppendOutline(StringBuilder sb, XElement root, XmlLabels labels, bool skipEmpty)
        {
            WriteElement(sb, root, 0, labels, skipEmpty, 0);
        }

        private static void WriteElement(StringBuilder sb, XElement element, int depth, XmlLabels labels, bool skipEmpty, int nesting)
        {
            var indent = new string(' ', depth * 2);
            var name = element.Name.LocalName;
            if (name == "Signature" && element.Name.NamespaceName.Contains("xmldsig"))
            {
                sb.Append(indent).Append("- **").Append(name).Append("**: ").Append(labels.SignaturePresent).Append('\n');
                return;
            }
            var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
            var children = element.Elements().ToList();
            var text = children.Count == 0 ? element.Value.Trim() : "";
            if (skipEmpty && children.Count == 0 && text.Length == 0 && attributes.Count == 0) return;

            sb.Append(indent).Append("- **").Append(MarkdownText.EscapeInline(name)).Append("**");
            if (attributes.Count > 0)
                sb.Append(" (").Append(string.Join(", ", attributes.Select(a => MarkdownText.EscapeInline(a.Name.LocalName) + "=" + MarkdownText.EscapeInline(a.Value)))).Append(')');
            XElement stored = null;
            if (text.Length > 0)
            {
                // XML stored as text (a CDATA part holding a document) is read and its fields follow, indented, so that nothing in it is hidden;
                // when it cannot be read it is said, not dumped on one line
                if (text.StartsWith("<") && nesting < 3) { try { stored = Parse(text).Root; } catch (Exception ex) when (ex is XmlException or InvalidOperationException) { } }
                if (stored != null) sb.Append(": ").Append(labels.EmbeddedXml);
                else if (text.Length > 400 && text.StartsWith("<")) sb.Append(": ").Append(labels.EmbeddedXml).Append(" (").Append(text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" ").Append(labels.Characters).Append(')');
                else sb.Append(": ").Append(MarkdownText.EscapeInline(OneLine(text)));
            }
            sb.Append('\n');
            if (stored != null) WriteElement(sb, stored, depth + 1, labels, skipEmpty, nesting + 1);

            // equal siblings with only simple children: one table
            var groups = children.GroupBy(c => c.Name).ToList();
            var written = new HashSet<XElement>();
            foreach (var group in groups)
            {
                var rows = group.ToList();
                if (rows.Count >= 3 && rows.All(IsRecord))
                {
                    AppendRecordTable(sb, rows, depth + 1, labels);
                    foreach (var r in rows) written.Add(r);
                }
            }
            foreach (var child in children)
                if (!written.Contains(child)) WriteElement(sb, child, depth + 1, labels, skipEmpty, nesting);
        }

        // A row of data: element children that all have text and no children of their own, no more than 12 columns.
        private static bool IsRecord(XElement element)
        {
            var kids = element.Elements().ToList();
            return kids.Count is >= 1 and <= 12 && kids.All(k => !k.HasElements && k.Value.Trim().Length > 0) && kids.Select(k => k.Name).Distinct().Count() == kids.Count;
        }

        private static void AppendRecordTable(StringBuilder sb, List<XElement> rows, int depth, XmlLabels labels)
        {
            var indent = new string(' ', depth * 2);
            var columns = rows.SelectMany(r => r.Elements().Select(e => e.Name.LocalName)).Distinct().ToList();
            sb.Append('\n');
            var table = new List<IReadOnlyList<string>> { columns.ToList() };
            foreach (var row in rows)
                table.Add(columns.Select(c => CellText(row.Elements().FirstOrDefault(e => e.Name.LocalName == c))).ToList());
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
