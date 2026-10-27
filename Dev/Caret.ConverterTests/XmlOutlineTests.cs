using System.Linq;
using Typedown.WinUI.Services.XmlEditing;
using Xunit;

namespace Caret.ConverterTests
{
    // The outline of an XML document (Services/XmlEditing/XmlOutline.cs): what is listed, how deep, with which hint, on which line.
    public class XmlOutlineTests
    {
        private static System.Collections.Generic.IReadOnlyList<XmlOutlineEntry> Outline(string xml) => XmlAnalysis.Of(xml).Outline;

        private static string Show(System.Collections.Generic.IReadOnlyList<XmlOutlineEntry> outline) =>
            string.Join("|", outline.Select(e => e.Limit ? "LIMIT" : $"{e.Level}:{e.Name}{(e.Hint != null ? "=" + e.Hint : "")}{(e.More > 0 ? " +" + e.More : "")}@{e.Line}"));

        [Fact]
        public void Containers_are_listed_with_their_depth_and_the_line_of_their_start_tag()
        {
            var xml = "<a>\n  <b>\n    <c>\n      <d>x</d>\n    </c>\n  </b>\n  <e>\n    <f>y</f>\n  </e>\n</a>";
            Assert.Equal("1:a@1|2:b@2|3:c@3|2:e@7", Show(Outline(xml)));   // d and f hold only text
        }

        [Fact]
        public void An_element_with_only_text_is_left_out_unless_the_whole_file_is_one_flat_record()
        {
            // the fields of an invoice would bury its structure, so only the elements that hold elements are listed
            var xml = "<invoice><id>1</id><date>2026-01-01</date><party><name>Ann</name></party><line><price>3</price></line></invoice>";
            Assert.Equal("1:invoice=1@1|2:party=Ann@1|2:line@1", Show(Outline(xml)));
            Assert.Equal("1:settings@1|2:theme@1|2:size@1", Show(Outline("<settings><theme>dark</theme><size>12</size></settings>")));
        }

        [Fact]
        public void An_id_or_a_name_tells_siblings_apart_from_an_attribute_or_a_child_element()
        {
            var xml = "<shop><item id=\"7\"><x><y/></x></item><item><ID>A-12</ID><x><y/></x></item><item><title>A long and winding title that goes on and on</title><x><y/></x></item></shop>";
            Assert.Equal("1:shop@1|2:item=7@1|3:x@1|2:item=A-12@1|3:x@1|2:item@1|3:x@1", Show(Outline(xml.Replace("<y/>", "<y>1</y>"))));
        }

        [Fact]
        public void Repeated_siblings_are_listed_three_times_and_then_counted()
        {
            var records = string.Concat(Enumerable.Range(1, 50).Select(i => $"<book><title>T{i}</title><price>1</price></book>"));
            var show = Show(Outline("<library>" + records + "</library>"));
            // three books (told apart by their title), then one row for the other 47
            Assert.Equal("1:library@1|2:book=T1@1|2:book=T2@1|2:book=T3@1|2:book +47@1", show);
            Assert.Equal(1, show.Split('|').Count(r => r.Contains("+47")));
            Assert.DoesNotContain("T4", show);
        }

        [Fact]
        public void The_depth_is_cut_and_the_whole_list_has_a_limit()
        {
            var deep = string.Concat(Enumerable.Range(1, 20).Select(i => $"<n{i}>")) + "x" + string.Concat(Enumerable.Range(1, 20).Reverse().Select(i => $"</n{i}>"));
            Assert.Equal(XmlOutline.MaxDepth, Outline(deep).Count);

            // 300 different parents each with one container: more than the limit
            var many = "<r>" + string.Concat(Enumerable.Range(1, 400).Select(i => $"<p{i}><q{i}><z/></q{i}></p{i}>")) + "</r>";
            var outline = Outline(many);
            Assert.Equal(XmlOutline.MaxEntries + 1, outline.Count);
            Assert.True(outline.Last().Limit);
            Assert.All(outline.Take(XmlOutline.MaxEntries), e => Assert.False(e.Limit));
        }

        [Fact]
        public void A_text_that_is_not_well_formed_has_no_outline_and_namespaces_give_local_names()
        {
            Assert.Empty(Outline("<a><b></a>"));
            Assert.Empty(Outline(""));
            var xml = "<inv:Invoice xmlns:inv=\"urn:x\" xmlns:cac=\"urn:y\"><cac:Party><cac:Name/></cac:Party></inv:Invoice>";
            Assert.Equal("1:Invoice@1|2:Party@1", Show(Outline(xml)));   // Name holds nothing
        }
    }
}
