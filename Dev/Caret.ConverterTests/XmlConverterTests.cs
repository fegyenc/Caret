using System;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Native XML files as readable Markdown (Services/Conversion/XmlConverter.cs, XmlUbl.cs): the Colombian electronic invoice (DIAN, UBL 2.1)
    // and the generic outline for any other XML. The samples in samples/ are synthetic (invented names, numbers and codes).
    public class XmlConverterTests
    {
        private static string ConvertText(string xml, string language = "en")
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

        [Fact]
        public void The_Colombian_invoice_in_its_container_reads_in_Spanish_as_the_expected_file()
        {
            var source = Path.Combine(TestPaths.Samples, "co-factura-adjunta.xml");
            var actual = DocumentConverter.Convert(source, new ConversionOptions { Language = "es" }).Markdown;
            var expectedPath = source + ".es.expected.md";
            if (Environment.GetEnvironmentVariable("CARET_UPDATE_SAMPLES") == "1") { File.WriteAllText(expectedPath, actual, new UTF8Encoding(false)); return; }
            Assert.True(File.Exists(expectedPath), "run with CARET_UPDATE_SAMPLES=1, read the result and commit it");
            Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n"), actual);
        }

        [Fact]
        public void The_invoice_inside_the_container_and_the_answer_of_the_authority_are_both_shown()
        {
            var md = DocumentConverter.Convert(Path.Combine(TestPaths.Samples, "co-factura-adjunta.xml"), new ConversionOptions { Language = "es" }).Markdown;
            Assert.Contains("Factura electrónica de venta SETP990000001", md);
            Assert.Contains("**Identificación:** 900000001-1 (NIT)", md);
            Assert.Contains("Respuesta de la DIAN", md);
            Assert.Contains("FAD00", md);
            Assert.Contains("1190000.00 COP", md);
            // the signature is named, not printed
            Assert.DoesNotContain("U2FtcGxlU2lnbmF0dXJlVmFsdWVOb3RSZWFs", md);
        }

        [Fact]
        public void Every_field_of_the_file_is_still_there_after_the_summary()
        {
            var md = DocumentConverter.Convert(Path.Combine(TestPaths.Samples, "co-nota-credito.xml"), new ConversionOptions { Language = "en" }).Markdown;
            Assert.Contains("## All fields of the file", md);
            Assert.Contains("**CreditNoteTypeCode**: 91", md); // a code the reader does not explain is not hidden
            Assert.Contains("Electronic credit note NCSE990000001", md);
            Assert.Contains("Related invoice:** SETP990000001", md);
        }

        [Fact]
        public void A_code_that_is_not_certain_is_printed_as_it_is_not_guessed()
        {
            var xml = "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
                      "<cbc:ID>X1</cbc:ID><cbc:InvoiceTypeCode>99</cbc:InvoiceTypeCode></Invoice>";
            Assert.Contains("# Invoice (type 99) X1", ConvertText(xml));
            Assert.Contains("# Factura (tipo 99) X1", ConvertText(xml, "es"));
        }

        [Fact]
        public void An_xml_file_that_no_pack_knows_gets_the_generic_outline_with_a_table_for_repeated_records()
        {
            var md = DocumentConverter.Convert(Path.Combine(TestPaths.Samples, "xml-generic.xml"), new ConversionOptions { Language = "en" }).Markdown;
            Assert.StartsWith("# Reporte", md);
            Assert.Contains("| Fecha | Concepto | Valor |", md);
            Assert.Contains("digital signature (not printed)", md);
            // an embedded value that cannot be read as one document keeps its text (it is not replaced by its length)
            Assert.Contains("Dato", md);
            Assert.Contains("Lorem ipsum", md);
            // an element with no content is still a field of the file
            Assert.Contains("**Vacio**", md);
        }

        [Fact]
        public void Text_between_child_elements_stays_where_it_is_and_a_table_does_not_move_other_fields()
        {
            var md = ConvertText("<r><m>Pay <a>10</a> now</m><Row><V>A</V></Row><Note>between</Note><Row><V>B</V></Row><Row><V>C</V></Row><Row><V>D</V></Row></r>");
            var pay = md.IndexOf("Pay", StringComparison.Ordinal);
            var a = md.IndexOf("**a**: 10", StringComparison.Ordinal);
            var now = md.IndexOf("now", StringComparison.Ordinal);
            Assert.True(pay > 0 && a > pay && now > a, md);
            // the Note stands between the first Row and the run of three rows, so it is listed before the table (which is made of the three)
            Assert.True(md.IndexOf("**Note**: between", StringComparison.Ordinal) < md.IndexOf("| V |", StringComparison.Ordinal), md);
        }

        [Fact]
        public void The_attributes_of_the_rows_of_a_table_are_columns()
        {
            var md = ConvertText("<r><Row id=\"A\"><V>1</V></Row><Row id=\"B\"><V>2</V></Row><Row id=\"C\"><V>3</V></Row></r>");
            Assert.Contains("| @id | V |", md);
            Assert.Contains("| B | 2 |", md);
        }

        [Fact]
        public void A_file_over_the_size_limit_is_refused_with_its_own_reason_not_as_a_damaged_file()
        {
            using var big = new BigStream(61L * 1024 * 1024);
            var ex = Assert.ThrowsAny<Exception>(() => XmlConverter.Convert(big, new ConversionContext(new ConversionOptions())));
            Assert.IsNotType<InvalidDataException>(ex);
            Assert.Contains("too big", ex.Message);
        }

        // A stream that says it is that long (it is never read: the size is checked first).
        private sealed class BigStream : Stream
        {
            private readonly long length;
            public BigStream(long length) => this.length = length;
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position { get => 0; set { } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) { }
        }

        [Fact]
        public void A_huge_attribute_value_is_cut_and_a_line_break_in_it_does_not_split_the_item()
        {
            var md = ConvertText("<r><a v=\"line one&#10;line two\" big=\"" + new string('x', 5000) + "\">text</a></r>");
            Assert.Contains("v=line one line two", md);
            Assert.Contains("more characters", md);
            Assert.True(md.Length < 4000, md.Length.ToString());
        }

        [Fact]
        public void A_file_nested_far_deeper_than_can_be_followed_is_cut_with_a_note_and_does_not_crash()
        {
            var xml = new string('<', 0) + string.Concat(Enumerable.Range(0, 400).Select(i => "<e>")) + "x" + string.Concat(Enumerable.Range(0, 400).Select(i => "</e>"));
            var md = ConvertText(xml);
            Assert.Contains("nesting too deep", md);
        }

        [Fact]
        public void A_document_is_taken_for_UBL_only_when_its_name_and_namespace_go_together()
        {
            var wrong = "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2\"><ID>X</ID></Invoice>";
            Assert.DoesNotContain("All fields of the file", ConvertText(wrong));
            // an unrelated Invoice stored as text in a container does not get the fiscal heading
            var container = "<AttachedDocument xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2\" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
                            "<cac:Attachment><cac:ExternalReference><cbc:Description><![CDATA[<Invoice><ID>X</ID></Invoice>]]></cbc:Description></cac:ExternalReference></cac:Attachment></AttachedDocument>";
            var md = ConvertText(container, "es");
            Assert.DoesNotContain("Factura electrónica", md);
            Assert.DoesNotContain("Respuesta de la DIAN", md);
        }

        [Fact]
        public void A_note_with_missing_identifiers_and_two_tax_rates_is_read_without_judging_the_totals()
        {
            // modelled on the scenarios of a third-party test generator (a minimal invoice, two tax rates, missing identifiers, wrong totals)
            var xml = "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
                      "<cbc:ID>T-1</cbc:ID><cbc:DocumentCurrencyCode>COP</cbc:DocumentCurrencyCode>" +
                      "<cac:AccountingSupplierParty><cac:Party><cac:PartyName><cbc:Name>Seller</cbc:Name></cac:PartyName></cac:Party></cac:AccountingSupplierParty>" +
                      "<cac:TaxTotal><cac:TaxSubtotal><cbc:TaxableAmount currencyID=\"COP\">100</cbc:TaxableAmount><cbc:TaxAmount currencyID=\"COP\">19</cbc:TaxAmount><cac:TaxCategory><cbc:Percent>19</cbc:Percent><cac:TaxScheme><cbc:ID>01</cbc:ID></cac:TaxScheme></cac:TaxCategory></cac:TaxSubtotal>" +
                      "<cac:TaxSubtotal><cbc:TaxableAmount currencyID=\"COP\">50</cbc:TaxableAmount><cbc:TaxAmount currencyID=\"COP\">2.5</cbc:TaxAmount><cac:TaxCategory><cbc:Percent>5</cbc:Percent><cac:TaxScheme><cbc:ID>01</cbc:ID></cac:TaxScheme></cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>" +
                      "<cac:LegalMonetaryTotal><cbc:PayableAmount currencyID=\"COP\">999</cbc:PayableAmount></cac:LegalMonetaryTotal></Invoice>";
            var md = ConvertText(xml, "es");
            Assert.Contains("| 01 · IVA | 100 COP | 19 % | 19 COP |", md);
            Assert.Contains("| 01 · IVA | 50 COP | 5 % | 2.5 COP |", md);
            Assert.Contains("**Total a pagar:** 999 COP", md); // the total is shown as written, whatever the lines add up to
            Assert.DoesNotContain("Identificación", md);       // no tax ID in the file: no line is made up
        }

        [Fact]
        public void An_Invoice_that_is_not_in_the_UBL_namespace_is_not_taken_for_one()
        {
            var md = ConvertText("<Invoice><Number>7</Number></Invoice>");
            Assert.StartsWith("# Invoice", md);
            Assert.Contains("**Number**: 7", md);
            Assert.DoesNotContain("All fields of the file", md);
        }

        [Fact]
        public void A_file_that_is_not_well_formed_is_refused_with_the_place_of_the_error()
        {
            var ex = Assert.Throws<InvalidDataException>(() => ConvertText("<a>\n  <b></a>"));
            Assert.Contains("line 2", ex.Message);
        }

        [Fact]
        public void A_file_that_asks_for_another_file_through_a_DTD_is_refused_and_reads_nothing()
        {
            var secret = TestPaths.NewTempFolder();
            try
            {
                var target = Path.Combine(secret, "secret.txt");
                File.WriteAllText(target, "TOP-SECRET-CONTENT");
                var xml = "<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY x SYSTEM \"" + new Uri(target).AbsoluteUri + "\">]><a>&x;</a>";
                var ex = Assert.Throws<InvalidDataException>(() => ConvertText(xml));
                Assert.DoesNotContain("TOP-SECRET-CONTENT", ex.Message);
            }
            finally { Directory.Delete(secret, true); }
        }

        [Fact]
        public void The_readable_text_does_not_depend_on_the_regional_settings_of_the_PC()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var german = DocumentConverter.Convert(Path.Combine(TestPaths.Samples, "co-factura-adjunta.xml"), new ConversionOptions { Language = "es" }).Markdown;
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
                var english = DocumentConverter.Convert(Path.Combine(TestPaths.Samples, "co-factura-adjunta.xml"), new ConversionOptions { Language = "es" }).Markdown;
                Assert.Equal(english, german);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }
    }
}
