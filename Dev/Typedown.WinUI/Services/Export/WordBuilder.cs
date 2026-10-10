using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Typedown.WinUI.Services.Export
{
    // One export: the Markdown tree in, the parts of a .docx out. The blocks are here, the inlines in WordBuilder.Inlines.cs and the
    // pictures in WordBuilder.Images.cs.
    internal sealed partial class WordBuilder
    {
        // The reading of the Markdown matches the editor: tables, task lists, ~~strike~~, ~sub~ and ^super^, bare links. The
        // extension that reads ==x== as a highlight is left out on purpose: it would eat the {==x==} of a review.
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables().UseTaskLists().UseAutoLinks().UseYamlFrontMatter().UseFootnotes().UseMathematics()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Subscript | EmphasisExtraOptions.Superscript)
            .Build();

        // Where a paragraph is: how deep in quotes and lists, in a table, and what a table column asks of its text.
        private sealed record Ctx(int Quote, int ListLevel, bool InTable, W.JustificationValues? Align, bool Bold, bool InNote = false)
        {
            public static readonly Ctx Root = new(0, -1, false, null, false);
        }

        private readonly WordExportOptions options;
        private readonly List<string> skipped = new();
        private OpenXmlCompositeElement target;
        private W.Numbering numbering;
        private int nextNumId = WordStyles.FirstNumberedNum;
        private int textWidth;
        private (int NumId, int Level)? pendingMarker;
        // the next paragraph needs room above it: it comes after a table or a list
        private bool gapBefore;

        public WordBuilder(WordExportOptions options) => this.options = options;

        public WordExportResult Build(string markdown, Stream stream)
        {
            var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
            var tree = Markdown.Parse(text, Pipeline);
            using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                main = package.AddMainDocumentPart();
                var body = new W.Body();
                main.Document = new W.Document(body);
                target = body;
                currentPart = main;
                var meta = ReadFrontMatter(tree);
                var (pageWidth, pageHeight) = options.PageSize == WordPageSize.Letter ? (12240, 15840) : (11906, 16838);
                textWidth = pageWidth - 2 * 1440;
                main.AddNewPart<StyleDefinitionsPart>().Styles = WordStyles.Create(meta.Language ?? options.Language ?? "en-US", textWidth);
                numbering = WordStyles.CreateNumbering();
                main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;

                CollectHeadings(tree);
                RenderBlocks(tree, Ctx.Root);
                if (body.LastChild is W.Table) body.Append(new W.Paragraph());
                var section = new W.SectionProperties();
                if (options.PageNumbers) section.Append(new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = AddPageNumberFooter() });
                section.Append(new W.PageSize { Width = (uint)pageWidth, Height = (uint)pageHeight });
                section.Append(new W.PageMargin { Top = 1440, Right = 1440u, Bottom = 1440, Left = 1440u, Header = 708u, Footer = 708u, Gutter = 0u });
                body.Append(section);
                AddSettings();

                package.PackageProperties.Title = meta.Title ?? FirstHeading(tree) ?? options.Title;
                package.PackageProperties.Creator = meta.Author;
                package.PackageProperties.Subject = meta.Subject;
                package.PackageProperties.Description = meta.Description;
                package.PackageProperties.Keywords = meta.Keywords;
                package.PackageProperties.Created = package.PackageProperties.Modified = DateTime.UtcNow;
            }
            return new WordExportResult { Pictures = pictures, SkippedPictures = skipped };
        }

        private static string FirstHeading(MarkdownDocument tree)
        {
            var heading = tree.Descendants<HeadingBlock>().FirstOrDefault();
            var text = heading?.Inline == null ? null : PlainText(heading.Inline).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private void RenderBlocks(ContainerBlock container, Ctx ctx)
        {
            foreach (var block in container) RenderBlock(block, ctx);
        }

        private void RenderBlock(Block block, Ctx ctx)
        {
            switch (block)
            {
                case YamlFrontMatterBlock: break; // its few known keys are the properties of the file; none of it is printed
                case FootnoteGroup or Footnote: break; // written where they are referred to
                case HeadingBlock heading: AddParagraph(heading.Inline, ctx, WordStyles.Heading(Math.Clamp(heading.Level, 1, 6)), headingBookmarks.GetValueOrDefault(heading)); break;
                case ParagraphBlock paragraph when IsTocMarker(paragraph): RenderToc(); break;
                case ParagraphBlock paragraph: AddParagraph(paragraph.Inline, ctx, null); break;
                case QuoteBlock quote: RenderBlocks(quote, ctx with { Quote = ctx.Quote + 1 }); break;
                case ListBlock list: RenderList(list, ctx); break;
                case MathBlock math: RenderMathBlock(math, ctx); break;
                case CodeBlock code: RenderCode(code, ctx); break;
                case ThematicBreakBlock: RenderRule(); break;
                case Table table: RenderTable(table, ctx); break;
                case HtmlBlock html: RenderHtml(html, ctx); break;
                case ContainerBlock container: RenderBlocks(container, ctx); break;
                case LeafBlock leaf when leaf.Inline != null: AddParagraph(leaf.Inline, ctx, null); break;
            }
        }
    }
}
