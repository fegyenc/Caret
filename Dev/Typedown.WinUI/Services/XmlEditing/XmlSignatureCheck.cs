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
        // a method of signing or of preparing the text that is not checked here
        Unsupported,
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
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            try
            {
                using var reader = XmlReader.Create(new StringReader(text ?? ""), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                });
                document.Load(reader);
            }
            catch (XmlException)
            {
                return Array.Empty<XmlSignatureResult>();
            }
            var signatures = document.GetElementsByTagName("Signature", DsigNamespace).OfType<XmlElement>().ToList();
            return signatures.Select(s => CheckOne(document, s)).ToList();
        }

        private static XmlSignatureResult CheckOne(XmlDocument document, XmlElement signature)
        {
            // What it signs, read from the text (SignedXml is not needed for that): its first reference, an Id in this file or "" for all of it.
            var uris = signature.GetElementsByTagName("Reference", DsigNamespace).OfType<XmlElement>()
                .Where(r => r.ParentNode is XmlElement { LocalName: "SignedInfo" }).Select(r => r.GetAttribute("URI")).ToList();
            var uri = uris.FirstOrDefault() ?? "";
            var target = uri.Length == 0 ? document.DocumentElement : uri.StartsWith('#') ? FindById(document, uri.Substring(1)) : null;
            var signs = uri.Length == 0 ? "" : target?.LocalName ?? uri.TrimStart('#');
            if (uris.Any(u => u.Length > 0 && !u.StartsWith('#')))
                return new XmlSignatureResult { State = XmlSignatureState.NotChecked, Problem = XmlSignatureProblem.ReferenceOutside, Signs = signs };

            // The certificate before SignedXml reads the signature (it would fail on a damaged one with a less useful message).
            var certificateData = signature.GetElementsByTagName("X509Certificate", DsigNamespace).OfType<XmlElement>().FirstOrDefault()?.InnerText;
            if (string.IsNullOrWhiteSpace(certificateData))
                return new XmlSignatureResult { State = XmlSignatureState.NotChecked, Problem = XmlSignatureProblem.NoCertificate, Signs = signs };
            X509Certificate2 certificate;
            try
            {
                var bytes = Convert.FromBase64String(Regex.Replace(certificateData, @"\s", ""));
                // a plain certificate only: a PFX bundle (one with a private key) would be imported with its key, which a file must never cause
                if (X509Certificate2.GetCertContentType(bytes) != X509ContentType.Cert)
                    return new XmlSignatureResult { State = XmlSignatureState.NotChecked, Problem = XmlSignatureProblem.BadCertificate, Signs = signs };
                certificate = new X509Certificate2(bytes);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return new XmlSignatureResult { State = XmlSignatureState.NotChecked, Problem = XmlSignatureProblem.BadCertificate, Signs = signs };
            }

            using (certificate)
            {
                try
                {
                    var signedXml = new SignedXml(document) { Resolver = null };
                    signedXml.LoadXml(signature);
                    // the signature against the key of this certificate only: no chain is built, nothing is looked up
                    var valid = signedXml.CheckSignature(certificate, verifySignatureOnly: true);
                    var common = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "";
                    var idMatch = BrazilianId.Match(common);
                    var signerId = idMatch.Success ? idMatch.Groups[1].Value : null;
                    var issuerId = IssuerIdOf(target);
                    return new XmlSignatureResult
                    {
                        State = valid ? XmlSignatureState.Valid : XmlSignatureState.Changed,
                        Signs = signs,
                        Signer = idMatch.Success ? common.Substring(0, idMatch.Index).Trim() : common,
                        IssuedBy = BrazilianId.Replace(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true) ?? "", "").Trim(),
                        ValidFrom = certificate.NotBefore,
                        ValidTo = certificate.NotAfter,
                        SignerId = signerId,
                        IssuerId = issuerId,
                        SameAsIssuer = signerId == null || issuerId == null ? null : SameCompany(signerId, issuerId),
                    };
                }
                catch (CryptographicException)
                {
                    // an algorithm or a transform SignedXml does not take, or a reference it cannot find
                    return new XmlSignatureResult { State = XmlSignatureState.NotChecked, Problem = XmlSignatureProblem.Unsupported, Signs = signs };
                }
            }
        }

        // The element with this Id (as SignedXml finds it: an attribute Id, ID or id).
        private static XmlElement FindById(XmlDocument document, string id) =>
            document.SelectSingleNode($"//*[@Id='{Escape(id)}' or @ID='{Escape(id)}' or @id='{Escape(id)}']") as XmlElement;

        private static string Escape(string id) => id.Replace("'", "");

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
