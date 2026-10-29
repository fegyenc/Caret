using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // The sixth Brazilian pack of the XML reader: the MDF-e (model 58, the electronic manifest of fiscal documents, "manifesto eletrônico de documentos
    // fiscais"), version 3.00, as the national portal defines it (the namespace http://www.portalfiscal.inf.br/mdfe): the processed manifest (mdfeProc:
    // the MDFe and the protocol of authorization, protMDFe), the bare MDFe, and the events (procEventoMDFe: a cancellation, the closing of the trip,
    // the inclusion of a driver...). The structure and the codes were read from the official schema package (PL_MDFe_300b, NT 2025.001, 1.04) and the
    // names of the codes from the official manual (MOC 3.00b, annex I), not from memory; they are in Portuguese whatever the language of the interface,
    // and a code that is not in the lists is printed as it is. The road transport is read in detail (the vehicles, the drivers); the air, water and rail
    // modals are in the generic view that follows the summary.
    //
    // A manifest lists documents that are carried; it is not an invoice, so it has no row in a table of invoices (the folder table lists it as such).
    internal static class XmlMdfe
    {
        private const string Namespace = "http://www.portalfiscal.inf.br/mdfe";

        internal static bool IsMdfe(XElement root) => root.Name.NamespaceName == Namespace && (root.Name.LocalName is "mdfeProc" or "MDFe");

        internal static bool IsEvent(XElement root) => root.Name.NamespaceName == Namespace && (root.Name.LocalName is "procEventoMDFe" or "eventoMDFe");

        private static XElement Document(XElement root)
        {
            var mdfe = root.Name.LocalName == "MDFe" ? root : XmlUbl.Child(root, "MDFe");
            return mdfe == null ? null : XmlUbl.Child(mdfe, "infMDFe");
        }

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            var l = MdfeLabels.For(xl);
            var n = NfeLabels.For(xl);
            if (IsEvent(root)) return RenderEvent(root, context, xl, l, n, sb);
            if (!IsMdfe(root)) return false;
            var inf = Document(root);
            if (inf == null) return false;

            var ide = XmlUbl.Child(inf, "ide");
            var number = T(ide, "nMDF");
            var series = T(ide, "serie");
            XmlUbl.Heading(sb, 1, l.Mdfe + (number == null ? "" : " " + number) + (series == null ? "" : " — " + n.Series + " " + series));
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append('\n');
            var version = inf.Attribute("versao")?.Value;
            if (version != null) sb.Append("> MDF-e ").Append(XmlUbl.Safe(version)).Append('\n');
            sb.Append('\n');

            var facts = new List<(string, string)>();
            Add(facts, n.AccessKey, Key(inf));
            Add(facts, n.IssueDate, T(ide, "dhEmi"));
            Add(facts, n.Model, Coded(Models, T(ide, "mod")));
            Add(facts, Capital(n.Series), series);
            Add(facts, n.Number, number);
            Add(facts, l.Modal, Coded(Modals, T(ide, "modal")));
            Add(facts, l.EmitterType, Coded(EmitterTypes, T(ide, "tpEmit")));
            Add(facts, l.TransporterType, Coded(TransporterTypes, T(ide, "tpTransp")));
            Add(facts, n.Environment, Coded(XmlNfe.Environments, T(ide, "tpAmb")));
            Add(facts, n.IssueForm, Coded(IssueForms, T(ide, "tpEmis")));
            Add(facts, l.StateLoading, T(ide, "UFIni"));
            Add(facts, l.StateUnloading, T(ide, "UFFim"));
            Add(facts, l.LoadingPlaces, string.Join(", ", XmlUbl.Children(ide, "infMunCarrega").Select(m => T(m, "xMunCarrega")).Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, l.Route, string.Join(" → ", XmlUbl.Children(ide, "infPercurso").Select(p => T(p, "UFPer")).Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, l.TripStart, T(ide, "dhIniViagem"));
            Add(facts, n.SoftwareVersion, T(ide, "verProc"));
            XmlUbl.Facts(sb, facts);

            Issuer(sb, XmlUbl.Child(inf, "emit"), n);
            Road(sb, XmlUbl.Child(inf, "infModal/rodo"), l);
            Documents(sb, XmlUbl.Child(inf, "infDoc"), l);
            Cargo(sb, XmlUbl.Child(inf, "prodPred"), XmlUbl.Child(inf, "tot"), XmlUbl.Children(inf, "lacres").Select(s => T(s, "nLacre")).Where(s => !string.IsNullOrEmpty(s)), l);
            Additional(sb, XmlUbl.Child(inf, "infAdic"), n);
            Protocol(sb, root.Name.LocalName == "mdfeProc" ? XmlUbl.Child(root, "protMDFe/infProt") : null, n);
            return true;
        }

        // The access key: the Id of infMDFe without the "MDFe" in front of it.
        private static string Key(XElement inf)
        {
            var id = inf.Attribute("Id")?.Value.Trim();
            return string.IsNullOrEmpty(id) ? null : id.StartsWith("MDFe", StringComparison.Ordinal) ? id.Substring(4) : id;
        }

        private static void Issuer(StringBuilder sb, XElement party, NfeLabels n)
        {
            if (party == null) return;
            var facts = new List<(string, string)>();
            Add(facts, n.Name, T(party, "xNome"));
            Add(facts, n.TradeName, T(party, "xFant"));
            Add(facts, n.TaxId, T(party, "CNPJ") ?? T(party, "CPF"));
            Add(facts, n.StateRegistration, T(party, "IE"));
            var address = XmlUbl.Child(party, "enderEmit");
            if (address != null)
            {
                var street = string.Join(", ", new[] { T(address, "xLgr"), T(address, "nro"), T(address, "xCpl"), T(address, "xBairro") }.Where(s => !string.IsNullOrEmpty(s)));
                var city = string.Join("/", new[] { T(address, "xMun"), T(address, "UF") }.Where(s => !string.IsNullOrEmpty(s)));
                Add(facts, n.Address, string.Join(" — ", new[] { street, city, T(address, "CEP") }.Where(s => !string.IsNullOrEmpty(s))));
                Add(facts, n.Phone, T(address, "fone"));
                Add(facts, n.Email, T(address, "email"));
            }
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, n.Issuer);
            XmlUbl.Facts(sb, facts);
        }

        // The road modal: the registry of the carrier, the CIOT, the vehicles (the tractor and the trailers), the drivers.
        private static void Road(StringBuilder sb, XElement road, MdfeLabels l)
        {
            if (road == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.Rntrc, T(road, "infANTT/RNTRC"));
            var ciot = string.Join(", ", XmlUbl.Children(XmlUbl.Child(road, "infANTT") ?? road, "infCIOT").Select(c => T(c, "CIOT")).Where(s => !string.IsNullOrEmpty(s)));
            Add(facts, "CIOT", ciot);
            var vehicles = new List<(string Role, XElement Vehicle)>();
            if (XmlUbl.Child(road, "veicTracao") is { } tractor) vehicles.Add((l.Tractor, tractor));
            foreach (var trailer in XmlUbl.Children(road, "veicReboque")) vehicles.Add((l.Trailer, trailer));
            var drivers = XmlUbl.Children(XmlUbl.Child(road, "veicTracao") ?? road, "condutor").ToList();
            if (facts.Count == 0 && vehicles.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Road);
            XmlUbl.Facts(sb, facts);
            if (vehicles.Count > 0)
            {
                var rows = new List<IReadOnlyList<string>> { new[] { l.Vehicle, l.Plate, l.Tare, l.CapacityKg, l.CapacityM3, l.WheelType, l.BodyType, l.State, l.Owner } };
                foreach (var (role, v) in vehicles)
                    rows.Add(new[]
                    {
                        role, T(v, "placa") ?? "", T(v, "tara") ?? "", T(v, "capKG") ?? "", T(v, "capM3") ?? "", Coded(WheelTypes, T(v, "tpRod")) ?? "", Coded(BodyTypes, T(v, "tpCar")) ?? "", T(v, "UF") ?? "",
                        XmlUbl.Child(v, "prop") is { } owner ? string.Join(" · ", new[] { T(owner, "xNome"), T(owner, "CNPJ") ?? T(owner, "CPF"), Coded(OwnerTypes, T(owner, "tpProp")) }.Where(s => !string.IsNullOrEmpty(s))) : "",
                    });
                XmlUbl.Table(sb, rows);
            }
            if (drivers.Count > 0)
            {
                var rows = new List<IReadOnlyList<string>> { new[] { l.Driver, "CPF" } };
                foreach (var d in drivers) rows.Add(new[] { T(d, "xNome") ?? "", T(d, "CPF") ?? "" });
                XmlUbl.Table(sb, rows);
            }
        }

        // The documents of the manifest, by the place where they are unloaded: the CT-e, the NF-e and the manifests carried by a ship, by the key.
        private static void Documents(StringBuilder sb, XElement documents, MdfeLabels l)
        {
            if (documents == null) return;
            foreach (var place in XmlUbl.Children(documents, "infMunDescarga"))
            {
                var rows = new List<IReadOnlyList<string>> { new[] { l.Document, l.Key } };
                foreach (var cte in XmlUbl.Children(place, "infCTe")) rows.Add(new[] { "CT-e", T(cte, "chCTe") ?? "" });
                foreach (var nfe in XmlUbl.Children(place, "infNFe")) rows.Add(new[] { "NF-e", T(nfe, "chNFe") ?? "" });
                foreach (var mdfe in XmlUbl.Children(place, "infMDFeTransp")) rows.Add(new[] { "MDF-e", T(mdfe, "chMDFe") ?? "" });
                XmlUbl.Heading(sb, 2, l.Unloading + (T(place, "xMunDescarga") is { } city ? ": " + city : ""));
                if (rows.Count > 1) XmlUbl.Table(sb, rows);
            }
        }

        private static void Cargo(StringBuilder sb, XElement product, XElement total, IEnumerable<string> seals, MdfeLabels l)
        {
            var facts = new List<(string, string)>();
            if (product != null)
            {
                Add(facts, l.CargoType, Coded(CargoTypes, T(product, "tpCarga")));
                Add(facts, l.Product, T(product, "xProd"));
                Add(facts, "NCM", T(product, "NCM"));
            }
            if (total != null)
            {
                Add(facts, l.CteCount, T(total, "qCTe"));
                Add(facts, l.NfeCount, T(total, "qNFe"));
                Add(facts, l.MdfeCount, T(total, "qMDFe"));
                Add(facts, l.CargoValue, T(total, "vCarga"));
                // the weight with the name of its unit (KG or TON)
                var weight = T(total, "qCarga");
                var unit = T(total, "cUnid");
                var unitName = unit != null && Units.TryGetValue(unit, out var known) ? known : unit;
                Add(facts, l.GrossWeight, weight == null ? null : weight + (unitName == null ? "" : " " + unitName));
            }
            Add(facts, l.Seals, string.Join(", ", seals));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Cargo);
            XmlUbl.Facts(sb, facts);
        }

        private static void Additional(StringBuilder sb, XElement info, NfeLabels n)
        {
            if (info == null) return;
            var facts = new List<(string, string)>();
            Add(facts, n.FiscalInfo, T(info, "infAdFisco"));
            Add(facts, n.ComplementaryInfo, T(info, "infCpl"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, n.AdditionalInfo);
            XmlUbl.Facts(sb, facts);
        }

        // The answer of the tax authority that comes with a processed manifest (mdfeProc): what it says, as it was written, not whether it is right.
        private static void Protocol(StringBuilder sb, XElement protocol, NfeLabels n)
        {
            if (protocol == null) return;
            var facts = new List<(string, string)>();
            Add(facts, n.Result, string.Join(": ", new[] { T(protocol, "cStat"), T(protocol, "xMotivo") }.Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, n.ProtocolNumber, T(protocol, "nProt"));
            Add(facts, n.ReceivedAt, T(protocol, "dhRecbto"));
            Add(facts, n.Environment, Coded(XmlNfe.Environments, T(protocol, "tpAmb")));
            Add(facts, n.AppVersion, T(protocol, "verAplic"));
            Add(facts, n.AccessKey, T(protocol, "chMDFe"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, n.Authorization);
            XmlUbl.Facts(sb, facts);
        }

        // ---- the events of an MDF-e --------------------------------------------------------------------------------------------------

        private static XElement EventElement(XElement root) => root.Name.LocalName == "eventoMDFe" ? root : XmlUbl.Child(root, "eventoMDFe");

        // The element inside detEvento that holds the particulars of the event (evCancMDFe, evEncMDFe, evIncCondutorMDFe...).
        private static XElement EventBody(XElement info) => XmlUbl.Child(info, "detEvento")?.Elements().FirstOrDefault();

        private static bool RenderEvent(XElement root, ConversionContext context, XmlLabels xl, MdfeLabels l, NfeLabels n, StringBuilder sb)
        {
            var evento = EventElement(root);
            var info = evento == null ? null : XmlUbl.Child(evento, "infEvento");
            var key = info == null ? null : T(info, "chMDFe");
            if (key == null) return false;
            var body = EventBody(info);
            var answer = XmlUbl.Child(root, "retEventoMDFe/infEvento");
            var name = (body == null ? null : T(body, "descEvento")) ?? (answer == null ? null : T(answer, "xEvento")) ?? T(info, "tpEvento") ?? "?";
            XmlUbl.Heading(sb, 1, l.EventTitle + ": " + name);
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append("\n\n");
            else sb.Append('\n');
            var facts = new List<(string, string)>();
            Add(facts, n.AccessKey, key);
            Add(facts, n.EventType, T(info, "tpEvento"));
            Add(facts, n.EventDate, T(info, "dhEvento"));
            Add(facts, n.EventSequence, T(info, "nSeqEvento"));
            Add(facts, n.Environment, Coded(XmlNfe.Environments, T(info, "tpAmb")));
            Add(facts, n.TaxId, T(info, "CNPJ") ?? T(info, "CPF"));
            if (body != null)
            {
                Add(facts, n.Description, T(body, "descEvento"));
                Add(facts, n.ProtocolNumber, T(body, "nProt"));
                Add(facts, n.Justification, T(body, "xJust"));
                Add(facts, l.ClosedOn, T(body, "dtEnc"));
                Add(facts, l.ClosedIn, string.Join(" · ", new[] { T(body, "cMun"), T(body, "cUF") }.Where(s => !string.IsNullOrEmpty(s))));
                Add(facts, l.Driver, XmlUbl.Child(body, "condutor") is { } driver ? string.Join(" · ", new[] { T(driver, "xNome"), T(driver, "CPF") }.Where(s => !string.IsNullOrEmpty(s))) : null);
            }
            XmlUbl.Facts(sb, facts);
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

        // ---- small helpers -----------------------------------------------------------------------------------------------------------

        private static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string T(XElement parent, string path) => parent == null ? null : XmlUbl.Text(parent, path);

        private static void Add(List<(string, string)> facts, string label, string value)
        {
            if (!string.IsNullOrEmpty(value)) facts.Add((label, value));
        }

        // "1 — Rodoviário": the code and the name the manual gives it; the code alone when it is not in the list.
        private static string Coded(IReadOnlyDictionary<string, string> table, string code) =>
            code == null ? null : table.TryGetValue(code, out var name) ? code + " — " + name : code;

        // Codes of the national schema (mdfeTiposBasico_v3.00.xsd) and of the manual (MOC 3.00b, annex I), with their own words (Portuguese).
        private static readonly IReadOnlyDictionary<string, string> Models = new Dictionary<string, string> { ["58"] = "MDF-e" };
        private static readonly IReadOnlyDictionary<string, string> Modals = new Dictionary<string, string>
        {
            ["1"] = "Rodoviário", ["2"] = "Aéreo", ["3"] = "Aquaviário", ["4"] = "Ferroviário",
        };
        private static readonly IReadOnlyDictionary<string, string> EmitterTypes = new Dictionary<string, string>
        {
            ["1"] = "Prestador de serviço de transporte", ["2"] = "Transportador de Carga Própria", ["3"] = "Prestador de serviço de transporte que emitirá CT-e Globalizado",
        };
        private static readonly IReadOnlyDictionary<string, string> TransporterTypes = new Dictionary<string, string> { ["1"] = "ETC", ["2"] = "TAC", ["3"] = "CTC" };
        private static readonly IReadOnlyDictionary<string, string> IssueForms = new Dictionary<string, string>
        {
            ["1"] = "Normal", ["2"] = "Contingência Off-Line", ["3"] = "Regime Especial NFF",
        };
        private static readonly IReadOnlyDictionary<string, string> CargoTypes = new Dictionary<string, string>
        {
            ["01"] = "Granel sólido", ["02"] = "Granel líquido", ["03"] = "Frigorificada", ["04"] = "Conteinerizada", ["05"] = "Carga Geral", ["06"] = "Neogranel",
            ["07"] = "Perigosa (granel sólido)", ["08"] = "Perigosa (granel líquido)", ["09"] = "Perigosa (carga frigorificada)", ["10"] = "Perigosa (conteinerizada)", ["11"] = "Perigosa (carga geral)",
        };
        private static readonly IReadOnlyDictionary<string, string> Units = new Dictionary<string, string> { ["01"] = "KG", ["02"] = "TON" };
        private static readonly IReadOnlyDictionary<string, string> WheelTypes = new Dictionary<string, string>
        {
            ["01"] = "Truck", ["02"] = "Toco", ["03"] = "Cavalo Mecânico", ["04"] = "VAN", ["05"] = "Utilitário", ["06"] = "Outros",
        };
        private static readonly IReadOnlyDictionary<string, string> BodyTypes = new Dictionary<string, string>
        {
            ["00"] = "não aplicável", ["01"] = "Aberta", ["02"] = "Fechada/Baú", ["03"] = "Granelera", ["04"] = "Porta Container", ["05"] = "Sider",
        };
        private static readonly IReadOnlyDictionary<string, string> OwnerTypes = new Dictionary<string, string>
        {
            ["0"] = "TAC Agregado", ["1"] = "TAC Independente", ["2"] = "Outros",
        };
    }

    // The words of the readable view of an MDF-e, in English, Spanish and Portuguese, besides those it shares with the NF-e (NfeLabels). The names of
    // the codes (above) are those of the manual, in Portuguese.
    internal sealed class MdfeLabels
    {
        public string Mdfe, Modal, EmitterType, TransporterType, StateLoading, StateUnloading, LoadingPlaces, Route, TripStart;
        public string Road, Rntrc, Vehicle, Tractor, Trailer, Plate, Tare, CapacityKg, CapacityM3, WheelType, BodyType, State, Owner, Driver;
        public string Unloading, Document, Key, Cargo, CargoType, Product, CteCount, NfeCount, MdfeCount, CargoValue, GrossWeight, Seals;
        public string EventTitle, ClosedOn, ClosedIn;

        public static MdfeLabels For(XmlLabels xl) => xl.IsPortuguese ? Portuguese : xl.IsSpanish ? Spanish : English;

        private static readonly MdfeLabels English = new()
        {
            Mdfe = "MDF-e (electronic manifest of fiscal documents)", Modal = "Mode of transport", EmitterType = "Type of issuer", TransporterType = "Type of carrier",
            StateLoading = "State of loading", StateUnloading = "State of unloading", LoadingPlaces = "Places of loading", Route = "Route (states)", TripStart = "Planned start of the trip",
            Road = "Road transport", Rntrc = "RNTRC (registry of road carriers)", Vehicle = "Vehicle", Tractor = "Tractor", Trailer = "Trailer", Plate = "Plate", Tare = "Tare (kg)",
            CapacityKg = "Capacity (kg)", CapacityM3 = "Capacity (m³)", WheelType = "Wheel type", BodyType = "Body", State = "State", Owner = "Owner", Driver = "Driver",
            Unloading = "Unloading", Document = "Document", Key = "Access key", Cargo = "Cargo", CargoType = "Type of cargo", Product = "Main product",
            CteCount = "Number of CT-e", NfeCount = "Number of NF-e", MdfeCount = "Number of MDF-e", CargoValue = "Total value of the cargo", GrossWeight = "Gross weight", Seals = "Seals",
            EventTitle = "Event of an MDF-e", ClosedOn = "Closed on", ClosedIn = "Closed in (municipality · state)",
        };

        private static readonly MdfeLabels Spanish = new()
        {
            Mdfe = "MDF-e (manifiesto electrónico de documentos fiscales)", Modal = "Modalidad de transporte", EmitterType = "Tipo de emisor", TransporterType = "Tipo de transportista",
            StateLoading = "Estado de carga", StateUnloading = "Estado de descarga", LoadingPlaces = "Lugares de carga", Route = "Recorrido (estados)", TripStart = "Inicio previsto del viaje",
            Road = "Transporte por carretera", Rntrc = "RNTRC (registro de transportistas de carga)", Vehicle = "Vehículo", Tractor = "Tractor", Trailer = "Remolque", Plate = "Placa", Tare = "Tara (kg)",
            CapacityKg = "Capacidad (kg)", CapacityM3 = "Capacidad (m³)", WheelType = "Tipo de rodado", BodyType = "Carrocería", State = "Estado", Owner = "Propietario", Driver = "Conductor",
            Unloading = "Descarga", Document = "Documento", Key = "Clave de acceso", Cargo = "Carga", CargoType = "Tipo de carga", Product = "Producto predominante",
            CteCount = "Cantidad de CT-e", NfeCount = "Cantidad de NF-e", MdfeCount = "Cantidad de MDF-e", CargoValue = "Valor total de la carga", GrossWeight = "Peso bruto", Seals = "Precintos",
            EventTitle = "Evento de un MDF-e", ClosedOn = "Cerrado el", ClosedIn = "Cerrado en (municipio · estado)",
        };

        private static readonly MdfeLabels Portuguese = new()
        {
            Mdfe = "MDF-e (manifesto eletrônico de documentos fiscais)", Modal = "Modal de transporte", EmitterType = "Tipo do emitente", TransporterType = "Tipo do transportador",
            StateLoading = "UF do carregamento", StateUnloading = "UF do descarregamento", LoadingPlaces = "Municípios de carregamento", Route = "Percurso (UFs)", TripStart = "Início previsto da viagem",
            Road = "Transporte rodoviário", Rntrc = "RNTRC (registro de transportadores rodoviários de carga)", Vehicle = "Veículo", Tractor = "Tração", Trailer = "Reboque", Plate = "Placa", Tare = "Tara (kg)",
            CapacityKg = "Capacidade (kg)", CapacityM3 = "Capacidade (m³)", WheelType = "Tipo de rodado", BodyType = "Carroceria", State = "UF", Owner = "Proprietário", Driver = "Condutor",
            Unloading = "Descarregamento", Document = "Documento", Key = "Chave de acesso", Cargo = "Carga", CargoType = "Tipo de carga", Product = "Produto predominante",
            CteCount = "Quantidade de CT-e", NfeCount = "Quantidade de NF-e", MdfeCount = "Quantidade de MDF-e", CargoValue = "Valor total da carga", GrossWeight = "Peso bruto", Seals = "Lacres",
            EventTitle = "Evento de um MDF-e", ClosedOn = "Encerrado em", ClosedIn = "Encerrado em (município · UF)",
        };
    }
}
