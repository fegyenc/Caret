using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Typedown.WinUI.Services.XmlEditing;
using Xunit;

namespace Caret.ConverterTests
{
    // The check of the signature of an electronic invoice. The documents are signed here, with certificates made for the test (a real one cannot
    // be in the repository), in the shapes the invoices have: an NF-e signs its infNFe by its Id and the authority signs its answer apart; a UBL
    // invoice (DIAN, SUNAT) signs the whole document, with XAdES properties as a second reference.
    public class XmlSignatureCheckTests
    {
        private const string NfeId = "NFe35260312345678000195550010000001231000000019";

        private static string Nfe(string cnpj = "12345678000195", string total = "100.00") =>
            "<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"4.00\"><NFe>" +
            $"<infNFe Id=\"{NfeId}\" versao=\"4.00\"><ide><nNF>123</nNF></ide><emit><CNPJ>{cnpj}</CNPJ><xNome>EMPRESA TESTE LTDA</xNome></emit>" +
            $"<total><ICMSTot><vNF>{total}</vNF></ICMSTot></total></infNFe></NFe>" +
            "<protNFe versao=\"4.00\"><infProt Id=\"ID135260000000001\"><nProt>135260000000001</nProt><cStat>100</cStat></infProt></protNFe></nfeProc>";

        private const string Ubl =
            "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\" " +
            "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"><ext:UBLExtensions><ext:UBLExtension><ext:ExtensionContent/></ext:UBLExtension></ext:UBLExtensions>" +
            "<cbc:ID>SETP990000001</cbc:ID><cbc:PayableAmount>1190.00</cbc:PayableAmount></Invoice>";

        private static X509Certificate2 Certificate(string commonName)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={commonName}, O=Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }

