using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // What a person wants to know about an XML file before reading it: how big it is and what it is made of. Written at the top of the generic
    // view (not for a format pack, which has its own summary): the number of elements, how deep, and the elements that repeat most (a file
    // with 20 "task" and 154 "instruction" says what it is at a glance); and, when the numbers of repeated elements (ids such as TW.1, TW.2...)
    // have a gap or a repeat, a note with them. Rules only: it says what is in the file, it does not say what is right.
    internal static class XmlStructure
    {
        // Smaller files are read in full at a glance; the overview is for the others.
        private const int OverviewFrom = 30;
        private const int TopElements = 10;
        private const int MaxNotes = 10;

        public static void Append(StringBuilder sb, XElement root, XmlLabels labels)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            var notes = new List<string>();
            var total = 0;
            var deepest = 0;
            // a stack, not recursion: a file can nest far deeper than the call stack allows
            var stack = new Stack<(XElement Element, int Depth)>();
            stack.Push((root, 1));
            while (stack.Count > 0)
            {
                var (element, depth) = stack.Pop();
                total++;
                deepest = Math.Max(deepest, depth);
                var name = element.Name.LocalName;
                if (counts.TryGetValue(name, out var seen)) counts[name] = seen + 1;
                else { counts[name] = 1; order.Add(name); }
                foreach (var group in element.Elements().GroupBy(c => c.Name.LocalName).Where(g => g.Count() >= 3))
                    if (notes.Count < MaxNotes) Numbering(group, name: group.Key, notes, labels);
                // pushed in reverse so that the children are popped in document order (the order of first appearance and the notes follow the file)
                foreach (var child in element.Elements().Reverse()) stack.Push((child, depth + 1));
            }

            if (total >= OverviewFrom)
            {
                sb.Append("**").Append(labels.Structure).Append("**\n\n");
                sb.Append(string.Format(labels.StructureLine, total.ToString(CultureInfo.InvariantCulture), deepest.ToString(CultureInfo.InvariantCulture))).Append("\n\n");
                var top = order.Where(n => counts[n] >= 2).OrderByDescending(n => counts[n]).ThenBy(n => order.IndexOf(n)).Take(TopElements).ToList();
                if (top.Count > 0)
                {
                    var rows = new List<IReadOnlyList<string>> { new[] { labels.Element, labels.Count } };
                    rows.AddRange(top.Select(n => (IReadOnlyList<string>)new[] { MarkdownText.EscapeInline(n), counts[n].ToString(CultureInfo.InvariantCulture) }));
                    sb.Append(MarkdownText.Table(rows)).Append("\n\n");
                }
            }
            if (notes.Count > 0)
            {
                sb.Append("**").Append(labels.NumberingTitle).Append("**\n\n");
                foreach (var note in notes) sb.Append("- ").Append(note).Append('\n');
                sb.Append('\n');
            }
        }

        // The ids (or order, number...) of siblings with the same name, when they are a prefix and a number (TW.1, TW.2, TW.3...): a number that
        // is there twice, and a gap in the run. Only when all of them are like that, share the prefix and lie close together (identifiers that
        // are not a count, such as hashes, say nothing).
        private static void Numbering(IEnumerable<XElement> group, string name, List<string> notes, XmlLabels labels)
        {
            var members = group.ToList();
            foreach (var key in new[] { "id", "order", "number", "num", "n" })
            {
                var values = members.Select(e => e.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && string.Equals(a.Name.LocalName, key, StringComparison.OrdinalIgnoreCase))?.Value.Trim()).ToList();
                if (values.Any(string.IsNullOrEmpty)) continue;
                var parsed = values.Select(v => Regex.Match(v, @"^(.*?)([0-9]{1,9})$")).ToList();
                if (parsed.Any(m => !m.Success) || parsed.Select(m => m.Groups[1].Value).Distinct().Count() != 1) continue;
                // the numbers are compared, the ids are said as they are in the file ("T.02", not "T.2")
                var entries = values.Select((v, i) => (Text: v, Number: int.Parse(parsed[i].Groups[2].Value, CultureInfo.InvariantCulture))).ToList();
                foreach (var repeated in entries.GroupBy(e => e.Text, StringComparer.Ordinal).Where(g => g.Count() > 1).OrderBy(g => g.First().Number))
                    if (notes.Count < MaxNotes) notes.Add(string.Format(labels.NumberingRepeat, Code(name), key, Code(repeated.Key), repeated.Count().ToString(CultureInfo.InvariantCulture)));
                var distinct = entries.GroupBy(e => e.Number).OrderBy(g => g.Key).Select(g => (Number: g.Key, Text: g.First().Text)).ToList();
                if (distinct.Count >= 3 && distinct[distinct.Count - 1].Number - distinct[0].Number <= 2 * distinct.Count)
                    for (var k = 1; k < distinct.Count; k++)
                        if (distinct[k].Number - distinct[k - 1].Number > 1 && notes.Count < MaxNotes)
                            notes.Add(string.Format(labels.NumberingGap, Code(name), key, Code(distinct[k - 1].Text), Code(distinct[k].Text)));
                return;
            }
        }

        private static string Code(string text) => MarkdownText.CodeSpan(XmlConverter.OneLine(text));
    }
}
