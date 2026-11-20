using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Xml;

namespace Typedown.WinUI.Services.XmlEditing
{
    internal enum XmlSignatureState
    {
        // the signed part is exactly what was signed, with the key of the certificate in the file
        Valid,
        // the signed part or the signature itself was changed after signing
        Changed,
        // not checked: see Problem
        NotChecked,
    }

    internal enum XmlSignatureProblem
    {
        None,
        // no certificate in KeyInfo: the key to check with is not in the file
        NoCertificate,
        // a certificate is there but cannot be read
        BadCertificate,
        // what is signed is not in the file (a reference to another file or to the internet: nothing is fetched)
        ReferenceOutside,
        // a method of signing or of preparing the text that is not checked here (a transform other than the canonical ones, an Id that is not
        // one element's alone)
        Unsupported,
        // the signature itself cannot be read (its value is not Base64)
        Damaged,
    }

    internal sealed record XmlSignatureResult
    {
        public XmlSignatureState State { get; init; }
        public XmlSignatureProblem Problem { get; init; }
        // the element the signature covers (its local name; "" for the whole document)
        public string Signs { get; init; } = "";
        // the certificate: who it names (its common name, without a Brazilian ":CNPJ" ending), who issued it, and when it is valid
        public string Signer { get; init; }
        public string IssuedBy { get; init; }
        public DateTime? ValidFrom { get; init; }
        public DateTime? ValidTo { get; init; }
        // a Brazilian ICP-Brasil certificate names the CNPJ (or the CPF) of its holder after a colon in its common name
        public string SignerId { get; init; }
        // the CNPJ or CPF of the issuer of the signed document (emit, or the provider of an NFS-e), when it has one
        public string IssuerId { get; init; }
        // the two are the same company: the same root of CNPJ (its first 8 digits, as the tax authority compares them) or the same CPF
        public bool? SameAsIssuer { get; init; }
    }

    // Checks the digital signatures (XML Signature, the W3C standard) of an electronic invoice: whether what was signed is still what is in the file,
    // with the public key of the certificate the file carries. Offline: nothing is fetched (a reference outside the file is not checked), and whether
    // the certificate was revoked, or comes from a recognised authority, is not checked: only that this certificate's key signed this text. The text
    // checked is the one in the editor, so a change made since opening shows as Changed. Plain .NET, no AI.
    internal static class XmlSignatureCheck
    {
        private const string DsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
        private static readonly Regex BrazilianId = new(@":(\d{14}|\d{11})\s*$", RegexOptions.CultureInvariant);

        public static IReadOnlyList<XmlSignatureResult> Check(string text)
        {
            using var input = new StringReader(text ?? "");
            return Check(XmlReader.Create(input, ReaderSettings()));
        }

        // A file as it is on disk (the table of a folder of invoices): read from its bytes, in the encoding it declares.
        public static IReadOnlyList<XmlSignatureResult> Check(Stream stream) => Check(XmlReader.Create(stream, ReaderSettings()));

        private static XmlReaderSettings ReaderSettings() => new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            CloseInput = false,
        };

        private static IReadOnlyList<XmlSignatureResult> Check(XmlReader reader)
        {
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            try
            {
                using (reader) document.Load(reader);
            }
            catch (XmlException)
            {
                return Array.Empty<XmlSignatureResult>();
            }
            var signatures = document.GetElementsByTagName("Signature", DsigNamespace).OfType<XmlElement>().ToList();
            return signatures.Select(s => CheckOne(document, s)).ToList();
        }

        // The ways of preparing the signed text that are checked here: the enveloped signature and the canonical forms (with or without comments,
        // inclusive or exclusive), which is what the invoices of Latin America use. Anything else (an XPath or XSLT transform, a newer canonical
        // form) is "not checked": SignedXml would refuse it and report a failure, which must not read as "changed".
        private static readonly HashSet<string> KnownTransforms = new(StringComparer.Ordinal)
        {
            SignedXml.XmlDsigEnvelopedSignatureTransformUrl,
            SignedXml.XmlDsigC14NTransformUrl, SignedXml.XmlDsigC14NWithCommentsTransformUrl,
            SignedXml.XmlDsigExcC14NTransformUrl, SignedXml.XmlDsigExcC14NWithCommentsTransformUrl,
        };

