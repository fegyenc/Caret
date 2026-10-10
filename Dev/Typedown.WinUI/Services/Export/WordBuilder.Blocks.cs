using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Typedown.WinUI.Services.Export
{
    internal sealed partial class WordBuilder
    {
        // The properties of a paragraph in the order the schema wants them.
        private W.ParagraphProperties Properties(Ctx ctx, string style, string spaceAfter = null)
        {
            var props = new W.ParagraphProperties();
            var marker = pendingMarker;
            pendingMarker = null;
            style ??= ctx.ListLevel >= 0 ? WordStyles.ListParagraph : ctx.Quote > 0 ? WordStyles.Quote : null;
            if (style != null) props.Append(new W.ParagraphStyleId { Val = style });
            if (marker is (int numId, int level))
                props.Append(new W.NumberingProperties(new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = numId }));
            var heading = style != null && style.StartsWith("Heading", StringComparison.Ordinal);
            if (ctx.InTable) props.Append(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
            else if ((gapBefore && !heading) || spaceAfter != null)
            {
                var spacing = new W.SpacingBetweenLines();
                if (gapBefore && !heading) spacing.Before = "160";
                if (spaceAfter != null) spacing.After = spaceAfter;
                props.Append(spacing);
            }
            gapBefore = false;
            // A paragraph that goes on under a list marker lines up with the text after it; a deeper quote moves in.
            if (ctx.ListLevel >= 0 && marker == null) props.Append(new W.Indentation { Left = (WordStyles.ListIndent * (Math.Min(ctx.ListLevel, 8) + 1)).ToString() });
            else if (ctx.ListLevel < 0 && ctx.Quote > 1) props.Append(new W.Indentation { Left = (WordStyles.ListIndent * ctx.Quote).ToString() });
            if (ctx.Align != null) props.Append(new W.Justification { Val = ctx.Align.Value });
            return props;
        }

        private void AddParagraph(ContainerInline inline, Ctx ctx, string style)
        {
            var paragraph = new W.Paragraph(Properties(ctx, style));
            if (inline != null) AppendInlines(paragraph, inline, new Fmt { Bold = ctx.Bold });
            target.Append(paragraph);
        }

        private void RenderList(ListBlock list, Ctx ctx)
        {
            var level = Math.Min(ctx.ListLevel + 1, 8);
            var ordered = 0;
            if (list.IsOrdered)
            {
                ordered = nextNumId++;
                var start = int.TryParse(list.OrderedStart, out var n) && n >= 0 ? n : 1;
                numbering.Append(WordStyles.NumberedInstance(ordered, level, start));
            }
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var numId = list.IsOrdered ? ordered : IsTask(item) ? WordStyles.TaskNum : WordStyles.BulletNum;
                var itemCtx = ctx with { ListLevel = level };
                var first = true;
                foreach (var child in item)
                {
                    if (first) pendingMarker = (numId, level);
                    RenderBlock(child, itemCtx);
                    pendingMarker = null;
                    first = false;
                }
                if (first) target.Append(new W.Paragraph(new W.ParagraphProperties(
                    new W.ParagraphStyleId { Val = WordStyles.ListParagraph },
                    new W.NumberingProperties(new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = numId }))));
            }
            if (ctx.ListLevel < 0) gapBefore = true; // the paragraph after a list does not sit on its last item
        }

        private static bool IsTask(ListItemBlock item) =>
            item.FirstOrDefault() is ParagraphBlock { Inline: { } inline } && inline.FirstChild is TaskList;

        private void RenderCode(CodeBlock code, Ctx ctx)
        {
            var lines = code.Lines.ToString().Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            for (var i = 0; i < lines.Count; i++)
            {
                var props = Properties(ctx, WordStyles.Code, i == lines.Count - 1 ? "160" : null);
                var paragraph = new W.Paragraph(props);
                AppendCodeText(paragraph, lines[i]);
                target.Append(paragraph);
            }
        }

        private static void AppendCodeText(W.Paragraph paragraph, string line)
        {
            var parts = line.Split('\t');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) paragraph.Append(new W.Run(new W.TabChar()));
                if (parts[i].Length > 0) paragraph.Append(new W.Run(new W.Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve }));
            }
        }

        private void RenderRule()
        {
            pendingMarker = null;
            gapBefore = false;
            target.Append(new W.Paragraph(new W.ParagraphProperties(
                new W.ParagraphBorders(new W.BottomBorder { Val = W.BorderValues.Single, Size = 6, Space = 1, Color = "A6A6A6" }),
                new W.SpacingBetweenLines { After = "160" })));
        }
    }
}