        // Signs the element with this Id (or the whole document) and puts the signature at the end of the first element with that local name.
        private static string Sign(string xml, string id, string into, X509Certificate2 certificate, bool sha1 = true, bool xadesProperties = false)
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(xml);
            var signed = new SignedXml(document) { SigningKey = certificate.GetRSAPrivateKey() };
            signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
            signed.SignedInfo.SignatureMethod = sha1 ? SignedXml.XmlDsigRSASHA1Url : SignedXml.XmlDsigRSASHA256Url;
            var reference = new Reference(id == null ? "" : "#" + id) { DigestMethod = sha1 ? SignedXml.XmlDsigSHA1Url : SignedXml.XmlDsigSHA256Url };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            signed.AddReference(reference);
            if (xadesProperties)
            {
                // A real XAdES signature carries its properties in a ds:Object of the signature; SignedXml cannot sign a reference into an object it is
                // still building, so here they are in the document already (a second extension). Checking finds them by Id in the document either way.
                var properties = new XmlDocument { PreserveWhitespace = true };
                properties.LoadXml("<xades:QualifyingProperties xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\"><xades:SignedProperties Id=\"xmldsig-1-signedprops\">" +
                                   "<xades:SignedSignatureProperties><xades:SigningTime>2026-03-10T09:00:00-05:00</xades:SigningTime></xades:SignedSignatureProperties>" +
                                   "</xades:SignedProperties></xades:QualifyingProperties>");
                var extensions = document.GetElementsByTagName("*").OfType<XmlElement>().First(e => e.LocalName == "UBLExtensions");
                var extension = document.CreateElement("ext", "UBLExtension", extensions.NamespaceURI);
                var content = document.CreateElement("ext", "ExtensionContent", extensions.NamespaceURI);
                content.AppendChild(document.ImportNode(properties.DocumentElement!, true));
                extension.AppendChild(content);
                extensions.AppendChild(extension);
                signed.AddReference(new Reference("#xmldsig-1-signedprops") { DigestMethod = SignedXml.XmlDsigSHA256Url });
            }
            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signed.KeyInfo = keyInfo;
            signed.ComputeSignature();
            var host = document.GetElementsByTagName("*").OfType<XmlElement>().First(e => e.LocalName == into);
            host.AppendChild(document.ImportNode(signed.GetXml(), true));
            return document.OuterXml;
        }

        [Fact]
        public void An_nfe_signed_by_its_issuer_is_valid_and_names_the_signer()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var result = Assert.Single(XmlSignatureCheck.Check(Sign(Nfe(), NfeId, "NFe", certificate)));

            Assert.Equal(XmlSignatureState.Valid, result.State);
            Assert.Equal("infNFe", result.Signs);
            Assert.Equal("EMPRESA TESTE LTDA", result.Signer);
            Assert.Equal("EMPRESA TESTE LTDA", result.IssuedBy);   // self-signed here: the issuer's name loses its ":CNPJ" ending too
            Assert.Equal("12345678000195", result.SignerId);
            Assert.Equal("12345678000195", result.IssuerId);
            Assert.True(result.SameAsIssuer);
            Assert.Equal(new DateTime(2026, 1, 1), result.ValidFrom!.Value.ToUniversalTime().Date);
        }

        [Fact]
        public void A_branch_signs_for_the_same_company_but_another_company_does_not()
        {
            using var branch = Certificate("EMPRESA TESTE LTDA FILIAL:12345678000276");
            Assert.True(XmlSignatureCheck.Check(Sign(Nfe(), NfeId, "NFe", branch))[0].SameAsIssuer);

            using var other = Certificate("OUTRA EMPRESA SA:98765432000110");
            var result = XmlSignatureCheck.Check(Sign(Nfe(), NfeId, "NFe", other))[0];
            Assert.Equal(XmlSignatureState.Valid, result.State);
            Assert.False(result.SameAsIssuer);
        }

        [Fact]
        public void A_value_changed_after_signing_is_found()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var signed = Sign(Nfe(), NfeId, "NFe", certificate);

            var changed = signed.Replace("<vNF>100.00</vNF>", "<vNF>900.00</vNF>");
            Assert.Equal(XmlSignatureState.Changed, XmlSignatureCheck.Check(changed)[0].State);

            // the signature itself
            var value = XmlSignatureCheck_SignatureValue(signed);
            var forged = signed.Replace(value, (value[0] == 'A' ? 'B' : 'A') + value.Substring(1));
            Assert.Equal(XmlSignatureState.Changed, XmlSignatureCheck.Check(forged)[0].State);
        }

        private static string XmlSignatureCheck_SignatureValue(string xml)
        {
            var start = xml.IndexOf("<SignatureValue>", StringComparison.Ordinal) + "<SignatureValue>".Length;
            return xml.Substring(start, xml.IndexOf("</SignatureValue>", StringComparison.Ordinal) - start);
        }

        [Fact]
        public void A_change_outside_the_signed_part_leaves_it_valid()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var signed = Sign(Nfe(), NfeId, "NFe", certificate);
            var protocolChanged = signed.Replace("<cStat>100</cStat>", "<cStat>101</cStat>");
            Assert.Equal(XmlSignatureState.Valid, XmlSignatureCheck.Check(protocolChanged)[0].State);
        }

        [Fact]
        public void The_answer_of_the_authority_is_a_signature_of_its_own()
        {
            using var issuer = Certificate("EMPRESA TESTE LTDA:12345678000195");
            using var authority = Certificate("SECRETARIA DA FAZENDA");
            var both = Sign(Sign(Nfe(), NfeId, "NFe", issuer), "ID135260000000001", "protNFe", authority);

            var results = XmlSignatureCheck.Check(both);
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(XmlSignatureState.Valid, r.State));
            Assert.Equal("infProt", results[1].Signs);
            Assert.Equal("SECRETARIA DA FAZENDA", results[1].Signer);
            Assert.Null(results[1].SameAsIssuer);   // the answer has no issuer of its own to compare with
        }

        [Fact]
        public void A_ubl_invoice_signed_whole_with_xades_properties_is_checked()
        {
            using var certificate = Certificate("Comercializadora Ejemplo S.A.S.");
            var signed = Sign(Ubl, null, "ExtensionContent", certificate, sha1: false, xadesProperties: true);

            var result = Assert.Single(XmlSignatureCheck.Check(signed));
            Assert.Equal(XmlSignatureState.Valid, result.State);
            Assert.Equal("", result.Signs);
            Assert.Null(result.SameAsIssuer);

            Assert.Equal(XmlSignatureState.Changed, XmlSignatureCheck.Check(signed.Replace("1190.00", "1.00"))[0].State);
            Assert.Equal(XmlSignatureState.Changed, XmlSignatureCheck.Check(signed.Replace("2026-03-10T09:00:00", "2026-03-11T09:00:00"))[0].State);
        }

        [Fact]
        public void What_cannot_be_checked_says_why()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var signed = Sign(Nfe(), NfeId, "NFe", certificate);

            var start = signed.IndexOf("<KeyInfo>", StringComparison.Ordinal);
            var noKey = signed.Remove(start, signed.IndexOf("</KeyInfo>", StringComparison.Ordinal) + "</KeyInfo>".Length - start);
            Assert.Equal(XmlSignatureProblem.NoCertificate, XmlSignatureCheck.Check(noKey)[0].Problem);

            var certStart = signed.IndexOf("<X509Certificate>", StringComparison.Ordinal) + "<X509Certificate>".Length;
            var badCert = signed.Remove(certStart, signed.IndexOf("</X509Certificate>", StringComparison.Ordinal) - certStart).Insert(certStart, "QUJD");
            Assert.Equal(XmlSignatureProblem.BadCertificate, XmlSignatureCheck.Check(badCert)[0].Problem);

            // a PFX (a certificate with its private key) in place of the certificate is not loaded
            var pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "x"));
            var withPfx = signed.Remove(certStart, signed.IndexOf("</X509Certificate>", StringComparison.Ordinal) - certStart).Insert(certStart, pfx);
            Assert.Equal(XmlSignatureProblem.BadCertificate, XmlSignatureCheck.Check(withPfx)[0].Problem);

            // nothing is fetched from outside the file
            var outside = signed.Replace($"URI=\"#{NfeId}\"", "URI=\"http://example.invalid/nota.xml\"");
            var result = XmlSignatureCheck.Check(outside)[0];
            Assert.Equal(XmlSignatureState.NotChecked, result.State);
            Assert.Equal(XmlSignatureProblem.ReferenceOutside, result.Problem);
        }

        [Fact]
        public void The_certificate_that_verifies_is_the_signers_when_the_authoritys_comes_first()
        {
            using var signer = Certificate("EMPRESA TESTE LTDA:12345678000195");
            using var authority = Certificate("AC TESTE v5");
            var signed = Sign(Nfe(), NfeId, "NFe", signer);
            var chain = signed.Replace("<X509Certificate>", $"<X509Certificate>{Convert.ToBase64String(authority.RawData)}</X509Certificate><X509Certificate>");

            var result = XmlSignatureCheck.Check(chain)[0];
            Assert.Equal(XmlSignatureState.Valid, result.State);
            Assert.Equal("EMPRESA TESTE LTDA", result.Signer);
        }

        [Fact]
        public void A_pfx_anywhere_among_the_certificates_is_never_loaded()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var signed = Sign(Nfe(), NfeId, "NFe", certificate);
            var pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx));   // no password: it would load with its key
            var second = signed.Replace("</X509Certificate>", $"</X509Certificate><X509Certificate>{pfx}</X509Certificate>");
            Assert.Equal(XmlSignatureProblem.BadCertificate, XmlSignatureCheck.Check(second)[0].Problem);
        }

        [Fact]
        public void A_transform_that_is_not_checked_is_not_called_a_change()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var signed = Sign(Nfe(), NfeId, "NFe", certificate);
            var xpath = signed.Replace("<Transforms>",
                "<Transforms><Transform Algorithm=\"http://www.w3.org/TR/1999/REC-xpath-19991116\"><XPath>not(ancestor-or-self::Signature)</XPath></Transform>");
            var result = XmlSignatureCheck.Check(xpath)[0];
            Assert.Equal(XmlSignatureState.NotChecked, result.State);
            Assert.Equal(XmlSignatureProblem.Unsupported, result.Problem);
        }

        [Fact]
        public void A_damaged_signature_value_spoils_only_its_own_signature()
        {
            using var issuer = Certificate("EMPRESA TESTE LTDA:12345678000195");
            using var authority = Certificate("SECRETARIA DA FAZENDA");
            var both = Sign(Sign(Nfe(), NfeId, "NFe", issuer), "ID135260000000001", "protNFe", authority);
            var value = XmlSignatureCheck_SignatureValue(both);   // the first one, the issuer's
            var damaged = both.Replace(value, "!!! not base64 !!!");

            var results = XmlSignatureCheck.Check(damaged);
            Assert.Equal(2, results.Count);
            Assert.Equal(XmlSignatureProblem.Damaged, results[0].Problem);
            Assert.Equal(XmlSignatureState.Valid, results[1].State);
        }

        [Fact]
        public void An_id_that_two_elements_carry_is_not_checked()
        {
            using var certificate = Certificate("EMPRESA TESTE LTDA:12345678000195");
            var signed = Sign(Nfe(), NfeId, "NFe", certificate);
            // an element with the same Id in another spelling, before the signed one: the verifier and the description could take different ones
            var twice = signed.Replace("<NFe>", $"<NFe><infAdic ID=\"{NfeId}\"><emit><CNPJ>98765432000110</CNPJ></emit></infAdic>");
            var result = XmlSignatureCheck.Check(twice)[0];
            Assert.Equal(XmlSignatureState.NotChecked, result.State);
            Assert.Equal(XmlSignatureProblem.Unsupported, result.Problem);
        }

        [Fact]
        public void The_strip_says_the_worst_of_all_signatures()
        {
            XmlSignatureResult Valid(bool? same = true) => new() { State = XmlSignatureState.Valid, SameAsIssuer = same };
            var changed = new XmlSignatureResult { State = XmlSignatureState.Changed };
            var notChecked = new XmlSignatureResult { State = XmlSignatureState.NotChecked, Problem = XmlSignatureProblem.NoCertificate };

            Assert.Null(XmlSignatureCheck.Overall(Array.Empty<XmlSignatureResult>()));
            Assert.Equal("valid", XmlSignatureCheck.Overall(new[] { Valid(), Valid(null) }));
            Assert.Equal("other", XmlSignatureCheck.Overall(new[] { Valid(false), Valid(null) }));
            Assert.Equal("changed", XmlSignatureCheck.Overall(new[] { Valid(), changed, notChecked }));
            Assert.Equal("unknown", XmlSignatureCheck.Overall(new[] { Valid(), notChecked }));
        }

        [Fact]
        public void A_cnpj_and_a_cpf_are_written_as_people_write_them()
        {
            Assert.Equal("12.345.678/0001-95", XmlSignatureCheck.FormatId("12345678000195"));
            Assert.Equal("123.456.789-01", XmlSignatureCheck.FormatId("12345678901"));
            Assert.Equal("900000001", XmlSignatureCheck.FormatId("900000001"));
            Assert.Null(XmlSignatureCheck.FormatId(null));
        }

        [Fact]
        public void A_text_without_a_signature_or_not_well_formed_has_none()
        {
            Assert.Empty(XmlSignatureCheck.Check(Nfe()));
            Assert.Empty(XmlSignatureCheck.Check("<nfeProc><NFe>"));
            Assert.Empty(XmlSignatureCheck.Check(""));
        }
    }
}
