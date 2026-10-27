using System;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.XmlEditing;
using Xunit;

namespace Caret.ConverterTests
{
    // Checking an XML text against a schema and saying the problems in plain words (Services/XmlEditing/XmlSchemaCheck.cs, XmlSchemaWords.cs).
    public class XmlSchemaCheckTests
    {
        private const string Library = @"<xs:schema xmlns:xs=""http://www.w3.org/2001/XMLSchema"">
  <xs:element name=""library"">
    <xs:complexType>
      <xs:sequence>
        <xs:element name=""book"" maxOccurs=""unbounded"">
          <xs:complexType>
            <xs:sequence>
              <xs:element name=""title"" type=""xs:string""/>
              <xs:element name=""price"" type=""xs:decimal""/>
              <xs:element name=""kind"" minOccurs=""0"">
                <xs:simpleType><xs:restriction base=""xs:string""><xs:enumeration value=""novel""/><xs:enumeration value=""essay""/></xs:restriction></xs:simpleType>
              </xs:element>
            </xs:sequence>
            <xs:attribute name=""id"" type=""xs:int"" use=""required""/>
          </xs:complexType>
        </xs:element>
      </xs:sequence>
    </xs:complexType>
  </xs:element>
</xs:schema>";

        private static string Folder() => TestPaths.NewTempFolder();

        private static string Write(string folder, string name, string content)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static XmlSchemaOutcome Check(string xml, string schema = Library)
        {
            var folder = Folder();
            try { return XmlSchemaCheck.Run(xml, new[] { Write(folder, "library.xsd", schema) }); }
            finally { Directory.Delete(folder, true); }
        }

        private const string Good = "<library><book id=\"1\"><title>A</title><price>12.50</price></book></library>";

