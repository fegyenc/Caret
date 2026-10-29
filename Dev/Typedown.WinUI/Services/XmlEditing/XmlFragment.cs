using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.XmlEditing
{
    // New since the fork: XML fragment mode. A piece cut out of a document (the lines of a log, a message from a chat, two elements copied from a
    // file) is several elements side by side with no root around them, which is not a document but is still XML a person wants to check, format and
    // read. A text that is not a document is read as a fragment when every top-level node is an element, a comment or a processing instruction and there
    // are at least two elements (one root is a document; text outside an element is not XML). A text with a declaration or a DOCTYPE is a document,
    // never a fragment. Plain .NET, offline.
    internal sealed class XmlFragment
    {
        private const string WrapperName = "fragment";
        private static readonly Regex Declaration = new(@"^﻿?\s*<\?xml[\s?]", RegexOptions.CultureInvariant);
        private static readonly Regex Doctype = new(@"<!DOCTYPE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // The elements of the fragment side by side, with the comments and processing instructions between them (in the order of the text).
        // Their line numbers are those of the text (when read with LoadOptions.SetLineInfo).
        public XElement Nodes { get; private init; }
        public int Elements { get; private init; }
        public bool IsFragment => Nodes != null;
        public IEnumerable<XElement> TopElements => Nodes.Elements();

        // Why the text is not a fragment either, when it breaks later than the document reading did: the error to tell the person (a text such as
        // `<a/><b>` has a multiple-root problem at `<b>`, but the real problem is that `<b>` is never closed).
        public XmlException Error { get; private init; }

        // Null when the text is neither a fragment nor broken further on than the document reading found; otherwise IsFragment says which of the two it is.
        public static XmlFragment Read(string text, XmlException documentError, LoadOptions options = LoadOptions.SetLineInfo)
        {
            if (string.IsNullOrWhiteSpace(text) || Declaration.IsMatch(text) || Doctype.IsMatch(text)) return null;
            var top = 0;
            var words = false;
            try
            {
                using var reader = XmlReader.Create(new StringReader(text), Settings(ConformanceLevel.Fragment));
                while (reader.Read())
                {
                    if (reader.Depth != 0) continue;
                    if (reader.NodeType == XmlNodeType.Element) top++;
                    else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.EntityReference) words = true;
                }
            }
            catch (XmlException ex)
            {
                return IsLater(ex, documentError) ? new XmlFragment { Error = ex } : null;
            }
            if (top < 2 || words) return null;
            try
            {
                // the same text inside one element, on the same line, so that every line number stays the one of the text
                using var wrapped = XmlReader.Create(new StringReader("<" + WrapperName + ">" + text + "</" + WrapperName + ">"), Settings(ConformanceLevel.Document));
                return new XmlFragment { Nodes = XDocument.Load(wrapped, options).Root, Elements = top };
            }
            catch (XmlException)
            {
                return null;
            }
        }

        // The error to show: the one of the fragment when the text is read further with it than as a document, else the document's own.
        public static XmlException Better(XmlFragment read, XmlException documentError) => read?.Error ?? documentError;

        private static bool IsLater(XmlException ex, XmlException than) =>
            than == null || ex.LineNumber > than.LineNumber || (ex.LineNumber == than.LineNumber && ex.LinePosition > than.LinePosition);

        private static XmlReaderSettings Settings(ConformanceLevel level) => new()
        {
            ConformanceLevel = level,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            IgnoreComments = false,
        };

    }
}