        private static readonly HashSet<string> KnownCanonicalizations = new(StringComparer.Ordinal)
        {
            SignedXml.XmlDsigC14NTransformUrl, SignedXml.XmlDsigC14NWithCommentsTransformUrl,
            SignedXml.XmlDsigExcC14NTransformUrl, SignedXml.XmlDsigExcC14NWithCommentsTransformUrl,
        };

        private static XmlSignatureResult NotChecked(XmlSignatureProblem problem, string signs = "") =>
            new() { State = XmlSignatureState.NotChecked, Problem = problem, Signs = signs };

        private static XmlSignatureResult CheckOne(XmlDocument document, XmlElement signature)
        {
            var signedInfo = signature.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "SignedInfo" && e.NamespaceURI == DsigNamespace);
            if (signedInfo == null) return NotChecked(XmlSignatureProblem.Damaged);
            var references = signedInfo.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "Reference" && e.NamespaceURI == DsigNamespace).ToList();
            var uris = references.Select(r => r.GetAttribute("URI")).ToList();
            var uri = uris.FirstOrDefault() ?? "";
            if (uris.Any(u => u.Length > 0 && !u.StartsWith('#'))) return NotChecked(XmlSignatureProblem.ReferenceOutside);

            // What it signs, found as SignedXml finds it (an attribute Id, id or ID); an Id that more than one element has is not checked: the
            // verifier and this description could take different elements.
            XmlElement target;
            if (uri.Length == 0) target = document.DocumentElement;
            else
            {
                var id = uri.Substring(1);
                if (id.IndexOfAny(new[] { '\'', '"' }) >= 0 || CountWithId(document, id) != 1) return NotChecked(XmlSignatureProblem.Unsupported, id);
                try { target = new SignedXml(document).GetIdElement(document, id); }
                catch (CryptographicException) { return NotChecked(XmlSignatureProblem.Unsupported, id); }
            }
            var signs = uri.Length == 0 ? "" : target?.LocalName ?? uri.Substring(1);

            var canonicalization = signedInfo.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "CanonicalizationMethod")?.GetAttribute("Algorithm") ?? "";
            var transforms = references.SelectMany(r => r.GetElementsByTagName("Transform", DsigNamespace).OfType<XmlElement>()).Select(t => t.GetAttribute("Algorithm"));
            if (!KnownCanonicalizations.Contains(canonicalization) || transforms.Any(t => !KnownTransforms.Contains(t))) return NotChecked(XmlSignatureProblem.Unsupported, signs);

            // Every certificate in the signature, read before SignedXml reads it (it loads each one): a plain certificate only, never a PFX bundle
            // (one with a private key would be imported with its key, which a file must never cause).
            var entries = signature.GetElementsByTagName("X509Certificate", DsigNamespace).OfType<XmlElement>().Select(e => e.InnerText).ToList();
            if (entries.Count == 0 || entries.All(string.IsNullOrWhiteSpace)) return NotChecked(XmlSignatureProblem.NoCertificate, signs);
            var certificates = new List<X509Certificate2>();
            try
            {
                foreach (var entry in entries)
                {
                    var bytes = Convert.FromBase64String(Regex.Replace(entry, @"\s", ""));
                    if (X509Certificate2.GetCertContentType(bytes) != X509ContentType.Cert) return NotChecked(XmlSignatureProblem.BadCertificate, signs);
                    certificates.Add(new X509Certificate2(bytes));
                }

                var signedXml = new SignedXml(document) { Resolver = null };
                signedXml.LoadXml(signature);
                // the signature against the key of each certificate in the file (a chain can carry the authority's before the signer's): no chain is
                // built, nothing is looked up; the one whose key verifies is the signer's
                var signer = certificates.FirstOrDefault(c => signedXml.CheckSignature(c, verifySignatureOnly: true));
                var shown = signer ?? certificates[0];
                var common = shown.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "";
                var idMatch = BrazilianId.Match(common);
                var signerId = idMatch.Success ? idMatch.Groups[1].Value : null;
                var issuerId = IssuerIdOf(target);
                return new XmlSignatureResult
                {
                    State = signer != null ? XmlSignatureState.Valid : XmlSignatureState.Changed,
                    Signs = signs,
                    Signer = idMatch.Success ? common.Substring(0, idMatch.Index).Trim() : common,
                    IssuedBy = BrazilianId.Replace(shown.GetNameInfo(X509NameType.SimpleName, forIssuer: true) ?? "", "").Trim(),
                    ValidFrom = shown.NotBefore,
                    ValidTo = shown.NotAfter,
                    SignerId = signerId,
                    IssuerId = issuerId,
                    SameAsIssuer = signerId == null || issuerId == null ? null : SameCompany(signerId, issuerId),
                };
            }
            catch (FormatException)
            {
                // a certificate or the signature value is not Base64: this signature cannot be read, the others are still checked
                return NotChecked(certificates.Count < entries.Count ? XmlSignatureProblem.BadCertificate : XmlSignatureProblem.Damaged, signs);
            }
            catch (CryptographicException)
            {
                // a certificate that cannot be read, or an algorithm SignedXml does not take
                return NotChecked(certificates.Count < entries.Count ? XmlSignatureProblem.BadCertificate : XmlSignatureProblem.Unsupported, signs);
            }
            catch (ArgumentException)
            {
                // a signature SignedXml cannot take in (the same prefix twice in an exclusive canonical form's InclusiveNamespaces): not checked,
                // and the next file of a folder is still read
                return NotChecked(XmlSignatureProblem.Unsupported, signs);
            }
            finally
            {
                foreach (var certificate in certificates) certificate.Dispose();
            }
        }

        // How many elements carry this Id (in any of the three spellings SignedXml looks at).
        private static int CountWithId(XmlDocument document, string id) =>
            document.SelectNodes($"//*[@Id='{id}' or @id='{id}' or @ID='{id}']")?.Count ?? 0;

        // The CNPJ (or CPF) of whoever issued the signed document: the emit of an NF-e, CT-e or MDF-e, the provider of an NFS-e (prest, or emit in the
        // national layout). Only its own children are looked at, so the answer of the authority (its signature has no emit) has none.
        private static string IssuerIdOf(XmlElement signed)
        {
            if (signed == null) return null;
            foreach (var party in new[] { "emit", "prest" })
            {
                var element = signed.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == party);
                var id = element?.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName is "CNPJ" or "CPF")?.InnerText.Trim();
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return null;
        }

        // What the strip says of all the signatures together: one changed is enough for "changed"; all valid is "valid", or "other" when a signer is
        // a company other than the issuer (worth a look: an invoice is signed by its issuer); anything not checked leaves "unknown". Null with none.
        public static string Overall(IReadOnlyList<XmlSignatureResult> results) =>
            results == null || results.Count == 0 ? null
            : results.Any(r => r.State == XmlSignatureState.Changed) ? "changed"
            : results.All(r => r.State == XmlSignatureState.Valid) ? (results.Any(r => r.SameAsIssuer == false) ? "other" : "valid")
            : "unknown";

        // A CNPJ or a CPF as people write it (12.345.678/0001-95, 123.456.789-01); anything else as it is.
        public static string FormatId(string id) =>
            id is { Length: 14 } && id.All(char.IsAsciiDigit) ? $"{id[..2]}.{id[2..5]}.{id[5..8]}/{id[8..12]}-{id[12..]}"
            : id is { Length: 11 } && id.All(char.IsAsciiDigit) ? $"{id[..3]}.{id[3..6]}.{id[6..9]}-{id[9..]}"
            : id;

        private static bool SameCompany(string signerId, string issuerId) =>
            signerId.Length == 14 && issuerId.Length == 14 ? signerId[..8] == issuerId[..8] : signerId == issuerId;
    }
}
