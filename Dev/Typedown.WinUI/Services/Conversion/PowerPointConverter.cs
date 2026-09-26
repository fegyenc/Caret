using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Typedown.WinUI.Services.Conversion
{
    // .pptx → one "## Slide N: Title" section per visible slide: text boxes in reading order (top to
    // bottom, then left to right), bullet levels kept, tables as pipe tables, speaker notes as a quote.
    internal static class PowerPointConverter
    {
        public static string Convert(Stream stream, ConversionContext context)
        {
            using var doc = PresentationDocument.Open(stream, false);
            var presentation = doc.PresentationPart;
            var slideIds = presentation?.Presentation?.SlideIdList?.Elements<P.SlideId>().ToList();
            if (slideIds == null) return "";
            var sb = new StringBuilder();
            var number = 0;
            foreach (var slideId in slideIds)
            {
                number++;
                if (slideId.RelationshipId?.Value == null || presentation.GetPartById(slideId.RelationshipId.Value) is not SlidePart slide) continue;
                if (slide.Slide?.Show?.Value == false) continue;
                var tree = slide.Slide?.CommonSlideData?.ShapeTree;
                if (tree == null) continue;

                string title = null;
                var body = new List<(long Y, long X, string Markdown)>();
                foreach (var element in Flatten(tree))
                {
                    switch (element)
                    {
                        case P.Shape shape:
                            var placeholder = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value;
                            if (title == null && (placeholder == P.PlaceholderValues.Title || placeholder == P.PlaceholderValues.CenteredTitle))
                            {
                                title = PlainText(shape.TextBody);
                                continue;
                            }
                            // Slide numbers, dates and footers repeat on every slide.
                            if (placeholder == P.PlaceholderValues.SlideNumber || placeholder == P.PlaceholderValues.DateAndTime || placeholder == P.PlaceholderValues.Footer)
                                continue;
                            var text = TextBody(shape.TextBody, bulleted: placeholder == P.PlaceholderValues.Body || placeholder == P.PlaceholderValues.Object || placeholder == null && CountParagraphs(shape.TextBody) > 1);
                            if (text.Length > 0) body.Add(Position(shape.ShapeProperties?.Transform2D, text));
                            break;
                        case P.GraphicFrame frame:
                            var table = frame.Descendants<A.Table>().FirstOrDefault();
                            if (table != null)
                            {
                                var rows = table.Elements<A.TableRow>()
                                    .Select(r => (IReadOnlyList<string>)r.Elements<A.TableCell>().Select(c => PlainText(c.TextBody, "<br>")).ToList())
                                    .Where(r => r.Any(c => c.Length > 0)).ToList();
                                var md = MarkdownText.Table(rows);
                                if (md.Length > 0) body.Add(Position(frame.Transform, md));
                            }
                            break;
                        case P.Picture picture:
                            var embed = picture.BlipFill?.Blip?.Embed?.Value;
                            if (embed != null && slide.GetPartById(embed) is ImagePart image)
                            {
                                var alt = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value ?? "";
                                using var data = image.GetStream();
                                var md = context.SaveImage(data, image.ContentType, alt);
                                if (md.Length > 0) body.Add(Position(picture.ShapeProperties?.Transform2D, md));
                            }
                            break;
                    }
                }

                var notes = NotesText(slide);
                if (title == null && body.Count == 0 && notes.Length == 0) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                var heading = string.IsNullOrWhiteSpace(title)
                    ? string.Format(context.Options.SlideHeadingUntitledFormat, number)
                    : string.Format(context.Options.SlideHeadingFormat, number, MarkdownText.EscapeInline(title.Replace('\n', ' ').Trim()));
                sb.Append("## ").Append(heading);
                foreach (var item in body.OrderBy(b => b.Y).ThenBy(b => b.X))
                    sb.Append("\n\n").Append(item.Markdown);
                if (notes.Length > 0)
                    sb.Append("\n\n> **").Append(context.Options.NotesLabel).Append(":** ").Append(notes.Replace("\n", "\n> "));
            }
            return sb.ToString();
        }

        private static IEnumerable<OpenXmlElement> Flatten(OpenXmlElement container)
        {
            foreach (var child in container.ChildElements)
            {
                if (child is P.GroupShape group)
                    foreach (var inner in Flatten(group)) yield return inner;
                else yield return child;
            }
        }

        private static (long, long, string) Position(OpenXmlElement transform, string markdown)
        {
            var offset = transform?.GetFirstChild<A.Offset>();
            return (offset?.Y?.Value ?? long.MaxValue / 2, offset?.X?.Value ?? 0, markdown);
        }

        private static int CountParagraphs(OpenXmlElement textBody) =>
            textBody?.Elements<A.Paragraph>().Count(p => ParagraphText(p).Trim().Length > 0) ?? 0;

        private static string TextBody(OpenXmlElement textBody, bool bulleted)
        {
            if (textBody == null) return "";
            var lines = new List<string>();
            foreach (var p in textBody.Elements<A.Paragraph>())
            {
                var inline = new InlineBuilder();
                AddRuns(p, inline);
                var text = inline.Build().Trim();
                if (text.Length == 0) continue;
                var noBullet = p.ParagraphProperties?.GetFirstChild<A.NoBullet>() != null;
                var level = p.ParagraphProperties?.Level?.Value ?? 0;
                if (bulleted && !noBullet)
                    lines.Add(new string(' ', 4 * Math.Min(level, 8)) + "- " + text);
                else
                    lines.Add(MarkdownText.EscapeBlockStart(text));
            }
            var allBullets = lines.All(l => l.TrimStart().StartsWith("- "));
            return string.Join(allBullets ? "\n" : "\n\n", lines);
        }

        private static void AddRuns(A.Paragraph p, InlineBuilder inline)
        {
            foreach (var child in p.ChildElements)
            {
                switch (child)
                {
                    case A.Run run:
                        var rp = run.RunProperties;
                        inline.Add(run.Text?.Text, rp?.Bold?.Value == true, rp?.Italic?.Value == true,
                            rp?.Strike != null && rp.Strike.Value != A.TextStrikeValues.NoStrike, false, null);
                        break;
                    case A.Field field:
                        inline.Add(field.Text?.Text);
                        break;
                    case A.Break:
                        inline.AddRaw("  \n");
                        break;
                }
            }
        }

        private static string ParagraphText(A.Paragraph p) => string.Concat(p.Descendants<A.Text>().Select(t => t.Text));

        private static string PlainText(OpenXmlElement textBody, string separator = "\n") =>
            textBody == null ? "" : string.Join(separator, textBody.Elements<A.Paragraph>().Select(ParagraphText).Where(t => t.Trim().Length > 0)).Trim();

        private static string NotesText(SlidePart slide)
        {
            var notes = slide.NotesSlidePart?.NotesSlide?.CommonSlideData?.ShapeTree;
            if (notes == null) return "";
            var bodies = notes.Descendants<P.Shape>()
                .Where(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == P.PlaceholderValues.Body)
                .Select(s => PlainText(s.TextBody));
            return string.Join("\n", bodies.Where(t => t.Length > 0)).Trim();
        }
    }
}
