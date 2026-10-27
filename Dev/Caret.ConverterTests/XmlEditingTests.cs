using System;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.XmlEditing;
using Xunit;

namespace Caret.ConverterTests
{
    // The XML editor (Services/XmlEditing): a file is saved the way it was written, the text is checked while it is typed, and Format XML changes
    // only the white space between elements.
    public class XmlEditingTests
    {
        private static byte[] Utf8(string text, bool bom = false) => (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();

        // ---- reading and saving a file as it was written ------------------------------------------------------------------------------

        [Fact]
        public void A_UTF8_file_with_Windows_line_ends_is_saved_with_the_same_bytes()
        {
            var original = Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<a>\r\n  <b>é</b>\r\n</a>\r\n");
            var (text, format) = XmlTextFile.Decode(original);
            Assert.DoesNotContain('\r', text);
            Assert.Equal("CRLF", format.NewLineName);
            Assert.Equal("UTF-8", format.EncodingName);
            Assert.Equal(original, XmlTextFile.Encode(text, format, out _));
        }

        [Fact]
        public void The_byte_order_mark_and_the_final_line_end_are_kept_or_absent_as_they_were()
        {
            var withBom = Utf8("<a/>", bom: true);
            var (text, format) = XmlTextFile.Decode(withBom);
            Assert.Equal("<a/>", text);
            Assert.True(format.Bom);
            Assert.Equal("UTF-8 (BOM)", format.EncodingName);
            Assert.Equal(withBom, XmlTextFile.Encode(text, format, out _));
            var noBom = Utf8("<a/>\n");
            var (text2, format2) = XmlTextFile.Decode(noBom);
            Assert.False(format2.Bom);
            Assert.Equal(noBom, XmlTextFile.Encode(text2, format2, out _));
        }

        [Fact]
        public void A_file_that_declares_ISO_8859_1_is_read_and_written_in_it_and_not_as_UTF8()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var latin1 = Encoding.GetEncoding("iso-8859-1");
            var original = latin1.GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<a>café ü</a>\n");
            var (text, format) = XmlTextFile.Decode(original);
            Assert.Contains("café ü", text);
            Assert.False(format.Legacy);
            Assert.Equal(original, XmlTextFile.Encode(text, format, out _));
        }

        [Fact]
        public void A_UTF16_file_keeps_its_mark_and_its_byte_order()
        {
            var original = new byte[] { 0xFE, 0xFF }.Concat(new UnicodeEncoding(true, false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-16\"?><a>x</a>")).ToArray();
            var (text, format) = XmlTextFile.Decode(original);
            Assert.StartsWith("<?xml", text);
            Assert.Equal("UTF-16 BE", format.EncodingName);
            Assert.Equal(original, XmlTextFile.Encode(text, format, out _));
        }

        [Fact]
        public void A_character_the_declared_encoding_cannot_hold_is_not_written_silently_and_the_message_says_where()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var original = Encoding.GetEncoding("iso-8859-1").GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<a>x</a>\n");
            var (text, format) = XmlTextFile.Decode(original);
            var edited = text.Replace("<a>x</a>", "<a>5 €</a>");
            var ex = Assert.Throws<XmlEncodingException>(() => XmlTextFile.Encode(edited, format, out _));
            Assert.Contains("€", ex.Message);
            Assert.Contains("line 2, position 6", ex.Message);
            // changing the declaration to UTF-8, which is what the message suggests, saves it
            var fixedText = edited.Replace("ISO-8859-1", "UTF-8");
            var bytes = XmlTextFile.Encode(fixedText, format, out var written);
            Assert.Equal("UTF-8", written.EncodingName);
            Assert.Contains((byte)0xE2, bytes);   // the euro sign as UTF-8 (E2 82 AC)
        }

        [Fact]
        public void An_encoding_the_system_does_not_know_is_refused_with_a_message()
        {
            var ex = Assert.Throws<XmlEncodingException>(() => XmlTextFile.Encode("<?xml version=\"1.0\" encoding=\"no-such-encoding\"?><a/>", new XmlTextFormat(), out _));
            Assert.Contains("no-such-encoding", ex.Message);
            Assert.Equal("utf8", XmlTextFile.DeclaredEncoding("﻿  <?xml version='1.0' encoding='utf8'?><a/>"));
            Assert.Null(XmlTextFile.DeclaredEncoding("<a/>"));
        }

        [Fact]
        public void Bytes_that_are_not_valid_in_the_declared_encoding_are_read_with_the_system_code_page_and_flagged()
        {
            // 0xE9 alone is not UTF-8; no declaration means UTF-8
            var original = new byte[] { (byte)'<', (byte)'a', (byte)'>', 0xE9, (byte)'<', (byte)'/', (byte)'a', (byte)'>' };
            var (text, format) = XmlTextFile.Decode(original);
            Assert.True(format.Legacy);
            Assert.StartsWith("<a>", text);
            // saved again, it is UTF-8 (nothing declares anything else)
            var bytes = XmlTextFile.Encode(text, format, out var written);
            Assert.Equal("UTF-8", written.EncodingName);
            Assert.Contains((byte)0xC3, bytes);
        }