        [Fact]
        public void A_valid_text_is_valid_and_no_schema_or_a_text_that_is_not_well_formed_is_not_checked()
        {
            Assert.Equal(XmlSchemaState.Valid, Check(Good).State);
            Assert.Equal(XmlSchemaState.None, XmlSchemaCheck.Run(Good, Array.Empty<string>()).State);
            var folder = Folder();
            try
            {
                var path = Write(folder, "library.xsd", Library);
                Assert.Equal(XmlSchemaState.Skipped, XmlSchemaCheck.Run("<library>", new[] { path }, wellFormed: false).State);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void An_element_in_the_wrong_place_says_where_and_what_is_expected()
        {
            var outcome = Check("<library>\n  <book id=\"1\">\n    <price>1</price>\n    <title>A</title>\n  </book>\n</library>");
            var issue = outcome.Issues.First();
            Assert.Equal(XmlIssueKind.NotExpected, issue.Kind);
            Assert.Equal("price", issue.Element);
            Assert.Equal("book", issue.Parent);
            Assert.Equal("title", issue.Expected);
            Assert.Equal(3, issue.Line);
            Assert.Equal("The element \"price\" is not expected here (inside \"book\"). Expected: title.", issue.Plain("en"));
            Assert.Equal("El elemento \"price\" no se espera aquí (dentro de \"book\"). Se esperaba: title.", issue.Plain("es"));
            Assert.Contains("book", issue.Raw);
        }

        [Fact]
        public void An_element_that_ends_too_early_is_incomplete_and_says_what_is_missing()
        {
            var issue = Check("<library><book id=\"1\"><title>A</title></book></library>").Issues.First();
            Assert.Equal(XmlIssueKind.Incomplete, issue.Kind);
            Assert.Equal("book", issue.Element);
            Assert.Equal("price", issue.Expected);
            Assert.Equal("The element \"book\" is incomplete: something is missing at the end. Expected: price.", issue.Plain("en"));
            Assert.StartsWith("El elemento \"book\" está incompleto", issue.Plain("es"));
        }

        [Fact]
        public void A_missing_required_attribute_and_an_attribute_the_schema_does_not_know_name_their_element()
        {
            var missing = Check("<library><book><title>A</title><price>1</price></book></library>").Issues.First();
            Assert.Equal(XmlIssueKind.MissingAttribute, missing.Kind);
            Assert.Equal("id", missing.Attribute);
            Assert.Equal("book", missing.Element);
            Assert.Equal("The element \"book\" is missing its required attribute \"id\".", missing.Plain("en"));
            Assert.Equal("Al elemento \"book\" le falta el atributo obligatorio \"id\".", missing.Plain("es"));

            var extra = Check("<library><book id=\"1\" colour=\"red\"><title>A</title><price>1</price></book></library>").Issues.First();
            Assert.Equal(XmlIssueKind.AttributeNotDeclared, extra.Kind);
            Assert.Equal("colour", extra.Attribute);
            Assert.Equal("book", extra.Element);
            Assert.Equal("The attribute \"colour\" is not allowed on the element \"book\".", extra.Plain("en"));
            Assert.Equal("El atributo \"colour\" no está permitido en el elemento \"book\".", extra.Plain("es"));
        }

        [Fact]
        public void An_element_the_schema_does_not_declare_is_said_in_both_languages()
        {
            var issue = Check("<catalog/>").Issues.First();
            Assert.Equal(XmlIssueKind.ElementNotDeclared, issue.Kind);
            Assert.Equal("The element \"catalog\" is not declared in the schema.", issue.Plain("en"));
            Assert.Equal("El elemento \"catalog\" no está declarado en el esquema.", issue.Plain("es"));
        }

        [Fact]
        public void A_value_that_does_not_fit_its_type_says_the_value_the_type_and_the_constraint()
        {
            var number = Check("<library><book id=\"1\"><title>A</title><price>cheap</price></book></library>").Issues.First();
            Assert.Equal(XmlIssueKind.InvalidValue, number.Kind);
            Assert.Equal("cheap", number.Value);
            Assert.Equal("decimal", number.Type);
            Assert.Equal("price", number.Element);
            Assert.StartsWith("The value \"cheap\" of the element \"price\" is not valid: it must be of type decimal.", number.Plain("en"));
            Assert.StartsWith("El valor \"cheap\" del elemento \"price\" no es válido: debe ser de tipo decimal.", number.Plain("es"));

            var attribute = Check("<library><book id=\"x\"><title>A</title><price>1</price></book></library>").Issues.First();
            Assert.True(attribute.OfAttribute);
            Assert.Equal("id", attribute.Attribute);
            Assert.StartsWith("The value \"x\" of the attribute \"id\" is not valid", attribute.Plain("en"));

            var kind = Check("<library><book id=\"1\"><title>A</title><price>1</price><kind>poem</kind></book></library>").Issues.First();
            Assert.Equal("enumeration", kind.Constraint);
            Assert.EndsWith(" It is not one of the allowed values.", kind.Plain("en"));
            Assert.EndsWith(" No es uno de los valores permitidos.", kind.Plain("es"));
        }

        [Fact]
        public void Text_where_only_elements_are_allowed_is_said_and_an_unknown_message_is_shown_as_it_is()
        {
            var issue = Check("<library>stray text<book id=\"1\"><title>A</title><price>1</price></book></library>").Issues.First();
            Assert.Equal(XmlIssueKind.TextNotAllowed, issue.Kind);
            Assert.Equal("library", issue.Element);
            Assert.StartsWith("The element \"library\" cannot contain text here.", issue.Plain("en"));
            var other = XmlSchemaWords.Classify("Something new that the parser says.", 4, 2, null);
            Assert.Equal(XmlIssueKind.Other, other.Kind);
            Assert.Equal("Something new that the parser says.", other.Plain("fr"));
        }

        [Fact]
        public void Every_kind_is_said_in_French_Polish_and_Portuguese_too_and_other_languages_read_English()
        {
            var issue = Check("<library><book id=\"1\"><title>A</title></book></library>").Issues.First();
            Assert.StartsWith("L'élément \"book\" est incomplet", issue.Plain("fr"));
            Assert.StartsWith("Element \"book\" jest niekompletny", issue.Plain("pl"));
            Assert.StartsWith("O elemento \"book\" está incompleto", issue.Plain("pt-BR"));
            Assert.StartsWith("The element \"book\" is incomplete", issue.Plain("de"));
            foreach (var language in new[] { "en", "es", "fr", "pl", "pt" })
                foreach (var kind in Enum.GetValues<XmlIssueKind>().Where(k => k != XmlIssueKind.Other))
                {
                    var sample = new XmlSchemaIssue { Kind = kind, Element = "e", Parent = "p", Attribute = "a", Value = "v", Type = "t", Expected = "x", Constraint = "pattern" };
                    var text = sample.Plain(language);
                    Assert.DoesNotContain("{", text);
                    Assert.False(string.IsNullOrWhiteSpace(text), language + " " + kind);
                }
        }

        [Fact]
        public void Lines_and_positions_are_right_with_Windows_line_ends()
        {
            var issue = Check("<library>\r\n  <book id=\"1\">\r\n    <title>A</title>\r\n    <cost>1</cost>\r\n  </book>\r\n</library>").Issues.First();
            Assert.Equal(4, issue.Line);
            Assert.Equal(6, issue.Position);
        }

        [Fact]
        public void The_problems_are_listed_up_to_a_limit_and_a_very_big_text_is_not_checked()
        {
            var many = "<library>" + string.Concat(Enumerable.Repeat("<book><title>A</title><price>1</price></book>", XmlSchemaCheck.MaxIssues + 20)) + "</library>";
            var outcome = Check(many);
            Assert.Equal(XmlSchemaState.Invalid, outcome.State);
            Assert.Equal(XmlSchemaCheck.MaxIssues, outcome.Issues.Count);
            Assert.True(outcome.Truncated);
            var big = "<library>" + new string(' ', XmlSchemaCheck.MaxCharacters + 1) + "</library>";
            Assert.Equal(XmlSchemaState.TooBig, Check(big).State);
        }

        [Fact]
        public void A_schema_that_includes_or_imports_another_is_read_from_the_folder_of_the_first()
        {
            var folder = Folder();
            try
            {
                Write(folder, "types.xsd", "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:simpleType name=\"Code\"><xs:restriction base=\"xs:string\"><xs:pattern value=\"[A-Z]{3}\"/></xs:restriction></xs:simpleType></xs:schema>");
                var main = Write(folder, "main.xsd", "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:include schemaLocation=\"types.xsd\"/><xs:element name=\"item\" type=\"Code\"/></xs:schema>");
                Assert.Equal(XmlSchemaState.Valid, XmlSchemaCheck.Run("<item>ABC</item>", new[] { main }).State);
                var bad = XmlSchemaCheck.Run("<item>abc</item>", new[] { main });
                Assert.Equal(XmlSchemaState.Invalid, bad.State);
                Assert.Equal("pattern", bad.Issues[0].Constraint);
                Assert.EndsWith(" It does not follow the required pattern.", bad.Issues[0].Plain("en"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void A_schema_on_the_web_or_on_a_network_share_is_never_read()
        {
            var folder = Folder();
            try
            {
                var web = Write(folder, "web.xsd", "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:include schemaLocation=\"http://127.0.0.1:9/never.xsd\"/><xs:element name=\"a\" type=\"xs:string\"/></xs:schema>");
                var outcome = XmlSchemaCheck.Run("<a/>", new[] { web });
                Assert.Equal(XmlSchemaState.Unusable, outcome.State);
                Assert.Contains("on this computer", outcome.Problem);
                var share = Write(folder, "share.xsd", "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:include schemaLocation=\"file://server/share/never.xsd\"/><xs:element name=\"a\" type=\"xs:string\"/></xs:schema>");
                Assert.Equal(XmlSchemaState.Unusable, XmlSchemaCheck.Run("<a/>", new[] { share }).State);
                // a schema file that is not there, or not a schema, is unusable too, with the reason
                Assert.Equal(XmlSchemaState.Unusable, XmlSchemaCheck.Run("<a/>", new[] { Path.Combine(folder, "missing.xsd") }).State);
                Assert.Equal(XmlSchemaState.Unusable, XmlSchemaCheck.Run("<a/>", new[] { Write(folder, "broken.xsd", "<xs:schema") }).State);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void The_schema_that_the_text_names_is_found_next_to_the_file_and_a_web_address_is_not_fetched()
        {
            var folder = Folder();
            try
            {
                var xsd = Write(folder, "library.xsd", Library);
                var xml = Path.Combine(folder, "books.xml");
                var named = "<library xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"library.xsd\"/>";
                Assert.Equal(new[] { xsd }, XmlSchemaCheck.Detect(named, xml));
                var pair = "<library xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"urn:books library.xsd\"/>";
                Assert.Equal(new[] { xsd }, XmlSchemaCheck.Detect(pair, xml));
                var web = "<library xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"https://example.com/library.xsd\"/>";
                Assert.Empty(XmlSchemaCheck.Detect(web, xml));
                var share = "<library xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"\\\\server\\share\\library.xsd\"/>";
                Assert.Empty(XmlSchemaCheck.Detect(share, xml));
                Assert.Empty(XmlSchemaCheck.Detect(named.Replace("library.xsd", "missing.xsd"), xml));
                Assert.Empty(XmlSchemaCheck.Detect(named, null));       // an unsaved text has no folder to look in
                Assert.Empty(XmlSchemaCheck.Detect("<library", xml));   // not well-formed: nothing to read
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
