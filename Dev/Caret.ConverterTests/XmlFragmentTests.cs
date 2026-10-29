using System.Linq;
using Typedown.WinUI.Services.XmlEditing;
using Xunit;

namespace Caret.ConverterTests
{
    // XML fragment mode (Services/XmlEditing/XmlFragment.cs): several elements side by side with no root around them are well-formed, are formatted
    // and read like documents, and are not taken for a document; and Paste as XML (XmlPaste.cs).
    public class XmlFragmentTests
    {
        [Fact]
        public void Elements_side_by_side_are_a_well_formed_fragment_not_an_error()
        {
            var analysis = XmlAnalysis.Of("<a/>\n<b>text</b>\n<c x=\"1\"/>\n");
            Assert.True(analysis.WellFormed);
            Assert.True(analysis.Fragment);
            Assert.Equal(3, analysis.Elements);
            Assert.Equal(4, analysis.Lines);
            Assert.Null(analysis.Error);
        }

        [Fact]
        public void A_document_with_one_root_is_not_a_fragment()
        {
            var analysis = XmlAnalysis.Of("<a><b/><c/></a>");
            Assert.True(analysis.WellFormed);
            Assert.False(analysis.Fragment);
            Assert.Equal(0, analysis.Elements);
        }

        [Fact]
        public void A_fragment_keeps_its_comments_and_its_namespaces()
        {
            var analysis = XmlAnalysis.Of("<!-- the first -->\n<x:a xmlns:x=\"urn:t\"/>\n<?pi data?>\n<x:b xmlns:x=\"urn:t\"/>");
            Assert.True(analysis.Fragment);
            Assert.Equal(2, analysis.Elements);
        }

        [Theory]
        [InlineData("hello <a/><b/>")]                                     // text before the elements
        [InlineData("<a/> and <b/>")]                                      // text between them
        [InlineData("<a/><b/> tail")]                                      // text after them
        [InlineData("<?xml version=\"1.0\"?><a/><b/>")]                    // a declaration says it is meant to be a document
        [InlineData("<!DOCTYPE a><a/><b/>")]                               // and so does a DOCTYPE
        [InlineData("")]
        [InlineData("   \n")]
        [InlineData("plain text")]
        public void Text_that_is_not_elements_side_by_side_stays_an_error(string text)
        {
            var analysis = XmlAnalysis.Of(text);
            Assert.False(analysis.WellFormed);
            Assert.False(analysis.Fragment);
        }

        [Fact]
        public void A_fragment_that_breaks_further_on_says_where_it_really_breaks()
        {
            // a reading as a document stops at the second root (position 6, "multiple root elements"); the real problem is the end tag of <b>
            var analysis = XmlAnalysis.Of("<a/><b></c>");
            Assert.False(analysis.WellFormed);
            Assert.False(analysis.Fragment);
            Assert.Equal(1, analysis.Line);
            Assert.True(analysis.Position > 6, $"position {analysis.Position}");
            Assert.False(XmlAnalysis.Of("<a/><b>").WellFormed);
            // and a text that breaks at the same place as a document does keeps the document's own error
            var mismatched = XmlAnalysis.Of("<a>\n  <b>text</c>\n</a>");
            Assert.False(mismatched.WellFormed);
            Assert.Equal(2, mismatched.Line);
        }

        [Fact]
        public void The_readable_view_of_a_fragment_reads_each_element_and_the_outline_follows_the_lines()
        {
            var analysis = XmlAnalysis.Of("<order>\n  <line><sku>A1</sku></line>\n</order>\n<invoice>\n  <item><sku>B2</sku></item>\n</invoice>\n", "en", "orders.xml");
            Assert.True(analysis.Fragment);
            Assert.NotNull(analysis.Preview);
            Assert.Contains("A1", analysis.Preview);
            Assert.Contains("B2", analysis.Preview);
            // the name of the file is said once, with the first element
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(analysis.Preview, @"orders\.xml"));
            var names = analysis.Outline.Select(e => (e.Level, e.Name, e.Line)).ToList();
            Assert.Contains((1, "order", 1), names);
            Assert.Contains((2, "line", 2), names);
            Assert.Contains((1, "invoice", 4), names);
            Assert.Contains((2, "item", 5), names);
        }

        [Fact]
        public void A_fragment_is_formatted_element_by_element()
        {
            var formatted = XmlFormatter.Format("<a><b>1</b></a><c/>");
            Assert.Equal("<a>\n  <b>1</b>\n</a>\n<c />\n", formatted);
            Assert.Equal(formatted, XmlFormatter.Format(formatted));
            Assert.Equal("<!-- note -->\n<a>\n  <b />\n</a>\n<c />\n", XmlFormatter.Format("<!-- note --><a><b/></a>\n\n   <c/>"));
        }

        [Fact]
        public void A_broken_fragment_is_not_formatted_and_says_where()
        {
            var ex = Assert.Throws<XmlFormatException>(() => XmlFormatter.Format("<a/><b></c>"));
            Assert.Equal(XmlFormatProblem.NotWellFormed, ex.Problem);
            Assert.True(ex.Position > 6);
            Assert.Equal(XmlFormatProblem.NotWellFormed, Assert.Throws<XmlFormatException>(() => XmlFormatter.Format("<a/> text <b/>")).Problem);
        }

        [Theory]
        [InlineData("<a/>", true)]
        [InlineData("  \r\n<?xml version=\"1.0\"?><a/>", true)]
        [InlineData("﻿<a/>", true)]
        [InlineData("<broken", true)]                  // a broken message is what a person pastes to see where it breaks
        [InlineData("plain text", false)]
        [InlineData("{\"a\": \"<b>\"}", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_text_that_starts_like_XML_is_pasted_as_XML(string text, bool xml) => Assert.Equal(xml, XmlPaste.LooksLikeXml(text));

        [Fact]
        public void The_pasted_text_is_made_a_fenced_block_with_a_fence_that_cannot_end_early()
        {
            Assert.Equal("a\nb", XmlPaste.Normalize("﻿ \r\na\r\nb\r\n\r\n"));
            Assert.Equal("```xml\n<a/>\n```\n", XmlPaste.Fenced("<a/>"));
            // a CDATA section may hold a code fence of its own: the fence around it is longer
            var tricky = "<a><![CDATA[\n```\ncode\n```\n]]></a>";
            Assert.Equal("````xml\n" + tricky + "\n````\n", XmlPaste.Fenced(tricky));
        }
    }
}