        [Fact]
        public void The_most_frequent_line_end_is_kept_and_a_file_with_more_than_one_kind_is_flagged()
        {
            var (text, format) = XmlTextFile.Decode(Utf8("<a>\r\n<b/>\r\n<c/>\n</a>"));
            Assert.Equal("CRLF", format.NewLineName);
            Assert.True(format.MixedLineEndings);
            Assert.Equal("<a>\n<b/>\n<c/>\n</a>", text);
            var (_, lf) = XmlTextFile.Decode(Utf8("<a>\n</a>\n"));
            Assert.Equal("LF", lf.NewLineName);
            Assert.False(lf.MixedLineEndings);
        }

        // ---- the check while typing ---------------------------------------------------------------------------------------------------

        [Fact]
        public void A_well_formed_text_has_a_root_a_line_count_and_a_readable_view()
        {
            var analysis = XmlAnalysis.Of("<notes>\n  <title>Hello</title>\n</notes>\n", "en", "notes.xml");
            Assert.True(analysis.WellFormed);
            Assert.Equal("notes", analysis.Root);
            Assert.Equal(4, analysis.Lines);
            Assert.False(analysis.Signed);
            Assert.StartsWith("# notes", analysis.Preview);
            Assert.Contains("Hello", analysis.Preview);
        }

        [Fact]
        public void A_text_that_is_not_well_formed_says_where_and_what_without_repeating_the_place_in_the_message()
        {
            var analysis = XmlAnalysis.Of("<a>\n  <b>text</c>\n</a>");
            Assert.False(analysis.WellFormed);
            Assert.Equal(2, analysis.Line);
            Assert.True(analysis.Position > 0);
            Assert.Contains("'b'", analysis.Error);
            Assert.DoesNotMatch(@"Line \d+, position \d+\.?$", analysis.Error);
            Assert.Null(analysis.Preview);
            Assert.False(XmlAnalysis.Of("").WellFormed);
        }

        [Fact]
        public void A_signed_file_is_known_even_when_it_is_not_well_formed_now()
        {
            var signed = "<doc><ds:Signature xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\"><ds:SignatureValue>AAA</ds:SignatureValue></ds:Signature></doc>";
            Assert.True(XmlAnalysis.Of(signed).Signed);
            Assert.True(XmlAnalysis.Of(signed.Replace("</doc>", "")).Signed);
            Assert.False(XmlAnalysis.Of("<doc><Signature>not a digital one</Signature></doc>").Signed);
        }

        [Fact]
        public void A_DOCTYPE_is_skipped_and_never_followed_and_an_entity_bomb_is_just_an_error()
        {
            Assert.True(XmlAnalysis.Of("<!DOCTYPE a [<!ELEMENT a (#PCDATA)>]><a>x</a>").WellFormed);
            var bomb = "<!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">]><lolz>&lol2;</lolz>";
            var analysis = XmlAnalysis.Of(bomb);
            Assert.False(analysis.WellFormed);   // the entity is not expanded (nothing in the DTD is read), so it is undeclared
        }

        [Fact]
        public void A_very_big_text_is_checked_but_has_no_readable_view()
        {
            var big = "<a>" + string.Concat(Enumerable.Repeat("<b>x</b>", 600_000)) + "</a>";
            var analysis = XmlAnalysis.Of(big);
            Assert.True(analysis.WellFormed);
            Assert.True(analysis.TooBigForPreview);
            Assert.Null(analysis.Preview);
        }

        // ---- Format XML ---------------------------------------------------------------------------------------------------------------

        [Fact]
        public void Format_puts_each_element_on_its_own_line_and_is_the_same_the_second_time()
        {
            var formatted = XmlFormatter.Format("<a><b>1</b><c><d/><e x=\"1\">t</e></c></a>");
            Assert.Equal("<a>\n  <b>1</b>\n  <c>\n    <d />\n    <e x=\"1\">t</e>\n  </c>\n</a>\n", formatted);
            Assert.Equal(formatted, XmlFormatter.Format(formatted));
            // tabs and ragged indentation become the indentation of the nesting
            Assert.Equal("<a>\n  <b>\n    <c>1</c>\n  </b>\n</a>\n", XmlFormatter.Format("<a>\n\t\t<b>\n   <c>1</c>\n</b>      </a>"));
        }

        [Fact]
        public void Format_leaves_text_comments_CDATA_the_declaration_and_mixed_content_alone()
        {
            var source = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\" standalone=\"yes\"?><!-- top --><doc><p>Hello <b>big</b>  world</p><pre xml:space=\"preserve\">\n   <i>keep</i>   </pre><![CDATA[ x < y ]]><?pi go?></doc>";
            var formatted = XmlFormatter.Format(source);
            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"ISO-8859-1\" standalone=\"yes\"?>\n<!-- top -->\n<doc>", formatted);
            Assert.Contains("<p>Hello <b>big</b>  world</p>", formatted);
            Assert.Contains("<pre xml:space=\"preserve\">\n   <i>keep</i>   </pre>", formatted);
            Assert.Contains("<![CDATA[ x < y ]]>", formatted);
            Assert.Contains("<?pi go?>", formatted);
        }

        [Fact]
        public void Format_refuses_a_text_that_is_not_well_formed_and_a_file_with_a_DOCTYPE()
        {
            var broken = Assert.Throws<XmlFormatException>(() => XmlFormatter.Format("<a><b></a>"));
            Assert.Contains("not well-formed", broken.Message);
            var doctype = Assert.Throws<XmlFormatException>(() => XmlFormatter.Format("<!DOCTYPE a><a><b/></a>"));
            Assert.Contains("DOCTYPE", doctype.Message);
            Assert.Throws<XmlFormatException>(() => XmlFormatter.Format("  "));
        }
    }
}
