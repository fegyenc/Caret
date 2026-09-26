using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Typedown.WinUI.Services.Conversion
{
    // .docx → Markdown. Headings come from paragraph styles (Word stores built-in style names in
    // English — "heading 1" — whatever the UI language, so French and Spanish documents work the same)
    // or outline levels; lists from the numbering definitions; tables become pipe tables.
    internal static class WordConverter
    {
        private static readonly HashSet<string> MonospaceFonts = new(StringComparer.OrdinalIgnoreCase)
        {
            "Consolas", "Courier New", "Courier", "Cascadia Code", "Cascadia Mono", "Lucida Console", "Menlo", "Monaco", "Source Code Pro", "Fira Code",
        };

        public static string Convert(Stream stream, ConversionContext context)
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            var main = doc.MainDocumentPart;
            var body = main?.Document?.Body;
            if (body == null) return "";
            var state = new State(main, context);
            var blocks = new List<Block>();
            foreach (var element in BodyBlocks(body)) state.AddBlock(element, blocks);
            state.FlushCode(blocks);
            var sb = new StringBuilder();
            Block previous = null;
            foreach (var block in blocks)
            {
                if (previous != null) sb.Append(previous.IsListItem && block.IsListItem ? "\n" : "\n\n");
                sb.Append(block.Markdown);
                previous = block;
            }
            if (state.Footnotes.Count > 0)
            {
                sb.Append("\n\n");
                foreach (var (id, text) in state.Footnotes) sb.Append($"[^{id}]: {text}\n");
            }
            return sb.ToString();
        }

        // Body children in order, looking inside content controls (a whole document can sit in one).
        private static IEnumerable<OpenXmlElement> BodyBlocks(OpenXmlElement container)
        {
            foreach (var child in container.ChildElements)
            {
                if (child is W.SdtBlock sdt)
                {
                    var content = sdt.SdtContentBlock;
                    if (content != null) foreach (var inner in BodyBlocks(content)) yield return inner;
                }
                else if (child is W.CustomXmlBlock custom)
                {
                    foreach (var inner in BodyBlocks(custom)) yield return inner;
                }
                else yield return child;
            }
        }

        private sealed record Block(string Markdown, bool IsListItem, bool IsCode = false);

        private sealed class State
        {
            private readonly MainDocumentPart main;
            private readonly ConversionContext context;
            private readonly Dictionary<string, W.Style> styles;
            private readonly Dictionary<string, string> hyperlinks;
            private readonly List<string> codeLines = new();

            public List<(string Id, string Text)> Footnotes { get; } = new();

            public State(MainDocumentPart main, ConversionContext context)
            {
                this.main = main;
                this.context = context;
                styles = main.StyleDefinitionsPart?.Styles?.Elements<W.Style>()
                    .Where(s => s.StyleId?.Value != null)
                    .GroupBy(s => s.StyleId.Value).ToDictionary(g => g.Key, g => g.First())
                    ?? new Dictionary<string, W.Style>();
                hyperlinks = main.HyperlinkRelationships.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Uri.ToString());
            }

            public void AddBlock(OpenXmlElement element, List<Block> blocks)
            {
                if (element is W.Paragraph p)
                {
                    if (IsCodeParagraph(p))
                    {
                        codeLines.Add(string.Concat(p.Descendants<W.Text>().Select(t => t.Text)));
                        return;
                    }
                    FlushCode(blocks);
                    var block = Paragraph(p);
                    if (block != null) blocks.Add(block);
                }
                else if (element is W.Table table)
                {
                    FlushCode(blocks);
                    var md = Table(table);
                    if (md.Length > 0) blocks.Add(new Block(md, false));
                }
            }

            public void FlushCode(List<Block> blocks)
            {
                if (codeLines.Count == 0) return;
                blocks.Add(new Block(MarkdownText.CodeBlock(string.Join("\n", codeLines)), false, true));
                codeLines.Clear();
            }

            private Block Paragraph(W.Paragraph p)
            {
                var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var styleName = StyleName(styleId);
                // Tables of contents are page-number lists Word generates; they only add noise.
                if (styleName.StartsWith("toc ", StringComparison.OrdinalIgnoreCase) || styleName.Equals("toc heading", StringComparison.OrdinalIgnoreCase))
                    return null;

                var heading = HeadingLevel(p, styleId);
                var inline = new InlineBuilder();
                AddInlines(p, inline, "  \n");
                if (inline.IsEmpty) return null;

                if (heading > 0)
                {
                    var text = inline.Build(plainHeading: true).Replace("  \n", " ").Trim();
                    return new Block(new string('#', heading) + " " + text, false);
                }

                var numbering = Numbering(p, styleId);
                if (numbering is (int numId, int numberingLevel))
                {
                    // "List Bullet 2", "List Number 3"… put an item one level deeper through the style
                    // itself, while their numbering still says level 0.
                    var styleLevel = System.Text.RegularExpressions.Regex.Match(styleName, @"^list (bullet|number|continue) (\d)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var level = Math.Max(numberingLevel, styleLevel.Success ? int.Parse(styleLevel.Groups[2].Value) - 1 : 0);
                    var marker = IsOrdered(numId, numberingLevel) ? "1." : "-";
                    var indent = new string(' ', 4 * Math.Min(level, 8));
                    var text = inline.Build().Trim().Replace("  \n", "  \n" + indent + "  ");
                    return new Block(indent + marker + " " + text, true);
                }

                var content = MarkdownText.EscapeBlockStart(inline.Build().Trim());
                if (styleName.Equals("quote", StringComparison.OrdinalIgnoreCase) || styleName.Equals("intense quote", StringComparison.OrdinalIgnoreCase))
                    content = "> " + content.Replace("\n", "\n> ");
                return new Block(content, false);
            }

            private void AddInlines(OpenXmlElement container, InlineBuilder inline, string lineBreak, string link = null)
            {
                foreach (var child in container.ChildElements)
                {
                    switch (child)
                    {
                        case W.Run run:
                            AddRun(run, inline, lineBreak, link);
                            break;
                        case W.Hyperlink h:
                            var target = h.Id?.Value != null && hyperlinks.TryGetValue(h.Id.Value, out var uri) ? uri : null;
                            AddInlines(h, inline, lineBreak, target ?? link);
                            break;
                        case W.DeletedRun:
                        case W.ParagraphProperties:
                        case W.MoveFromRun:
                            break;
                        case W.SdtRun sdt when sdt.SdtContentRun != null:
                            AddInlines(sdt.SdtContentRun, inline, lineBreak, link);
                            break;
                        default:
                            if (child.HasChildren) AddInlines(child, inline, lineBreak, link);
                            break;
                    }
                }
            }

            private void AddRun(W.Run run, InlineBuilder inline, string lineBreak, string link)
            {
                var rp = run.RunProperties;
                var runStyle = rp?.RunStyle?.Val?.Value ?? "";
                var bold = IsOn(rp?.Bold) || runStyle.Contains("Strong", StringComparison.OrdinalIgnoreCase);
                var italic = IsOn(rp?.Italic) || runStyle.Contains("Emphasis", StringComparison.OrdinalIgnoreCase);
                var strike = IsOn(rp?.Strike) || IsOn(rp?.DoubleStrike);
                var code = MonospaceFonts.Contains(rp?.RunFonts?.Ascii?.Value ?? "") || runStyle.Contains("Code", StringComparison.OrdinalIgnoreCase);
                foreach (var part in run.ChildElements)
                {
                    switch (part)
                    {
                        case W.Text t:
                            inline.Add(t.Text, bold, italic, strike, code, link);
                            break;
                        case W.TabChar:
                            inline.Add(" ", bold, italic, strike, code, link);
                            break;
                        case W.NoBreakHyphen:
                            inline.Add("-", bold, italic, strike, code, link);
                            break;
                        case W.Break br when br.Type?.Value != W.BreakValues.Page && br.Type?.Value != W.BreakValues.Column:
                            inline.AddRaw(lineBreak);
                            break;
                        case W.CarriageReturn:
                            inline.AddRaw(lineBreak);
                            break;
                        case W.Drawing drawing:
                            inline.AddRaw(Image(drawing));
                            break;
                        case W.FootnoteReference fn when fn.Id?.Value != null:
                            inline.AddRaw(Footnote(fn.Id.Value.ToString()));
                            break;
                    }
                }
            }

            private static bool IsOn(W.OnOffType value) => value != null && (value.Val == null || value.Val.Value);

            private string Image(W.Drawing drawing)
            {
                var embed = drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
                if (embed == null) return "";
                if (main.GetPartById(embed) is not ImagePart image) return "";
                var props = drawing.Descendants<DW.DocProperties>().FirstOrDefault();
                var alt = props?.Description?.Value ?? props?.Title?.Value ?? "";
                using var data = image.GetStream();
                return context.SaveImage(data, image.ContentType, alt);
            }

            private string Footnote(string id)
            {
                var note = main.FootnotesPart?.Footnotes?.Elements<W.Footnote>().FirstOrDefault(f => f.Id?.Value.ToString() == id);
                if (note == null) return "";
                var inline = new InlineBuilder();
                foreach (var p in note.Elements<W.Paragraph>()) { AddInlines(p, inline, " "); inline.AddRaw(" "); }
                var text = inline.Build().Trim();
                if (text.Length == 0) return "";
                if (!Footnotes.Any(f => f.Id == id)) Footnotes.Add((id, text));
                return $"[^{id}]";
            }

            private string Table(W.Table table)
            {
                var rows = new List<IReadOnlyList<string>>();
                foreach (var row in table.Elements<W.TableRow>())
                {
                    var cells = new List<string>();
                    // The header row is bold in Markdown already; "**Region**" there only costs tokens.
                    var isHeader = rows.Count == 0;
                    foreach (var cell in row.Elements<W.TableCell>())
                    {
                        var props = cell.TableCellProperties;
                        var continued = props?.VerticalMerge != null && (props.VerticalMerge.Val == null || props.VerticalMerge.Val.Value == W.MergedCellValues.Continue);
                        cells.Add(continued ? "" : CellText(cell, isHeader));
                        var span = props?.GridSpan?.Val?.Value ?? 1;
                        for (var i = 1; i < span; i++) cells.Add("");
                    }
                    rows.Add(cells);
                }
                // Drop rows that are entirely empty (spacer rows).
                rows = rows.Where(r => r.Any(c => c.Length > 0)).ToList();
                return MarkdownText.Table(rows);
            }

            private string CellText(W.TableCell cell, bool isHeader)
            {
                var parts = new List<string>();
                foreach (var p in cell.Descendants<W.Paragraph>())
                {
                    var inline = new InlineBuilder();
                    AddInlines(p, inline, "<br>");
                    var text = inline.Build(plainHeading: isHeader).Trim();
                    if (text.Length == 0) continue;
                    var numbering = Numbering(p, p.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
                    parts.Add(numbering != null ? "• " + text : text);
                }
                return string.Join("<br>", parts);
            }

            private bool IsCodeParagraph(W.Paragraph p)
            {
                var name = StyleName(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
                if (name.Contains("code", StringComparison.OrdinalIgnoreCase) || name.Contains("preformatted", StringComparison.OrdinalIgnoreCase)) return true;
                var runs = p.Descendants<W.Run>().Where(r => r.Descendants<W.Text>().Any(t => t.Text.Trim().Length > 0)).ToList();
                return runs.Count > 0 && runs.All(r => MonospaceFonts.Contains(r.RunProperties?.RunFonts?.Ascii?.Value ?? ""));
            }

            private string StyleName(string styleId)
            {
                if (styleId == null || !styles.TryGetValue(styleId, out var style)) return "";
                return style.StyleName?.Val?.Value ?? styleId;
            }

            // When the document has a Title, it is the one "#" and Heading 1 becomes "##".
            private int? headingOffset;

            private int HeadingOffset(W.Paragraph anyParagraph)
            {
                headingOffset ??= anyParagraph.Ancestors<W.Body>().FirstOrDefault()?.Descendants<W.ParagraphStyleId>()
                    .Any(s => StyleName(s.Val?.Value).Equals("title", StringComparison.OrdinalIgnoreCase)) == true ? 1 : 0;
                return headingOffset.Value;
            }

            private int HeadingLevel(W.Paragraph p, string styleId)
            {
                var outline = p.ParagraphProperties?.OutlineLevel?.Val?.Value;
                if (outline is int o && o < 6) return Math.Min(o + 1 + HeadingOffset(p), 6);
                for (var id = styleId; id != null && styles.TryGetValue(id, out var style); id = style.BasedOn?.Val?.Value)
                {
                    var name = (style.StyleName?.Val?.Value ?? "").ToLowerInvariant();
                    if (name == "title") return 1;
                    if (name == "subtitle") return 2;
                    if (name.StartsWith("heading ") && int.TryParse(name.Substring(8), out var n) && n >= 1)
                        return Math.Min(n + HeadingOffset(p), 6);
                    var styleOutline = style.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
                    if (styleOutline is int so && so < 6) return Math.Min(so + 1 + HeadingOffset(p), 6);
                    if (id == style.BasedOn?.Val?.Value) break;
                }
                return 0;
            }

            private (int NumId, int Level)? Numbering(W.Paragraph p, string styleId)
            {
                var numPr = p.ParagraphProperties?.NumberingProperties;
                var level = numPr?.NumberingLevelReference?.Val?.Value;
                var numId = numPr?.NumberingId?.Val?.Value;
                for (var id = styleId; numId == null && id != null && styles.TryGetValue(id, out var style); id = style.BasedOn?.Val?.Value)
                {
                    var styleNum = style.StyleParagraphProperties?.NumberingProperties;
                    numId = styleNum?.NumberingId?.Val?.Value;
                    level ??= styleNum?.NumberingLevelReference?.Val?.Value;
                    if (id == style.BasedOn?.Val?.Value) break;
                }
                if (numId is not int n || n == 0) return null;
                return (n, level ?? 0);
            }

            private bool IsOrdered(int numId, int level)
            {
                var numbering = main.NumberingDefinitionsPart?.Numbering;
                var instance = numbering?.Elements<W.NumberingInstance>().FirstOrDefault(i => i.NumberID?.Value == numId);
                var abstractId = instance?.AbstractNumId?.Val?.Value;
                var abstractNum = numbering?.Elements<W.AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
                var lvl = abstractNum?.Elements<W.Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
                var format = lvl?.NumberingFormat?.Val?.Value;
                return format != null && format != W.NumberFormatValues.Bullet && format != W.NumberFormatValues.None;
            }
        }
    }
}
