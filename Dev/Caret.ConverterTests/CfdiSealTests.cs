using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Typedown.WinUI.Services.Conversion;
using Typedown.WinUI.Services.XmlEditing;
using Xunit;

namespace Caret.ConverterTests
{
    // The seal of a Mexican CFDI (Services/XmlEditing/CfdiSeal.cs), checked with the SAT's own stylesheets for the cadena original. The fixture is a
    // CFDI 4.0 sealed with the SAT's public test certificate (sat-fixtures/README.md): a seal made by another program, so the cadena original made here
    // is checked against it, not against itself.
    public class CfdiSealTests
    {
        private static string Fixture() => File.ReadAllText(Path.Combine(TestPaths.ProjectFolder, "sat-fixtures", "cfdi40-valid.xml"));

        private static string Attribute(string xml, string name) => Regex.Match(xml, "\\s" + name + "=\"([^\"]*)\"").Groups[1].Value;

        [Fact]
        public void A_cfdi_sealed_by_its_issuer_is_valid_and_names_the_holder_of_the_certificate()
        {
            var results = XmlSignatureCheck.Check(Fixture());
            var seal = Assert.Single(results);
            Assert.Equal(XmlSignatureKind.CfdiSeal, seal.Kind);
            Assert.Equal(XmlSignatureState.Valid, seal.State);
            Assert.Equal("ESCUELA KEMPER URGATE SA DE CV", seal.Signer);
            Assert.Equal("EKU9003173C9", seal.SignerId);
            Assert.Equal("EKU9003173C9", seal.IssuerId);
            Assert.True(seal.SameAsIssuer);
            Assert.True(seal.CertificateMatches);
            Assert.Equal("valid", XmlSignatureCheck.Overall(results));
        }

        [Fact]
        public void A_value_changed_after_sealing_is_found()
        {
            var xml = Fixture();
            var total = Attribute(xml, "Total");
            var changed = xml.Replace("Total=\"" + total + "\"", "Total=\"" + total + "1\"");
            Assert.NotEqual(xml, changed);
            Assert.Equal("changed", XmlSignatureCheck.Overall(XmlSignatureCheck.Check(changed)));

            // a value of a line, read by the stylesheet too
            var description = Attribute(xml, "Descripcion");
            Assert.Equal("changed", XmlSignatureCheck.Overall(XmlSignatureCheck.Check(xml.Replace("Descripcion=\"" + description + "\"", "Descripcion=\"" + description + " X\""))));
        }

        [Fact]
        public void Spaces_and_line_breaks_outside_the_values_do_not_count()
        {
            // the cadena original is made of the values: the layout of the file is not sealed
            var original = Fixture();
            var xml = Regex.Replace(original, @">\s*<", ">\n\t\t  <");
            Assert.NotEqual(original, xml);
            Assert.Equal("valid", XmlSignatureCheck.Overall(XmlSignatureCheck.Check(xml)));
        }

        [Fact]
        public void Text_inside_the_stamp_is_no_part_of_the_seal()
        {
            // the stamp is added after the seal; whatever it holds must not reach the cadena original
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(Fixture());
            var stamp = document.GetElementsByTagName("TimbreFiscalDigital", "http://www.sat.gob.mx/TimbreFiscalDigital").OfType<XmlElement>().Single();
            stamp.AppendChild(document.CreateTextNode("text added by a program"));
            Assert.Equal("valid", XmlSignatureCheck.Overall(XmlSignatureCheck.Check(document.OuterXml)));
        }

        [Fact]
        public void A_value_with_a_greater_than_sign_before_the_seal_still_counts_as_sealed()
        {
            var xml = Fixture();
            const string version = "Version=\"4.0\"";
            Assert.Contains(version, xml);
            var withSerie = xml.Replace(version, version + " Serie=\"A>B\"");
            Assert.True(CfdiSeal.LooksSealed(withSerie));
            Assert.True(XmlAnalysis.Of(withSerie, "en", "cfdi.xml").Signed);
        }

        [Fact]
        public void A_namespace_written_with_character_references_still_counts_as_sealed()
        {
            const string ns = "http://www.sat.gob.mx/cfd/4";
            var xml = Fixture().Replace(ns, "http:&#x2F;&#x2F;www.sat.gob.mx/cfd/4");
            Assert.DoesNotContain(ns, xml);
            // read as XML: the parser turns the references back into the namespace
            Assert.True(CfdiSeal.LooksSealed(xml));
            Assert.True(XmlAnalysis.LooksSigned(xml));
            Assert.True(XmlAnalysis.Of(xml, "en", "cfdi.xml").Signed);
            Assert.Equal("valid", XmlSignatureCheck.Overall(XmlSignatureCheck.Check(xml)));
            // a text whose start is not read as XML (here a DOCTYPE, which is never read) falls back to looking at the text
            Assert.True(CfdiSeal.LooksSealed("<!DOCTYPE x>" + Fixture()));
        }

