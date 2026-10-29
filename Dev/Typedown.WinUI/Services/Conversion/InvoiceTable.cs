using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
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
        public InvoiceAmount Base = new(), Taxes = new(), Withholdings = new(), Total = new(), Payable = new();
        public IEnumerable<InvoiceAmount> Amounts => new[] { Base, Taxes, Withholdings, Total, Payable };
        // an "Egreso" CFDI (a credit note, a return, a discount) is subtracted in the totals like a credit note
        public bool IsCreditNote => Kind == "CreditNote" || (Kind == "Cfdi" && TypeCode == "E") || (Kind == "Nfe" && XmlNfe.IsCreditNote(TypeCode)) || (Kind == "Dte" && XmlChile.IsCreditNote(TypeCode))
            || (Kind == "SaftPt" && XmlSaftPt.IsCreditNote(TypeCode));
    }

    internal enum InvoiceSkip { NotInvoice, AnswerOnly, NotWellFormed, TooBig, Unreadable, Duplicate, OtherCfdiType, NfeEvent, OtherDte, NfseNotIssued, SaftOther, Manifest }

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
        public int FilesSeen { get; set; }
        public long SourceBytes { get; set; }
    }

    internal static class InvoiceTable
    {
        // ---- reading ------------------------------------------------------------------------------------------------------------------

        // Every .xml file of the folder and its subfolders (hidden and system ones too), in the order of their paths. A folder that cannot be read,
        // and a link to another folder (not followed: it could lead back into this one), are listed, not skipped in silence.
        public static InvoiceTableResult ReadFolder(string folder)
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
                    files.AddRange(Directory.EnumerateFiles(directory, "*.xml", options));
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
                FileStream stream;
                try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.FilesSeen++;
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message });
                    continue;
                }
                using (stream) Add(result, stream, display);
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

        // One file into the result (for the tests, which use streams); Finish is called after the last one.
        internal static void Add(InvoiceTableResult result, Stream stream, string display)
        {
            result.FilesSeen++;
            XDocument document;
            // the length is asked inside the try too: a stream whose length cannot be read is one unreadable file, not the end of the scan
            try
            {
                if (stream.CanSeek) result.SourceBytes += stream.Length;
                document = XmlConverter.Load(stream);
            }
            catch (NotSupportedException ex) { result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.TooBig, Detail = ex.Message }); return; }
            // the file was opened and then could not be read to its end (a network drive that went away, a file that was cut): the file is counted once, here
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message }); return; }
            catch (InvalidDataException ex)
            {
                var place = ex.InnerException as System.Xml.XmlException;
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotWellFormed, Line = place?.LineNumber ?? 0, Position = place?.LinePosition ?? 0 });
                return;
            }
            var root = document.Root;
            if (root != null && XmlNfe.IsEvent(root))
            {
                var ev = XmlNfe.ReadEvent(root);
                if (ev != null)
                {
                    ev.File = display;
                    result.Events.Add(ev);
                    return;
                }
            }
            if (root != null && XmlNfse.ReadEvent(root) is { } nfseEvent)
            {
                nfseEvent.File = display;
                result.Events.Add(nfseEvent);
                return;
            }
            if (root != null && XmlCte.ReadEvent(root) is { } cteEvent)
            {
                cteEvent.File = display;
                result.Events.Add(cteEvent);
                return;
            }
            if (root != null && XmlCte.IsCte(root) && XmlCte.ReadRow(root, display) is { } cte)
            {
                result.Rows.Add(cte);
                return;
            }
            // a manifest of transport (and its events) lists documents that are carried; it is not an invoice and has no amounts to add
            if (root != null && (XmlMdfe.IsMdfe(root) || XmlMdfe.IsEvent(root)))
            {
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Manifest });
                return;
            }
            if (root != null && XmlNfe.IsNfe(root) && XmlNfe.ReadRow(root, display) is { } nfe)
            {
                result.Rows.Add(nfe);
                return;
            }
            if (root != null && XmlNfse.IsNfse(root))
            {
                if (XmlNfse.ReadRow(root, display) is { } nfse) result.Rows.Add(nfse);
                else result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NfseNotIssued });
                return;
            }
            if (root != null && XmlSaftPt.IsSaftPt(root))
            {
                var sales = XmlSaftPt.ReadRows(root, display, out var others);
                result.Rows.AddRange(sales);
                if (others.Length > 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.SaftOther, Detail = others });
                else if (sales.Count == 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                return;
            }
            if (root != null && XmlChile.IsChile(root))
            {
                // an envelope has up to 2000 documents; a dispatch note (52) is not an invoice, so it is listed once for the file, not added
                var documents = XmlChile.ReadRows(root, display);
                var apart = documents.Where(r => XmlChile.IsDispatchNote(r.TypeCode)).Select(r => r.TypeCode).Distinct().ToList();
                result.Rows.AddRange(documents.Where(r => !XmlChile.IsDispatchNote(r.TypeCode)));
                if (apart.Count > 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.OtherDte, Detail = string.Join(", ", apart) });
                else if (documents.Count == 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                return;
            }
            if (root != null && XmlCfdi.IsCfdi(root))
            {
                var cfdi = XmlCfdi.ReadRow(root, display);
                // only invoices (I) and their credit notes (E) are rows of a table of invoices: a transfer, a payroll or a payment CFDI is another kind
                // of document, and its amounts must not be added to the totals of the invoices (it is listed, with its type)
                if (cfdi.TypeCode is "I" or "E") result.Rows.Add(cfdi);
                else result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.OtherCfdiType, Detail = cfdi.TypeCode });
                return;
            }
            if (root == null || !XmlUbl.IsUbl(root))
            {
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NotInvoice });
                return;
            }
            switch (root.Name.LocalName)
            {
                case "Invoice":
                case "CreditNote":
                case "DebitNote":
                    result.Rows.Add(ReadDocument(root, display, null));
                    break;
                case "AttachedDocument":
                    var parts = ContainerParts(root);
                    var invoice = parts.FirstOrDefault(p => XmlUbl.IsUbl(p) && p.Name.LocalName is "Invoice" or "CreditNote" or "DebitNote");
                    if (invoice != null)
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
            result.Skipped.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.File, b.File));
        }

        // ---- the table ----------------------------------------------------------------------------------------------------------------

        private static string[] Headers(XmlLabels l) => new[]
        {
            l.Date, l.DocType, l.Number, l.Issuer, l.IssuerTaxId, l.Customer, l.CustomerTaxId, l.Currency,
            l.TaxExclusive, l.Taxes, l.Withholdings, l.TaxInclusive, l.Payable, l.Cuf, l.AuthorityAnswer, l.File,
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
            r.Base.Text, r.Taxes.Text, r.Withholdings.Text, r.Total.Text, r.Payable.Text, r.Cuf ?? "", r.Answer ?? "", r.File,
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

        public static string ToMarkdown(InvoiceTableResult result, XmlLabels l, string folderName)
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
                    var cells = new string[16];
                    for (var i = 0; i < cells.Length; i++) cells[i] = "";
                    cells[0] = "**" + MarkdownText.EscapeInline(TotalLabel(total, l)) + "**";
                    for (var c = 0; c < 5; c++) cells[8 + c] = total.Sums[c] is { } v ? "**" + v.ToString(CultureInfo.InvariantCulture) + "**" : "";
                    table.Add(cells);
                }
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
                if (result.Rows.Any(r => r.Void)) sb.Append(l.VoidNote).Append("\n\n");
            }
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
        public static void WriteXlsx(InvoiceTableResult result, XmlLabels l, Stream stream)
        {
            // the Open XML library needs a stream it can read back, which a file opened for writing is not: the workbook is made in memory
            using var memory = new MemoryStream();
            WriteWorkbook(result, l, memory);
            memory.WriteTo(stream);
        }

        private static void WriteWorkbook(InvoiceTableResult result, XmlLabels l, Stream stream)
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

            var widths = new double[] { 12, 22, 16, 34, 16, 34, 16, 9, 16, 14, 14, 16, 16, 40, 40, 36 };
            var columns = new S.Columns(widths.Select((w, i) => new S.Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = w, CustomWidth = true }));
            var pane = new S.Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen };
            var view = new S.SheetView(pane, new S.Selection { Pane = S.PaneValues.BottomLeft }) { WorkbookViewId = 0U };
            sheetPart.Worksheet = new S.Worksheet(new S.SheetViews(view), columns, data);

            var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1U, Name = SheetName(l.TableTitle) });
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
