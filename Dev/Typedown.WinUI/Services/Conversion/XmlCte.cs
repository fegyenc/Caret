using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // The fifth Brazilian pack of the XML reader: the CT-e (model 57, the electronic transport document, "conhecimento de transporte eletrônico")
    // version 4.00 as the national portal defines it (the namespace http://www.portalfiscal.inf.br/cte): the processed document (cteProc: the CTe
    // and the protocol of authorization, protCTe), the bare CTe, and the EVENTS of a CT-e (procEventoCTe: a cancellation, a letter of correction, the
    // proof of delivery...), which come as separate files. The structure and the codes were read from the official schema package (PL_CTe_400,
    // NT 2026.004, 1.00), not from memory; the names of the codes are the schema's own words, in Portuguese whatever the language of the interface,
    // and a code that is not in the lists is printed as it is. The other models of the family (CT-e OS, model 67; the simplified CT-e; the GTV-e) have
    // roots of their own and are shown by the generic view.
    //
    // The principles of the other packs: amounts and dates as the file has them, nothing checked or judged (the answer of the tax authority is shown
    // as it was written), the digital signature named and not printed, and the caller adds every field of the file after this summary.
    internal static class XmlCte
    {
        private const string Namespace = "http://www.portalfiscal.inf.br/cte";

        internal static bool IsCte(XElement root) => root.Name.NamespaceName == Namespace && (root.Name.LocalName is "cteProc" or "CTe");

        internal static bool IsEvent(XElement root) => root.Name.NamespaceName == Namespace && (root.Name.LocalName is "procEventoCTe" or "eventoCTe");

        private static XElement Document(XElement root)
        {
            var cte = root.Name.LocalName == "CTe" ? root : XmlUbl.Child(root, "CTe");
            return cte == null ? null : XmlUbl.Child(cte, "infCte");
        }

        // ---- the document ------------------------------------------------------------------------------------------------------------

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            var l = CteLabels.For(xl);
            var n = NfeLabels.For(xl);
            if (IsEvent(root)) return RenderEvent(root, context, xl, l, n, sb);
            if (!IsCte(root)) return false;
            var inf = Document(root);
            if (inf == null) return false;

            var ide = XmlUbl.Child(inf, "ide");
            var number = T(ide, "nCT");
            var series = T(ide, "serie");
            XmlUbl.Heading(sb, 1, l.Cte + (number == null ? "" : " " + number) + (series == null ? "" : " — " + n.Series + " " + series));
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append('\n');
            var version = inf.Attribute("versao")?.Value;
            if (version != null) sb.Append("> CT-e ").Append(XmlUbl.Safe(version)).Append('\n');
            sb.Append('\n');

            var taker = Taker(ide, inf, out var takerRole);
            var facts = new List<(string, string)>();
            Add(facts, n.AccessKey, Key(inf));
            Add(facts, n.Operation, T(ide, "natOp"));
            Add(facts, l.Cfop, T(ide, "CFOP"));
            Add(facts, n.IssueDate, T(ide, "dhEmi"));
            Add(facts, n.Model, Coded(Models, T(ide, "mod")));
            Add(facts, Capital(n.Series), series);
            Add(facts, n.Number, number);
            Add(facts, l.Type, Coded(Types, T(ide, "tpCTe")));
            Add(facts, l.ServiceType, Coded(ServiceTypes, T(ide, "tpServ")));
            Add(facts, l.Modal, Coded(Modals, T(ide, "modal")));
            Add(facts, n.Environment, Coded(XmlNfe.Environments, T(ide, "tpAmb")));
            Add(facts, n.IssueForm, Coded(IssueForms, T(ide, "tpEmis")));
            Add(facts, l.SentFrom, Place(T(ide, "xMunEnv"), T(ide, "UFEnv")));
            Add(facts, l.ServiceStart, Place(T(ide, "xMunIni"), T(ide, "UFIni")));
            Add(facts, l.ServiceEnd, Place(T(ide, "xMunFim"), T(ide, "UFFim")));
            Add(facts, l.TakerRole, Coded(TakerRoles, takerRole));
            Add(facts, n.IeIndicator, Coded(IeIndicators, T(ide, "indIEToma")));
            Add(facts, l.Picks, string.Join(" · ", new[] { Coded(Picks, T(ide, "retira")), T(ide, "xDetRetira") }.Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, l.Globalized, T(ide, "indGlobalizado") is { } globalized ? globalized + " — Sim" : null);
            Add(facts, l.AdvancePayment, Coded(AdvancePayments, T(ide, "tpPagAnt")));
            Add(facts, n.SoftwareVersion, T(ide, "verProc"));
            XmlUbl.Facts(sb, facts);

            Party(sb, XmlUbl.Child(inf, "emit"), n.Issuer, n, issuer: true);
            Party(sb, XmlUbl.Child(inf, "rem"), l.Sender, n, issuer: false);
            Party(sb, XmlUbl.Child(inf, "exped"), l.Dispatcher, n, issuer: false);
            Party(sb, XmlUbl.Child(inf, "receb"), l.CargoReceiver, n, issuer: false);
            Party(sb, XmlUbl.Child(inf, "dest"), n.Recipient, n, issuer: false);
            // a taker who is one of the parties above is said by the role (the fact above); a "third" taker has a group of his own
            if (ide != null && XmlUbl.Child(ide, "toma4") is { } other) Party(sb, other, l.Taker, n, issuer: false);

            Service(sb, XmlUbl.Child(inf, "vPrest"), l, n);
            Taxes(sb, XmlUbl.Child(inf, "imp"), l, n);
            var normal = XmlUbl.Child(inf, "infCTeNorm");
            if (normal != null)
            {
                Cargo(sb, XmlUbl.Child(normal, "infCarga"), l);
                Documents(sb, XmlUbl.Child(normal, "infDoc"), l);
                Road(sb, XmlUbl.Child(normal, "infModal/rodo"), l);
                Billing(sb, XmlUbl.Child(normal, "cobr"), n);
            }
            Related(sb, inf, normal, l);
            Notes(sb, XmlUbl.Child(inf, "compl"), l, n);
            Protocol(sb, root.Name.LocalName == "cteProc" ? XmlUbl.Child(root, "protCTe/infProt") : null, n);
            return true;
        }

        // The access key: the Id of infCte without the "CTe" in front of it (44 characters; a CNPJ can have letters since 2026, so it is text).
        private static string Key(XElement inf)
        {
            var id = inf.Attribute("Id")?.Value.Trim();
            return string.IsNullOrEmpty(id) ? null : id.StartsWith("CTe", StringComparison.Ordinal) ? id.Substring(3) : id;
        }

        // The taker of the service (who pays): one of the parties (toma3: 0 sender, 1 dispatcher, 2 receiver, 3 recipient) or a group of his own (toma4).
        private static XElement Taker(XElement ide, XElement inf, out string role)
        {
            role = null;
            if (ide == null) return null;
            var toma3 = XmlUbl.Child(ide, "toma3");
            if (toma3 != null)
            {
                role = T(toma3, "toma");
                return role switch
                {
                    "0" => XmlUbl.Child(inf, "rem"),
                    "1" => XmlUbl.Child(inf, "exped"),
                    "2" => XmlUbl.Child(inf, "receb"),
                    "3" => XmlUbl.Child(inf, "dest"),
                    _ => null,
                };
            }
            var toma4 = XmlUbl.Child(ide, "toma4");
            role = toma4 == null ? null : T(toma4, "toma");
            return toma4;
        }

        private static void Party(StringBuilder sb, XElement party, string title, NfeLabels n, bool issuer)
        {
            if (party == null) return;
            var facts = new List<(string, string)>();
            Add(facts, n.Name, T(party, "xNome"));
            Add(facts, n.TradeName, T(party, "xFant"));
            Add(facts, n.TaxId, T(party, "CNPJ") ?? T(party, "CPF"));
            Add(facts, n.StateRegistration, T(party, "IE"));
            if (issuer) Add(facts, n.TaxRegime, Coded(XmlNfe.TaxRegimes, T(party, "CRT")));
            var address = party.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("ender", StringComparison.Ordinal));
            if (address != null)
            {
                var street = string.Join(", ", new[] { T(address, "xLgr"), T(address, "nro"), T(address, "xCpl"), T(address, "xBairro") }.Where(s => !string.IsNullOrEmpty(s)));
                var city = string.Join("/", new[] { T(address, "xMun"), T(address, "UF") }.Where(s => !string.IsNullOrEmpty(s)));
                Add(facts, n.Address, string.Join(" — ", new[] { street, city, T(address, "CEP"), T(address, "xPais") }.Where(s => !string.IsNullOrEmpty(s))));
            }
            // the phone is in the address of the issuer and in the party itself for the others
            Add(facts, n.Phone, (address == null ? null : T(address, "fone")) ?? T(party, "fone"));
            Add(facts, n.Email, T(party, "email"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, title);
            XmlUbl.Facts(sb, facts);
        }

        private static void Service(StringBuilder sb, XElement price, CteLabels l, NfeLabels n)
        {
            if (price == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.ServiceTotal, T(price, "vTPrest"));
            Add(facts, l.ServiceNet, T(price, "vTPrestLiq"));
            Add(facts, l.Receivable, T(price, "vRec"));
            var components = XmlUbl.Children(price, "Comp").ToList();
            if (facts.Count == 0 && components.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Service);
            XmlUbl.Facts(sb, facts);
            if (components.Count == 0) return;
            var rows = new List<IReadOnlyList<string>> { new[] { l.Component, n.Amount } };
            foreach (var component in components) rows.Add(new[] { T(component, "xNome") ?? "", T(component, "vComp") ?? "" });
            XmlUbl.Table(sb, rows);
        }

        // The ICMS of the service: the group the file has (ICMS00, ICMS20, ICMS45, ICMS60, ICMS90, ICMSOutraUF, ICMSSN) with the fields it has.
        private static void Taxes(StringBuilder sb, XElement tax, CteLabels l, NfeLabels n)
        {
            if (tax == null) return;
            var group = IcmsGroup(tax);
            var facts = new List<(string, string)>();
            if (group != null)
            {
                // the names of the CST are those of the ICMS groups; in the groups of the Simples Nacional and of another state the same code 90 means
                // something else, so there it is printed as it is
                Add(facts, l.Cst, group.Name.LocalName is "ICMSSN" or "ICMSOutraUF" ? T(group, "CST") : Coded(Csts, T(group, "CST")));
                Add(facts, n.IcmsBase, T(group, "vBC") ?? T(group, "vBCOutraUF"));
                Add(facts, l.ReductionPercent, T(group, "pRedBC") ?? T(group, "pRedBCOutraUF"));
                Add(facts, l.IcmsRate, T(group, "pICMS") ?? T(group, "pICMSOutraUF") ?? T(group, "pICMSSTRet"));
                Add(facts, n.Icms, T(group, "vICMS") ?? T(group, "vICMSOutraUF"));
                Add(facts, n.StBase, T(group, "vBCSTRet"));
                Add(facts, n.St, T(group, "vICMSSTRet"));
                Add(facts, l.PresumedCredit, T(group, "vCred"));
                Add(facts, n.IcmsExempt, T(group, "vICMSDeson"));
                Add(facts, l.BenefitCode, T(group, "cBenef"));
            }
            Add(facts, n.ApproxTaxes, T(tax, "vTotTrib"));
            Add(facts, n.FiscalInfo, T(tax, "infAdFisco"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Taxes);
            XmlUbl.Facts(sb, facts);
        }

        private static XElement IcmsGroup(XElement tax) => XmlUbl.Child(tax, "ICMS")?.Elements().FirstOrDefault();

        private static void Cargo(StringBuilder sb, XElement cargo, CteLabels l)
        {
            if (cargo == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.CargoValue, T(cargo, "vCarga"));
            Add(facts, l.MainProduct, T(cargo, "proPred"));
            Add(facts, l.OtherFeatures, T(cargo, "xOutCat"));
            var quantities = XmlUbl.Children(cargo, "infQ").ToList();
            if (facts.Count == 0 && quantities.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Cargo);
            XmlUbl.Facts(sb, facts);
            if (quantities.Count == 0) return;
            var rows = new List<IReadOnlyList<string>> { new[] { l.MeasureType, l.Measure, l.Quantity } };
            foreach (var q in quantities) rows.Add(new[] { Coded(MeasureTypes, T(q, "tpMed")) ?? "", Coded(Units, T(q, "cUnid")) ?? "", T(q, "qCarga") ?? "" });
            XmlUbl.Table(sb, rows);
        }

        // The documents carried: the NF-e (by the key), the paper invoices (by the number), the others.
        private static void Documents(StringBuilder sb, XElement documents, CteLabels l)
        {
            if (documents == null) return;
            var rows = new List<IReadOnlyList<string>> { new[] { l.Document, l.KeyOrNumber, l.Value } };
            foreach (var nfe in XmlUbl.Children(documents, "infNFe")) rows.Add(new[] { "NF-e", T(nfe, "chave") ?? "", "" });
            foreach (var nf in XmlUbl.Children(documents, "infNF"))
                rows.Add(new[] { Coded(InvoiceModels, T(nf, "mod")) ?? "NF", string.Join(" / ", new[] { T(nf, "serie"), T(nf, "nDoc") }.Where(s => !string.IsNullOrEmpty(s))), T(nf, "vNF") ?? "" });
            foreach (var other in XmlUbl.Children(documents, "infOutros"))
                rows.Add(new[] { Coded(OtherDocuments, T(other, "tpDoc")) ?? "", string.Join(" · ", new[] { T(other, "descOutros"), T(other, "nDoc") }.Where(s => !string.IsNullOrEmpty(s))), T(other, "vDocFisc") ?? "" });
            foreach (var dce in XmlUbl.Children(documents, "infDCe")) rows.Add(new[] { "DC-e", T(dce, "chave") ?? "", "" });
            if (rows.Count == 1) return;
            XmlUbl.Heading(sb, 2, l.Documents);
            XmlUbl.Table(sb, rows);
        }

        private static void Road(StringBuilder sb, XElement road, CteLabels l)
        {
            if (road == null) return;
            var rntrc = T(road, "RNTRC");
            if (rntrc == null) return;
            XmlUbl.Heading(sb, 2, l.Road);
            XmlUbl.Facts(sb, new List<(string, string)> { (l.Rntrc, rntrc) });
        }

        private static void Billing(StringBuilder sb, XElement billing, NfeLabels n)
        {
            if (billing == null) return;
            var facts = new List<(string, string)>();
            var bill = XmlUbl.Child(billing, "fat");
            if (bill != null)
            {
                Add(facts, n.BillNumber, T(bill, "nFat"));
                Add(facts, n.OriginalValue, T(bill, "vOrig"));
                Add(facts, n.Discount, T(bill, "vDesc"));
                Add(facts, n.NetValue, T(bill, "vLiq"));
            }
            var installments = XmlUbl.Children(billing, "dup").ToList();
            if (facts.Count == 0 && installments.Count == 0) return;
            XmlUbl.Heading(sb, 2, n.Billing);
            XmlUbl.Facts(sb, facts);
            if (installments.Count == 0) return;
            var rows = new List<IReadOnlyList<string>> { new[] { n.InstallmentNumber, n.DueDate, n.Amount } };
            foreach (var dup in installments) rows.Add(new[] { T(dup, "nDup") ?? "", T(dup, "dVenc") ?? "", T(dup, "vDup") ?? "" });
            XmlUbl.Table(sb, rows);
        }

        // The CT-e this one complements (a CT-e of complement of values) or replaces (a CT-e of substitution).
        private static void Related(StringBuilder sb, XElement inf, XElement normal, CteLabels l)
        {
            var facts = new List<(string, string)>();
            foreach (var complemented in XmlUbl.Children(inf, "infCteComp")) Add(facts, l.Complements, T(complemented, "chCTe"));
            if (normal != null) Add(facts, l.Replaces, T(normal, "infCteSub/chCte"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Related);
            XmlUbl.Facts(sb, facts);
        }

        private static void Notes(StringBuilder sb, XElement complement, CteLabels l, NfeLabels n)
        {
            if (complement == null) return;
            var facts = new List<(string, string)>();
            Add(facts, n.ComplementaryInfo, T(complement, "xObs"));
            // free fields of the taxpayer (ObsCont) and of the tax authority (ObsFisco): the name of the field is an attribute, the text a child
            foreach (var note in XmlUbl.Children(complement, "ObsCont").Concat(XmlUbl.Children(complement, "ObsFisco"))) Add(facts, note.Attribute("xCampo")?.Value ?? l.Field, T(note, "xTexto"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Notes);
            XmlUbl.Facts(sb, facts);
        }

        // The answer of the tax authority that comes with a processed document (cteProc): what it says, as it was written, not whether it is right.
        private static void Protocol(StringBuilder sb, XElement protocol, NfeLabels n)
        {
            if (protocol == null) return;
            var facts = new List<(string, string)>();
            Add(facts, n.Result, string.Join(": ", new[] { T(protocol, "cStat"), T(protocol, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, n.ProtocolNumber, T(protocol, "nProt"));
            Add(facts, n.ReceivedAt, T(protocol, "dhRecbto"));
            Add(facts, n.Environment, Coded(XmlNfe.Environments, T(protocol, "tpAmb")));
            Add(facts, n.AppVersion, T(protocol, "verAplic"));
            Add(facts, n.AccessKey, T(protocol, "chCTe"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, n.Authorization);
            XmlUbl.Facts(sb, facts);
        }

        // ---- the events of a CT-e ----------------------------------------------------------------------------------------------------

        private static XElement EventElement(XElement root) => root.Name.LocalName == "eventoCTe" ? root : XmlUbl.Child(root, "eventoCTe");

        // The element inside detEvento that holds the particulars of the event (evCancCTe, evCCeCTe, evCECTe...): its name depends on the event.
        private static XElement EventBody(XElement info) => XmlUbl.Child(info, "detEvento")?.Elements().FirstOrDefault();

        private static bool RenderEvent(XElement root, ConversionContext context, XmlLabels xl, CteLabels l, NfeLabels n, StringBuilder sb)
        {
            var ev = ReadEvent(root);
            if (ev == null) return false;
            XmlUbl.Heading(sb, 1, l.EventTitle + ": " + ev.Name);
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append("\n\n");
            else sb.Append('\n');
            var info = XmlUbl.Child(EventElement(root), "infEvento");
            var answer = XmlUbl.Child(root, "retEventoCTe/infEvento");
            var facts = new List<(string, string)>();
            Add(facts, n.AccessKey, ev.Key);
            Add(facts, n.EventType, ev.TypeCode);
            Add(facts, n.EventDate, ev.Date);
            Add(facts, n.EventSequence, T(info, "nSeqEvento"));
            Add(facts, n.Environment, Coded(XmlNfe.Environments, T(info, "tpAmb")));
            Add(facts, n.TaxId, T(info, "CNPJ") ?? T(info, "CPF"));
            var body = EventBody(info);
            if (body != null)
            {
                Add(facts, n.Description, T(body, "descEvento"));
                Add(facts, n.ProtocolNumber, T(body, "nProt"));
                Add(facts, n.Justification, T(body, "xJust"));
                Add(facts, l.DeliveredAt, T(body, "dhEntrega"));
                Add(facts, l.ReceivedBy, T(body, "xNome"));
                Add(facts, l.ReceiverDocument, T(body, "nDoc"));
            }
            XmlUbl.Facts(sb, facts);
            var corrections = body == null ? new List<XElement>() : XmlUbl.Children(body, "infCorrecao").ToList();
            if (corrections.Count > 0)
            {
                XmlUbl.Heading(sb, 2, n.Correction);
                var rows = new List<IReadOnlyList<string>> { new[] { l.CorrectionGroup, l.CorrectionField, l.CorrectionValue, l.CorrectionItem } };
                foreach (var c in corrections) rows.Add(new[] { T(c, "grupoAlterado") ?? "", T(c, "campoAlterado") ?? "", T(c, "valorAlterado") ?? "", T(c, "nroItemAlterado") ?? "" });
                XmlUbl.Table(sb, rows);
            }
            if (answer != null)
            {
                XmlUbl.Heading(sb, 2, n.Authorization);
                var reply = new List<(string, string)>();
                Add(reply, n.Result, string.Join(": ", new[] { T(answer, "cStat"), T(answer, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s))));
                Add(reply, n.EventDescription, T(answer, "xEvento"));
                Add(reply, n.ProtocolNumber, T(answer, "nProt"));
                Add(reply, n.RegisteredAt, T(answer, "dhRegEvento"));
                XmlUbl.Facts(sb, reply);
            }
            return true;
        }

        // What a table of invoices needs to know about an event file: the document it is about, what it is, when, and what the authority answered.
        internal static NfeEvent ReadEvent(XElement root)
        {
            if (!IsEvent(root)) return null;
            var evento = EventElement(root);
            var info = evento == null ? null : XmlUbl.Child(evento, "infEvento");
            var key = info == null ? null : T(info, "chCTe");
            if (key == null) return null;
            var answer = XmlUbl.Child(root, "retEventoCTe/infEvento");
            var status = answer == null ? null : string.Join(": ", new[] { T(answer, "cStat"), T(answer, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s)));
            var type = T(info, "tpEvento");
            var description = EventBody(info) is { } body ? T(body, "descEvento") : null;
            return new NfeEvent
            {
                Key = key,
                TypeCode = type,
                Name = description ?? (answer == null ? null : T(answer, "xEvento")) ?? type ?? "?",
                Date = T(info, "dhEvento"),
                Status = string.IsNullOrEmpty(status) ? null : status,
            };
        }

        // ---- the document as one row of a table of invoices (InvoiceTable) ---------------------------------------------------------------

        internal static InvoiceRow ReadRow(XElement root, string file)
        {
            if (!IsCte(root)) return null;
            var inf = Document(root);
            if (inf == null) return null;
            var ide = XmlUbl.Child(inf, "ide");
            var emit = XmlUbl.Child(inf, "emit");
            var taker = Taker(ide, inf, out _);
            var price = XmlUbl.Child(inf, "vPrest");
            var protocol = root.Name.LocalName == "cteProc" ? XmlUbl.Child(root, "protCTe/infProt") : null;
            var number = T(ide, "nCT");
            var series = T(ide, "serie");
            var group = XmlUbl.Child(inf, "imp") is { } tax ? IcmsGroup(tax) : null;
            var answer = protocol == null ? null : string.Join(": ", new[] { T(protocol, "cStat"), T(protocol, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s)));
            return new InvoiceRow
            {
                File = file,
                Kind = "Cte",
                TypeCode = T(ide, "tpCTe"),
                Number = series == null ? number : number + "/" + series,
                Date = T(ide, "dhEmi"),
                Issuer = emit == null ? null : T(emit, "xNome"),
                IssuerId = emit == null ? null : T(emit, "CNPJ") ?? T(emit, "CPF"),
                // the customer of a freight document is the taker of the service, who pays it
                Customer = taker == null ? null : T(taker, "xNome"),
                CustomerId = taker == null ? null : T(taker, "CNPJ") ?? T(taker, "CPF"),
                Currency = "BRL",
                Cuf = Key(inf),
                Answer = string.IsNullOrEmpty(answer) ? null : answer,
                // a CT-e of substitution replaces the CT-e it names: the table lists the replaced one and does not add it (InvoiceTable.Finish)
                Replaces = XmlUbl.Child(inf, "infCTeNorm") is { } normal ? T(normal, "infCteSub/chCte") : null,
                Base = Amount(price == null ? null : T(price, "vTPrestLiq")),
                Taxes = Amount(group == null ? null : T(group, "vICMS") ?? T(group, "vICMSOutraUF")),
                Total = Amount(price == null ? null : T(price, "vTPrest")),
                Payable = Amount(price == null ? null : T(price, "vRec")),
            };
        }

        // "CT-e", or "CT-e — CT-e de Complemento de Valores" when it is not a normal one.
        internal static string RowType(string typeCode) =>
            typeCode is { Length: > 0 } && typeCode != "0" && Types.TryGetValue(typeCode, out var name) ? "CT-e — " + name : "CT-e";

        private static InvoiceAmount Amount(string text) => new InvoiceAmount
        {
            Text = text ?? "",
            Value = decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null,
        };

        // ---- small helpers -----------------------------------------------------------------------------------------------------------

        private static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string T(XElement parent, string path) => parent == null ? null : XmlUbl.Text(parent, path);

        private static void Add(List<(string, string)> facts, string label, string value)
        {
            if (!string.IsNullOrEmpty(value)) facts.Add((label, value));
        }

        private static string Place(string city, string state) => string.Join("/", new[] { city, state }.Where(s => !string.IsNullOrEmpty(s)));

        // "01 — Rodoviário": the code and the name the schema gives it; the code alone when it is not in the list.
        private static string Coded(IReadOnlyDictionary<string, string> table, string code) =>
            code == null ? null : table.TryGetValue(code, out var name) ? code + " — " + name : code;

        // Codes of the national schema (cteTiposBasico_v4.00.xsd), with its own words (Portuguese).
        private static readonly IReadOnlyDictionary<string, string> Models = new Dictionary<string, string> { ["57"] = "CT-e" };
        internal static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>
        {
            ["0"] = "CT-e Normal", ["1"] = "CT-e de Complemento de Valores", ["3"] = "CT-e de Substituição",
        };
        private static readonly IReadOnlyDictionary<string, string> ServiceTypes = new Dictionary<string, string>
        {
            ["0"] = "Normal", ["1"] = "Subcontratação", ["2"] = "Redespacho", ["3"] = "Redespacho Intermediário", ["4"] = "Serviço Vinculado a Multimodal",
        };
        private static readonly IReadOnlyDictionary<string, string> Modals = new Dictionary<string, string>
        {
            ["01"] = "Rodoviário", ["02"] = "Aéreo", ["03"] = "Aquaviário", ["04"] = "Ferroviário", ["05"] = "Dutoviário", ["06"] = "Multimodal",
        };
        private static readonly IReadOnlyDictionary<string, string> IssueForms = new Dictionary<string, string>
        {
            ["1"] = "Normal", ["2"] = "Contingência Offline", ["3"] = "Regime Especial NFF", ["4"] = "EPEC (em desativação)", ["7"] = "Autorização pela SVC-RS", ["8"] = "Autorização pela SVC-SP",
        };
        private static readonly IReadOnlyDictionary<string, string> TakerRoles = new Dictionary<string, string>
        {
            ["0"] = "Remetente", ["1"] = "Expedidor", ["2"] = "Recebedor", ["3"] = "Destinatário", ["4"] = "Outros",
        };
        private static readonly IReadOnlyDictionary<string, string> IeIndicators = new Dictionary<string, string>
        {
            ["1"] = "Contribuinte ICMS", ["2"] = "Contribuinte isento de inscrição", ["9"] = "Não Contribuinte",
        };
        private static readonly IReadOnlyDictionary<string, string> Picks = new Dictionary<string, string> { ["0"] = "Sim", ["1"] = "Não" };
        private static readonly IReadOnlyDictionary<string, string> AdvancePayments = new Dictionary<string, string>
        {
            ["1"] = "Pagamento Antecipado", ["3"] = "Fornecimento com pagamento realizado anteriormente",
        };
        private static readonly IReadOnlyDictionary<string, string> Csts = new Dictionary<string, string>
        {
            ["00"] = "Tributação normal do ICMS", ["20"] = "Tributação com BC reduzida do ICMS", ["40"] = "ICMS isenção", ["41"] = "ICMS não tributada", ["51"] = "ICMS diferido",
            ["60"] = "ICMS cobrado por substituição tributária", ["90"] = "ICMS outros",
        };
        private static readonly IReadOnlyDictionary<string, string> Units = new Dictionary<string, string>
        {
            ["00"] = "M3", ["01"] = "KG", ["02"] = "TON", ["03"] = "UNIDADE", ["04"] = "LITROS", ["05"] = "MMBTU",
        };
        private static readonly IReadOnlyDictionary<string, string> MeasureTypes = new Dictionary<string, string>
        {
            ["00"] = "Cubagem da NF-e", ["01"] = "Cubagem Aferida pelo Transportador", ["02"] = "Peso Bruto da NF-e", ["03"] = "Peso Bruto Aferido pelo Transportador", ["04"] = "Peso Cubado",
            ["05"] = "Peso Base do Cálculo do Frete", ["06"] = "Peso para uso Operacional", ["07"] = "Caixas", ["08"] = "Paletes", ["09"] = "Sacas", ["10"] = "Containers", ["11"] = "Rolos",
            ["12"] = "Bombonas", ["13"] = "Latas", ["14"] = "Litragem", ["15"] = "Milhão de BTU (British Thermal Units)", ["99"] = "Outros",
        };
        private static readonly IReadOnlyDictionary<string, string> InvoiceModels = new Dictionary<string, string>
        {
            ["01"] = "NF Modelo 01/1A e Avulsa", ["04"] = "NF de Produtor",
        };
        private static readonly IReadOnlyDictionary<string, string> OtherDocuments = new Dictionary<string, string>
        {
            ["00"] = "Declaração", ["10"] = "Dutoviário", ["59"] = "CF-e SAT", ["65"] = "NFC-e", ["99"] = "Outros",
        };
    }

    // The words of the readable view of a CT-e, in English, Spanish and Portuguese, besides those it shares with the NF-e (NfeLabels). The names of the
    // codes (above) are the schema's, in Portuguese.
    internal sealed class CteLabels
    {
        public string Cte, Cfop, Type, ServiceType, Modal, SentFrom, ServiceStart, ServiceEnd, Taker, TakerRole, Picks, Globalized, AdvancePayment;
        public string Sender, Dispatcher, CargoReceiver;
        public string Service, ServiceTotal, ServiceNet, Receivable, Component;
        public string Taxes, Cst, IcmsRate, ReductionPercent, PresumedCredit, BenefitCode;
        public string Cargo, CargoValue, MainProduct, OtherFeatures, Measure, MeasureType, Quantity;
        public string Documents, Document, KeyOrNumber, Value, Road, Rntrc;
        public string Related, Complements, Replaces, Notes, Field;
        public string EventTitle, CorrectionGroup, CorrectionField, CorrectionValue, CorrectionItem, DeliveredAt, ReceivedBy, ReceiverDocument;

        public static CteLabels For(XmlLabels xl) => xl.IsPortuguese ? Portuguese : xl.IsSpanish ? Spanish : English;

        private static readonly CteLabels English = new()
        {
            Cte = "CT-e (electronic transport document)", Cfop = "CFOP", Type = "Type", ServiceType = "Type of service", Modal = "Mode of transport", SentFrom = "Sent from",
            ServiceStart = "The service starts in", ServiceEnd = "The service ends in", Taker = "Taker of the service", TakerRole = "Who pays (taker of the service)",
            Picks = "The recipient collects the cargo", Globalized = "Globalised CT-e", AdvancePayment = "Advance payment",
            Sender = "Sender", Dispatcher = "Dispatcher", CargoReceiver = "Receiver of the cargo",
            Service = "Value of the service", ServiceTotal = "Total value of the service", ServiceNet = "Net value (without taxes)", Receivable = "Amount to receive", Component = "Component",
            Taxes = "Taxes", Cst = "Tax situation (CST)", IcmsRate = "Rate (%)", ReductionPercent = "Reduction of the base (%)", PresumedCredit = "Presumed credit", BenefitCode = "Benefit code",
            Cargo = "Cargo", CargoValue = "Value of the cargo", MainProduct = "Main product", OtherFeatures = "Other features", Measure = "Unit", MeasureType = "Measure", Quantity = "Quantity",
            Documents = "Documents carried", Document = "Document", KeyOrNumber = "Key or number", Value = "Value", Road = "Road transport", Rntrc = "RNTRC (registry of road carriers)",
            Related = "Related CT-e", Complements = "Complements the CT-e", Replaces = "Replaces the CT-e", Notes = "Observations", Field = "Field",
            EventTitle = "Event of a CT-e", CorrectionGroup = "Group", CorrectionField = "Field", CorrectionValue = "New value", CorrectionItem = "Item",
            DeliveredAt = "Delivered at", ReceivedBy = "Received by", ReceiverDocument = "Document of the receiver",
        };

        private static readonly CteLabels Spanish = new()
        {
            Cte = "CT-e (documento de transporte electrónico)", Cfop = "CFOP", Type = "Tipo", ServiceType = "Tipo de servicio", Modal = "Modalidad de transporte", SentFrom = "Enviado desde",
            ServiceStart = "El servicio comienza en", ServiceEnd = "El servicio termina en", Taker = "Tomador del servicio", TakerRole = "Quién paga (tomador del servicio)",
            Picks = "El destinatario retira la carga", Globalized = "CT-e globalizado", AdvancePayment = "Pago anticipado",
            Sender = "Remitente", Dispatcher = "Expedidor", CargoReceiver = "Receptor de la carga",
            Service = "Valor del servicio", ServiceTotal = "Valor total del servicio", ServiceNet = "Valor neto (sin impuestos)", Receivable = "Importe a recibir", Component = "Componente",
            Taxes = "Impuestos", Cst = "Situación tributaria (CST)", IcmsRate = "Alícuota (%)", ReductionPercent = "Reducción de la base (%)", PresumedCredit = "Crédito presunto", BenefitCode = "Código de beneficio",
            Cargo = "Carga", CargoValue = "Valor de la carga", MainProduct = "Producto predominante", OtherFeatures = "Otras características", Measure = "Unidad", MeasureType = "Medida", Quantity = "Cantidad",
            Documents = "Documentos transportados", Document = "Documento", KeyOrNumber = "Clave o número", Value = "Valor", Road = "Transporte por carretera", Rntrc = "RNTRC (registro de transportistas de carga)",
            Related = "CT-e relacionados", Complements = "Complementa el CT-e", Replaces = "Sustituye el CT-e", Notes = "Observaciones", Field = "Campo",
            EventTitle = "Evento de un CT-e", CorrectionGroup = "Grupo", CorrectionField = "Campo", CorrectionValue = "Valor nuevo", CorrectionItem = "Ítem",
            DeliveredAt = "Entregado el", ReceivedBy = "Recibido por", ReceiverDocument = "Documento de quien recibió",
        };

        private static readonly CteLabels Portuguese = new()
        {
            Cte = "CT-e (conhecimento de transporte eletrônico)", Cfop = "CFOP", Type = "Tipo", ServiceType = "Tipo de serviço", Modal = "Modal de transporte", SentFrom = "Enviado de",
            ServiceStart = "O serviço começa em", ServiceEnd = "O serviço termina em", Taker = "Tomador do serviço", TakerRole = "Quem paga (tomador do serviço)",
            Picks = "O destinatário retira a carga", Globalized = "CT-e globalizado", AdvancePayment = "Pagamento antecipado",
            Sender = "Remetente", Dispatcher = "Expedidor", CargoReceiver = "Recebedor da carga",
            Service = "Valor da prestação", ServiceTotal = "Valor total da prestação do serviço", ServiceNet = "Valor líquido (sem tributos)", Receivable = "Valor a receber", Component = "Componente",
            Taxes = "Tributos", Cst = "Situação tributária (CST)", IcmsRate = "Alíquota (%)", ReductionPercent = "Redução da base de cálculo (%)", PresumedCredit = "Crédito presumido", BenefitCode = "Código de benefício",
            Cargo = "Carga", CargoValue = "Valor da carga", MainProduct = "Produto predominante", OtherFeatures = "Outras características", Measure = "Unidade", MeasureType = "Medida", Quantity = "Quantidade",
            Documents = "Documentos transportados", Document = "Documento", KeyOrNumber = "Chave ou número", Value = "Valor", Road = "Transporte rodoviário", Rntrc = "RNTRC (registro de transportadores rodoviários de carga)",
            Related = "CT-e relacionados", Complements = "Complementa o CT-e", Replaces = "Substitui o CT-e", Notes = "Observações", Field = "Campo",
            EventTitle = "Evento de um CT-e", CorrectionGroup = "Grupo", CorrectionField = "Campo", CorrectionValue = "Novo valor", CorrectionItem = "Item",
            DeliveredAt = "Entregue em", ReceivedBy = "Recebido por", ReceiverDocument = "Documento de quem recebeu",
        };
    }
}
