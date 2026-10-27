using System;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // The generic view of an XML file that no format pack knows (Services/Conversion/XmlConverter.cs, XmlStructure.cs): headings for the
    // elements that have a title, numbered lists for lists of text, the overview at the top and the notes on the numbering. The sample
    // samples/xml-prompt.xml is invented (an instruction document, like a prompt for a spreadsheet assistant).
    public class XmlHumanViewTests
    {
        private static string Read(string xml, string language = "en")
        {
            var work = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(work, "test.xml");
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                return DocumentConverter.Convert(path, new ConversionOptions { Language = language }).Markdown;
            }
            finally { Directory.Delete(work, true); }
        }

        private static string Sample(string name, string language = "en") =>
            DocumentConverter.Convert(Path.Combine(TestPaths.Samples, name), new ConversionOptions { Language = language }).Markdown;

        [Fact]
        public void An_element_with_an_id_and_a_name_is_a_heading_and_its_instructions_are_a_numbered_list()
        {
            var md = Sample("xml-prompt.xml");
            Assert.Contains("\n## tasks\n", md);
            Assert.Contains("\n### T.1 — Check the workbook\n", md);
            Assert.Contains("- **instruction** × 3\n  1. Confirm that the workbook has a sheet named Summary.\n  2. Confirm that the file name is not the protected original.\n  3. Report the address", md);
            // nothing is dropped: the attributes of a list item stay with it
            Assert.Contains("1. Summary (order=1)", md);
        }

        [Fact]
        public void What_follows_the_headings_is_a_heading_too_and_not_a_part_of_the_last_one()
        {
            var md = Sample("xml-prompt.xml");
            var tasks = md.IndexOf("### T.4", StringComparison.Ordinal);
            var constraints = md.IndexOf("\n## constraints\n", StringComparison.Ordinal);
            Assert.True(tasks > 0 && constraints > tasks);
            Assert.Contains("\n## success_criteria\n", md);
            Assert.Contains("\n## response_format\n", md);
            // the constraints are not between T.4 and the next heading
            Assert.DoesNotContain("Do not modify the protected original.", md.Substring(tasks, constraints - tasks));
        }

        [Fact]
        public void The_overview_says_how_big_the_file_is_and_what_repeats_and_the_numbering_notes_the_gap()
        {
            var md = Sample("xml-prompt.xml");
            Assert.Contains("35 elements, up to 5 levels deep", md);
            Assert.Contains("| instruction | 7 |", md);
            // the same count: the element that comes first in the file comes first (sheet before task before constraint)
            var sheet = md.IndexOf("| sheet | 3 |", StringComparison.Ordinal);
            var task = md.IndexOf("| task | 3 |", StringComparison.Ordinal);
            var constraint = md.IndexOf("| constraint | 3 |", StringComparison.Ordinal);
            Assert.True(sheet >= 0 && task >= 0 && constraint >= 0);
            Assert.True(sheet < task);
            Assert.True(task < constraint);
            Assert.Contains("- The id of `task`: `T.2` is followed by `T.4`", md);
            var spanish = Sample("xml-prompt.xml", "es");
            Assert.Contains("35 elementos, hasta 5 niveles de profundidad", spanish);
            Assert.Contains("- El id de `task`: después de `T.2` viene `T.4`", spanish);
        }

        [Fact]
        public void A_small_file_has_no_overview_and_a_file_without_titles_reads_as_one_list_as_before()
        {
            var md = Read("<a><b>1</b><c><d>2</d></c></a>");
            Assert.DoesNotContain("Structure", md);
            Assert.DoesNotContain("\n## ", md);
            Assert.Contains("- **a**\n  - **b**: 1\n  - **c**\n    - **d**: 2", md);
        }

        [Fact]
        public void A_repeated_number_and_a_gap_are_noted_but_ids_that_are_not_a_count_say_nothing()
        {
            var counted = "<r><i id=\"A1\"/><i id=\"A2\"/><i id=\"A2\"/><i id=\"A5\"/>" + Pad() + "</r>";
            var md = Read(counted);
            Assert.Contains("`A2` appears 2 times", md);
            Assert.Contains("`A2` is followed by `A5`", md);
            // hashes, unrelated codes and numbers far apart are not a count
            var hashes = Read("<r><i id=\"x9f3a\"/><i id=\"k77\"/><i id=\"zz4021\"/><i id=\"b8\"/></r>".Replace("</r>", Pad() + "</r>"));
            Assert.DoesNotContain("Notes on the numbering", hashes);
            var far = Read("<r><i id=\"1\"/><i id=\"500\"/><i id=\"9000\"/></r>".Replace("</r>", Pad() + "</r>"));
            Assert.DoesNotContain("Notes on the numbering", far);
        }

        private static string Pad() => string.Concat(Enumerable.Range(0, 30).Select(n => "<p>" + n + "</p>"));

        [Fact]
        public void A_title_child_makes_a_heading_and_is_not_said_twice_and_every_attribute_is_said_once()
        {
            var md = Read("<doc lang=\"en\"><section ref=\"s1\" kind=\"main\"><title>First part</title><para>Text one</para></section><section><title>Second part</title><para>Text two</para></section></doc>");
            Assert.Contains("\n## First part\n", md);
            Assert.Contains("\n## Second part\n", md);
            Assert.Equal(1, Count(md, "First part"));
            Assert.Contains("(ref=s1, kind=main)", md);
            Assert.Contains("(lang=en)", md);
            Assert.Contains("- **para**: Text one", md);
        }

        [Fact]
        public void An_id_and_a_title_are_both_said_when_there_is_no_name()
        {
            var md = Read("<doc><s id=\"7\" title=\"Seven\"><x>1</x></s><s id=\"8\" title=\"Eight\"><x>2</x></s></doc>");
            Assert.Contains("\n## 7 — Seven\n", md);
        }

        [Fact]
        public void A_digital_signature_is_never_a_heading_and_never_printed_even_with_an_id()
        {
            var xml = "<r><a>1</a><ds:Signature xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\" Id=\"sig-1\"><ds:SignatureValue>SECRETSIGNATUREBYTES</ds:SignatureValue></ds:Signature></r>";
            var md = Read(xml);
            Assert.DoesNotContain("SECRETSIGNATUREBYTES", md);
            Assert.DoesNotContain("## sig-1", md);
            Assert.Contains("digital signature (not printed)", md);
        }

        [Fact]
        public void Headings_stop_at_level_six_and_a_very_deep_file_does_not_run_out_of_stack()
        {
            var open = string.Concat(Enumerable.Range(0, 400).Select(n => "<n id=\"L" + n + "\">"));
            var close = string.Concat(Enumerable.Repeat("</n>", 400));
            var md = Read("<r>" + open + "<leaf>end</leaf>" + close + "</r>");
            Assert.Contains("\n###### L4\n", md);
            Assert.DoesNotContain("####### ", md);
            Assert.Contains("(nesting too deep", md);
        }

        [Fact]
        public void A_text_after_the_last_heading_stays_under_its_own_heading()
        {
            var md = Read("<doc><part name=\"One\"><x>1</x></part><part name=\"Two\"><x>2</x></part><footer>All rights reserved</footer></doc>");
            Assert.Contains("\n## footer\n\nAll rights reserved", md);
            Assert.Equal(1, Count(md, "All rights reserved"));
        }

        [Fact]
        public void A_run_of_elements_that_are_only_attributes_is_a_table_of_those_attributes()
        {
            var md = Read("<configuration><appSettings><add key=\"Mode\" value=\"Fast\"/><add key=\"Limit\" value=\"10 | 20\"/><add key=\"Name\" value=\"x\"/></appSettings></configuration>");
            Assert.Contains("| key | value |", md);
            Assert.Contains("| Mode | Fast |", md);
            // the pipe in a value does not break the table
            Assert.Contains("| Limit | 10 \\| 20 |", md);
            Assert.DoesNotContain("@key", md);
            // two of them are not a table (as before), and a mix with element columns keeps the marks
            Assert.Contains("- **add** × 2\n    1. (key=A, value=1)\n    2. (key=B, value=2)", Read("<c><add key=\"A\" value=\"1\"/><add key=\"B\" value=\"2\"/></c>"));
            Assert.Contains("| @id | name |", Read("<c><r id=\"1\"><name>a</name></r><r id=\"2\"><name>b</name></r><r id=\"3\"><name>c</name></r></c>"));
        }

        [Fact]
        public void An_instruction_with_enumerated_lines_keeps_its_list_under_the_item()
        {
            var xml = "<d><t id=\"1\" name=\"One\"><instruction>Confirm:\n  1. Taiwan exists exactly once;\n  2. the file exists;\n  continued on a second line\n  3. all ten sheets exist.</instruction>"
                + "<instruction>Plain text\nover two lines</instruction></t><t id=\"2\" name=\"Two\"><x>1</x></t></d>";
            var md = Read(xml);
            Assert.Contains("1. Confirm:\n     1. Taiwan exists exactly once;\n     2. the file exists; continued on a second line\n     3. all ten sheets exist.", md);
            // a text with line breaks but no list stays one line
            Assert.Contains("2. Plain text over two lines", md);
        }

        [Fact]
        public void A_single_element_with_enumerated_lines_is_a_nested_list_and_one_dash_is_not_a_list()
        {
            var md = Read("<d><role>You are:\n- careful\n- exact\n- brief</role><note>five\n- degrees</note></d>");
            Assert.Contains("  - **role**: You are:\n    - careful\n    - exact\n    - brief", md);
            Assert.Contains("  - **note**: five - degrees", md);
        }

        [Fact]
        public void Three_titled_sections_are_headings_and_not_the_rows_of_a_table()
        {
            var md = Read("<doc><section><title>One</title><para>a</para></section><section><title>Two</title><para>b</para></section><section><title>Three</title><para>c</para></section></doc>");
            Assert.Contains("\n## One\n", md);
            Assert.Contains("\n## Two\n", md);
            Assert.Contains("\n## Three\n", md);
            Assert.DoesNotContain("| title | para |", md);
            // rows of data with an id are still a table
            Assert.Contains("| @id | name |", Read("<c><r id=\"1\"><name>a</name></r><r id=\"2\"><name>b</name></r><r id=\"3\"><name>c</name></r></c>"));
        }

        [Fact]
        public void The_attributes_of_a_title_child_are_said_under_the_heading()
        {
            var md = Read("<doc><section><title lang=\"fr\">Bonjour</title><para>x</para></section><section><title>Two</title><para>y</para></section></doc>");
            Assert.Contains("\n## Bonjour\n", md);
            Assert.Contains("- **title** (lang=fr)", md);
            Assert.Equal(1, Count(md, "Bonjour"));
        }

        [Fact]
        public void The_numbering_notes_say_the_ids_as_the_file_has_them()
        {
            var md = Read("<r><t id=\"T.01\"/><t id=\"T.02\"/><t id=\"T.04\"/><t id=\"T.04\"/>" + Pad() + "</r>");
            Assert.Contains("`T.02` is followed by `T.04`", md);
            Assert.Contains("`T.04` appears 2 times", md);
            Assert.DoesNotContain("`T.2`", md);
            Assert.DoesNotContain("`T.4`", md);
        }

        [Fact]
        public void A_very_long_list_in_a_text_is_bounded_as_one_value_and_says_how_much_was_left_out()
        {
            var text = "Confirm:\n" + string.Join("\n", Enumerable.Range(1, 5000).Select(n => n + ". line number " + n));
            var md = Read("<d><instruction>" + text + "</instruction></d>");
            Assert.True(md.Length < 30000, md.Length.ToString());
            Assert.Contains("more characters)", md);
            Assert.Contains("1. line number 1", md);
            Assert.DoesNotContain("line number 4999", md);
            // a list that fits is not cut
            Assert.DoesNotContain("more characters", Read("<d><i>Confirm:\n1. a\n2. b\n3. c</i></d>"));
        }

        private static int Count(string text, string part)
        {
            var n = 0;
            for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
