using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: Portugal's SAF-T (PT), the standard audit file of the tax authority (AT), schema 1.04_01 (Portaria 302/2016). A company, or its
    // certified accountant, sends it every month (invoicing) or every year (accounting); a business of any size can have a file of many megabytes that
    // nobody can read raw. The summary is what a person wants first: whose file it is and for which period, which software made it, what it holds, the
    // sales by type and by VAT rate, the biggest customers; then the things worth a look: totals that do not add up, dates outside the period, a tax number
    // with a wrong check digit, a gap in the numbering of a series, a document without ATCUD, a customer that is not in the master file.
    //
    // The principles of the other packs: amounts and dates as the file has them (sums are printed with two decimals and a point), nothing is sent anywhere,
    // and the checks are plain arithmetic and format rules, not the AT's validator: a file with no finding can still be refused, and the summary says so.
    // The text is in English, Spanish and Portuguese (Portugal's words: "ficheiro", "fatura", "NIF"); the caller does not add the list of all fields,
    // because a file with thousands of documents would not be readable (the XML itself is in the Code view).
    internal static class XmlSaftPt
    {
        private const string NamespacePrefix = "urn:OECD:StandardAuditFile-Tax:PT";
        private const int Examples = 5;
        private const int MaxGapSeries = 8;
        private const int TopCustomers = 10;
        // the tolerance of the sums: a rounding of a cent per line is normal
        private const decimal Cent = 0.011m;
        private const string FinalConsumer = "999999990";

        internal static bool IsSaftPt(XElement root) =>
            root.Name.LocalName == "AuditFile" && root.Name.NamespaceName.StartsWith(NamespacePrefix, StringComparison.Ordinal) && XmlUbl.Child(root, "Header") != null;

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            if (!IsSaftPt(root)) return false;
            var w = new Words(xl.IsPortuguese ? 2 : xl.IsSpanish ? 1 : 0);
            var header = XmlUbl.Child(root, "Header");
            var findings = new List<string>();

            var company = T(header, "CompanyName");
            var year = T(header, "FiscalYear");
            XmlUbl.Heading(sb, 1, "SAF-T (PT)" + (company == null ? "" : " · " + company) + (year == null ? "" : " · " + year));
            var version = T(header, "AuditFileVersion");
            var basis = T(header, "TaxAccountingBasis");
            // one line (two quote lines would be joined by Markdown anyway): the file, the version of the schema and the kind of file
            sb.Append("> ").Append(XmlUbl.Safe(Join(" · ", string.IsNullOrEmpty(context.SourceName) ? null : xl.File + ": " + context.SourceName, version ?? root.Name.NamespaceName, basis == null ? null : Coded(w, BasisNames, basis)))).Append("\n\n");

            Company(sb, header, w, findings);
            FileFacts(sb, header, w, findings);

            var master = XmlUbl.Child(root, "MasterFiles");
            var customers = Parties(master, "Customer", "CustomerID", "CustomerTaxID", w, findings);
            var suppliers = Parties(master, "Supplier", "SupplierID", "SupplierTaxID", w, findings);
            var period = (Date(T(header, "StartDate")), Date(T(header, "EndDate")));
            var sources = XmlUbl.Child(root, "SourceDocuments");

            Contents(sb, root, master, w);
            var sales = Sales(sb, XmlUbl.Child(sources, "SalesInvoices"), customers, period, w, findings);
            Others(sb, sources, w, findings);
            Ledger(sb, XmlUbl.Child(root, "GeneralLedgerEntries"), w, findings);
            Checks(sb, findings, w, sales);
            return true;
        }

        // ---- the company and the file ---------------------------------------------------------------------------------------------------

        private static void Company(StringBuilder sb, XElement header, Words w, List<string> findings)
        {
            XmlUbl.Heading(sb, 2, w.W("The company", "La empresa", "A empresa"));
            var facts = new List<(string, string)>();
            Add(facts, w.W("Name", "Nombre", "Nome"), T(header, "CompanyName"));
            Add(facts, w.W("Business name", "Nombre comercial", "Nome comercial"), T(header, "BusinessName"));
            var nif = T(header, "TaxRegistrationNumber");
            Add(facts, "NIF", nif);
            Add(facts, w.W("Address", "Dirección", "Morada"), AddressText(XmlUbl.Child(header, "CompanyAddress")));
            Add(facts, "Email", T(header, "Email"));
            Add(facts, w.W("Phone", "Teléfono", "Telefone"), T(header, "Telephone"));
            Add(facts, "Website", T(header, "Website"));
            XmlUbl.Facts(sb, facts);
            if (nif != null && !IsValidNif(nif))
                findings.Add(w.W($"The tax number (NIF) of the company, {nif}, does not have a valid check digit.", $"El número de contribuyente (NIF) de la empresa, {nif}, no tiene un dígito de control válido.", $"O NIF da empresa, {nif}, não tem um dígito de controlo válido."));
        }

        private static void FileFacts(StringBuilder sb, XElement header, Words w, List<string> findings)
        {
            XmlUbl.Heading(sb, 2, w.W("The file", "El archivo", "O ficheiro"));
            var facts = new List<(string, string)>();
            var basis = T(header, "TaxAccountingBasis");
            Add(facts, w.W("Kind of file", "Tipo de archivo", "Tipo de ficheiro"), Coded(w, BasisNames, basis));
            Add(facts, w.W("SAF-T version", "Versión de SAF-T", "Versão do SAF-T"), T(header, "AuditFileVersion"));
            Add(facts, w.W("Fiscal year", "Año fiscal", "Ano fiscal"), T(header, "FiscalYear"));
            var start = T(header, "StartDate");
            var end = T(header, "EndDate");
            if (start != null || end != null) Add(facts, w.W("Period", "Período", "Período"), start + " → " + end);
            Add(facts, w.W("Created", "Creado", "Criado em"), T(header, "DateCreated"));
            var currency = T(header, "CurrencyCode");
            Add(facts, w.W("Currency", "Moneda", "Moeda"), currency);
            Add(facts, w.W("Tax entity", "Entidad fiscal", "Entidade fiscal"), T(header, "TaxEntity"));
            Add(facts, "Software", Join(" ", T(header, "ProductID"), T(header, "ProductVersion")));
            Add(facts, w.W("Software certificate number", "Número de certificado del software", "Número do certificado do software"), T(header, "SoftwareCertificateNumber"));
            Add(facts, w.W("Software producer NIF", "NIF del productor del software", "NIF do produtor do software"), T(header, "ProductCompanyTaxID"));
            Add(facts, w.W("Comment", "Comentario", "Comentário"), T(header, "HeaderComment"));
            XmlUbl.Facts(sb, facts);
            if (currency != null && currency != "EUR")
                findings.Add(w.W($"The currency of the file is {currency}; a SAF-T (PT) file is in EUR.", $"La moneda del archivo es {currency}; un archivo SAF-T (PT) está en EUR.", $"A moeda do ficheiro é {currency}; um ficheiro SAF-T (PT) está em EUR."));
        }

        // ---- the master files ------------------------------------------------------------------------------------------------------------

        private sealed record Party(string Id, string Name, string Nif, string Country);

        private static Dictionary<string, Party> Parties(XElement master, string element, string idName, string nifName, Words w, List<string> findings)
        {
            var map = new Dictionary<string, Party>(StringComparer.Ordinal);
            var bad = new List<string>();
            var badCount = 0;
            foreach (var p in master == null ? Enumerable.Empty<XElement>() : XmlUbl.Children(master, element))
            {
                var id = T(p, idName);
                var nif = T(p, nifName);
                var country = T(p, "BillingAddress/Country");
                if (id != null) map[id] = new Party(id, T(p, "CompanyName"), nif, country);
                // a Portuguese tax number has a check digit; the other countries' numbers are not checked, and the final consumer has no number
                if (nif != null && nif != FinalConsumer && country == "PT" && !IsValidNif(nif))
                {
                    badCount++;
                    if (bad.Count < Examples) bad.Add(nif + " (" + (T(p, "CompanyName") ?? id) + ")");
                }
            }
            if (badCount > 0)
            {
                var customer = element == "Customer";
                findings.Add(Item(w,
                    customer ? "Customers with a Portuguese address and an invalid NIF check digit" : "Suppliers with a Portuguese address and an invalid NIF check digit",
                    customer ? "Clientes con dirección en Portugal y un NIF con dígito de control no válido" : "Proveedores con dirección en Portugal y un NIF con dígito de control no válido",
                    customer ? "Clientes com morada em Portugal e um NIF com dígito de controlo inválido" : "Fornecedores com morada em Portugal e um NIF com dígito de controlo inválido",
                    badCount, string.Join(", ", bad) + More(badCount, bad.Count)));
            }
            return map;
        }

        private static void Contents(StringBuilder sb, XElement root, XElement master, Words w)
        {
            var rows = new List<IReadOnlyList<string>> { new[] { w.W("What", "Qué", "O quê"), w.W("How many", "Cuántos", "Quantos") } };
            void Row(string label, int count) { if (count > 0) rows.Add(new[] { label, Num(count) }); }
            if (master != null)
            {
                Row(w.W("Customers", "Clientes", "Clientes"), XmlUbl.Children(master, "Customer").Count());
                Row(w.W("Suppliers", "Proveedores", "Fornecedores"), XmlUbl.Children(master, "Supplier").Count());
                Row(w.W("Products and services", "Productos y servicios", "Produtos e serviços"), XmlUbl.Children(master, "Product").Count());
                Row(w.W("Tax table entries", "Entradas de la tabla de impuestos", "Entradas da tabela de impostos"), XmlUbl.Child(master, "TaxTable")?.Elements().Count() ?? 0);
                Row(w.W("Ledger accounts", "Cuentas contables", "Contas do plano de contas"), XmlUbl.Child(master, "GeneralLedgerAccounts")?.Elements().Count() ?? 0);
            }
            var sources = XmlUbl.Child(root, "SourceDocuments");
            Row(w.W("Sales documents", "Documentos de venta", "Documentos de venda"), XmlUbl.Child(sources, "SalesInvoices")?.Elements().Count(e => e.Name.LocalName == "Invoice") ?? 0);
            Row(w.W("Stock movements", "Movimientos de existencias", "Movimentos de mercadorias"), XmlUbl.Child(sources, "MovementOfGoods")?.Elements().Count(e => e.Name.LocalName == "StockMovement") ?? 0);
            Row(w.W("Working documents", "Documentos de trabajo", "Documentos de conferência"), XmlUbl.Child(sources, "WorkingDocuments")?.Elements().Count(e => e.Name.LocalName == "WorkDocument") ?? 0);
            Row(w.W("Payments (receipts)", "Pagos (recibos)", "Pagamentos (recibos)"), XmlUbl.Child(sources, "Payments")?.Elements().Count(e => e.Name.LocalName == "Payment") ?? 0);
            Row(w.W("Ledger transactions", "Asientos contables", "Lançamentos contabilísticos"), XmlUbl.Child(root, "GeneralLedgerEntries")?.Descendants().Count(e => e.Name.LocalName == "Transaction") ?? 0);
            if (rows.Count == 1) return;
            XmlUbl.Heading(sb, 2, w.W("What the file contains", "Qué contiene el archivo", "O que o ficheiro contém"));
            XmlUbl.Table(sb, rows);
        }

        // ---- the sales documents ---------------------------------------------------------------------------------------------------------

        private sealed class Totals
        {
            public int Count, Voided;
            public decimal Net, Tax, Gross;
        }

        private sealed class SalesResult
        {
            public int DocumentCount;
        }

        private static readonly Regex Numbering = new(@"^(?<t>\S+)\s+(?<s>.+)/(?<n>\d+)$", RegexOptions.CultureInvariant);

        private static SalesResult Sales(StringBuilder sb, XElement section, Dictionary<string, Party> customers, (DateTime? Start, DateTime? End) period, Words w, List<string> findings)
        {
            var result = new SalesResult();
            if (section == null) return result;
            var byType = new SortedDictionary<string, Totals>(StringComparer.Ordinal);
            var vat = new SortedDictionary<string, (decimal Base, decimal Tax, string Label, decimal Rate)>(StringComparer.Ordinal);
            var perCustomer = new Dictionary<string, (int Count, decimal Gross)>(StringComparer.Ordinal);
            var series = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            var duplicateCount = 0;
            decimal debitAll = 0, creditAll = 0, debitLive = 0, creditLive = 0;
            int count = 0, live = 0, finalConsumerDocs = 0;
            decimal finalConsumerGross = 0;
            var badTotals = new List<string>(); var badTotalsCount = 0;
            var outside = new List<string>(); var outsideCount = 0;
            var noAtcud = 0; string firstNoAtcud = null;
            var unknownCustomers = new SortedSet<string>(StringComparer.Ordinal);
            var zeroNoExemption = 0;

            foreach (var inv in XmlUbl.Children(section, "Invoice"))
            {
                count++;
                var no = T(inv, "InvoiceNo") ?? "?";
                var type = T(inv, "InvoiceType") ?? "?";
                var voided = T(inv, "DocumentStatus/InvoiceStatus") == "A";
                var date = Date(T(inv, "InvoiceDate"));
                var customer = T(inv, "CustomerID");
                var totals = XmlUbl.Child(inv, "DocumentTotals");
                var net = Dec(T(totals, "NetTotal")) ?? 0;
                var tax = Dec(T(totals, "TaxPayable")) ?? 0;
                var gross = Dec(T(totals, "GrossTotal")) ?? 0;
                var sign = Subtracts(type) ? -1 : 1;

                if (!seen.Add(no))
                {
                    duplicateCount++;
                    if (duplicates.Count < Examples && !duplicates.Contains(no)) duplicates.Add(no);
                }
                var m = Numbering.Match(no);
                if (m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    var key = m.Groups["t"].Value + " " + m.Groups["s"].Value;
                    if (!series.TryGetValue(key, out var list)) series[key] = list = new List<int>();
                    list.Add(n);
                }

                if (!byType.TryGetValue(type, out var t)) byType[type] = t = new Totals();
                t.Count++;
                if (voided) t.Voided++;
                else
                {
                    live++;
                    t.Net += net; t.Tax += tax; t.Gross += gross;
                    if (customer != null)
                    {
                        perCustomer.TryGetValue(customer, out var c);
                        perCustomer[customer] = (c.Count + 1, c.Gross + sign * gross);
                    }
                    if (Math.Abs(gross - (net + tax)) > Cent && badTotals.Count < Examples) badTotals.Add($"{no}: {Num(gross)} ≠ {Num(net)} + {Num(tax)}");
                    if (Math.Abs(gross - (net + tax)) > Cent) badTotalsCount++;
                    if (customer != null && customers.TryGetValue(customer, out var party) && party.Nif == FinalConsumer) { finalConsumerDocs++; finalConsumerGross += sign * gross; }
                }
                if (date != null && period.Start != null && period.End != null && (date < period.Start || date > period.End))
                {
                    outsideCount++;
                    if (outside.Count < Examples) outside.Add($"{no} ({T(inv, "InvoiceDate")})");
                }
                if (date != null && date >= new DateTime(2023, 1, 1))
                {
                    var atcud = T(inv, "ATCUD");
                    if (string.IsNullOrEmpty(atcud) || atcud == "0") { noAtcud++; firstNoAtcud ??= no; }
                }
                if (customer != null && customer.Length > 0 && !customers.ContainsKey(customer)) unknownCustomers.Add(customer);

                foreach (var line in XmlUbl.Children(inv, "Line"))
                {
                    var credit = Dec(T(line, "CreditAmount")) ?? 0;
                    var debit = Dec(T(line, "DebitAmount")) ?? 0;
                    creditAll += credit; debitAll += debit;
                    if (!voided) { creditLive += credit; debitLive += debit; }
                    var taxElement = XmlUbl.Child(line, "Tax");
                    var rate = Dec(T(taxElement, "TaxPercentage"));
                    if (!voided && taxElement != null)
                    {
                        var kind = T(taxElement, "TaxType") + " " + (T(taxElement, "TaxCountryRegion") ?? "") + " " + (T(taxElement, "TaxCode") ?? "");
                        var key = kind.Trim() + " " + (rate?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?");
                        vat.TryGetValue(key, out var v);
                        var baseAmount = credit - debit;
                        vat[key] = (v.Base + baseAmount, v.Tax + baseAmount * (rate ?? 0) / 100m, kind.Trim(), rate ?? 0);
                        if (rate == 0 && T(taxElement, "TaxType") == "IVA" && string.IsNullOrEmpty(T(line, "TaxExemptionCode")) && string.IsNullOrEmpty(T(line, "TaxExemptionReason"))) zeroNoExemption++;
                    }
                }
            }
            result.DocumentCount = count;

            // the header of the section: what the file says against what is in it (the voided documents are in the file; the rules of the totals are read both ways)
            var declaredEntries = Dec(T(section, "NumberOfEntries"));
            var declaredDebit = Dec(T(section, "TotalDebit"));
            var declaredCredit = Dec(T(section, "TotalCredit"));
            var name = w.W("sales documents", "documentos de venta", "documentos de venda");
            if (declaredEntries != null && declaredEntries != count && declaredEntries != live)
                findings.Add(w.W($"The header of the {name} says {Whole(declaredEntries.Value)} entries; the file has {Num(count)}{(live != count ? $" ({Num(live)} without the voided ones)" : "")}.",
                    $"La cabecera de los {name} dice {Whole(declaredEntries.Value)} entradas; el archivo tiene {Num(count)}{(live != count ? $" ({Num(live)} sin los anulados)" : "")}.",
                    $"O cabeçalho dos {name} diz {Whole(declaredEntries.Value)} entradas; o ficheiro tem {Num(count)}{(live != count ? $" ({Num(live)} sem os anulados)" : "")}."));
            SumMismatch(findings, w, name, "TotalCredit", declaredCredit, creditAll, creditLive);
            SumMismatch(findings, w, name, "TotalDebit", declaredDebit, debitAll, debitLive);

            // the table by type; credit notes and returns are subtracted in the last row
            XmlUbl.Heading(sb, 2, w.W("Sales documents", "Documentos de venta", "Documentos de venda"));
            var rows = new List<IReadOnlyList<string>> { new[] { w.W("Type", "Tipo", "Tipo"), w.W("Documents", "Documentos", "Documentos"), w.W("Voided", "Anulados", "Anulados"), w.W("Total before VAT", "Total sin IVA", "Total sem IVA"), w.W("VAT", "IVA", "IVA"), w.W("Total with VAT", "Total con IVA", "Total com IVA") } };
            decimal netSum = 0, taxSum = 0, grossSum = 0;
            foreach (var (type, t) in byType)
            {
                rows.Add(new[] { Coded(w, InvoiceNames, type), Num(t.Count), t.Voided == 0 ? "" : Num(t.Voided), Num(t.Net), Num(t.Tax), Num(t.Gross) });
                var sign = Subtracts(type) ? -1 : 1;
                netSum += sign * t.Net; taxSum += sign * t.Tax; grossSum += sign * t.Gross;
            }
            rows.Add(new[] { w.W("Total (credit notes and returns subtracted, voided left out)", "Total (notas de crédito y devoluciones restadas, anulados excluidos)", "Total (notas de crédito e devoluções subtraídas, anulados excluídos)"), Num(count), Num(count - live), Num(netSum), Num(taxSum), Num(grossSum) });
            XmlUbl.Table(sb, rows);
            if (finalConsumerDocs > 0)
                XmlUbl.Facts(sb, new List<(string, string)> { (w.W("Documents to the final consumer (NIF 999999990)", "Documentos al consumidor final (NIF 999999990)", "Documentos a consumidor final (NIF 999999990)"), $"{Num(finalConsumerDocs)} · {Num(finalConsumerGross)}") });

            if (vat.Count > 0)
            {
                XmlUbl.Heading(sb, 3, w.W("By VAT rate", "Por tipo de IVA", "Por taxa de IVA"));
                var vr = new List<IReadOnlyList<string>> { new[] { w.W("Tax", "Impuesto", "Imposto"), w.W("Rate", "Tipo", "Taxa"), w.W("Base (credit notes subtracted)", "Base (notas de crédito restadas)", "Base (notas de crédito subtraídas)"), w.W("Calculated tax", "Impuesto calculado", "Imposto calculado") } };
                foreach (var (_, v) in vat.OrderByDescending(p => p.Value.Base))
                    vr.Add(new[] { v.Label, v.Rate.ToString("0.##", CultureInfo.InvariantCulture) + " %", Num(v.Base), Num(v.Tax) });
                XmlUbl.Table(sb, vr);
                sb.Append(w.W("The tax is the base times the rate, calculated from the lines; the totals of the documents can differ by rounding.",
                    "El impuesto es la base por el tipo, calculado a partir de las líneas; los totales de los documentos pueden diferir por redondeo.",
                    "O imposto é a base vezes a taxa, calculado a partir das linhas; os totais dos documentos podem diferir por arredondamento.")).Append("\n\n");
            }

            if (perCustomer.Count > 1)
            {
                XmlUbl.Heading(sb, 3, w.W("Biggest customers", "Mayores clientes", "Maiores clientes"));
                var cr = new List<IReadOnlyList<string>> { new[] { w.W("Customer", "Cliente", "Cliente"), "NIF", w.W("Documents", "Documentos", "Documentos"), w.W("Total with VAT", "Total con IVA", "Total com IVA") } };
                foreach (var (id, c) in perCustomer.OrderByDescending(p => p.Value.Gross).ThenBy(p => p.Key, StringComparer.Ordinal).Take(TopCustomers))
                {
                    customers.TryGetValue(id, out var party);
                    cr.Add(new[] { party?.Name ?? id, party?.Nif ?? "", Num(c.Count), Num(c.Gross) });
                }
                XmlUbl.Table(sb, cr);
            }

            // the findings of the documents
            if (badTotalsCount > 0)
                findings.Add(Item(w, "Documents whose total with VAT is not the total before VAT plus the VAT", "Documentos cuyo total con IVA no es el total sin IVA más el IVA", "Documentos cujo total com IVA não é o total sem IVA mais o IVA",
                    badTotalsCount, string.Join("; ", badTotals) + More(badTotalsCount, badTotals.Count)));
            if (outsideCount > 0)
                findings.Add(Item(w, $"Documents dated outside the period of the file ({period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd})", $"Documentos con fecha fuera del período del archivo ({period.Start:yyyy-MM-dd} a {period.End:yyyy-MM-dd})", $"Documentos com data fora do período do ficheiro ({period.Start:yyyy-MM-dd} a {period.End:yyyy-MM-dd})",
                    outsideCount, string.Join(", ", outside) + More(outsideCount, outside.Count)));
            if (duplicateCount > 0)
                findings.Add(Item(w, "Document numbers that appear more than once", "Números de documento que aparecen más de una vez", "Números de documento que aparecem mais de uma vez", duplicateCount, string.Join(", ", duplicates) + More(duplicateCount, duplicates.Count)));
            Gaps(findings, series, w);
            if (noAtcud > 0)
                findings.Add(Item(w, "Documents dated 2023 or later with no ATCUD, or an ATCUD of 0", "Documentos de 2023 o posteriores sin ATCUD, o con un ATCUD de 0", "Documentos de 2023 ou posteriores sem ATCUD, ou com ATCUD 0", noAtcud, w.W("the first is ", "el primero es ", "o primeiro é ") + firstNoAtcud));
            if (unknownCustomers.Count > 0)
                findings.Add(Item(w, "Customer codes used in documents but not in the customer list", "Códigos de cliente usados en documentos pero que no están en la lista de clientes", "Códigos de cliente usados em documentos mas que não estão na lista de clientes",
                    unknownCustomers.Count, string.Join(", ", unknownCustomers.Take(Examples)) + More(unknownCustomers.Count, Math.Min(Examples, unknownCustomers.Count))));
            if (zeroNoExemption > 0)
                findings.Add(Item(w, "Lines with 0 % VAT and no exemption code or reason", "Líneas con 0 % de IVA y sin código ni motivo de exención", "Linhas com IVA a 0 % e sem código nem motivo de isenção", zeroNoExemption, null));
            return result;
        }

        // One series is a run of numbers; a number that is missing between two that are there is a gap (a voided document stays in the file, so it is not one).
        private static void Gaps(List<string> findings, Dictionary<string, List<int>> series, Words w)
        {
            var shown = 0;
            var total = 0;
            var parts = new List<string>();
            foreach (var (key, numbers) in series.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var sorted = numbers.Distinct().OrderBy(n => n).ToList();
                var gaps = new List<string>();
                var missing = 0;
                for (var i = 1; i < sorted.Count; i++)
                {
                    var step = sorted[i] - sorted[i - 1];
                    if (step <= 1) continue;
                    missing += step - 1;
                    if (gaps.Count < 3) gaps.Add(step == 2 ? Num(sorted[i - 1] + 1) : Num(sorted[i - 1] + 1) + "–" + Num(sorted[i] - 1));
                }
                if (missing == 0) continue;
                total += missing;
                if (shown++ < MaxGapSeries) parts.Add($"{key}: {string.Join(", ", gaps)}");
            }
            if (total == 0) return;
            findings.Add(Item(w, "Numbers missing in the numbering of a series", "Números que faltan en la numeración de una serie", "Números em falta na numeração de uma série", total, string.Join("; ", parts) + (shown > MaxGapSeries ? "; …" : "")));
        }

        // The totals of the header of a section: they match the sum of all the lines, or the sum without the voided documents (the rules for the voided ones
        // are not read here; either is accepted).
        private static void SumMismatch(List<string> findings, Words w, string name, string field, decimal? declared, decimal all, decimal live)
        {
            if (declared == null) return;
            if (Math.Abs(declared.Value - all) <= Cent || Math.Abs(declared.Value - live) <= Cent) return;
            findings.Add(w.W($"{field} in the header of the {name} is {Num(declared.Value)}; the lines add up to {Num(all)}{(all != live ? $" ({Num(live)} without the voided documents)" : "")}.",
                $"{field} en la cabecera de los {name} es {Num(declared.Value)}; las líneas suman {Num(all)}{(all != live ? $" ({Num(live)} sin los documentos anulados)" : "")}.",
                $"{field} no cabeçalho dos {name} é {Num(declared.Value)}; as linhas somam {Num(all)}{(all != live ? $" ({Num(live)} sem os documentos anulados)" : "")}."));
        }

        // ---- the other sections ----------------------------------------------------------------------------------------------------------

        private static void Others(StringBuilder sb, XElement sources, Words w, List<string> findings)
        {
            Simple(sb, XmlUbl.Child(sources, "WorkingDocuments"), "WorkDocument", "WorkType", "DocumentStatus/WorkStatus", WorkNames, w.W("Working documents", "Documentos de trabajo", "Documentos de conferência"), w, findings);
            Simple(sb, XmlUbl.Child(sources, "MovementOfGoods"), "StockMovement", "MovementType", "DocumentStatus/MovementStatus", MovementNames, w.W("Stock movements", "Movimientos de existencias", "Movimentos de mercadorias"), w, findings);
            Simple(sb, XmlUbl.Child(sources, "Payments"), "Payment", "PaymentType", "DocumentStatus/PaymentStatus", PaymentNames, w.W("Payments (receipts)", "Pagos (recibos)", "Pagamentos (recibos)"), w, findings);
        }

        // A section with a count by type and its header against its lines. `voidedCode`: the status that means voided (A) is read from the status path.
        private static void Simple(StringBuilder sb, XElement section, string item, string typeName, string statusPath, IReadOnlyDictionary<string, (string En, string Es, string Pt)> names, string title, Words w, List<string> findings)
        {
            if (section == null) return;
            var byType = new SortedDictionary<string, (int Count, int Voided, decimal Gross)>(StringComparer.Ordinal);
            decimal debitAll = 0, creditAll = 0, debitLive = 0, creditLive = 0;
            int count = 0, live = 0;
            foreach (var doc in XmlUbl.Children(section, item))
            {
                count++;
                var type = T(doc, typeName) ?? "?";
                var voided = T(doc, statusPath) == "A";
                var gross = Dec(T(doc, "DocumentTotals/GrossTotal")) ?? 0;
                byType.TryGetValue(type, out var t);
                byType[type] = (t.Count + 1, t.Voided + (voided ? 1 : 0), t.Gross + (voided ? 0 : gross));
                if (!voided) live++;
                foreach (var line in XmlUbl.Children(doc, "Line"))
                {
                    var credit = Dec(T(line, "CreditAmount")) ?? 0;
                    var debit = Dec(T(line, "DebitAmount")) ?? 0;
                    creditAll += credit; debitAll += debit;
                    if (!voided) { creditLive += credit; debitLive += debit; }
                }
            }
            if (count == 0) return;
            XmlUbl.Heading(sb, 2, title);
            var rows = new List<IReadOnlyList<string>> { new[] { w.W("Type", "Tipo", "Tipo"), w.W("Documents", "Documentos", "Documentos"), w.W("Voided", "Anulados", "Anulados"), w.W("Total with VAT", "Total con IVA", "Total com IVA") } };
            foreach (var (type, t) in byType)
                rows.Add(new[] { names.TryGetValue(type, out var n) ? type + " — " + w.Pick(n) : type, Num(t.Count), t.Voided == 0 ? "" : Num(t.Voided), item == "StockMovement" ? "" : Num(t.Gross) });
            XmlUbl.Table(sb, rows);
            var entries = Dec(T(section, item == "StockMovement" ? "NumberOfMovementLines" : "NumberOfEntries"));
            var lowered = title.ToLowerInvariant();
            if (item != "StockMovement")
            {
                if (entries != null && entries != count && entries != live)
                    findings.Add(w.W($"The header of the {lowered} says {Whole(entries.Value)} entries; the file has {Num(count)}.", $"La cabecera de {lowered} dice {Whole(entries.Value)} entradas; el archivo tiene {Num(count)}.", $"O cabeçalho de {lowered} diz {Whole(entries.Value)} entradas; o ficheiro tem {Num(count)}."));
                SumMismatch(findings, w, lowered, "TotalCredit", Dec(T(section, "TotalCredit")), creditAll, creditLive);
                SumMismatch(findings, w, lowered, "TotalDebit", Dec(T(section, "TotalDebit")), debitAll, debitLive);
            }
        }

        // The ledger of an accounting file: every entry has to balance, and the totals of the header are the sums of the lines.
        private static void Ledger(StringBuilder sb, XElement ledger, Words w, List<string> findings)
        {
            if (ledger == null) return;
            decimal debit = 0, credit = 0;
            int transactions = 0, unbalanced = 0;
            var examples = new List<string>();
            foreach (var tx in ledger.Descendants().Where(e => e.Name.LocalName == "Transaction"))
            {
                transactions++;
                decimal d = 0, c = 0;
                foreach (var line in tx.Descendants())
                {
                    if (line.Name.LocalName == "DebitAmount" && !line.HasElements) d += Dec(line.Value) ?? 0;
                    else if (line.Name.LocalName == "CreditAmount" && !line.HasElements) c += Dec(line.Value) ?? 0;
                }
                debit += d; credit += c;
                if (Math.Abs(d - c) > Cent)
                {
                    unbalanced++;
                    if (examples.Count < Examples) examples.Add($"{T(tx, "TransactionID") ?? "?"} ({Num(d)} ≠ {Num(c)})");
                }
            }
            if (transactions == 0) return;
            XmlUbl.Heading(sb, 2, w.W("Ledger", "Contabilidad", "Contabilidade"));
            XmlUbl.Facts(sb, new List<(string, string)>
            {
                (w.W("Transactions", "Asientos", "Lançamentos"), Num(transactions)),
                (w.W("Total debit", "Total debe", "Total débito"), Num(debit)),
                (w.W("Total credit", "Total haber", "Total crédito"), Num(credit)),
            });
            if (unbalanced > 0)
                findings.Add(Item(w, "Ledger transactions where debit and credit are not equal", "Asientos contables en los que el debe y el haber no son iguales", "Lançamentos contabilísticos em que o débito e o crédito não são iguais", unbalanced, string.Join("; ", examples) + More(unbalanced, examples.Count)));
            var declaredDebit = Dec(T(ledger, "TotalDebit"));
            var declaredCredit = Dec(T(ledger, "TotalCredit"));
            var name = w.W("ledger entries", "asientos contables", "lançamentos contabilísticos");
            SumMismatch(findings, w, name, "TotalDebit", declaredDebit, debit, debit);
            SumMismatch(findings, w, name, "TotalCredit", declaredCredit, credit, credit);
            var entries = Dec(T(ledger, "NumberOfEntries"));
            if (entries != null && entries != transactions)
                findings.Add(w.W($"The header of the ledger says {Whole(entries.Value)} entries; the file has {Num(transactions)}.", $"La cabecera de la contabilidad dice {Whole(entries.Value)} entradas; el archivo tiene {Num(transactions)}.", $"O cabeçalho da contabilidade diz {Whole(entries.Value)} entradas; o ficheiro tem {Num(transactions)}."));
        }

        // ---- the findings ----------------------------------------------------------------------------------------------------------------

        private static void Checks(StringBuilder sb, List<string> findings, Words w, SalesResult sales)
        {
            XmlUbl.Heading(sb, 2, w.W("Things to look at", "Cosas que revisar", "Pontos a verificar"));
            if (findings.Count == 0)
                sb.Append(w.W("The checks below found nothing.", "Las verificaciones de abajo no encontraron nada.", "As verificações abaixo não encontraram nada.")).Append("\n\n");
            else
                foreach (var f in findings) sb.Append("- ").Append(XmlUbl.Safe(f)).Append('\n');
            if (findings.Count > 0) sb.Append('\n');
            sb.Append(w.W(
                "What is checked: the counts and totals in the headers against the lines, the total of each document, the dates against the period, the check digit of Portuguese tax numbers, gaps and repeats in the numbering, the ATCUD, the customers against the customer list, 0 % VAT lines without an exemption code or reason, the currency, and the balance of ledger entries. These are simple rules, not the validator of the Autoridade Tributária: a file with nothing here can still be refused, and the schema (XSD) is checked with Edit > Check against schema.",
                "Qué se revisa: los recuentos y totales de las cabeceras frente a las líneas, el total de cada documento, las fechas frente al período, el dígito de control de los NIF portugueses, los saltos y repeticiones en la numeración, el ATCUD, los clientes frente a la lista de clientes, las líneas con 0 % de IVA sin código ni motivo de exención, la moneda y el equilibrio de los asientos contables. Son reglas sencillas, no el validador de la Autoridade Tributária: un archivo sin nada aquí aún puede ser rechazado, y el esquema (XSD) se verifica con Edición > Validar con un esquema.",
                "O que se verifica: as contagens e os totais dos cabeçalhos face às linhas, o total de cada documento, as datas face ao período, o dígito de controlo dos NIF portugueses, as falhas e repetições na numeração, o ATCUD, os clientes face à lista de clientes, as linhas com IVA a 0 % sem código nem motivo de isenção, a moeda e o equilíbrio dos lançamentos contabilísticos. São regras simples, não o validador da Autoridade Tributária: um ficheiro sem nada aqui pode ainda assim ser rejeitado, e o esquema (XSD) verifica-se com Editar > Validar com um esquema.")).Append("\n");
        }

        // ---- small helpers -----------------------------------------------------------------------------------------------------------------

        // The Portuguese tax number (NIF): nine digits, a first digit or first two digits that the DGCI gives, and a check digit (modulus 11).
        internal static bool IsValidNif(string nif)
        {
            if (nif == null || nif.Length != 9 || !nif.All(char.IsAsciiDigit)) return false;
            var prefixes = new[] { "1", "2", "3", "5", "6", "8", "9", "45", "70", "71", "72", "74", "75", "77", "78", "79" };
            if (!prefixes.Any(p => nif.StartsWith(p, StringComparison.Ordinal))) return false;
            var sum = 0;
            for (var i = 0; i < 8; i++) sum += (nif[i] - '0') * (9 - i);
            var check = 11 - sum % 11;
            if (check >= 10) check = 0;
            return check == nif[8] - '0';
        }

        // Credit notes and returns count against the sales.
        private static bool Subtracts(string type) => type is "NC" or "TD" or "DA";

        private static string T(XElement parent, string path) => parent == null ? null : XmlUbl.Text(parent, path) is { Length: > 0 } text ? text : null;

        private static decimal? Dec(string text) =>
            text != null && decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static DateTime? Date(string text) =>
            text != null && DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

        private static string Num(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        // A declared count as it is written (it may be huge or have decimals in a damaged file: no cast to int, which would throw).
        private static string Whole(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string More(int total, int shown) => total > shown ? ", …" : "";

        // One finding: what it is, how many, and a few examples ("label (3): a, b, c"); the same form in every language, so that no word has to agree with the number.
        private static string Item(Words w, string en, string es, string pt, int count, string details) =>
            w.W(en, es, pt) + " (" + Num(count) + ")" + (string.IsNullOrEmpty(details) ? "." : ": " + details + ".");

        private static void Add(List<(string, string)> facts, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) facts.Add((label, value));
        }

        private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        private static string AddressText(XElement address)
        {
            if (address == null) return null;
            var street = T(address, "AddressDetail") ?? Join(" ", T(address, "StreetName"), T(address, "BuildingNumber"));
            return Join(", ", street, Join(" ", T(address, "PostalCode"), T(address, "City")), T(address, "Region"), T(address, "Country"));
        }

        // "FT — Fatura": the code and the name; the code alone when it is not in the list.
        private static string Coded(Words w, IReadOnlyDictionary<string, (string En, string Es, string Pt)> names, string code) =>
            code == null ? null : names.TryGetValue(code, out var name) ? code + " — " + w.Pick(name) : code;

        private sealed class Words
        {
            private readonly int language;
            public Words(int language) => this.language = language;
            public string W(string en, string es, string pt) => language == 2 ? pt : language == 1 ? es : en;
            public string Pick((string En, string Es, string Pt) names) => W(names.En, names.Es, names.Pt);
        }

        // ---- the codes of the schema (Portaria 302/2016) ---------------------------------------------------------------------------------

        private static readonly Dictionary<string, (string En, string Es, string Pt)> BasisNames = new()
        {
            ["C"] = ("Accounting", "Contabilidad", "Contabilidade"),
            ["E"] = ("Invoicing issued by third parties", "Facturación emitida por terceros", "Faturação emitida por terceiros"),
            ["F"] = ("Invoicing", "Facturación", "Faturação"),
            ["I"] = ("Accounting integrated with invoicing", "Contabilidad integrada con la facturación", "Contabilidade integrada com a faturação"),
            ["P"] = ("Partial invoicing", "Facturación parcial", "Faturação parcial"),
            ["R"] = ("Receipts", "Recibos", "Recibos"),
            ["S"] = ("Self-billing", "Autofacturación", "Autofaturação"),
            ["T"] = ("Transport documents", "Documentos de transporte", "Documentos de transporte"),
        };

        private static readonly Dictionary<string, (string En, string Es, string Pt)> InvoiceNames = new()
        {
            ["FT"] = ("Invoice", "Factura", "Fatura"),
            ["FS"] = ("Simplified invoice", "Factura simplificada", "Fatura simplificada"),
            ["FR"] = ("Invoice-receipt", "Factura-recibo", "Fatura-recibo"),
            ["ND"] = ("Debit note", "Nota de débito", "Nota de débito"),
            ["NC"] = ("Credit note", "Nota de crédito", "Nota de crédito"),
            ["VD"] = ("Sale for cash (obsolete)", "Venta al contado (obsoleto)", "Venda a dinheiro (obsoleto)"),
            ["TV"] = ("Sales slip", "Ticket de venta", "Talão de venda"),
            ["TD"] = ("Return slip", "Ticket de devolución", "Talão de devolução"),
            ["AA"] = ("Disposal of assets", "Enajenación de activos", "Alienação de ativos"),
            ["DA"] = ("Return of assets", "Devolución de activos", "Devolução de ativos"),
            ["RP"] = ("Premium or premium receipt", "Prima o recibo de prima", "Prémio ou recibo de prémio"),
            ["RE"] = ("Reversal or reversal receipt", "Extorno o recibo de extorno", "Estorno ou recibo de estorno"),
            ["CS"] = ("Allocation to co-insurers", "Imputación a coaseguradoras", "Imputação a co-seguradoras"),
            ["LD"] = ("Allocation to the lead co-insurer", "Imputación a la coaseguradora líder", "Imputação à co-seguradora líder"),
            ["RA"] = ("Reinsurance accepted", "Reaseguro aceptado", "Resseguro aceite"),
        };

        private static readonly Dictionary<string, (string En, string Es, string Pt)> WorkNames = new()
        {
            ["CM"] = ("Table check", "Consulta de mesa", "Consulta de mesa"),
            ["FO"] = ("Job sheet", "Hoja de obra", "Folha de obra"),
            ["NE"] = ("Order", "Nota de pedido", "Nota de encomenda"),
            ["OR"] = ("Quote", "Presupuesto", "Orçamento"),
            ["PF"] = ("Pro forma", "Proforma", "Pró-forma"),
            ["OU"] = ("Other", "Otros", "Outros"),
        };

        private static readonly Dictionary<string, (string En, string Es, string Pt)> MovementNames = new()
        {
            ["GR"] = ("Delivery note", "Albarán de entrega", "Guia de remessa"),
            ["GT"] = ("Transport guide", "Guía de transporte", "Guia de transporte"),
            ["GA"] = ("Movement of own assets", "Movimiento de activos propios", "Guia de movimentação de ativos próprios"),
            ["GC"] = ("Consignment guide", "Guía de consignación", "Guia de consignação"),
            ["GD"] = ("Return note", "Nota de devolución", "Guia ou nota de devolução"),
        };

        private static readonly Dictionary<string, (string En, string Es, string Pt)> PaymentNames = new()
        {
            ["RC"] = ("Receipt (cash VAT scheme)", "Recibo (IVA de caja)", "Recibo (IVA de Caixa)"),
            ["RG"] = ("Other receipt", "Otro recibo", "Outro recibo"),
        };
    }
}
