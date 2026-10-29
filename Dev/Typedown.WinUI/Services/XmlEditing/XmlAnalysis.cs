using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Typedown.WinUI.Services.Conversion;

namespace Typedown.WinUI.Services.XmlEditing
{
    // What the XML editor tells the person while they type: whether the text is well-formed XML (and where it is not), whether the file carries a
    // digital signature (any change makes it invalid), and the readable view of the text (the same view as File > Convert, so what the person edits
    // and what they read are one thing). Plain .NET, offline, no AI. A DOCTYPE is skipped, never followed, and nothing external is loaded.
    internal sealed class XmlAnalysis
    {
        // A text bigger than this is checked for well-formedness only: the readable view of a very large file would be unreadable anyway and slow
        // to make on every pause in typing.
        public const int MaxPreviewCharacters = 4_000_000;

        private const string DsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
        private static readonly Regex ErrorPlace = new(@"\s*Line \d+, position \d+\.?\s*$", RegexOptions.CultureInvariant);

        public bool WellFormed { get; private init; }
        // where it is not well-formed, with the message of the parser (English)
        public string Error { get; private init; }
        public int Line { get; private init; }
        public int Position { get; private init; }
        public bool Signed { get; private init; }
        // several elements side by side with no root around them (XmlFragment.cs): well-formed, but not a document; Elements is how many
        public bool Fragment { get; private init; }
        public int Elements { get; private init; }
        public int Lines { get; private init; }
        // the name of the root element
        public string Root { get; private init; }
        // the readable view as Markdown; null when the text is not well-formed, is too big, or has no view
        public string Preview { get; private init; }
        public bool TooBigForPreview { get; private init; }
        // the structure of the document, for the Outline list and the document map (empty when the text is not well-formed or is too big)
        public IReadOnlyList<XmlOutlineEntry> Outline { get; private init; } = Array.Empty<XmlOutlineEntry>();

        public static XmlAnalysis Of(string text, string language = "en", string fileName = null)
        {
            text ??= "";
            var lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
            var big = text.Length > MaxPreviewCharacters;
            XDocument document = null;
            try
            {
                using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                    IgnoreComments = false,
                });
                if (big)
                    while (reader.Read()) { }
                else
                    document = XDocument.Load(reader, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                // several elements side by side are a fragment, not an error (a text too big for the readable view is not looked at again)
                var fragment = big ? null : XmlFragment.Read(text, ex);
                if (fragment is { IsFragment: true }) return OfFragment(fragment, text, lines, language, fileName);
                var shown = XmlFragment.Better(fragment, ex);
                return new XmlAnalysis
                {
                    WellFormed = false,
                    Error = ErrorPlace.Replace(shown.Message, ""),
                    Line = shown.LineNumber,
                    Position = shown.LinePosition,
                    Signed = LooksSigned(text),
                    Lines = lines,
                    TooBigForPreview = big,
                };
            }
            var root = document?.Root;
            string preview = null;
            if (document != null && root != null)
            {
                try
                {
                    var context = new ConversionContext(new ConversionOptions { Language = language }) { SourceName = fileName };
                    preview = MarkdownText.Tidy(XmlConverter.ConvertText(document, context));
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException)
                {
                    preview = null;
                }
            }
            return new XmlAnalysis
            {
                WellFormed = true,
                Signed = document != null ? document.Descendants().Any(e => e.Name.LocalName == "Signature" && e.Name.NamespaceName == DsigNamespace) : LooksSigned(text),
                Lines = lines,
                Root = root?.Name.LocalName,
                Preview = preview,
                TooBigForPreview = big,
                Outline = XmlOutline.Build(document),
            };
        }

        // A fragment (XmlFragment.cs): each element is read like a document of its own, and the readable views and the outlines follow one another.
        private static XmlAnalysis OfFragment(XmlFragment fragment, string text, int lines, string language, string fileName)
        {
            var pieces = fragment.TopElements.ToList();
            foreach (var piece in pieces) piece.Remove();
            var documents = pieces.Select(p => new XDocument(p)).ToList();
            string preview = null;
            try
            {
                // the name of the file is said once, with the first element, not again with each
                var first = new ConversionContext(new ConversionOptions { Language = language }) { SourceName = fileName };
                var others = new ConversionContext(new ConversionOptions { Language = language });
                preview = MarkdownText.Tidy(string.Join("\n\n---\n\n", documents.Select((d, i) => XmlConverter.ConvertText(d, i == 0 ? first : others).Trim())));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                preview = null;
            }
            var outline = documents.SelectMany(XmlOutline.Build).ToList();
            if (outline.Count > XmlOutline.MaxEntries)
            {
                outline.RemoveRange(XmlOutline.MaxEntries, outline.Count - XmlOutline.MaxEntries);
                outline.Add(new XmlOutlineEntry(1, "", null, 0, Limit: true));
            }
            return new XmlAnalysis
            {
                WellFormed = true,
                Fragment = true,
                Elements = fragment.Elements,
                Signed = LooksSigned(text),
                Lines = lines,
                Root = pieces[0].Name.LocalName,
                Preview = preview,
                Outline = outline,
            };
        }

        // A quick look, without reading the text as a tree (before a save): the namespace of the signature and a Signature tag in it.
        public static bool LooksSigned(string text) =>
            text.Contains(DsigNamespace, StringComparison.Ordinal) && Regex.IsMatch(text, @"<(?:[\w.\-]+:)?Signature[\s>/]", RegexOptions.CultureInvariant);
    }
}
