using System;
using System.IO;
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
            Assert.Contains("an XML document stored as text", md);
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
