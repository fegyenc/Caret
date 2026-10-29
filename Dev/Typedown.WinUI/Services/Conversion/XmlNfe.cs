using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // The third format pack of the XML reader: Brazil's electronic invoice, the NF-e (model 55) and the NFC-e (model 65), version 4.00, as the
    // NF-e national portal defines it (the namespace http://www.portalfiscal.inf.br/nfe): the processed invoice (nfeProc: the NFe and the
    // protocol of authorization, protNFe), the bare NFe, and the EVENTS of an invoice (procEventoNFe: a cancellation, a letter of correction, the
    // manifestations of the recipient), which come as separate files and are the way a cancellation is known WITHOUT asking anyone online.
    // The structure and the codes were read from the national schema (leiauteNFe_v4.00.xsd, package PL_010 v1.30), not from memory; the names
    // of the codes are the schema's, in Portuguese whatever the language of the interface; a code that is not in the lists is printed as it is.
    //
    // The principles of the other packs: amounts and dates as the file has them, nothing checked or judged (the answer of the tax authority, the
    // protocol, is shown as it was written), the digital signature named and not printed, and the caller adds every field of the file after
    // this summary.
    internal static class XmlNfe
    {
        private const string Namespace = "http://www.portalfiscal.inf.br/nfe";

        internal static bool IsNfe(XElement root) => root.Name.NamespaceName == Namespace && (root.Name.LocalName is "nfeProc" or "NFe");

        internal static bool IsEvent(XElement root) => root.Name.NamespaceName == Namespace && (root.Name.LocalName is "procEventoNFe" or "evento");

        private static XElement Invoice(XElement root)
        {
            var nfe = root.Name.LocalName == "NFe" ? root : XmlUbl.Child(root, "NFe");
            return nfe == null ? null : XmlUbl.Child(nfe, "infNFe");
        }

        // ---- the invoice -------------------------------------------------------------------------------------------------------------

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            var l = NfeLabels.For(xl);
            if (IsEvent(root)) return RenderEvent(root, context, xl, l, sb);
            if (!IsNfe(root)) return false;
            var inf = Invoice(root);
            if (inf == null) return false;

            var ide = XmlUbl.Child(inf, "ide");
            var model = T(ide, "mod");
            var number = T(ide, "nNF");
            var series = T(ide, "serie");
            var title = (model == "65" ? l.Nfce : l.Nfe) + (number == null ? "" : " " + number) + (series == null ? "" : " — " + l.Series + " " + series);
            XmlUbl.Heading(sb, 1, title);
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append('\n');
            var version = inf.Attribute("versao")?.Value;
            if (version != null) sb.Append("> ").Append(model == "65" ? "NFC-e " : "NF-e ").Append(XmlUbl.Safe(version)).Append('\n');
            sb.Append('\n');

            var facts = new List<(string, string)>();
            Add(facts, l.AccessKey, Key(inf));
            Add(facts, l.Operation, T(ide, "natOp"));
            Add(facts, l.IssueDate, T(ide, "dhEmi"));
            Add(facts, l.DepartureDate, T(ide, "dhSaiEnt"));
            Add(facts, l.Model, Coded(Models, model));
            Add(facts, Capital(l.Series), series);
            Add(facts, l.Number, number);
            Add(facts, l.Kind, Coded(Kinds, T(ide, "tpNF")));
            Add(facts, l.Destination, Coded(Destinations, T(ide, "idDest")));
            Add(facts, l.Purpose, Coded(Purposes, T(ide, "finNFe")));
            Add(facts, l.FinalConsumer, Coded(FinalConsumer, T(ide, "indFinal")));
            Add(facts, l.Presence, Coded(Presences, T(ide, "indPres")));
            Add(facts, l.Environment, Coded(Environments, T(ide, "tpAmb")));
            Add(facts, l.IssueForm, Coded(IssueForms, T(ide, "tpEmis")));
            Add(facts, l.SoftwareVersion, T(ide, "verProc"));
            XmlUbl.Facts(sb, facts);

            Party(sb, XmlUbl.Child(inf, "emit"), XmlUbl.Child(inf, "emit/enderEmit"), l.Issuer, l, issuer: true);
            Party(sb, XmlUbl.Child(inf, "dest"), XmlUbl.Child(inf, "dest/enderDest"), l.Recipient, l, issuer: false);
            Items(sb, inf, l);
            Totals(sb, XmlUbl.Child(inf, "total/ICMSTot"), l);
            Transport(sb, XmlUbl.Child(inf, "transp"), l);
            Billing(sb, XmlUbl.Child(inf, "cobr"), l);
            Payment(sb, XmlUbl.Child(inf, "pag"), l);
            Additional(sb, XmlUbl.Child(inf, "infAdic"), l);
            Protocol(sb, root.Name.LocalName == "nfeProc" ? XmlUbl.Child(root, "protNFe/infProt") : null, l);
            return true;
        }

        // The access key: the Id of infNFe without the "NFe" in front of it (44 characters; since 2026 a CNPJ can have letters, so it is text).
        private static string Key(XElement inf)
        {
            var id = inf.Attribute("Id")?.Value.Trim();
            return string.IsNullOrEmpty(id) ? null : id.StartsWith("NFe", StringComparison.Ordinal) ? id.Substring(3) : id;
        }

        private static void Party(StringBuilder sb, XElement party, XElement address, string title, NfeLabels l, bool issuer)
        {
            if (party == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.Name, T(party, "xNome"));
            if (issuer) Add(facts, l.TradeName, T(party, "xFant"));
            Add(facts, l.TaxId, T(party, "CNPJ") ?? T(party, "CPF") ?? T(party, "idEstrangeiro"));
            Add(facts, l.StateRegistration, T(party, "IE"));
            if (issuer) Add(facts, l.TaxRegime, Coded(TaxRegimes, T(party, "CRT")));
            else Add(facts, l.IeIndicator, Coded(IeIndicators, T(party, "indIEDest")));
            if (address != null)
            {
                var street = string.Join(", ", new[] { T(address, "xLgr"), T(address, "nro"), T(address, "xCpl"), T(address, "xBairro") }.Where(s => !string.IsNullOrEmpty(s)));
                var city = string.Join("/", new[] { T(address, "xMun"), T(address, "UF") }.Where(s => !string.IsNullOrEmpty(s)));
                var full = string.Join(" — ", new[] { street, city, T(address, "CEP"), T(address, "xPais") }.Where(s => !string.IsNullOrEmpty(s)));
                Add(facts, l.Address, full);
                Add(facts, l.Phone, T(address, "fone"));
            }
            if (!issuer) Add(facts, l.Email, T(party, "email"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, title);
            XmlUbl.Facts(sb, facts);
        }

        private static void Items(StringBuilder sb, XElement inf, NfeLabels l)
        {
            var items = XmlUbl.Children(inf, "det").ToList();
            if (items.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Items);
            var rows = new List<IReadOnlyList<string>> { new[] { "#", l.Code, l.Description, "NCM", "CFOP", l.Unit, l.Quantity, l.UnitPrice, l.Amount, l.Discount } };
            foreach (var det in items)
            {
                var prod = XmlUbl.Child(det, "prod");
                if (prod == null) continue;
                rows.Add(new[]
                {
                    det.Attribute("nItem")?.Value ?? "", T(prod, "cProd") ?? "", T(prod, "xProd") ?? "", T(prod, "NCM") ?? "", T(prod, "CFOP") ?? "", T(prod, "uCom") ?? "",
                    T(prod, "qCom") ?? "", T(prod, "vUnCom") ?? "", T(prod, "vProd") ?? "", T(prod, "vDesc") ?? "",
                });
            }
            XmlUbl.Table(sb, rows);
        }

        private static void Totals(StringBuilder sb, XElement total, NfeLabels l)
        {
            if (total == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.IcmsBase, T(total, "vBC"));
            Add(facts, l.Icms, T(total, "vICMS"));
            Add(facts, l.IcmsExempt, T(total, "vICMSDeson"));
            Add(facts, l.StBase, T(total, "vBCST"));
            Add(facts, l.St, T(total, "vST"));
            Add(facts, l.Products, T(total, "vProd"));
            Add(facts, l.Freight, T(total, "vFrete"));
            Add(facts, l.Insurance, T(total, "vSeg"));
            Add(facts, l.Discount, T(total, "vDesc"));
            Add(facts, l.ImportTax, T(total, "vII"));
            Add(facts, l.Ipi, T(total, "vIPI"));
            Add(facts, l.Pis, T(total, "vPIS"));
            Add(facts, l.Cofins, T(total, "vCOFINS"));
            Add(facts, l.Other, T(total, "vOutro"));
            Add(facts, l.InvoiceTotal, T(total, "vNF"));
            Add(facts, l.ApproxTaxes, T(total, "vTotTrib"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Totals);
            XmlUbl.Facts(sb, facts);
        }

        private static void Transport(StringBuilder sb, XElement transport, NfeLabels l)
        {
            if (transport == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.FreightMode, Coded(FreightModes, T(transport, "modFrete")));
            var carrier = XmlUbl.Child(transport, "transporta");
            if (carrier != null) Add(facts, l.Carrier, string.Join(" · ", new[] { T(carrier, "xNome"), T(carrier, "CNPJ") ?? T(carrier, "CPF") }.Where(s => !string.IsNullOrEmpty(s))));
            foreach (var volume in XmlUbl.Children(transport, "vol"))
                Add(facts, l.Volumes, string.Join(" · ", new[] { T(volume, "qVol"), T(volume, "esp"), T(volume, "pesoL") is { } net ? l.NetWeight + " " + net : null, T(volume, "pesoB") is { } gross ? l.GrossWeight + " " + gross : null }.Where(s => !string.IsNullOrEmpty(s))));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Transport);
            XmlUbl.Facts(sb, facts);
        }

        private static void Billing(StringBuilder sb, XElement billing, NfeLabels l)
        {
            if (billing == null) return;
            var facts = new List<(string, string)>();
            var bill = XmlUbl.Child(billing, "fat");
            if (bill != null)
            {
                Add(facts, l.BillNumber, T(bill, "nFat"));
                Add(facts, l.OriginalValue, T(bill, "vOrig"));
                Add(facts, l.Discount, T(bill, "vDesc"));
                Add(facts, l.NetValue, T(bill, "vLiq"));
            }
            var installments = XmlUbl.Children(billing, "dup").ToList();
            if (facts.Count == 0 && installments.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Billing);
            XmlUbl.Facts(sb, facts);
            if (installments.Count > 0)
            {
                var rows = new List<IReadOnlyList<string>> { new[] { l.InstallmentNumber, l.DueDate, l.Amount } };
                foreach (var dup in installments) rows.Add(new[] { T(dup, "nDup") ?? "", T(dup, "dVenc") ?? "", T(dup, "vDup") ?? "" });
                XmlUbl.Table(sb, rows);
            }
        }

        private static void Payment(StringBuilder sb, XElement payment, NfeLabels l)
        {
            if (payment == null) return;
            var details = XmlUbl.Children(payment, "detPag").ToList();
            var change = T(payment, "vTroco");
            if (details.Count == 0 && change == null) return;
            XmlUbl.Heading(sb, 2, l.Payment);
            if (details.Count > 0)
            {
                var rows = new List<IReadOnlyList<string>> { new[] { l.PaymentMethod, l.Description, l.Amount } };
                foreach (var detail in details) rows.Add(new[] { Coded(PaymentMethods, T(detail, "tPag")) ?? "", T(detail, "xPag") ?? "", T(detail, "vPag") ?? "" });
                XmlUbl.Table(sb, rows);
            }
            if (change != null) XmlUbl.Facts(sb, new List<(string, string)> { (l.Change, change) });
        }

        private static void Additional(StringBuilder sb, XElement info, NfeLabels l)
        {
            if (info == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.FiscalInfo, T(info, "infAdFisco"));
            Add(facts, l.ComplementaryInfo, T(info, "infCpl"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.AdditionalInfo);
            XmlUbl.Facts(sb, facts);
        }

        // The answer of the tax authority that comes with a processed invoice (nfeProc): what it says, as it was written, not whether it is right.
        private static void Protocol(StringBuilder sb, XElement protocol, NfeLabels l)
        {
            if (protocol == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.Result, string.Join(": ", new[] { T(protocol, "cStat"), T(protocol, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, l.ProtocolNumber, T(protocol, "nProt"));
            Add(facts, l.ReceivedAt, T(protocol, "dhRecbto"));
            Add(facts, l.Environment, Coded(Environments, T(protocol, "tpAmb")));
            Add(facts, l.AppVersion, T(protocol, "verAplic"));
            Add(facts, l.AccessKey, T(protocol, "chNFe"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Authorization);
            XmlUbl.Facts(sb, facts);
        }

        // ---- the events of an invoice -----------------------------------------------------------------------------------------------

        private static bool RenderEvent(XElement root, ConversionContext context, XmlLabels xl, NfeLabels l, StringBuilder sb)
        {
            var ev = ReadEvent(root);
            if (ev == null) return false;
            XmlUbl.Heading(sb, 1, l.EventTitle + ": " + ev.Name);
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append("\n\n");
            else sb.Append('\n');
            var info = XmlUbl.Child(root.Name.LocalName == "evento" ? root : XmlUbl.Child(root, "evento") ?? root, "infEvento");
            var answer = XmlUbl.Child(root, "retEvento/infEvento");
            var facts = new List<(string, string)>();
            Add(facts, l.AccessKey, ev.Key);
            Add(facts, l.EventType, Coded(EventTypes, ev.TypeCode) ?? ev.TypeCode);
            Add(facts, l.EventDate, ev.Date);
            Add(facts, l.EventSequence, info == null ? null : T(info, "nSeqEvento"));
            Add(facts, l.Environment, info == null ? null : Coded(Environments, T(info, "tpAmb")));
            Add(facts, l.TaxId, info == null ? null : T(info, "CNPJ") ?? T(info, "CPF"));
            var detail = info == null ? null : XmlUbl.Child(info, "detEvento");
            if (detail != null)
            {
                Add(facts, l.Description, T(detail, "descEvento"));
                Add(facts, l.ProtocolNumber, T(detail, "nProt"));
                Add(facts, l.Justification, T(detail, "xJust"));
                Add(facts, l.Correction, T(detail, "xCorrecao"));
            }
            XmlUbl.Facts(sb, facts);
            if (answer != null)
            {
                XmlUbl.Heading(sb, 2, l.Authorization);
                var reply = new List<(string, string)>();
                Add(reply, l.Result, string.Join(": ", new[] { T(answer, "cStat"), T(answer, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s))));
                Add(reply, l.EventDescription, T(answer, "xEvento"));
                Add(reply, l.ProtocolNumber, T(answer, "nProt"));
                Add(reply, l.RegisteredAt, T(answer, "dhRegEvento"));
                XmlUbl.Facts(sb, reply);
            }
            return true;
        }

        // What a table of invoices needs to know about an event file: the invoice it is about, what it is, when, and what the authority answered.
        internal static NfeEvent ReadEvent(XElement root)
        {
            if (!IsEvent(root)) return null;
            var evento = root.Name.LocalName == "evento" ? root : XmlUbl.Child(root, "evento");
            var info = evento == null ? null : XmlUbl.Child(evento, "infEvento");
            var key = info == null ? null : T(info, "chNFe");
            if (key == null) return null;
            var type = T(info, "tpEvento");
            var answer = XmlUbl.Child(root, "retEvento/infEvento");
            var status = answer == null ? null : string.Join(": ", new[] { T(answer, "cStat"), T(answer, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s)));
            var description = XmlUbl.Child(info, "detEvento") is { } d ? T(d, "descEvento") : null;
            return new NfeEvent
            {
                Key = key,
                TypeCode = type,
                Name = EventTypes.TryGetValue(type ?? "", out var name) ? name : description ?? type ?? "?",
                Date = T(info, "dhEvento"),
                Status = string.IsNullOrEmpty(status) ? null : status,
            };
        }

        // ---- the invoice as one row of a table of invoices (InvoiceTable) -----------------------------------------------------------------

        internal static InvoiceRow ReadRow(XElement root, string file)
        {
            if (!IsNfe(root)) return null;
            var inf = Invoice(root);
            if (inf == null) return null;
            var ide = XmlUbl.Child(inf, "ide");
            var emit = XmlUbl.Child(inf, "emit");
            var dest = XmlUbl.Child(inf, "dest");
            var total = XmlUbl.Child(inf, "total/ICMSTot");
            var protocol = root.Name.LocalName == "nfeProc" ? XmlUbl.Child(root, "protNFe/infProt") : null;
            var number = T(ide, "nNF");
            var series = T(ide, "serie");
            var answer = protocol == null ? null : string.Join(": ", new[] { T(protocol, "cStat"), T(protocol, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s)));
            return new InvoiceRow
            {
                File = file,
                Kind = "Nfe",
                // the model and the purpose: "55|1"
                TypeCode = (T(ide, "mod") ?? "") + "|" + (T(ide, "finNFe") ?? ""),
                Number = series == null ? number : number + "/" + series,
                Date = T(ide, "dhEmi"),
                Issuer = emit == null ? null : T(emit, "xNome"),
                IssuerId = emit == null ? null : T(emit, "CNPJ") ?? T(emit, "CPF"),
                Customer = dest == null ? null : T(dest, "xNome"),
                CustomerId = dest == null ? null : T(dest, "CNPJ") ?? T(dest, "CPF") ?? T(dest, "idEstrangeiro"),
                Currency = "BRL",
                Cuf = Key(inf),
                Answer = string.IsNullOrEmpty(answer) ? null : answer,
                // Brazil's taxes are many and mostly inside the price (ICMS, IPI, PIS, COFINS...): the table has the total of the invoice, and the
                // other columns stay empty rather than say something the file does not say
                Total = Amount(total == null ? null : T(total, "vNF")),
            };
        }

        // "NF-e", or "NFC-e", and what is special about it when it is not a normal one ("NF-e — Devolução/Retorno").
        internal static string RowType(string typeCode)
        {
            var parts = (typeCode ?? "").Split('|');
            var kind = parts[0] == "65" ? "NFC-e" : "NF-e";
            return parts.Length > 1 && parts[1].Length > 0 && parts[1] != "1" && Purposes.TryGetValue(parts[1], out var purpose) ? kind + " — " + purpose : kind;
        }

        internal static bool IsCreditNote(string typeCode) => (typeCode ?? "").EndsWith("|5", StringComparison.Ordinal);

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

        // "55 — NF-e": the code and the name the schema gives it; the code alone when it is not in the list.
        private static string Coded(IReadOnlyDictionary<string, string> table, string code) =>
            code == null ? null : table.TryGetValue(code, out var name) ? code + " — " + name : code;

        // Codes of the national schema (leiauteNFe_v4.00.xsd), with its own words (Portuguese).
        private static readonly IReadOnlyDictionary<string, string> Models = new Dictionary<string, string> { ["55"] = "NF-e", ["65"] = "NFC-e" };
        private static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string> { ["0"] = "Entrada", ["1"] = "Saída" };
        private static readonly IReadOnlyDictionary<string, string> Destinations = new Dictionary<string, string> { ["1"] = "Operação interna", ["2"] = "Operação interestadual", ["3"] = "Operação com o exterior" };
        private static readonly IReadOnlyDictionary<string, string> Purposes = new Dictionary<string, string>
        {
            ["1"] = "NF-e normal", ["2"] = "NF-e complementar", ["3"] = "NF-e de ajuste", ["4"] = "Devolução/Retorno", ["5"] = "Nota de crédito", ["6"] = "Nota de débito",
        };
        private static readonly IReadOnlyDictionary<string, string> FinalConsumer = new Dictionary<string, string> { ["0"] = "Não", ["1"] = "Consumidor final" };
        private static readonly IReadOnlyDictionary<string, string> Presences = new Dictionary<string, string>
        {
            ["0"] = "Não se aplica (por exemplo, nota fiscal complementar ou de ajuste)", ["1"] = "Operação presencial", ["2"] = "Operação não presencial, internet",
            ["3"] = "Operação não presencial, teleatendimento", ["4"] = "Operação não presencial com NFC-e e NF-e com DANFE Simplificado Tipo 2 (com entrega)",
            ["5"] = "Operação presencial, fora do estabelecimento", ["9"] = "Operação não presencial, outros",
        };
        internal static readonly IReadOnlyDictionary<string, string> Environments = new Dictionary<string, string> { ["1"] = "Produção", ["2"] = "Homologação" };
        private static readonly IReadOnlyDictionary<string, string> IssueForms = new Dictionary<string, string>
        {
            ["1"] = "Normal", ["2"] = "Contingência FS", ["3"] = "Regime Especial NFF", ["4"] = "Contingência DPEC", ["5"] = "Contingência FSDA", ["6"] = "Contingência SVC-AN",
            ["7"] = "Contingência SVC-RS", ["9"] = "Contingência off-line da NFC-e e da NF-e com DANFE Simplificado Tipo 2",
        };
        internal static readonly IReadOnlyDictionary<string, string> TaxRegimes = new Dictionary<string, string>
        {
            ["1"] = "Simples Nacional", ["2"] = "Simples Nacional, excesso de sublimite de receita bruta", ["3"] = "Regime Normal", ["4"] = "Simples Nacional, Microempreendedor Individual (MEI)",
        };
        private static readonly IReadOnlyDictionary<string, string> IeIndicators = new Dictionary<string, string> { ["1"] = "Contribuinte ICMS", ["2"] = "Contribuinte isento de inscrição", ["9"] = "Não contribuinte" };
        private static readonly IReadOnlyDictionary<string, string> FreightModes = new Dictionary<string, string>
        {
            ["0"] = "Contratação do frete por conta do remetente (CIF)", ["1"] = "Contratação do frete por conta do destinatário/remetente (FOB)", ["2"] = "Contratação do frete por conta de terceiros",
            ["3"] = "Transporte próprio por conta do remetente", ["4"] = "Transporte próprio por conta do destinatário", ["9"] = "Sem ocorrência de transporte",
        };
        // The means of payment: the schema only says "two digits"; the names are those of the national table of means of payment (NT 2020.006). Only
        // those that are certain are here; any other code is printed as it is.
        private static readonly IReadOnlyDictionary<string, string> PaymentMethods = new Dictionary<string, string>
        {
            ["01"] = "Dinheiro", ["02"] = "Cheque", ["03"] = "Cartão de crédito", ["04"] = "Cartão de débito", ["05"] = "Crédito loja", ["10"] = "Vale alimentação", ["11"] = "Vale refeição",
            ["12"] = "Vale presente", ["13"] = "Vale combustível", ["15"] = "Boleto bancário", ["16"] = "Depósito bancário", ["17"] = "Pagamento instantâneo (PIX)",
            ["18"] = "Transferência bancária, carteira digital", ["19"] = "Programa de fidelidade, cashback, crédito virtual", ["90"] = "Sem pagamento", ["99"] = "Outros",
        };
        // The events of an invoice (the manifestation of the recipient and the cancellation are in the national table of events).
        private static readonly IReadOnlyDictionary<string, string> EventTypes = new Dictionary<string, string>
        {
            ["110111"] = "Cancelamento", ["110110"] = "Carta de Correção", ["210200"] = "Confirmação da Operação", ["210210"] = "Ciência da Operação",
            ["210220"] = "Desconhecimento da Operação", ["210240"] = "Operação não Realizada",
        };
    }

    // An event of an invoice, as a folder table needs it.
    internal sealed class NfeEvent
    {
        public string Key, TypeCode, Name, Date, Status, File;

        // "Cancelamento (2026-03-10T09:00:00-03:00): 135: Evento registrado e vinculado a NF-e"
        public string Text => Name + (string.IsNullOrEmpty(Date) ? "" : " (" + Date + ")") + (string.IsNullOrEmpty(Status) ? "" : ": " + Status);
    }

    // The words of the readable view of an NF-e, in English, Spanish and Portuguese. The names of the codes (above) are the schema's, in Portuguese.
    internal sealed class NfeLabels
    {
        public string Nfe, Nfce, Series, Number, AccessKey, Operation, IssueDate, DepartureDate, Model, Kind, Destination, Purpose, FinalConsumer, Presence, Environment, IssueForm, SoftwareVersion;
        public string Issuer, Recipient, Name, TradeName, TaxId, StateRegistration, TaxRegime, IeIndicator, Address, Phone, Email;
        public string Items, Code, Description, Unit, Quantity, UnitPrice, Amount, Discount;
        public string Totals, IcmsBase, Icms, IcmsExempt, StBase, St, Products, Freight, Insurance, ImportTax, Ipi, Pis, Cofins, Other, InvoiceTotal, ApproxTaxes;
        public string Transport, FreightMode, Carrier, Volumes, NetWeight, GrossWeight;
        public string Billing, BillNumber, OriginalValue, NetValue, InstallmentNumber, DueDate;
        public string Payment, PaymentMethod, Change, AdditionalInfo, FiscalInfo, ComplementaryInfo;
        public string Authorization, Result, ProtocolNumber, ReceivedAt, AppVersion;
        public string EventTitle, EventType, EventDate, EventSequence, EventDescription, Justification, Correction, RegisteredAt;

        public static NfeLabels For(XmlLabels xl) => xl.IsPortuguese ? Portuguese : xl.IsSpanish ? Spanish : English;

        private static readonly NfeLabels English = new()
        {
            Nfe = "NF-e (electronic invoice)", Nfce = "NFC-e (electronic consumer invoice)", Series = "series", Number = "Number", AccessKey = "Access key", Operation = "Nature of the operation",
            IssueDate = "Issue date", DepartureDate = "Date of departure or entry", Model = "Model", Kind = "Type", Destination = "Destination of the operation", Purpose = "Purpose of the issue",
            FinalConsumer = "Final consumer", Presence = "Presence of the buyer", Environment = "Environment", IssueForm = "Form of issue", SoftwareVersion = "Version of the issuing software",
            Issuer = "Issuer", Recipient = "Recipient", Name = "Name", TradeName = "Trade name", TaxId = "Tax ID (CNPJ / CPF)", StateRegistration = "State registration (IE)", TaxRegime = "Tax regime (CRT)",
            IeIndicator = "Indicator of the state registration", Address = "Address", Phone = "Phone", Email = "Email",
            Items = "Items", Code = "Code", Description = "Description", Unit = "Unit", Quantity = "Quantity", UnitPrice = "Unit price", Amount = "Amount", Discount = "Discount",
            Totals = "Totals", IcmsBase = "ICMS base", Icms = "ICMS", IcmsExempt = "ICMS exempted", StBase = "ICMS-ST base", St = "ICMS-ST", Products = "Total of the products", Freight = "Freight", Insurance = "Insurance",
            ImportTax = "Import tax (II)", Ipi = "IPI", Pis = "PIS", Cofins = "COFINS", Other = "Other expenses", InvoiceTotal = "Total of the invoice", ApproxTaxes = "Approximate total of taxes",
            Transport = "Transport", FreightMode = "Mode of freight", Carrier = "Carrier", Volumes = "Volumes", NetWeight = "net weight", GrossWeight = "gross weight",
            Billing = "Billing", BillNumber = "Bill number", OriginalValue = "Original value", NetValue = "Net value", InstallmentNumber = "Installment", DueDate = "Due date",
            Payment = "Payment", PaymentMethod = "Means of payment", Change = "Change", AdditionalInfo = "Additional information", FiscalInfo = "For the tax authority", ComplementaryInfo = "Complementary",
            Authorization = "Answer of the tax authority", Result = "Result", ProtocolNumber = "Protocol", ReceivedAt = "Received at", AppVersion = "Version of the application",
            EventTitle = "Event of an NF-e", EventType = "Type of event", EventDate = "Date of the event", EventSequence = "Sequence number", EventDescription = "Event", Justification = "Justification",
            Correction = "Correction", RegisteredAt = "Registered at",
        };

        private static readonly NfeLabels Spanish = new()
        {
            Nfe = "NF-e (factura electrónica)", Nfce = "NFC-e (factura electrónica al consumidor)", Series = "serie", Number = "Número", AccessKey = "Clave de acceso", Operation = "Naturaleza de la operación",
            IssueDate = "Fecha de emisión", DepartureDate = "Fecha de salida o entrada", Model = "Modelo", Kind = "Tipo", Destination = "Destino de la operación", Purpose = "Finalidad de la emisión",
            FinalConsumer = "Consumidor final", Presence = "Presencia del comprador", Environment = "Ambiente", IssueForm = "Forma de emisión", SoftwareVersion = "Versión del software emisor",
            Issuer = "Emisor", Recipient = "Destinatario", Name = "Nombre", TradeName = "Nombre comercial", TaxId = "Identificación fiscal (CNPJ / CPF)", StateRegistration = "Inscripción estatal (IE)", TaxRegime = "Régimen tributario (CRT)",
            IeIndicator = "Indicador de la inscripción estatal", Address = "Dirección", Phone = "Teléfono", Email = "Correo electrónico",
            Items = "Ítems", Code = "Código", Description = "Descripción", Unit = "Unidad", Quantity = "Cantidad", UnitPrice = "Precio unitario", Amount = "Valor", Discount = "Descuento",
            Totals = "Totales", IcmsBase = "Base del ICMS", Icms = "ICMS", IcmsExempt = "ICMS exonerado", StBase = "Base del ICMS-ST", St = "ICMS-ST", Products = "Total de los productos", Freight = "Flete", Insurance = "Seguro",
            ImportTax = "Impuesto de importación (II)", Ipi = "IPI", Pis = "PIS", Cofins = "COFINS", Other = "Otros gastos", InvoiceTotal = "Total de la factura", ApproxTaxes = "Total aproximado de impuestos",
            Transport = "Transporte", FreightMode = "Modalidad del flete", Carrier = "Transportista", Volumes = "Volúmenes", NetWeight = "peso neto", GrossWeight = "peso bruto",
            Billing = "Cobranza", BillNumber = "Número de la factura de cobro", OriginalValue = "Valor original", NetValue = "Valor líquido", InstallmentNumber = "Cuota", DueDate = "Vencimiento",
            Payment = "Pago", PaymentMethod = "Medio de pago", Change = "Cambio", AdditionalInfo = "Información adicional", FiscalInfo = "Para la autoridad tributaria", ComplementaryInfo = "Complementaria",
            Authorization = "Respuesta de la autoridad tributaria", Result = "Resultado", ProtocolNumber = "Protocolo", ReceivedAt = "Recibido el", AppVersion = "Versión de la aplicación",
            EventTitle = "Evento de una NF-e", EventType = "Tipo de evento", EventDate = "Fecha del evento", EventSequence = "Número de secuencia", EventDescription = "Evento", Justification = "Justificación",
            Correction = "Corrección", RegisteredAt = "Registrado el",
        };

        private static readonly NfeLabels Portuguese = new()
        {
            Nfe = "NF-e (nota fiscal eletrônica)", Nfce = "NFC-e (nota fiscal eletrônica ao consumidor)", Series = "série", Number = "Número", AccessKey = "Chave de acesso", Operation = "Natureza da operação",
            IssueDate = "Data de emissão", DepartureDate = "Data de saída ou entrada", Model = "Modelo", Kind = "Tipo", Destination = "Destino da operação", Purpose = "Finalidade da emissão",
            FinalConsumer = "Consumidor final", Presence = "Presença do comprador", Environment = "Ambiente", IssueForm = "Forma de emissão", SoftwareVersion = "Versão do software emissor",
            Issuer = "Emitente", Recipient = "Destinatário", Name = "Nome", TradeName = "Nome fantasia", TaxId = "Identificação fiscal (CNPJ / CPF)", StateRegistration = "Inscrição estadual (IE)", TaxRegime = "Regime tributário (CRT)",
            IeIndicator = "Indicador da inscrição estadual", Address = "Endereço", Phone = "Telefone", Email = "E-mail",
            Items = "Itens", Code = "Código", Description = "Descrição", Unit = "Unidade", Quantity = "Quantidade", UnitPrice = "Valor unitário", Amount = "Valor", Discount = "Desconto",
            Totals = "Totais", IcmsBase = "Base de cálculo do ICMS", Icms = "ICMS", IcmsExempt = "ICMS desonerado", StBase = "Base de cálculo do ICMS-ST", St = "ICMS-ST", Products = "Total dos produtos", Freight = "Frete", Insurance = "Seguro",
            ImportTax = "Imposto de importação (II)", Ipi = "IPI", Pis = "PIS", Cofins = "COFINS", Other = "Outras despesas", InvoiceTotal = "Total da nota", ApproxTaxes = "Total aproximado dos tributos",
            Transport = "Transporte", FreightMode = "Modalidade do frete", Carrier = "Transportadora", Volumes = "Volumes", NetWeight = "peso líquido", GrossWeight = "peso bruto",
            Billing = "Cobrança", BillNumber = "Número da fatura", OriginalValue = "Valor original", NetValue = "Valor líquido", InstallmentNumber = "Duplicata", DueDate = "Vencimento",
            Payment = "Pagamento", PaymentMethod = "Meio de pagamento", Change = "Troco", AdditionalInfo = "Informações adicionais", FiscalInfo = "Para o fisco", ComplementaryInfo = "Complementares",
            Authorization = "Resposta da autoridade fiscal", Result = "Resultado", ProtocolNumber = "Protocolo", ReceivedAt = "Recebido em", AppVersion = "Versão do aplicativo",
            EventTitle = "Evento de uma NF-e", EventType = "Tipo de evento", EventDate = "Data do evento", EventSequence = "Número de sequência", EventDescription = "Evento", Justification = "Justificativa",
            Correction = "Correção", RegisteredAt = "Registrado em",
        };
    }
}
