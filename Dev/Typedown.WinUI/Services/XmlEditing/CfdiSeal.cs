using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;

namespace Typedown.WinUI.Services.XmlEditing
{
    // The seal (sello) of a Mexican CFDI 4.0 or 3.3: not an XML signature, but an RSA signature (SHA-256) of the "cadena original", a text that the
    // SAT's own XSLT stylesheets make from the invoice (Anexo 20). The stylesheets are bundled (Sat/cfd, copied from the SAT's site through the
    // phpcfdi/resources-sat-xml mirror; see THIRD-PARTY-NOTICES), so the check runs offline: the cadena original of the file, verified with the public
    // key of the certificate (CSD) the file carries. What is not checked, and said so: whether the certificate was revoked or is the SAT's, and the
    // SAT's own stamp (the Timbre Fiscal Digital is signed with a SAT certificate that is not in the file); the stamp is only checked to belong to this
    // seal (its SelloCFD is the seal of the file).
    internal static class CfdiSeal
    {
        private const string Cfdi4 = "http://www.sat.gob.mx/cfd/4";
        private const string Cfdi3 = "http://www.sat.gob.mx/cfd/3";
        private const string TfdNamespace = "http://www.sat.gob.mx/TimbreFiscalDigital";
        private const string ResourcePrefix = "Sat/";

        // A sealed CFDI: the root is cfdi:Comprobante 4.0 or 3.3 with a Sello.
        public static bool IsSealed(XmlDocument document) =>
            document.DocumentElement is { LocalName: "Comprobante" } root && StylesheetOf(root) != null && root.HasAttribute("Sello");

        // A quick look at a text, also one that is not well-formed (before a save): the namespace of a CFDI and a Sello on its Comprobante. The
        // attributes are read quote by quote, so a value with a ">" in it (Serie="A>B") does not end the tag.
        public static bool LooksSealed(string text) =>
            text != null && (text.Contains(Cfdi4, StringComparison.Ordinal) || text.Contains(Cfdi3, StringComparison.Ordinal))
            && Regex.IsMatch(text, @"<(?:[\w.\-]+:)?Comprobante\b(?:[^>""']|""[^""]*""|'[^']*')*?\sSello\s*=", RegexOptions.CultureInvariant);

        // The same on a document already read: the root is a CFDI 4.0 or 3.3 Comprobante with a Sello.
        public static bool IsSealed(System.Xml.Linq.XElement root) =>
            root is { Name.LocalName: "Comprobante" } && (root.Name.NamespaceName is Cfdi4 or Cfdi3) && root.Attribute("Sello") != null;

        private static string StylesheetOf(XmlElement root) => (root.NamespaceURI, root.GetAttribute("Version")) switch
        {
            (Cfdi4, "4.0") => "cfd/4/cadenaoriginal_4_0/cadenaoriginal_4_0.xslt",
            (Cfdi3, "3.3") => "cfd/3/cadenaoriginal_3_3/cadenaoriginal_3_3.xslt",
            _ => null,
        };

        // The seal and, when the file is stamped, whether the stamp belongs to it (a second result only when it does not).
        public static XmlSignatureResult[] Check(XmlDocument document)
        {
            var root = document.DocumentElement!;
            var issuerRfc = root.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Emisor" && e.NamespaceURI == root.NamespaceURI)?.GetAttribute("Rfc");
            var seal = CheckSeal(document, root, string.IsNullOrEmpty(issuerRfc) ? null : issuerRfc.Trim());
            var stamp = root.GetElementsByTagName("TimbreFiscalDigital", TfdNamespace).OfType<XmlElement>().FirstOrDefault();
            if (stamp == null || !stamp.HasAttribute("SelloCFD")) return new[] { seal };
            var same = string.Equals(Compact(stamp.GetAttribute("SelloCFD")), Compact(root.GetAttribute("Sello")), StringComparison.Ordinal);
            return same ? new[] { seal } : new[] { seal, new XmlSignatureResult { State = XmlSignatureState.Changed, Signs = "TimbreFiscalDigital", Kind = XmlSignatureKind.CfdiStamp } };
        }

        private static string Compact(string base64) => Regex.Replace(base64 ?? "", @"\s", "");

        private static XmlSignatureResult NotChecked(XmlSignatureProblem problem) =>
            new() { State = XmlSignatureState.NotChecked, Problem = problem, Signs = "Comprobante", Kind = XmlSignatureKind.CfdiSeal };