        [Fact]
        public void A_cfdi_of_a_version_without_stylesheet_is_sealed_but_not_checked()
        {
            var xml = Fixture().Replace("http://www.sat.gob.mx/cfd/4", "http://www.sat.gob.mx/cfd/3").Replace("Version=\"4.0\"", "Version=\"3.2\"");
            Assert.True(CfdiSeal.LooksSealed(xml));
            Assert.True(XmlAnalysis.Of(xml, "en", "cfdi.xml").Signed);
            var result = Assert.Single(XmlSignatureCheck.Check(xml));
            Assert.Equal(XmlSignatureKind.CfdiSeal, result.Kind);
            Assert.Equal(XmlSignatureState.NotChecked, result.State);
            Assert.Equal(XmlSignatureProblem.Unsupported, result.Problem);
        }

        [Fact]
        public void A_stamp_of_another_seal_is_found()
        {
            var xml = Fixture();
            var stamped = Attribute(xml, "SelloCFD");
            Assert.False(string.IsNullOrEmpty(stamped));
            var other = xml.Replace("SelloCFD=\"" + stamped + "\"", "SelloCFD=\"" + (stamped[0] == 'A' ? 'B' : 'A') + stamped.Substring(1) + "\"");
            var results = XmlSignatureCheck.Check(other);
            Assert.Equal(2, results.Count);
            Assert.Equal(XmlSignatureState.Valid, results[0].State);
            Assert.Equal(XmlSignatureKind.CfdiStamp, results[1].Kind);
            Assert.Equal("changed", XmlSignatureCheck.Overall(results));
        }

        [Fact]
        public void Without_a_certificate_or_with_a_damaged_one_the_seal_is_not_checked()
        {
            var xml = Fixture();
            var certificate = Attribute(xml, "Certificado");
            var none = xml.Replace("Certificado=\"" + certificate + "\"", "Certificado=\"\"");
            Assert.Equal(XmlSignatureProblem.NoCertificate, XmlSignatureCheck.Check(none)[0].Problem);
            var damaged = xml.Replace("Certificado=\"" + certificate + "\"", "Certificado=\"AAAA\"");
            Assert.Equal(XmlSignatureProblem.BadCertificate, XmlSignatureCheck.Check(damaged)[0].Problem);
            var seal = Attribute(xml, "Sello");
            var notBase64 = xml.Replace("Sello=\"" + seal + "\"", "Sello=\"not a seal!\"");
            Assert.Equal(XmlSignatureProblem.Damaged, XmlSignatureCheck.Check(notBase64)[0].Problem);
        }

        [Fact]
        public void Another_certificate_number_than_the_file_names_is_said()
        {
            var xml = Fixture();
            var number = Attribute(xml, "NoCertificado");
            var result = XmlSignatureCheck.Check(xml.Replace("NoCertificado=\"" + number + "\"", "NoCertificado=\"00001000000000000000\""))[0];
            Assert.False(result.CertificateMatches);
        }

        [Fact]
        public void A_cfdi_counts_as_signed_before_a_save()
        {
            Assert.True(XmlAnalysis.LooksSigned(Fixture()));
            Assert.True(XmlAnalysis.Of(Fixture(), "en", "cfdi.xml").Signed);
            Assert.False(XmlAnalysis.LooksSigned("<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Version=\"4.0\" Total=\"1\"/>"));
        }

        [Fact]
        public void The_folder_table_shows_the_seal()
        {
            var result = new InvoiceTableResult();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(Fixture()))) InvoiceTable.Add(result, stream, "cfdi.xml");
            InvoiceTable.Finish(result);
            Assert.Equal("valid", Assert.Single(result.Rows).Signature);
        }

        [Fact]
        public void The_cadena_original_starts_and_ends_with_two_bars()
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(Fixture());
            var cadena = CfdiSeal.CadenaOriginal(document, "cfd/4/cadenaoriginal_4_0/cadenaoriginal_4_0.xslt");
            Assert.StartsWith("||4.0|", cadena);
            Assert.EndsWith("||", cadena);
            Assert.Contains("|EKU9003173C9|", cadena);
        }
    }
}
