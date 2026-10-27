using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.XmlEditing
{
    // One row of the outline of an XML document: how deep it is (the root is 1), the element's name, a word that tells it from its siblings (an id, a
    // name), the line its start tag is on, and `More` when the row stands for elements that are not listed (their count), or Limit when the list stops.
    internal sealed record XmlOutlineEntry(int Level, string Name, string Hint, int Line, int More = 0, bool Limit = false);

    // New since the fork: the outline of an XML document, for the Outline list of the sidebar and the inspector and for the ticks of the document map.
    // What it lists (so that a person can find their way in a file of any size):
    //  - an element that has elements inside it, down to MaxDepth levels; an element with nothing but text is not listed (the fields of a record would
    //    bury the structure), except when the whole file is one flat record (a settings file: the root holds nothing but fields);
    //  - of the elements with the same name inside one parent, the first MaxSame, and one row "N more" for the rest;
    //  - at most MaxEntries rows altogether, then one row that says the list stops.
    // Plain .NET (no WinUI), so the tests in Caret.ConverterTests compile it as it is.
    internal static class XmlOutline
    {
        internal const int MaxEntries = 500;
        internal const int MaxDepth = 8;
        internal const int MaxSame = 3;
        private const int MaxHint = 40;

        // The attributes, then the child elements, whose value tells an element from its siblings (the first one found, in this order).
        private static readonly string[] HintAttributes = { "id", "name", "key", "code" };
        private static readonly string[] HintElements = { "id", "name", "title", "code" };

        public static List<XmlOutlineEntry> Build(XDocument document)
        {
            var entries = new List<XmlOutlineEntry>();
            var root = document?.Root;
            if (root == null) return entries;
            Add(entries, root, 1);
            Walk(entries, root, 2, flat: root.HasElements && root.Elements().All(c => !c.HasElements));
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
                entries.Add(new XmlOutlineEntry(1, "", null, 0, Limit: true));
            }
            return entries;
        }

        private static void Add(List<XmlOutlineEntry> entries, XElement element, int level, int more = 0) =>
            entries.Add(new XmlOutlineEntry(level, element.Name.LocalName, more > 0 ? null : HintOf(element), LineOf(element), more));

        private static void Walk(List<XmlOutlineEntry> entries, XElement parent, int level, bool flat = false)
        {
            if (level > MaxDepth || entries.Count > MaxEntries) return;
            var eligible = parent.Elements().Where(c => flat || c.HasElements).ToList();
            var total = eligible.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Count());
            var seen = new Dictionary<XName, int>();
            foreach (var child in eligible)
            {
                if (entries.Count > MaxEntries) return;
                seen.TryGetValue(child.Name, out var n);
                seen[child.Name] = ++n;
                if (n > MaxSame + 1) continue;
                if (n == MaxSame + 1)
                {
                    Add(entries, child, level, total[child.Name] - MaxSame);
                    continue;
                }
                Add(entries, child, level);
                if (child.HasElements) Walk(entries, child, level + 1);
            }
        }

        private static int LineOf(XElement element) => ((IXmlLineInfo)element).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : 0;

        private static string HintOf(XElement element)
        {
            foreach (var wanted in HintAttributes)
            {
                var attribute = element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && string.Equals(a.Name.LocalName, wanted, StringComparison.OrdinalIgnoreCase));
                var text = Short(attribute?.Value);
                if (text != null) return text;
            }
            foreach (var wanted in HintElements)
            {
                var child = element.Elements().FirstOrDefault(e => !e.HasElements && string.Equals(e.Name.LocalName, wanted, StringComparison.OrdinalIgnoreCase));
                var text = Short(child?.Value);
                if (text != null) return text;
            }
            return null;
        }

        // The value as one short line, or null when there is none or it is too long to tell anything at a glance.
        private static string Short(string value)
        {
            if (value == null) return null;
            var text = string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length == 0 || text.Length > MaxHint ? null : text;
        }
    }
}