        private static XmlSignatureResult CheckSeal(XmlDocument document, XmlElement root, string issuerRfc)
        {
            var stylesheet = StylesheetOf(root);
            if (stylesheet == null) return NotChecked(XmlSignatureProblem.Unsupported);
            var certificateText = Compact(root.GetAttribute("Certificado"));
            if (certificateText.Length == 0) return NotChecked(XmlSignatureProblem.NoCertificate);
            byte[] sealBytes;
            try { sealBytes = Convert.FromBase64String(Compact(root.GetAttribute("Sello"))); }
            catch (FormatException) { return NotChecked(XmlSignatureProblem.Damaged); }

            X509Certificate2 certificate;
            try
            {
                var bytes = Convert.FromBase64String(certificateText);
                // a plain certificate only, never a PFX bundle (one with a private key would be imported with its key)
                if (X509Certificate2.GetCertContentType(bytes) != X509ContentType.Cert) return NotChecked(XmlSignatureProblem.BadCertificate);
                certificate = new X509Certificate2(bytes);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException) { return NotChecked(XmlSignatureProblem.BadCertificate); }

            using (certificate)
            {
                string cadena;
                try { cadena = CadenaOriginal(document, stylesheet); }
                catch (Exception ex) when (ex is XsltException or XmlException or IOException or InvalidOperationException) { return NotChecked(XmlSignatureProblem.Unsupported); }

                // Windows can refuse to import the key of a certificate it could read
                RSA rsa;
                try { rsa = certificate.GetRSAPublicKey(); }
                catch (CryptographicException) { return NotChecked(XmlSignatureProblem.BadCertificate); }
                if (rsa == null) return NotChecked(XmlSignatureProblem.BadCertificate);
                using var key = rsa;
                bool valid;
                try { valid = rsa.VerifyData(Encoding.UTF8.GetBytes(cadena), sealBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
                catch (CryptographicException) { valid = false; }

                var signerRfc = RfcOf(certificate);
                return new XmlSignatureResult
                {
                    State = valid ? XmlSignatureState.Valid : XmlSignatureState.Changed,
                    Signs = "Comprobante",
                    Kind = XmlSignatureKind.CfdiSeal,
                    Signer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                    IssuedBy = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
                    ValidFrom = certificate.NotBefore,
                    ValidTo = certificate.NotAfter,
                    SignerId = signerRfc,
                    IssuerId = issuerRfc,
                    SameAsIssuer = signerRfc == null || issuerRfc == null ? null : string.Equals(signerRfc, issuerRfc, StringComparison.OrdinalIgnoreCase),
                    CertificateMatches = NumberOf(certificate) is { } number && root.GetAttribute("NoCertificado") is { Length: > 0 } named ? number == named.Trim() : null,
                };
            }
        }

        // The RFC of the holder of a CSD: the x500UniqueIdentifier of the subject (2.5.4.45), "RFC" or "RFC / RFC of the legal representative".
        private static string RfcOf(X509Certificate2 certificate)
        {
            var match = Regex.Match(certificate.SubjectName.Format(true), @"(?:OID\.)?2\.5\.4\.45=\s*([A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        // The number of a SAT certificate (NoCertificado, 20 digits) is its serial number written as ASCII digits in hexadecimal.
        private static string NumberOf(X509Certificate2 certificate)
        {
            var hex = certificate.SerialNumber;
            if (hex.Length % 2 != 0) return null;
            var sb = new StringBuilder();
            for (var i = 0; i < hex.Length; i += 2)
            {
                var c = (char)Convert.ToByte(hex.Substring(i, 2), 16);
                if (!char.IsAsciiDigit(c)) return null;
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---- the cadena original ------------------------------------------------------------------------------------------------------------

        private static readonly ConcurrentDictionary<string, Lazy<XslCompiledTransform>> Transforms = new();

        internal static string CadenaOriginal(XmlDocument document, string stylesheet)
        {
            var transform = Transforms.GetOrAdd(stylesheet, s => new Lazy<XslCompiledTransform>(() => Compile(s))).Value;
            // The stamp is added after the seal, so it is no part of what was sealed: neither stylesheet has a template for it, and the built-in rules
            // would copy any text inside it. The cadena is made from a copy without it, as the issuer's was before the stamp existed.
            var stamps = document.GetElementsByTagName("TimbreFiscalDigital", TfdNamespace).OfType<XmlElement>().ToList();
            if (stamps.Count > 0)
            {
                document = (XmlDocument)document.CloneNode(deep: true);
                foreach (var stamp in document.GetElementsByTagName("TimbreFiscalDigital", TfdNamespace).OfType<XmlElement>().ToList())
                    stamp.ParentNode!.RemoveChild(stamp);
            }
            using var writer = new StringWriter();
            // no document(), no script, nothing outside the bundled stylesheets
            transform.Transform(document, null, writer);
            return writer.ToString();
        }

        private static XslCompiledTransform Compile(string stylesheet)
        {
            var resolver = new BundledResolver();
            var uri = new Uri(BundleRoot, stylesheet);
            using var input = (Stream)resolver.GetEntity(uri, null, typeof(Stream));
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }, uri.AbsoluteUri);
            var transform = new XslCompiledTransform();
            transform.Load(reader, new XsltSettings(enableDocumentFunction: false, enableScript: false), resolver);
            return transform;
        }

        // The includes of the stylesheets are relative paths: they resolve against a made-up root (the .invalid domain never exists) and are read from
        // the resources of this assembly, never from disk or the network.
        private static readonly Uri BundleRoot = new("http://caret-sat.invalid/");

        private sealed class BundledResolver : XmlResolver
        {
            public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
            {
                if (!BundleRoot.IsBaseOf(absoluteUri)) throw new XmlException("Only the bundled SAT stylesheets can be read: " + absoluteUri);
                var path = Uri.UnescapeDataString(absoluteUri.AbsolutePath).TrimStart('/');
                // MSBuild names the resources after their folders, with Windows separators
                var assembly = typeof(CfdiSeal).Assembly;
                var stream = assembly.GetManifestResourceStream(ResourcePrefix + path) ?? assembly.GetManifestResourceStream(ResourcePrefix + path.Replace('/', '\\'));
                var name = ResourcePrefix + path;
                return stream ?? throw new XmlException("A SAT stylesheet is missing: " + name);
            }

            public override Uri ResolveUri(Uri baseUri, string relativeUri) =>
                new Uri(baseUri ?? BundleRoot, relativeUri);
        }
    }
}
