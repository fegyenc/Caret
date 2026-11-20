using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Typedown.WinUI.Services.XmlEditing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Typedown.WinUI.Services.Conversion
{
    // A folder of electronic invoices (Colombia's DIAN, UBL 2.1; the same files XmlUbl reads one at a time) as ONE table: a row per document,
    // as Markdown (to read, or to give to an AI assistant), CSV (for programs) and Excel (for accountants: the amounts are numbers, so they
    // open right in any regional setting, which a CSV does not do).
    //
    // The same principles as the reader: the cells are what the files say (no code is guessed, nothing is checked or judged, the answer of the
    // tax authority is copied as it was written); a file that is not in the table is LISTED with the reason, never skipped in silence; and the
    // same document twice (the same unique code, in two copies of the file) is counted once, because double counting is what a table of
    // invoices must not do. Totals are per currency, and credit notes are subtracted (that is said on the table).
    internal sealed class InvoiceAmount
    {
        public string Text = "";
        public decimal? Value;
        public bool IsEmpty => Text.Length == 0;
    }

    internal sealed class InvoiceRow
    {
        public string File, Kind, TypeCode, Number, Date, Issuer, IssuerId, Customer, CustomerId, Currency, Cuf, Answer;
        // a Peruvian invoice (SUNAT): the code of the type of identity document of the parties (6 = RUC, 1 = DNI) is kept apart, to say it in words
        public string IssuerIdType, CustomerIdType;
        public bool Peru;
        // what says that two rows are the same document, when the code shown (Cuf) is not enough: a Portuguese ATCUD is not unique by itself
        public string Key;
        // the unique code of the document that this one replaces (a CT-e of substitution): that one is listed and not added
        public string Replaces;
        // an annulled document (a SAF-T status A or F) is listed, and left out of the totals
        public bool Void;
        // the bytes of the file it comes from (SHA-256) and its place in it: a document without a unique code that arrives twice, the same file in two
        // e-mails of a thread, is still counted once
        public string SameBytes;
        // read from inside an e-mail or a ZIP archive of the folder (not from a file of the folder itself)
        public bool FromContainer;
        // a Mexican invoice paid later (MetodoPago PPD), and what the payment complements of the folder say of it (InvoiceTable.Payments.cs)
        public bool Ppd;
        public int PpdComplements;
        public decimal PpdPaid;
        public decimal? PpdBalance;
        // the digital signatures of the file, checked on this PC (XmlSignatureCheck.Overall: valid, other, changed, unknown); null when the file has none.
        // A file with several documents (a Chilean envelope) gives every row of it the state of the whole file.
        public string Signature;
        public InvoiceAmount Base = new(), Taxes = new(), Withholdings = new(), Total = new(), Payable = new();
        public IEnumerable<InvoiceAmount> Amounts => new[] { Base, Taxes, Withholdings, Total, Payable };
        // an "Egreso" CFDI (a credit note, a return, a discount) is subtracted in the totals like a credit note
        public bool IsCreditNote => Kind == "CreditNote" || (Kind == "Cfdi" && TypeCode == "E") || (Kind == "Nfe" && XmlNfe.IsCreditNote(TypeCode)) || (Kind == "Dte" && XmlChile.IsCreditNote(TypeCode))
            || (Kind == "SaftPt" && XmlSaftPt.IsCreditNote(TypeCode));
    }

    internal enum InvoiceSkip { NotInvoice, AnswerOnly, NotWellFormed, TooBig, Unreadable, Duplicate, OtherCfdiType, NfeEvent, OtherDte, NfseNotIssued, SaftOther, Manifest, SupportDocument,
        SameFile, NothingInside, Encrypted, TooMuchInside }

    internal sealed class SkippedInvoiceFile
    {
        public string File, Detail;
        public int Line, Position;   // where a file that is not well-formed breaks
        public InvoiceSkip Reason;
    }

    internal sealed class InvoiceTableResult
    {
        public List<InvoiceRow> Rows { get; } = new();
        public List<SkippedInvoiceFile> Skipped { get; } = new();
        // The events of the invoices of Brazil (a cancellation...): files of their own, shown in the row of the invoice they are about.
        internal List<NfeEvent> Events { get; } = new();
        // the payments that the payment complements (CFDI of type P) of the folder list
        internal List<CfdiPayment> Payments { get; } = new();
        public int FilesSeen { get; set; }
        public long SourceBytes { get; set; }
    }

    internal static partial class InvoiceTable
    {
        // ---- reading ------------------------------------------------------------------------------------------------------------------

        // Every .xml file of the folder and its subfolders (hidden and system ones too), in the order of their paths, and the XML files inside its ZIP
        // archives and e-mails (.zip, .eml, .msg: InvoiceTable.Containers.cs). A folder that cannot be read, and a link to another folder (not
        // followed: it could lead back into this one), are listed, not skipped in silence.
        public static InvoiceTableResult ReadFolder(string folder) => ReadFolder(folder, MaxUnpacked);

        // maxUnpacked: what one ZIP archive or e-mail of the folder may unpack to in all (smaller in the tests)
        internal static InvoiceTableResult ReadFolder(string folder, long maxUnpacked)
        {
            var result = new InvoiceTableResult();
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(folder);
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.None, IgnoreInaccessible = false };
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                try
                {
                    files.AddRange(Directory.EnumerateFiles(directory, "*", options).Where(f => KindOf(f) != SourceKind.None));
                    foreach (var sub in Directory.EnumerateDirectories(directory, "*", options))
                    {
                        if (IsLink(sub)) result.Skipped.Add(new SkippedInvoiceFile { File = Display(folder, sub) + "/", Reason = InvoiceSkip.Unreadable, Detail = "a link to another folder, not followed" });
                        else pending.Push(sub);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Skipped.Add(new SkippedInvoiceFile { File = Display(folder, directory) + "/", Reason = InvoiceSkip.Unreadable, Detail = ex.Message });
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var display = Display(folder, path);
                if (KindOf(path) is var kind && kind != SourceKind.Xml)
                {
                    ReadContainerFile(result, path, display, kind, maxUnpacked);
                    continue;
                }
                FileStream stream;
                try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.FilesSeen++;
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message });
                    continue;
                }
                // One copy of the file, read once: the rows and the check of its signature are made from the same bytes (it is opened sharing writes,
                // and another program could change it between the two reads). The file itself is never read again: one too big for the reader is
                // listed here, and the copy stops one byte past the limit (a file that grows while it is read is then listed as too big by Add).
                MemoryStream snapshot;
                using (stream)
                {
                    try
                    {
                        var length = stream.Length;
                        if (length > XmlConverter.MaxBytes)
                        {
                            result.FilesSeen++;
                            result.SourceBytes += length;
                            result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.TooBig });
                            continue;
                        }
                        snapshot = new MemoryStream((int)length);
                        var buffer = new byte[81920];
                        int read;
                        while (snapshot.Length <= XmlConverter.MaxBytes
                               && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, XmlConverter.MaxBytes + 1 - snapshot.Length))) > 0)
                            snapshot.Write(buffer, 0, read);
                        snapshot.Position = 0;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        result.FilesSeen++;
                        result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message });
                        continue;
                    }
                }
                using (snapshot) Add(result, snapshot, display);
            }
            Finish(result);
            return result;
        }

        // The path under the folder, written the same in every output on any PC.
        private static string Display(string folder, string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');

        private static bool IsLink(string directory)
        {
            try { return (new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }

        // One file into the result (for the tests, which use streams); Finish is called after the last one. A file that gave rows and carries an XML
        // signature is read a second time, as it is (the reader above drops whitespace and comments, which are signed too), to check the signature.
        internal static void Add(InvoiceTableResult result, Stream stream, string display, bool fromContainer = false)
        {
            var first = result.Rows.Count;
            var root = AddRows(result, stream, display);
            if (result.Rows.Count == first) return;
            for (var i = first; i < result.Rows.Count; i++) result.Rows[i].FromContainer = fromContainer;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                for (var i = first; i < result.Rows.Count; i++) result.Rows[i].SameBytes = hash + ":" + (i - first).ToString(CultureInfo.InvariantCulture);
            }
            if (root == null || !(root.DescendantsAndSelf(DsigSignature).Any() || IsSealedCfdi(root))) return;
            var signature = SignatureOf(stream);
            for (var i = first; i < result.Rows.Count; i++) result.Rows[i].Signature = signature;
        }

        private static readonly XName DsigSignature = XName.Get("Signature", "http://www.w3.org/2000/09/xmldsig#");

        // a Mexican CFDI carries a seal (CfdiSeal.cs) instead of an XML signature: Sello, or sello in a CFDI 3.2
        private static bool IsSealedCfdi(XElement root) => CfdiSeal.IsSealed(root);

        // "unknown" when the file cannot be read again (it went away, or the stream cannot go back): it has a signature, and it was not checked.
        private static string SignatureOf(Stream stream)
        {
            if (!stream.CanSeek) return "unknown";
            try
            {
                stream.Position = 0;
                return XmlSignatureCheck.Overall(XmlSignatureCheck.Check(stream)) ?? "unknown";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return "unknown"; }
        }

        // The rows of one file; the root of the file when it was read (null when it was not).
        private static XElement AddRows(InvoiceTableResult result, Stream stream, string display)
        {
            result.FilesSeen++;
            XDocument document;
            // the length is asked inside the try too: a stream whose length cannot be read is one unreadable file, not the end of the scan
            try
            {
                if (stream.CanSeek) result.SourceBytes += stream.Length;
                document = XmlConverter.Load(stream);
            }
            catch (NotSupportedException ex) { result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.TooBig, Detail = ex.Message }); return null; }
            // the file was opened and then could not be read to its end (a network drive that went away, a file that was cut): the file is counted once, here
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message }); return null; }
            catch (InvalidDataException ex)
            {
                var place = ex.InnerException as System.Xml.XmlException;
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotWellFormed, Line = place?.LineNumber ?? 0, Position = place?.LinePosition ?? 0 });
                return null;
            }
            var root = document.Root;
            if (root != null && XmlNfe.IsEvent(root))
            {
                var ev = XmlNfe.ReadEvent(root);
                if (ev != null)
                {
                    ev.File = display;
                    result.Events.Add(ev);
                    return root;
                }
            }
            if (root != null && XmlNfse.ReadEvent(root) is { } nfseEvent)
            {
                nfseEvent.File = display;
                result.Events.Add(nfseEvent);
                return root;
            }
            if (root != null && XmlCte.ReadEvent(root) is { } cteEvent)
            {
                cteEvent.File = display;
                result.Events.Add(cteEvent);
                return root;
            }
            if (root != null && XmlCte.IsCte(root) && XmlCte.ReadRow(root, display) is { } cte)
            {
                result.Rows.Add(cte);
                return root;
            }
            // a manifest of transport (and its events) lists documents that are carried; it is not an invoice and has no amounts to add
            if (root != null && (XmlMdfe.IsMdfe(root) || XmlMdfe.IsEvent(root)))
            {
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Manifest });
                return root;
            }
            if (root != null && XmlNfe.IsNfe(root) && XmlNfe.ReadRow(root, display) is { } nfe)
            {
                result.Rows.Add(nfe);
                return root;
            }
            if (root != null && XmlNfse.IsNfse(root))
            {
                if (XmlNfse.ReadRow(root, display) is { } nfse) result.Rows.Add(nfse);
                else result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NfseNotIssued });
                return root;
            }
            if (root != null && XmlSaftPt.IsSaftPt(root))
            {
                var sales = XmlSaftPt.ReadRows(root, display, out var others);
                result.Rows.AddRange(sales);
                if (others.Length > 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.SaftOther, Detail = others });
                else if (sales.Count == 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                return root;
            }
            if (root != null && XmlChile.IsChile(root))
            {
                // an envelope has up to 2000 documents; a dispatch note (52) is not an invoice, so it is listed once for the file, not added
                var documents = XmlChile.ReadRows(root, display);
                var apart = documents.Where(r => XmlChile.IsDispatchNote(r.TypeCode)).Select(r => r.TypeCode).Distinct().ToList();
                result.Rows.AddRange(documents.Where(r => !XmlChile.IsDispatchNote(r.TypeCode)));
                if (apart.Count > 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.OtherDte, Detail = string.Join(", ", apart) });
                else if (documents.Count == 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                return root;
            }
            if (root != null && XmlCfdi.IsCfdi(root))
            {
                var cfdi = XmlCfdi.ReadRow(root, display);
                // only invoices (I) and their credit notes (E) are rows of a table of invoices: a transfer, a payroll or a payment CFDI is another kind
                // of document, and its amounts must not be added to the totals of the invoices (it is listed, with its type)
                if (cfdi.TypeCode is "I" or "E")
                {
                    cfdi.Ppd = cfdi.TypeCode == "I" && string.Equals(((string)root.Attribute("MetodoPago"))?.Trim(), "PPD", StringComparison.OrdinalIgnoreCase);
                    result.Rows.Add(cfdi);
                }
                else
                {
                    // a payment complement is not a row, but what it pays is said on the invoices it names
                    if (cfdi.TypeCode == "P") result.Payments.AddRange(ReadPayments(root, display, cfdi.Cuf));
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.OtherCfdiType, Detail = cfdi.TypeCode });
                }
                return root;
            }
            if (root == null || !XmlUbl.IsUbl(root))
            {
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                return root;
            }
            switch (root.Name.LocalName)
            {
                // a Colombian documento soporte is a purchase, issued by the buyer: listed, not added to the sales (XmlUbl.IsSupportDocument)
                case "Invoice" or "CreditNote" or "DebitNote" when XmlUbl.IsSupportDocument(root):
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.SupportDocument });
                    break;
                case "Invoice":
                case "CreditNote":
                case "DebitNote":
                    result.Rows.Add(ReadDocument(root, display, null));
                    break;
                case "AttachedDocument":
                    var parts = ContainerParts(root);
                    var invoice = parts.FirstOrDefault(p => XmlUbl.IsUbl(p) && p.Name.LocalName is "Invoice" or "CreditNote" or "DebitNote");
                    if (invoice != null && XmlUbl.IsSupportDocument(invoice))
                        result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.SupportDocument });
                    else if (invoice != null)
                        result.Rows.Add(ReadDocument(invoice, display, AnswerOf(root, parts)));
                    else
                        result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = parts.Any(p => p.Name.LocalName == "ApplicationResponse") ? InvoiceSkip.AnswerOnly : InvoiceSkip.NotInvoice });
                    break;
                case "ApplicationResponse":
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.AnswerOnly });
                    break;
                default:
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                    break;
            }
            return root;
        }

        // The documents stored as text in a container: the one in its own attachment, then one per referenced document.
        private static List<XElement> ContainerParts(XElement container)
        {
            var parts = new List<XElement>();
            void Take(string xml)
            {
                if (string.IsNullOrWhiteSpace(xml)) return;
                try { if (XmlConverter.Parse(xml.Trim()).Root is { } inner) parts.Add(inner); }
                catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException) { }
            }
            Take(XmlUbl.Text(container, "Attachment/ExternalReference/Description"));
            foreach (var reference in XmlUbl.Children(container, "ParentDocumentLineReference"))
                Take(XmlUbl.Text(reference, "DocumentReference/Attachment/ExternalReference/Description"));
            return parts;
        }

        // The answer as the authority wrote it ("code: text"); the result of the verification is the fallback.
        private static string AnswerOf(XElement container, List<XElement> parts)
        {
            var response = parts.FirstOrDefault(p => XmlUbl.IsUbl(p) && p.Name.LocalName == "ApplicationResponse");
            if (response != null)
                foreach (var document in XmlUbl.Children(response, "DocumentResponse"))
                {
                    var answer = XmlUbl.Child(document, "Response");
                    if (answer == null) continue;
                    var text = string.Join(": ", new[] { XmlUbl.Text(answer, "ResponseCode"), XmlUbl.Text(answer, "Description") }.Where(s => s != null));
                    if (text.Length > 0) return text;
                }
            foreach (var reference in XmlUbl.Children(container, "ParentDocumentLineReference"))
                if (XmlUbl.Text(reference, "DocumentReference/ResultOfVerification/ValidationResultCode") is { } code) return code;
            return null;
        }

        private static InvoiceRow ReadDocument(XElement root, string file, string answer)
        {
            var totals = XmlUbl.Child(root, "LegalMonetaryTotal") ?? XmlUbl.Child(root, "RequestedMonetaryTotal");
            var payable = totals == null ? null : XmlUbl.Child(totals, "PayableAmount");
            var uuid = XmlUbl.Text(root, "UUID");
            var peru = XmlPeru.IsPeruvian(root);
            var supplier = XmlUbl.Child(root, "AccountingSupplierParty");
            var customer = XmlUbl.Child(root, "AccountingCustomerParty");
            var typeCode = XmlUbl.Text(root, "InvoiceTypeCode");
            var number = XmlUbl.Text(root, "ID");
            // A Peruvian invoice has no unique code of its own (no UUID); what identifies it is the RUC of the issuer, the type and the number
            // (F001-123), so that is the code, built from the file, and the same document twice is counted once.
            if (uuid == null && peru && XmlUbl.PartyIdNumber(supplier) is { } ruc && number != null)
            {
                var type = typeCode ?? (root.Name.LocalName == "CreditNote" ? "07" : root.Name.LocalName == "DebitNote" ? "08" : null);
                if (type != null) uuid = ruc + "-" + type + "-" + number;
            }
            return new InvoiceRow
            {
                File = file,
                Kind = root.Name.LocalName,
                TypeCode = typeCode,
                Number = number,
                Date = XmlUbl.Text(root, "IssueDate"),
                Issuer = XmlUbl.PartyName(supplier),
                IssuerId = peru ? XmlUbl.PartyIdNumber(supplier) : XmlUbl.PartyId(supplier),
                Customer = XmlUbl.PartyName(customer),
                CustomerId = peru ? XmlUbl.PartyIdNumber(customer) : XmlUbl.PartyId(customer),
                IssuerIdType = peru ? XmlUbl.PartyIdType(supplier) : null,
                CustomerIdType = peru ? XmlUbl.PartyIdType(customer) : null,
                Peru = peru,
                Currency = XmlUbl.Text(root, "DocumentCurrencyCode") ?? payable?.Attribute("currencyID")?.Value,
                Cuf = uuid,
                Answer = answer,
                // a Peruvian total has no TaxExclusiveAmount: the value of sale (LineExtensionAmount) is what is without the tax
                Base = Single(totals == null ? null : XmlUbl.Child(totals, "TaxExclusiveAmount") ?? (peru ? XmlUbl.Child(totals, "LineExtensionAmount") : null)),
                Taxes = Sum(XmlUbl.Children(root, "TaxTotal").Select(t => XmlUbl.Child(t, "TaxAmount"))),
                Withholdings = Sum(XmlUbl.Children(root, "WithholdingTaxTotal").Select(t => XmlUbl.Child(t, "TaxAmount"))),
                Total = Single(totals == null ? null : XmlUbl.Child(totals, "TaxInclusiveAmount")),
                Payable = Single(payable),
            };
        }

        private static decimal? Parse(string text) =>
            decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static InvoiceAmount Single(XElement element)
        {
            var text = element?.Value.Trim() ?? "";
            return new InvoiceAmount { Text = text, Value = Parse(text) };
        }

        // Several tax blocks (one per tax) are added; if one of them is not a plain number the texts are shown joined and not added.
        internal static InvoiceAmount Sum(IEnumerable<XElement> elements)
        {
            var parts = elements.Where(e => e != null).Select(e => e.Value.Trim()).Where(t => t.Length > 0).ToList();
            if (parts.Count == 0) return new InvoiceAmount();
            if (parts.Count == 1) return new InvoiceAmount { Text = parts[0], Value = Parse(parts[0]) };
            var values = parts.Select(Parse).ToList();
            if (values.All(v => v != null))
            {
                var sum = values.Sum(v => v.Value);
                return new InvoiceAmount { Text = sum.ToString(CultureInfo.InvariantCulture), Value = sum };
            }
            return new InvoiceAmount { Text = string.Join(" + ", parts) };
        }

        // The rows by date (then issuer, then number); the same unique code twice is one document (the first file counts, the other is listed).
        internal static void Finish(InvoiceTableResult result)
        {
            var seen = new Dictionary<string, InvoiceRow>(StringComparer.OrdinalIgnoreCase);
            // the first file of the folder itself with these bytes: a copy from a mail or an archive yields to it, whatever the order of the names
            var standalone = new Dictionary<string, InvoiceRow>(StringComparer.Ordinal);
            foreach (var row in result.Rows)
                if (!row.FromContainer && !string.IsNullOrEmpty(row.SameBytes) && string.IsNullOrEmpty(row.Key) && string.IsNullOrEmpty(row.Cuf))
                    standalone.TryAdd(row.SameBytes, row);
            var kept = new List<InvoiceRow>();
            foreach (var row in result.Rows)
            {
                var identity = string.IsNullOrEmpty(row.Key) ? row.Cuf : row.Key;
                if (!string.IsNullOrEmpty(identity))
                {
                    if (seen.TryGetValue(identity, out var first))
                    {
                        result.Skipped.Add(new SkippedInvoiceFile { File = row.File, Reason = InvoiceSkip.Duplicate, Detail = first.File });
                        continue;
                    }
                    seen[identity] = row;
                }
                // Without a unique code, two files of the folder are two rows even when they are the same bytes (the person put both there). A copy
                // that came inside an e-mail or a ZIP archive is another matter: a thread of mails repeats its attachments, so the same bytes there
                // are the same document, counted once, and a file of the folder with them is the one kept.
                else if (row.FromContainer && !string.IsNullOrEmpty(row.SameBytes))
                {
                    if (standalone.TryGetValue(row.SameBytes, out var same) || seen.TryGetValue("#" + row.SameBytes, out same))
                    {
                        result.Skipped.Add(new SkippedInvoiceFile { File = row.File, Reason = InvoiceSkip.SameFile, Detail = same.File });
                        continue;
                    }
                    seen["#" + row.SameBytes] = row;
                }
                kept.Add(row);
            }
            result.Rows.Clear();
            result.Rows.AddRange(kept.OrderBy(r => r.Date ?? "", StringComparer.Ordinal).ThenBy(r => r.Issuer ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Number ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(r => r.File, StringComparer.OrdinalIgnoreCase));
            // a document that a CT-e of substitution replaces stays authorized in the files; it is listed, and not added (the replacement is)
            var replacedBy = new Dictionary<string, InvoiceRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in result.Rows)
                if (!string.IsNullOrEmpty(r.Replaces)) replacedBy.TryAdd(r.Replaces, r);
            foreach (var r in result.Rows)
            {
                if (string.IsNullOrEmpty(r.Cuf) || !replacedBy.TryGetValue(r.Cuf, out var replacement)) continue;
                r.Void = true;
                var note = "Substituído pelo CT-e " + replacement.Number;
                r.Answer = string.IsNullOrEmpty(r.Answer) ? note : r.Answer + " · " + note;
            }
            // the events of Brazilian invoices (a cancellation...) are said in the answer column of the invoice they are about, as the files have them; the
            // event files are listed too, so that none is skipped in silence
            var byKey = new Dictionary<string, InvoiceRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in result.Rows)
                if (!string.IsNullOrEmpty(r.Cuf)) byKey.TryAdd(r.Cuf, r);
            foreach (var ev in result.Events.OrderBy(e => e.Date ?? "", StringComparer.Ordinal).ThenBy(e => e.File, StringComparer.OrdinalIgnoreCase))
            {
                InvoiceRow row = null;
                if (!string.IsNullOrEmpty(ev.Key)) byKey.TryGetValue(ev.Key, out row);
                if (row != null) row.Answer = string.IsNullOrEmpty(row.Answer) ? ev.Text : row.Answer + " · " + ev.Text;
                result.Skipped.Add(new SkippedInvoiceFile { File = ev.File, Reason = InvoiceSkip.NfeEvent, Detail = row == null ? "" : "linked" });
            }
            SetPpd(result);
            result.Skipped.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.File, b.File));
        }

        // ---- the table ----------------------------------------------------------------------------------------------------------------

        private static string[] Headers(XmlLabels l) => new[]
        {
            l.Date, l.DocType, l.Number, l.Issuer, l.IssuerTaxId, l.Customer, l.CustomerTaxId, l.Currency,
            l.TaxExclusive, l.Taxes, l.Withholdings, l.TaxInclusive, l.Payable, l.Cuf, l.AuthorityAnswer, l.SignatureOf, l.File,
        };

        private static string TypeText(InvoiceRow row, XmlLabels l) => row.Kind switch
        {
            "Cfdi" => CfdiLabels.For(l.IsSpanish).KindTitle(row.TypeCode),
            "Nfe" => XmlNfe.RowType(row.TypeCode),
            "Nfse" => "NFS-e",
            "Cte" => XmlCte.RowType(row.TypeCode),
            "SaftPt" => XmlSaftPt.RowType(row.TypeCode, l) ?? l.Invoice,
            // a Peruvian document that names its type (catalog 01) is said by it, and a code the table does not have is printed as it is
            "Invoice" or "CreditNote" or "DebitNote" when row.Peru && row.TypeCode != null =>
                XmlPeru.DocumentType(row.TypeCode, l.IsSpanish) ?? string.Format(l.InvoiceOfType, row.TypeCode),
            "Dte" => XmlChile.RowType(row.TypeCode, l.IsSpanish) ?? (row.TypeCode is { } dte ? string.Format(l.InvoiceOfType, dte) : l.Invoice),
            "CreditNote" => l.CreditNote,
            "DebitNote" => l.DebitNote,
            _ when row.Peru => XmlPeru.DocumentType(row.TypeCode, l.IsSpanish) ?? (row.TypeCode is { } sunat ? string.Format(l.InvoiceOfType, sunat) : l.Invoice),
            _ => XmlLabels.InvoiceType(row.TypeCode ?? "", l.IsSpanish) ?? (row.TypeCode is { } code ? string.Format(l.InvoiceOfType, code) : l.Invoice),
        };

        // A Peruvian party's ID with its type said ("20000000001 (RUC)", "12345678 (DNI)"): the headers of the table are the same for every country.
        private static string IdCell(string id, string type, XmlLabels l) =>
            string.IsNullOrEmpty(id) ? "" : string.IsNullOrEmpty(type) ? id : id + " (" + (XmlPeru.IdTypeName(type, l.IsSpanish) ?? (l.IsSpanish ? "tipo " : "type ") + type) + ")";

        private static string[] Cells(InvoiceRow r, XmlLabels l) => new[]
        {
            r.Date ?? "", TypeText(r, l), r.Number ?? "", r.Issuer ?? "", IdCell(r.IssuerId, r.IssuerIdType, l), r.Customer ?? "", IdCell(r.CustomerId, r.CustomerIdType, l), r.Currency ?? "",
            r.Base.Text, r.Taxes.Text, r.Withholdings.Text, r.Total.Text, r.Payable.Text, r.Cuf ?? "", AnswerText(r, l), SignatureText(r.Signature, l), r.File,
        };

        // The answer of the authority as the file has it, and for a PPD invoice what its payment complements say.
        private static string AnswerText(InvoiceRow r, XmlLabels l)
        {
            var ppd = PpdText(r, l);
            return ppd == null ? r.Answer ?? "" : string.IsNullOrEmpty(r.Answer) ? ppd : r.Answer + " · " + ppd;
        }

        // Empty for a file without an XML signature (a CFDI's seal and a SAF-T's hash are not one), so the column only speaks of what was checked.
        private static string SignatureText(string state, XmlLabels l) => state switch
        {
            "valid" => l.SignatureValid,
            "other" => l.SignatureOther,
            "changed" => l.SignatureChanged,
            null => "",
            _ => l.SignatureUnknown,
        };

        // The totals of one currency: the five amounts added, a credit note subtracted. A column with a value that is not a plain number has no total.
        internal sealed class CurrencyTotal
        {
            public string Currency;
            public decimal?[] Sums = new decimal?[5];
        }

        internal static List<CurrencyTotal> Totals(InvoiceTableResult result)
        {
            var list = new List<CurrencyTotal>();
            foreach (var group in result.Rows.GroupBy(r => r.Currency ?? ""))
            {
                var total = new CurrencyTotal { Currency = group.Key };
                for (var c = 0; c < 5; c++)
                {
                    decimal sum = 0;
                    var valid = true;
                    var any = false;
                    foreach (var row in group)
                    {
                        if (row.Void) continue;
                        var amount = row.Amounts.ElementAt(c);
                        if (amount.IsEmpty) continue;
                        if (amount.Value == null) { valid = false; break; }
                        any = true;
                        sum += row.IsCreditNote ? -amount.Value.Value : amount.Value.Value;
                    }
                    // a column nobody filled in (no withholdings) has no total, not a total of 0
                    total.Sums[c] = valid && any ? sum : null;
                }
                list.Add(total);
            }
            return list;
        }

        private static string TotalLabel(CurrencyTotal total, XmlLabels l) =>
            l.TotalRow + (total.Currency.Length > 0 ? " " + total.Currency : "");

        // check: the comparison with the list of the tax authority (InvoiceCheck.cs), said after the table; null when there is none.
        public static string ToMarkdown(InvoiceTableResult result, XmlLabels l, string folderName, InvoiceCheckResult check = null)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(l.TableTitle).Append("\n\n");
            sb.Append("> ").Append(string.Format(l.TableSummary, result.Rows.Count, result.FilesSeen)).Append(string.IsNullOrEmpty(folderName) ? "" : " · " + MarkdownText.EscapeInline(folderName)).Append('\n');
            sb.Append("> ").Append(l.TableNote).Append("\n\n");
            if (result.Rows.Count > 0)
            {
                var table = new List<IReadOnlyList<string>> { Headers(l) };
                table.AddRange(result.Rows.Select(r => (IReadOnlyList<string>)Cells(r, l).Select(MdCell).ToList()));
                foreach (var total in Totals(result))
                {
                    var cells = new string[Headers(l).Length];
                    for (var i = 0; i < cells.Length; i++) cells[i] = "";
                    cells[0] = "**" + MarkdownText.EscapeInline(TotalLabel(total, l)) + "**";
                    for (var c = 0; c < 5; c++) cells[8 + c] = total.Sums[c] is { } v ? "**" + v.ToString(CultureInfo.InvariantCulture) + "**" : "";
                    table.Add(cells);
                }
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
                // the note says why the files void a document; one that the list voided says so in its answer column
                var listed = new HashSet<InvoiceRow>(check?.Cancelled.Select(x => x.Row) ?? Enumerable.Empty<InvoiceRow>());
                if (result.Rows.Any(r => r.Void && !listed.Contains(r))) sb.Append(l.VoidNote).Append("\n\n");
            }
            sb.Append(PaymentsMarkdown(result, l));
            if (check != null) sb.Append(InvoiceCheck.ToMarkdown(check, l));
            if (result.Skipped.Count > 0)
            {
                sb.Append("## ").Append(l.NotIncluded).Append("\n\n");
                sb.Append(l.NotIncludedNote).Append("\n\n");
                var table = new List<IReadOnlyList<string>> { new[] { l.File, l.Reason } };
                table.AddRange(result.Skipped.Select(s => (IReadOnlyList<string>)new[] { MdCell(s.File), MdCell(SkipText(s, l)) }));
                sb.Append(MarkdownText.Table(table)).Append("\n");
            }
            return sb.ToString();
        }

        private static string MdCell(string text) => MarkdownText.EscapeInline(XmlConverter.OneLine(text ?? ""));

        // The first files that were not read and why, in one line (for a folder where no XML file could be read at all).
        internal static string SkipSummary(InvoiceTableResult result, XmlLabels l, int count)
        {
            var parts = result.Skipped.Take(count).Select(s => s.File + " (" + SkipText(s, l) + ")");
            return string.Join("; ", parts) + (result.Skipped.Count > count ? "; …" : "");
        }

        private static string SkipText(SkippedInvoiceFile s, XmlLabels l) => s.Reason switch
        {
            InvoiceSkip.AnswerOnly => l.SkipAnswerOnly,
            InvoiceSkip.NotWellFormed => string.Format(l.SkipNotWellFormed, s.Line, s.Position),
            InvoiceSkip.TooBig => l.SkipTooBig,
            InvoiceSkip.Unreadable => l.SkipUnreadable + (string.IsNullOrEmpty(s.Detail) ? "" : ": " + s.Detail),
            InvoiceSkip.Duplicate => string.Format(l.SkipDuplicate, s.Detail),
            InvoiceSkip.OtherCfdiType => string.Format(l.SkipOtherCfdi, s.Detail ?? "—"),
            InvoiceSkip.NfeEvent => s.Detail == "linked" ? l.SkipEventLinked : l.SkipEventOrphan,
            InvoiceSkip.OtherDte => string.Format(l.SkipOtherDte, s.Detail ?? "—"),
            InvoiceSkip.NfseNotIssued => l.SkipNfseNotIssued,
            InvoiceSkip.SaftOther => string.Format(l.SkipSaftOther, s.Detail ?? "—"),
            InvoiceSkip.Manifest => l.SkipManifest,
            InvoiceSkip.SupportDocument => l.SkipSupportDocument,
            InvoiceSkip.SameFile => string.Format(l.SkipSameFile, s.Detail),
            InvoiceSkip.NothingInside => l.SkipNothingInside,
            InvoiceSkip.Encrypted => l.SkipEncrypted,
            InvoiceSkip.TooMuchInside => l.SkipTooMuchInside,
            _ => l.SkipNotInvoice,
        };

        // ---- CSV ----------------------------------------------------------------------------------------------------------------------

        // Comma-separated, UTF-8 with a byte order mark, one line per document and no totals (so a program can add them up itself). A text that a
        // spreadsheet would run as a formula (it starts with = + - @) gets an apostrophe in front: the files come from outside.
        public static string ToCsv(InvoiceTableResult result, XmlLabels l)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", Headers(l).Select(CsvCell))).Append("\r\n");
            foreach (var row in result.Rows)
            {
                var cells = Cells(row, l);
                // an annulled or replaced document keeps its row (the answer column says why) but not its amounts: this file has no totals, and a program
                // that adds up the columns must not count it
                if (row.Void) for (var i = 8; i <= 12; i++) cells[i] = "";
                sb.Append(string.Join(",", cells.Select(CsvCell))).Append("\r\n");
            }
            return sb.ToString();
        }

        private static string CsvCell(string text)
        {
            text ??= "";
            if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0 && Parse(text) == null) text = "'" + text;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

        // ---- Excel --------------------------------------------------------------------------------------------------------------------

        // One sheet: the header (frozen), a row per document, then a total row per currency. The amounts are numbers (formatted with two
        // decimals), everything else is text; the file's own text for an amount that is not a plain number is kept as text.
        public static void WriteXlsx(InvoiceTableResult result, XmlLabels l, Stream stream, InvoiceCheckResult check = null)
        {
            // the Open XML library needs a stream it can read back, which a file opened for writing is not: the workbook is made in memory
            using var memory = new MemoryStream();
            WriteWorkbook(result, l, memory, check);
            memory.WriteTo(stream);
        }

        private static void WriteWorkbook(InvoiceTableResult result, XmlLabels l, Stream stream, InvoiceCheckResult check)
        {
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = Styles();
            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var data = new S.SheetData();
            var headers = Headers(l);
            data.Append(XlsxRow(1, headers.Select(h => (h, (decimal?)null, 1u)).ToList()));
            var rowNumber = 2u;
            foreach (var row in result.Rows)
            {
                var cells = Cells(row, l);
                var amounts = row.Amounts.ToList();
                data.Append(XlsxRow(rowNumber++, cells.Select((text, i) =>
                    i >= 8 && i <= 12 && amounts[i - 8].Value is { } number ? (text, (decimal?)number, 2u) : (text, (decimal?)null, 0u)).ToList()));
            }
            foreach (var total in Totals(result))
            {
                var cells = new List<(string, decimal?, uint)>();
                for (var i = 0; i < headers.Length; i++)
                    cells.Add(i == 0 ? (TotalLabel(total, l), null, 1u)
                        : i >= 8 && i <= 12 && total.Sums[i - 8] is { } sum ? ("", sum, 3u) : ("", null, 0u));
                data.Append(XlsxRow(rowNumber++, cells));
            }

            var widths = new double[] { 12, 22, 16, 34, 16, 34, 16, 9, 16, 14, 14, 16, 16, 40, 40, 24, 36 };
            var columns = new S.Columns(widths.Select((w, i) => new S.Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = w, CustomWidth = true }));
            var pane = new S.Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen };
            var view = new S.SheetView(pane, new S.Selection { Pane = S.PaneValues.BottomLeft }) { WorkbookViewId = 0U };
            sheetPart.Worksheet = new S.Worksheet(new S.SheetViews(view), columns, data);

            var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1U, Name = SheetName(l.TableTitle) });

            // the comparison with the list of the tax authority: one sheet with every finding, a line each
            if (check != null && check.List.CodeColumn >= 0)
            {
                var findings = InvoiceCheck.FindingRows(check, l, out var findingHeaders);
                var checkData = new S.SheetData();
                checkData.Append(XlsxRow(1, findingHeaders.Select(h => (h, (decimal?)null, 1u)).ToList()));
                var n = 2u;
                foreach (var f in findings) checkData.Append(XlsxRow(n++, f.Select(text => (text, (decimal?)null, 0u)).ToList()));
                var checkWidths = new double[] { 30, 40, 12, 16, 34, 34, 16, 20, 60, 36 };
                var checkColumns = new S.Columns(checkWidths.Select((w, i) => new S.Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = w, CustomWidth = true }));
                var checkPane = new S.Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen };
                var checkView = new S.SheetView(checkPane, new S.Selection { Pane = S.PaneValues.BottomLeft }) { WorkbookViewId = 0U };
                var checkPart = workbookPart.AddNewPart<WorksheetPart>();
                checkPart.Worksheet = new S.Worksheet(new S.SheetViews(checkView), checkColumns, checkData);
                sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(checkPart), SheetId = 2U, Name = SheetName(InvoiceCheck.Labels.For(l).SheetName) });
            }
            workbookPart.Workbook.Save();
        }

        private static string SheetName(string title)
        {
            var name = new string(title.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray());
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }

        private static S.Row XlsxRow(uint index, List<(string Text, decimal? Number, uint Style)> cells)
        {
            var row = new S.Row { RowIndex = index };
            for (var i = 0; i < cells.Count; i++)
            {
                var (text, number, style) = cells[i];
                var reference = ColumnName(i) + index.ToString(CultureInfo.InvariantCulture);
                if (number != null)
                    row.Append(new S.Cell { CellReference = reference, StyleIndex = style, CellValue = new S.CellValue(number.Value.ToString(CultureInfo.InvariantCulture)) });
                else if (text.Length > 0)
                    row.Append(new S.Cell { CellReference = reference, StyleIndex = style, DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve }) });
                else if (style != 0)
                    row.Append(new S.Cell { CellReference = reference, StyleIndex = style });
            }
            return row;
        }

        private static string ColumnName(int index)
        {
            var name = "";
            for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        // 0 plain, 1 bold (header, total label), 2 number with two decimals, 3 the same in bold (total row).
        private static S.Stylesheet Styles() => new(
            new S.Fonts(
                new S.Font(new S.FontSize { Val = 11D }, new S.FontName { Val = "Calibri" }),
                new S.Font(new S.Bold(), new S.FontSize { Val = 11D }, new S.FontName { Val = "Calibri" })),
            new S.Fills(
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
            new S.Borders(new S.Border()),
            new S.CellStyleFormats(new S.CellFormat()),
            new S.CellFormats(
                new S.CellFormat(),
                new S.CellFormat { FontId = 1U, ApplyFont = true },
                new S.CellFormat { NumberFormatId = 4U, ApplyNumberFormat = true },
                new S.CellFormat { NumberFormatId = 4U, FontId = 1U, ApplyNumberFormat = true, ApplyFont = true }));
    }
}
