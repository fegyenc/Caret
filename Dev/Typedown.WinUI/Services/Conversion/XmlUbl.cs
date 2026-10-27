using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // The first format pack of the XML reader: electronic invoices in UBL 2.1, as Colombia's tax authority (DIAN) defines them: the Invoice,
    // CreditNote and DebitNote documents, the ApplicationResponse that the DIAN answers with, and the AttachedDocument container that carries
    // a document and its answer in one file (the documents are stored in it as text, in CDATA). Read from Resolution 000165 of 2023, annex
    // 1.9 (the UBL structure is standard, so the same view reads other UBL invoices, with the Colombian words only where the codes are Colombian).
    //
    // Principles: a code is explained only when it is certain, otherwise it is printed as it is; amounts and dates are printed as the file has
    // them (no reformatting, so the result does not depend on the PC's regional settings); nothing is validated or judged (that is the
    // authority's work, and the answer it gave is shown as it was written); and the caller adds every field of the file after this summary.
    internal static class XmlUbl
    {
        private const string UblNamespace = "urn:oasis:names:specification:ubl:schema:xsd:";

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels labels, StringBuilder sb)
        {
            if (!IsUbl(root)) return false;
            switch (root.Name.LocalName)
            {
                case "Invoice":
                case "CreditNote":
                case "DebitNote":
                    Header(sb, 1, DocumentTitle(root, labels), context, root, labels);
                    Document(sb, root, labels, 1);
                    return true;
                case "ApplicationResponse":
                    Header(sb, 1, labels.DianAnswer, context, root, labels);
                    Response(sb, root, labels, 1);
                    return true;
                case "AttachedDocument":
                    Container(sb, root, context, labels);
                    return true;
                default:
                    return false;
            }
        }

        // A root is a UBL document when its name and its namespace go together exactly (an Invoice in the namespace of the AttachedDocument is not one).
        internal static bool IsUbl(XElement element) =>
            element.Name.LocalName is "Invoice" or "CreditNote" or "DebitNote" or "ApplicationResponse" or "AttachedDocument"
            && element.Name.NamespaceName == UblNamespace + element.Name.LocalName + "-2";

        // ---- the documents -------------------------------------------------------------------------------------------------------------

        private static string DocumentTitle(XElement root, XmlLabels labels)
        {
            var spanish = labels.IsSpanish;
            var id = Text(root, "ID");
            string kind = root.Name.LocalName switch
            {
                "CreditNote" => spanish ? "Nota crédito electrónica" : "Electronic credit note",
                "DebitNote" => spanish ? "Nota débito electrónica" : "Electronic debit note",
                _ => InvoiceKind(root, spanish),
            };
            return id == null ? kind : kind + " " + id;
        }

        // The name of the kind of invoice. In a Peruvian file the codes are those of SUNAT (01 is a factura, 03 a boleta de venta), not the Colombian ones.
        private static string InvoiceKind(XElement root, bool spanish)
        {
            var typeCode = Text(root, "InvoiceTypeCode");
            var name = XmlPeru.IsPeruvian(root) ? XmlPeru.DocumentType(typeCode, spanish) : XmlLabels.InvoiceType(typeCode ?? "", spanish);
            return name ?? (typeCode is { } code ? (spanish ? $"Factura (tipo {code})" : $"Invoice (type {code})") : (spanish ? "Factura" : "Invoice"));
        }

        private static void Header(StringBuilder sb, int level, string title, ConversionContext context, XElement root, XmlLabels labels)
        {
            sb.Append(new string('#', level)).Append(' ').Append(Safe(title)).Append("\n\n");
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(labels.File).Append(": ").Append(Safe(context.SourceName)).Append('\n');
            var profile = Text(root, "ProfileID");
            if (profile != null && XmlPeru.IsPeruvian(root) && XmlPeru.Operation(profile, labels.IsSpanish) is { } operation) profile += " (" + operation + ")";
            var version = Text(root, "UBLVersionID");
            if (profile != null || version != null) sb.Append("> ").Append(string.Join(" · ", new[] { version, profile }.Where(s => s != null).Select(Safe))).Append('\n');
            sb.Append('\n');
        }

        private static void Document(StringBuilder sb, XElement root, XmlLabels labels, int level)
        {
            var spanish = labels.IsSpanish;
            var peru = XmlPeru.IsPeruvian(root);
            var facts = new List<(string, string)>();
            var issue = Text(root, "IssueDate");
            var time = Text(root, "IssueTime");
            if (issue != null) facts.Add((labels.Date, time == null ? issue : issue + " " + time));
            var due = Text(root, "DueDate") ?? Text(root, "PaymentMeans/PaymentDueDate");
            if (due != null) facts.Add((labels.DueDate, due));
            var currency = Text(root, "DocumentCurrencyCode");
            if (currency != null) facts.Add((labels.Currency, currency));
            var uuid = Child(root, "UUID");
            if (uuid != null && uuid.Value.Trim().Length > 0)
                facts.Add((labels.Cuf, uuid.Value.Trim() + (uuid.Attribute("schemeName") is { } scheme ? " (" + scheme.Value + ")" : "")));
            var environment = Text(root, "ProfileExecutionID");
            if (environment != null) facts.Add((labels.Environment, environment == "1" ? (spanish ? "Producción" : "Production") : environment == "2" ? (spanish ? "Pruebas" : "Test") : environment));
            Facts(sb, facts);

            var notes = Children(root, "Note").Where(n => n.Value.Trim().Length > 0).ToList();
            if (notes.Count > 0)
            {
                sb.Append("**").Append(labels.Note).Append("**\n\n");
                foreach (var n in notes)
                {
                    // a Peruvian note is a "legend" with a code (catalog 52): the amount in words, the internal code of the software...
                    // (a code that is not in the table is printed as it is)
                    var legendCode = peru ? n.Attribute("languageLocaleID")?.Value.Trim() : null;
                    var legend = string.IsNullOrEmpty(legendCode) ? null : XmlPeru.Legend(legendCode, spanish) ?? legendCode;
                    sb.Append("- ").Append(Safe(n.Value.Trim())).Append(legend == null ? "" : " (" + Safe(legend) + ")").Append('\n');
                }
                sb.Append('\n');
            }

            References(sb, root, labels, level, peru);
            Party(sb, Child(root, "AccountingSupplierParty"), labels.Issuer, labels, level + 1, peru);
            Party(sb, Child(root, "AccountingCustomerParty"), labels.Customer, labels, level + 1, peru);
            Payment(sb, root, labels, level + 1);
            Lines(sb, root, labels, level + 1, peru);
            TaxTable(sb, Children(root, "TaxTotal"), labels.Taxes, labels, level + 1, peru);
            TaxTable(sb, Children(root, "WithholdingTaxTotal"), labels.Withholdings, labels, level + 1, peru);
            Totals(sb, Child(root, "LegalMonetaryTotal") ?? Child(root, "RequestedMonetaryTotal"), labels, level + 1);
        }

        // A credit or debit note says which invoice it corrects and why.
        private static void References(StringBuilder sb, XElement root, XmlLabels labels, int level, bool peru = false)
        {
            var facts = new List<(string, string)>();
            var relatedIds = new HashSet<string>();
            foreach (var billing in Children(root, "BillingReference"))
            {
                var invoice = Child(billing, "InvoiceDocumentReference");
                if (invoice == null) continue;
                var id = Text(invoice, "ID");
                var uuid = Text(invoice, "UUID");
                if (id != null) relatedIds.Add(id);
                // in Peru the document that is corrected has a type (catalog 01: a factura, a boleta...)
                var typeCode = peru ? Text(invoice, "DocumentTypeCode") : null;
                var type = typeCode == null ? null : XmlPeru.DocumentType(typeCode, labels.IsSpanish) ?? typeCode;
                facts.Add((labels.RelatedInvoice, string.Join(" · ", new[] { id, uuid }.Where(s => s != null)) + (type == null ? "" : " (" + type + ")")));
            }
            foreach (var response in Children(root, "DiscrepancyResponse"))
            {
                var code = Text(response, "ResponseCode");
                var text = Text(response, "Description");
                // in Peru the reason of a note is a code of catalog 09 (credit note) or 10 (debit note)
                if (peru && XmlPeru.NoteReason(root.Name.LocalName, code, labels.IsSpanish) is { } reason)
                    text = text == null || string.Equals(text, reason, StringComparison.OrdinalIgnoreCase) ? reason : reason + " · " + text;
                facts.Add((labels.Reason, string.Join(": ", new[] { code, text }.Where(s => s != null))));
                // in Peru the affected document is also the ReferenceID here (the BillingReference is optional): said when no document above has that
                // number (when they differ, both are said)
                var affected = peru ? Text(response, "ReferenceID") : null;
                if (affected != null && relatedIds.Add(affected)) facts.Add((labels.RelatedInvoice, affected));
            }
            foreach (var order in Children(root, "OrderReference")) if (Text(order, "ID") is { } orderId) facts.Add((labels.Reference, orderId));
            Facts(sb, facts);
        }

        private static void Party(StringBuilder sb, XElement holder, string title, XmlLabels labels, int level, bool peru = false)
        {
            var party = holder == null ? null : Child(holder, "Party");
            if (party == null) return;
            var facts = new List<(string, string)>();
            var name = PartyName(holder);
            if (name != null) facts.Add((labels.Name, name));
            var id = PartyIdElement(party);
            if (id != null && id.Value.Trim().Length > 0)
            {
                string text;
                if (peru)
                {
                    // the schemeID is the type of identity document here (6 = RUC, 1 = DNI), not a check digit
                    var type = XmlPeru.IdType(id);
                    text = id.Value.Trim() + (string.IsNullOrEmpty(type) ? "" : " (" + (XmlPeru.IdTypeName(type, labels.IsSpanish) ?? (labels.IsSpanish ? "tipo " : "type ") + type) + ")");
                }
                else
                {
                    var kind = id.Attribute("schemeName")?.Value;
                    text = PartyId(holder);
                    if (!string.IsNullOrEmpty(kind)) text += " (" + (XmlLabels.IdTypesEs.TryGetValue(kind, out var kindName) && labels.IsSpanish ? kindName : kind) + ")";
                }
                facts.Add((labels.TaxId, text));
            }
            var address = Child(party, "PartyTaxScheme/RegistrationAddress") ?? Child(party, "PhysicalLocation/Address")
                ?? (peru ? Child(party, "PartyLegalEntity/RegistrationAddress") : null);
            if (address != null)
            {
                var parts = (peru
                        ? new[] { Text(address, "AddressLine/Line"), Text(address, "StreetName"), Text(address, "CitySubdivisionName"), Text(address, "District"), Text(address, "CityName"),
                                  Text(address, "CountrySubentity"), Text(address, "Country/Name") ?? Text(address, "Country/IdentificationCode") }
                        : new[] { Text(address, "AddressLine/Line"), Text(address, "CityName"), Text(address, "CountrySubentity"), Text(address, "Country/Name") })
                    .Where(p => !string.IsNullOrWhiteSpace(p));
                var joined = string.Join(", ", parts);
                if (joined.Length > 0) facts.Add((labels.Address, joined));
            }
            var mail = Text(party, "Contact/ElectronicMail");
            if (mail != null) facts.Add((labels.Email, mail));
            var phone = Text(party, "Contact/Telephone");
            if (phone != null) facts.Add((labels.Phone, phone));
            var responsibilities = Text(party, "PartyTaxScheme/TaxLevelCode");
            if (responsibilities != null) facts.Add((labels.FiscalResponsibilities, responsibilities));
            if (facts.Count == 0) return;
            Heading(sb, level, title);
            Facts(sb, facts);
        }

        // The name and the tax ID of a party (AccountingSupplierParty, AccountingCustomerParty); the ID has its check digit, which is the
        // attribute schemeID of the element (the kind of identification is the attribute schemeName).
        internal static string PartyName(XElement holder)
        {
            var party = holder == null ? null : Child(holder, "Party");
            return party == null ? null : Text(party, "PartyTaxScheme/RegistrationName") ?? Text(party, "PartyLegalEntity/RegistrationName") ?? Text(party, "PartyName/Name");
        }

        internal static string PartyId(XElement holder)
        {
            var party = holder == null ? null : Child(holder, "Party");
            var id = party == null ? null : PartyIdElement(party);
            if (id == null || id.Value.Trim().Length == 0) return null;
            var digit = id.Attribute("schemeID")?.Value;
            return id.Value.Trim() + (string.IsNullOrEmpty(digit) ? "" : "-" + digit);
        }

        // The number and the type of identity document of a Peruvian party, apart: the table says the type in words, in the language of the table.
        internal static string PartyIdNumber(XElement holder)
        {
            var party = holder == null ? null : Child(holder, "Party");
            var id = party == null ? null : PartyIdElement(party);
            return id == null || id.Value.Trim().Length == 0 ? null : id.Value.Trim();
        }

        internal static string PartyIdType(XElement holder)
        {
            var party = holder == null ? null : Child(holder, "Party");
            return XmlPeru.IdType(party == null ? null : PartyIdElement(party));
        }

        private static XElement PartyIdElement(XElement party) =>
            Child(party, "PartyTaxScheme/CompanyID") ?? Child(party, "PartyLegalEntity/CompanyID") ?? Child(party, "PartyIdentification/ID");

        private static void Payment(StringBuilder sb, XElement root, XmlLabels labels, int level)
        {
            var means = Child(root, "PaymentMeans");
            if (means == null) return;
            var facts = new List<(string, string)>();
            var form = Text(means, "ID");
            if (form != null) facts.Add((labels.PaymentForm, form == "1" ? labels.Cash : form == "2" ? labels.Credit : form));
            var code = Text(means, "PaymentMeansCode");
            if (code != null) facts.Add((labels.PaymentCode, code));
            if (facts.Count == 0) return;
            Heading(sb, level, labels.PaymentMeans);
            Facts(sb, facts);
        }

        private static void Lines(StringBuilder sb, XElement root, XmlLabels labels, int level, bool peru = false)
        {
            var lines = root.Elements().Where(e => e.Name.LocalName is "InvoiceLine" or "CreditNoteLine" or "DebitNoteLine").ToList();
            if (lines.Count == 0) return;
            Heading(sb, level, labels.Lines);
            // a Peruvian line has its affectation of the IGV (catalog 07) in the tax subtotal of the line; the column is there only when some line has it
            var treatments = peru ? lines.Select(Treatment).ToList() : null;
            var withTreatment = treatments != null && treatments.Any(t => t != null);
            var header = new List<string> { labels.Item, labels.Description, labels.Quantity, labels.Unit, labels.UnitPrice, labels.LineTotal };
            if (withTreatment) header.Add(XmlPeru.TaxTreatmentLabel(labels.IsSpanish));
            var rows = new List<IReadOnlyList<string>> { header };
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var quantity = line.Elements().FirstOrDefault(e => e.Name.LocalName is "InvoicedQuantity" or "CreditedQuantity" or "DebitedQuantity");
                var description = Text(line, "Item/Description") ?? Text(line, "Item/Name") ?? "";
                var row = new List<string>
                {
                    Text(line, "ID") ?? "", description, quantity?.Value.Trim() ?? "", quantity?.Attribute("unitCode")?.Value ?? "",
                    Money(Child(line, "Price/PriceAmount")), Money(Child(line, "LineExtensionAmount")),
                };
                if (withTreatment)
                {
                    var code = treatments[i];
                    row.Add(code == null ? "" : XmlPeru.Affectation(code, labels.IsSpanish) is { } name ? code + " · " + name : code);
                }
                rows.Add(row);
            }
            Table(sb, rows);
        }

        // The code of the affectation of the IGV of a Peruvian line (TaxExemptionReasonCode), if it has one.
        private static string Treatment(XElement line) =>
            Children(line, "TaxTotal").SelectMany(t => Children(t, "TaxSubtotal")).Select(s => Text(s, "TaxCategory/TaxExemptionReasonCode")).FirstOrDefault(c => c != null);

        private static void TaxTable(StringBuilder sb, IEnumerable<XElement> totals, string title, XmlLabels labels, int level, bool peru = false)
        {
            var rows = new List<IReadOnlyList<string>> { new[] { labels.Tax, labels.TaxableAmount, labels.Rate, labels.Amount } };
            foreach (var total in totals)
                foreach (var sub in Children(total, "TaxSubtotal"))
                {
                    var scheme = Child(sub, "TaxCategory/TaxScheme");
                    var code = scheme == null ? null : Text(scheme, "ID");
                    var name = scheme == null ? null : Text(scheme, "Name");
                    if (name == null && code != null)
                    {
                        if (peru) name = XmlPeru.TaxType(code, labels.IsSpanish);
                        else if (labels.IsSpanish && XmlLabels.TaxNamesEs.TryGetValue(code, out var known)) name = known;
                    }
                    var percent = Text(sub, "Percent") ?? Text(sub, "TaxCategory/Percent");
                    rows.Add(new[]
                    {
                        string.Join(" · ", new[] { code, name }.Where(s => !string.IsNullOrEmpty(s))),
                        Money(Child(sub, "TaxableAmount")), percent == null ? "" : percent + " %", Money(Child(sub, "TaxAmount")),
                    });
                }
            if (rows.Count == 1) return;
            Heading(sb, level, title);
            Table(sb, rows);
        }

        private static void Totals(StringBuilder sb, XElement totals, XmlLabels labels, int level)
        {
            if (totals == null) return;
            var facts = new List<(string, string)>();
            void Add(string label, string name) { var e = Child(totals, name); if (e != null && e.Value.Trim().Length > 0) facts.Add((label, Money(e))); }
            Add(labels.LineExtension, "LineExtensionAmount");
            Add(labels.TaxExclusive, "TaxExclusiveAmount");
            Add(labels.TaxInclusive, "TaxInclusiveAmount");
            Add(labels.Discounts, "AllowanceTotalAmount");
            Add(labels.Charges, "ChargeTotalAmount");
            Add(labels.Prepaid, "PrepaidAmount");
            Add(labels.Payable, "PayableAmount");
            if (facts.Count == 0) return;
            Heading(sb, level, labels.Totals);
            Facts(sb, facts);
        }

        // ---- the DIAN's answer, and the container -------------------------------------------------------------------------------------

        private static void Response(StringBuilder sb, XElement root, XmlLabels labels, int level, bool inContainer = false)
        {
            var facts = new List<(string, string)>();
            var date = Text(root, "IssueDate");
            var time = Text(root, "IssueTime");
            // inside the container the section already has the reference, the code and the date of the verification
            if (date != null && !inContainer) facts.Add((labels.Date, time == null ? date : date + " " + time));
            var sender = Text(root, "SenderParty/PartyTaxScheme/RegistrationName") ?? Text(root, "SenderParty/PartyName/Name");
            if (sender != null) facts.Add((labels.Provider, sender));
            var uuid = Text(root, "UUID");
            if (uuid != null && !inContainer) facts.Add((labels.Cuf, uuid));
            foreach (var response in Children(root, "DocumentResponse"))
            {
                var answer = Child(response, "Response");
                if (answer == null) continue;
                var code = Text(answer, "ResponseCode");
                var text = Text(answer, "Description");
                facts.Add((labels.Result, string.Join(": ", new[] { code, text }.Where(s => s != null))));
                var about = Text(response, "DocumentReference/ID");
                if (about != null && !inContainer) facts.Add((labels.Reference, about));
            }
            Facts(sb, facts);

            var rows = new List<IReadOnlyList<string>> { new[] { labels.Code, labels.Message } };
            foreach (var line in Children(root, "DocumentResponse").SelectMany(r => Children(r, "LineResponse")))
            {
                var answer = Child(line, "Response");
                if (answer == null) continue;
                rows.Add(new[] { Text(answer, "ResponseCode") ?? "", Text(answer, "Description") ?? "" });
            }
            if (rows.Count > 1)
            {
                Heading(sb, level + 1, labels.Responses);
                Table(sb, rows);
            }
        }

        private static void Container(StringBuilder sb, XElement root, ConversionContext context, XmlLabels labels)
        {
            var title = labels.Container + (Text(root, "ParentDocumentID") is { } parent ? " " + parent : "");
            Header(sb, 1, title, context, root, labels);
            sb.Append(labels.ContainerNote).Append("\n\n");
            var facts = new List<(string, string)>();
            var id = Text(root, "ID");
            if (id != null) facts.Add((labels.Reference, id));
            var date = Text(root, "IssueDate");
            var time = Text(root, "IssueTime");
            if (date != null) facts.Add((labels.Date, time == null ? date : date + " " + time));
            var sender = Text(root, "SenderParty/PartyTaxScheme/RegistrationName");
            if (sender != null) facts.Add((labels.Issuer, sender));
            var receiver = Text(root, "ReceiverParty/PartyTaxScheme/RegistrationName");
            if (receiver != null) facts.Add((labels.Customer, receiver));
            Facts(sb, facts);

            // the document itself, stored as text in the Description of the Attachment
            var embedded = Text(root, "Attachment/ExternalReference/Description");
            Embedded(sb, embedded, context, labels, 2, kind: null);

            // each document the container refers to: its code, and the answer (stored as text again)
            foreach (var reference in Children(root, "ParentDocumentLineReference"))
            {
                var doc = Child(reference, "DocumentReference");
                if (doc == null) continue;
                var type = Text(doc, "DocumentType");
                var rows = new List<(string, string)>();
                var docId = Text(doc, "ID");
                if (docId != null) rows.Add((labels.Reference, docId));
                var uuid = Text(doc, "UUID");
                if (uuid != null) rows.Add((labels.Cuf, uuid));
                var verification = Child(doc, "ResultOfVerification");
                if (verification != null)
                {
                    var code = Text(verification, "ValidationResultCode");
                    var vdate = Text(verification, "ValidationDate");
                    var vtime = Text(verification, "ValidationTime");
                    if (code != null) rows.Add((labels.Result, code));
                    if (vdate != null) rows.Add((labels.Date, vtime == null ? vdate : vdate + " " + vtime));
                }
                var isAnswer = type == "ApplicationResponse";
                if (rows.Count > 0 || isAnswer)
                {
                    Heading(sb, 2, isAnswer ? labels.DianAnswer : type ?? labels.References);
                    Facts(sb, rows);
                }
                Embedded(sb, Text(doc, "Attachment/ExternalReference/Description"), context, labels, 2, kind: type);
            }
        }

        private static void Embedded(StringBuilder sb, string xml, ConversionContext context, XmlLabels labels, int level, string kind)
        {
            if (string.IsNullOrWhiteSpace(xml)) return;
            XDocument inner;
            try { inner = XmlConverter.Parse(xml.Trim()); }
            catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
            {
                sb.Append("> ").Append(Safe(ex.Message)).Append("\n\n");
                return;
            }
            var root = inner.Root;
            if (root == null) return;
            // a document that is not in the UBL namespace of its name is not given a fiscal heading: it keeps its own name, and its fields are in the list at the end
            switch (IsUbl(root) ? root.Name.LocalName : "")
            {
                case "Invoice":
                case "CreditNote":
                case "DebitNote":
                    Heading(sb, level, DocumentTitle(root, labels));
                    Document(sb, root, labels, level);
                    break;
                case "ApplicationResponse":
                    // inside the container the section of the answer is already open (its heading and the result of the verification)
                    if (kind != "ApplicationResponse") Heading(sb, level, labels.DianAnswer);
                    Response(sb, root, labels, level, inContainer: kind == "ApplicationResponse");
                    break;
                default:
                    Heading(sb, level, root.Name.LocalName);
                    break;
            }
        }

        // ---- small helpers ------------------------------------------------------------------------------------------------------------

        // A table in the Markdown of the file, with a blank line after it, and every cell written so that its text stays text.
        internal static void Table(StringBuilder sb, List<IReadOnlyList<string>> rows)
        {
            var safe = rows.Select(r => (IReadOnlyList<string>)r.Select(c => Safe(c)).ToList()).ToList();
            sb.Append(MarkdownText.Table(safe).TrimEnd('\n')).Append("\n\n");
        }

        internal static void Facts(StringBuilder sb, List<(string Label, string Value)> facts)
        {
            if (facts.Count == 0) return;
            foreach (var (label, value) in facts) sb.Append("- **").Append(Safe(label)).Append(":** ").Append(Safe(value)).Append('\n');
            sb.Append('\n');
        }

        internal static void Heading(StringBuilder sb, int level, string title) =>
            sb.Append(new string('#', Math.Min(level, 6))).Append(' ').Append(Safe(title)).Append("\n\n");

        private static string Money(XElement amount)
        {
            if (amount == null) return "";
            var value = amount.Value.Trim();
            var currency = amount.Attribute("currencyID")?.Value;
            return string.IsNullOrEmpty(currency) ? value : value + " " + currency;
        }

        internal static string Safe(string text) => MarkdownText.EscapeInline(XmlConverter.OneLine(text ?? ""));

        // Direct children by local name (the UBL prefixes are not needed: the names are unique where they are used).
        internal static IEnumerable<XElement> Children(XElement parent, string name) => parent.Elements().Where(e => e.Name.LocalName == name);

        // A path of local names, "Party/PartyName/Name".
        internal static XElement Child(XElement parent, string path)
        {
            var current = parent;
            foreach (var step in path.Split('/'))
            {
                current = current?.Elements().FirstOrDefault(e => e.Name.LocalName == step);
                if (current == null) return null;
            }
            return current;
        }

        internal static string Text(XElement parent, string path)
        {
            var text = Child(parent, path)?.Value.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
