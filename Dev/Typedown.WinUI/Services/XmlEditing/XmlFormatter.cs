using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.XmlEditing
{
    internal enum XmlFormatProblem { Empty, Doctype, NotWellFormed, NoRoot, TooDeep }

    internal sealed class XmlFormatException : Exception
    {
        public XmlFormatException(XmlFormatProblem problem, string message, int line = 0, int position = 0) : base(message)
        {
            Problem = problem;
            Line = line;
            Position = position;
        }

        public XmlFormatProblem Problem { get; }
        public int Line { get; }
        public int Position { get; }
    }

    // Edit > Format XML: puts each element on its own line, indented by nesting, the way a person writes it. Only the white space between elements
    // changes: text, attribute values, comments, processing instructions and CDATA stay as they are, an element with text of its own (mixed content:
    // a paragraph with a bold word in it) and an element marked xml:space="preserve" are not touched, and the declaration is kept as written. It is
    // refused for a text that is not well-formed (there is nothing to format) and for one with a DOCTYPE (it is not read here, so it could be lost).
    //
    // What the writer cannot keep: the quotes of an attribute are written as double quotes, an empty element written <a/> as <a />, and a character
    // reference as the character itself. None of these changes what the XML says.
    internal static class XmlFormatter
    {
        private static readonly Regex Doctype = new(@"<!DOCTYPE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Declaration = new(@"^﻿?\s*(<\?xml\s[^>]*\?>)", RegexOptions.CultureInvariant);

        // Deeper than any real file: the recursion below stays far from the end of the stack.
        private const int MaxDepth = 200;

        public static string Format(string text, string indent = "  ")
        {
            if (string.IsNullOrWhiteSpace(text)) throw new XmlFormatException(XmlFormatProblem.Empty, "There is nothing to format.");
            if (Doctype.IsMatch(text)) throw new XmlFormatException(XmlFormatProblem.Doctype, "A file with a DOCTYPE is not formatted here (it would be lost).");
            XContainer document;
            XmlFragment fragment = null;
            try
            {
                using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                });
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException ex)
            {
                // several elements side by side (a fragment) are formatted one after the other, each on its own lines
                fragment = XmlFragment.Read(text, ex, LoadOptions.PreserveWhitespace);
                if (fragment is not { IsFragment: true })
                {
                    var shown = XmlFragment.Better(fragment, ex);
                    throw new XmlFormatException(XmlFormatProblem.NotWellFormed, $"The text is not well-formed XML (line {shown.LineNumber}, position {shown.LinePosition}): fix it first.", shown.LineNumber, shown.LinePosition);
                }
                document = fragment.Nodes;
            }
            var tops = document.Elements().ToList();
            if (tops.Count == 0) throw new XmlFormatException(XmlFormatProblem.NoRoot, "The XML has no root element.");
            foreach (var top in tops) Reindent(top, 0, indent);

            var sb = new StringBuilder();
            var declaration = Declaration.Match(text);
            if (declaration.Success) sb.Append(declaration.Groups[1].Value).Append('\n');
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                ConformanceLevel = fragment == null ? ConformanceLevel.Document : ConformanceLevel.Fragment,
            }))
            {
                var first = true;
                // (the white space between the top-level nodes is written again below, one line end each)
                foreach (var node in document.Nodes().Where(n => n is not XText))
                {
                    if (!first) writer.WriteWhitespace("\n");
                    first = false;
                    node.WriteTo(writer);
                }
            }
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Reindent(XElement element, int level, string indent)
        {
            if (level > MaxDepth) throw new XmlFormatException(XmlFormatProblem.TooDeep, "The XML is nested too deeply to be formatted.");
            if (element.Attribute(XNamespace.Xml + "space")?.Value == "preserve") return;
            var nodes = element.Nodes().ToList();
            // text of its own (CDATA is text too): mixed content, left as it is with everything inside it
            if (nodes.OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) return;
            if (!nodes.Any(n => n is XElement or XComment or XProcessingInstruction)) return;

            foreach (var blank in nodes.OfType<XText>().Where(t => t is not XCData).ToList()) blank.Remove();
            var children = element.Nodes().ToList();
            foreach (var child in children)
            {
                child.AddBeforeSelf(new XText("\n" + Repeat(indent, level + 1)));
                if (child is XElement childElement) Reindent(childElement, level + 1, indent);
            }
            element.Add(new XText("\n" + Repeat(indent, level)));
        }

        private static string Repeat(string indent, int count) => count <= 0 ? "" : string.Concat(Enumerable.Repeat(indent, count));
    }
}
