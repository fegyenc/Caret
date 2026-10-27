using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // The fifth format pack of the XML reader: Chile's electronic tax documents, the DTE of the SII (Servicio de Impuestos Internos), in the
    // namespace http://www.sii.cl/SiiDte: the envelope EnvioDTE (a set of up to 2000 documents), the bare DTE, and the envelope of electronic
    // receipts, EnvioBOLETA. The document is a Documento (invoice, notes, dispatch note, receipt), a Liquidacion (the liquidation invoice) or
    // Exportaciones (the export invoice and its notes).
    //
    // The structure and the codes were read from the schemas that the SII publishes (DTE_v10.xsd, EnvioDTE_v10.xsd, SiiTypes_v10.xsd, dated
    // 2026-02-06, and EnvioBOLETA_v11.xsd), and the names of the document types from the SII's list of document codes (IECVCodigosDocs.pdf),
    // not from memory. The names of the codes are the SII's, in Spanish whatever the language of the interface (those of the additional taxes
    // as the schema writes them, without accents); a code that the schema does not explain (the activity codes, the types of reference
    // beyond the document types, the units) is printed as it is.
    //
    // The principles of the other packs: amounts and dates as the file has them, nothing checked or judged, the digital signature and the
    // signature of the stamp (TED) named and not printed, and the caller adds every field of the file after this summary.
    internal static class XmlChile
    {
        private const string Namespace = "http://www.sii.cl/SiiDte";

        internal static bool IsChile(XElement root) =>
            root.Name.NamespaceName == Namespace && root.Name.LocalName is "EnvioDTE" or "DTE" or "EnvioBOLETA";

        // The signature of the stamp (FRMT) and the signature of the SII over the authorization of the folios (FRMA) are long and unreadable.
        internal static bool IsSeal(XElement element) =>
            element.Name.NamespaceName == Namespace && element.Name.LocalName is "FRMT" or "FRMA" && !element.HasElements;

        // The documents of the file: the DTE itself, or those of the envelope.
        private static List<XElement> Documents(XElement root)
        {
            if (root.Name.LocalName == "DTE") return new List<XElement> { root };
            var set = XmlUbl.Child(root, "SetDTE");
            return set == null ? new List<XElement>() : XmlUbl.Children(set, "DTE").ToList();
        }

        // The body of a document: Documento (invoices, notes, dispatch notes, receipts), Liquidacion or Exportaciones.
        private static XElement Body(XElement dte) =>
            XmlUbl.Child(dte, "Documento") ?? XmlUbl.Child(dte, "Liquidacion") ?? XmlUbl.Child(dte, "Exportaciones");

        // ---- the document -----------------------------------------------------------------------------------------------------------

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            if (!IsChile(root)) return false;
            var w = new Words(xl.IsSpanish);
            var documents = Documents(root);
            if (root.Name.LocalName == "DTE")
            {
                if (Body(root) == null) return false;
                XmlUbl.Heading(sb, 1, Title(Body(root), w));
                Source(sb, context, xl);
                sb.Append('\n');
                Render(sb, root, w, 2);
                return true;
            }
            XmlUbl.Heading(sb, 1, root.Name.LocalName == "EnvioBOLETA" ? w.W("Electronic receipts (EnvioBOLETA)", "Boletas electrónicas (EnvioBOLETA)") : w.W("Electronic tax documents (EnvioDTE)", "Documentos tributarios electrónicos (EnvioDTE)"));
            Source(sb, context, xl);
            sb.Append('\n');
            Envelope(sb, root, w);
            if (documents.Count == 0) return true;
            XmlUbl.Heading(sb, 2, w.W("Documents", "Documentos") + " (" + documents.Count.ToString(CultureInfo.InvariantCulture) + ")");
            foreach (var dte in documents)
            {
                var body = Body(dte);
                if (body == null) continue;
                XmlUbl.Heading(sb, 3, Title(body, w));
                Render(sb, dte, w, 4);
            }
            return true;
        }

        private static void Source(StringBuilder sb, ConversionContext context, XmlLabels xl)
        {
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append('\n');
        }

        // The cover of an envelope: who sends it to whom, the resolution that authorizes it, and how many documents of each type it has.
        private static void Envelope(StringBuilder sb, XElement root, Words w)
        {
            var cover = XmlUbl.Child(root, "SetDTE/Caratula");
            if (cover == null) return;
            var facts = new List<(string, string)>();
            Add(facts, w.W("Issuer RUT", "RUT del emisor"), T(cover, "RutEmisor"));
            Add(facts, w.W("RUT that sends", "RUT que envía"), T(cover, "RutEnvia"));
            Add(facts, w.W("RUT that receives", "RUT que recibe"), T(cover, "RutReceptor"));
            Add(facts, w.W("Resolution date", "Fecha de la resolución"), T(cover, "FchResol"));
            Add(facts, w.W("Resolution number", "Número de la resolución"), T(cover, "NroResol"));
            Add(facts, w.W("Signed at", "Firmado el"), T(cover, "TmstFirmaEnv"));
            foreach (var sub in XmlUbl.Children(cover, "SubTotDTE"))
                Add(facts, w.W("Documents of type", "Documentos del tipo") + " " + (T(sub, "TpoDTE") ?? "").Trim(), T(sub, "NroDTE"));
            XmlUbl.Facts(sb, facts);
        }

        private static string Title(XElement body, Words w)
        {
            var id = XmlUbl.Child(body, "Encabezado/IdDoc");
            var type = T(id, "TipoDTE");
            var folio = T(id, "Folio");
            var name = type != null && DocumentTypes.TryGetValue(type, out var n) ? (w.Spanish ? n.Es : n.En) : (w.Spanish ? "Documento tributario electrónico" : "Electronic tax document") + (type == null ? "" : " (" + (w.Spanish ? "tipo " : "type ") + type + ")");
            return folio == null ? name : name + " " + folio;
        }

        // One document, with its sections at the heading level `level`.
        private static void Render(StringBuilder sb, XElement dte, Words w, int level)
        {
            var body = Body(dte);
            var head = XmlUbl.Child(body, "Encabezado");
            var id = XmlUbl.Child(head, "IdDoc");
            var type = T(id, "TipoDTE");
            var receipt = type is "39" or "41";
            var export = type is "110" or "111" or "112";

            var facts = new List<(string, string)>();
            Add(facts, w.W("Type", "Tipo"), Coded(DocumentNames, type));
            Add(facts, "Folio", T(id, "Folio"));
            Add(facts, w.W("Issue date", "Fecha de emisión"), T(id, "FchEmis"));
            Add(facts, w.W("Due date", "Fecha de vencimiento"), T(id, "FchVenc"));
            var totals = XmlUbl.Child(head, "Totales");
            var currency = export ? T(totals, "TpoMoneda") : "CLP";
            Add(facts, w.W("Currency", "Moneda"), currency == "CLP" ? "CLP (" + w.W("Chilean pesos", "pesos chilenos") + ")" : currency);
            Add(facts, w.W("Form of payment", "Forma de pago"), Coded(PaymentForms, T(id, "FmaPago")));
            Add(facts, w.W("Means of payment", "Medio de pago"), receipt ? T(id, "MedioPago") : Coded(PaymentMeans, T(id, "MedioPago")));
            Add(facts, w.W("Kind of service", "Indicador de servicio"), Coded(receipt ? ReceiptServices : Services, T(id, "IndServicio")));
            Add(facts, w.W("Kind of transfer", "Tipo de traslado"), Coded(Transfers, T(id, "IndTraslado")));
            Add(facts, w.W("Dispatch", "Despacho"), Coded(Dispatches, T(id, "TipoDespacho")));
            Add(facts, w.W("Credit note without right to deduct the VAT", "Nota de crédito sin derecho a descontar el débito"), T(id, "IndNoRebaja") == "1" ? "1" : null);
            Add(facts, w.W("Billing period", "Período de facturación"), Period(T(id, "PeriodoDesde"), T(id, "PeriodoHasta")));
            var version = dte.Attribute("version")?.Value;
            Add(facts, w.W("Format version", "Versión del formato"), version);
            XmlUbl.Facts(sb, facts);

            Party(sb, XmlUbl.Child(head, "Emisor"), w.W("Issuer", "Emisor"), w, issuer: true, level);
            Party(sb, XmlUbl.Child(head, "Receptor"), w.W("Receiver", "Receptor"), w, issuer: false, level);
            Transport(sb, XmlUbl.Child(head, "Transporte"), w, level);
            Items(sb, body, w, level, receipt);
            Global(sb, body, w, level);
            Totals(sb, totals, w, level);
            References(sb, body, w, level, receipt);
            Commissions(sb, body, w, level);
            Stamp(sb, body, w, level);
        }

        private static string Period(string from, string to) =>
            from == null && to == null ? null : (from ?? "…") + " — " + (to ?? "…");

        private static void Party(StringBuilder sb, XElement party, string title, Words w, bool issuer, int level)
        {
            if (party == null) return;
            var facts = new List<(string, string)>();
            Add(facts, w.W("Name", "Razón social"), T(party, issuer ? "RznSoc" : "RznSocRecep") ?? T(party, issuer ? "RznSocEmisor" : "RznSocRecep"));
            Add(facts, "RUT", T(party, issuer ? "RUTEmisor" : "RUTRecep"));
            Add(facts, w.W("Line of business", "Giro"), T(party, issuer ? "GiroEmis" : "GiroRecep") ?? T(party, "GiroEmisor"));
            // the codes of economic activity are printed as they are (the table of the SII is not in this reader)
            var activities = XmlUbl.Children(party, "Acteco").Select(e => e.Value.Trim()).Where(s => s.Length > 0).ToList();
            if (activities.Count > 0) Add(facts, w.W("Economic activity code", "Código de actividad económica"), string.Join(", ", activities));
            var place = issuer
                ? new[] { T(party, "DirOrigen"), T(party, "CmnaOrigen"), T(party, "CiudadOrigen") }
                : new[] { T(party, "DirRecep"), T(party, "CmnaRecep"), T(party, "CiudadRecep") };
            Add(facts, w.W("Address", "Dirección"), string.Join(", ", place.Where(s => !string.IsNullOrEmpty(s))));
            if (!issuer) Add(facts, w.W("Postal address", "Dirección postal"), string.Join(", ", new[] { T(party, "DirPostal"), T(party, "CmnaPostal"), T(party, "CiudadPostal") }.Where(s => !string.IsNullOrEmpty(s))));
            if (issuer) Add(facts, w.W("Branch", "Sucursal"), T(party, "Sucursal") ?? T(party, "CdgSIISucur"));
            var phones = XmlUbl.Children(party, "Telefono").Select(e => e.Value.Trim()).Where(s => s.Length > 0);
            Add(facts, w.W("Phone", "Teléfono"), string.Join(", ", phones) is { Length: > 0 } joined ? joined : T(party, "TelefonoRecep"));
            Add(facts, w.W("Email", "Correo electrónico"), T(party, issuer ? "CorreoEmisor" : "CorreoRecep"));
            if (!issuer) Add(facts, w.W("Contact", "Contacto"), T(party, "Contacto"));
            if (!issuer && XmlUbl.Child(party, "Extranjero") is { } foreign)
                Add(facts, w.W("Foreign receiver", "Receptor extranjero"), string.Join(" · ", new[] { T(foreign, "NumId"), T(foreign, "Nacionalidad") }.Where(s => !string.IsNullOrEmpty(s))));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, level, title);
            XmlUbl.Facts(sb, facts);
        }

        private static void Transport(StringBuilder sb, XElement transport, Words w, int level)
        {
            if (transport == null) return;
            var facts = new List<(string, string)>();
            Add(facts, w.W("Vehicle plate", "Patente del vehículo"), T(transport, "Patente"));
            Add(facts, w.W("Carrier RUT", "RUT del transportista"), T(transport, "RUTTrans"));
            if (XmlUbl.Child(transport, "Chofer") is { } driver)
                Add(facts, w.W("Driver", "Chofer"), string.Join(" · ", new[] { T(driver, "NombreChofer"), T(driver, "RUTChofer") }.Where(s => !string.IsNullOrEmpty(s))));
            Add(facts, w.W("Destination", "Destino"), string.Join(", ", new[] { T(transport, "DirDest"), T(transport, "CmnaDest"), T(transport, "CiudadDest") }.Where(s => !string.IsNullOrEmpty(s))));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, level, w.W("Transport", "Transporte"));
            XmlUbl.Facts(sb, facts);
        }

        private static void Items(StringBuilder sb, XElement body, Words w, int level, bool receipt)
        {
            var details = XmlUbl.Children(body, "Detalle").ToList();
            if (details.Count == 0) return;
            var header = new[]
            {
                "#", w.W("Code", "Código"), w.W("Name", "Nombre"), w.W("Description", "Descripción"), w.W("Quantity", "Cantidad"), w.W("Unit", "Unidad"), w.W("Unit price", "Precio unitario"),
                w.W("Discount", "Descuento"), w.W("Surcharge", "Recargo"), w.W("Exempt", "Exento"), w.W("Amount", "Monto"),
            };
            var rows = new List<string[]>();
            foreach (var d in details)
            {
                var codes = string.Join(", ", XmlUbl.Children(d, "CdgItem").Select(c => string.Join(" ", new[] { T(c, "TpoCodigo"), T(c, "VlrCodigo") }.Where(s => !string.IsNullOrEmpty(s)))).Where(s => s.Length > 0));
                var discount = Reduction(T(d, "DescuentoMonto"), T(d, "DescuentoPct"));
                var surcharge = Reduction(T(d, "RecargoMonto"), T(d, "RecargoPct"));
                rows.Add(new[]
                {
                    T(d, "NroLinDet") ?? "", codes, T(d, "NmbItem") ?? "", T(d, "DscItem") ?? "", T(d, "QtyItem") ?? "", T(d, "UnmdItem") ?? "", T(d, "PrcItem") ?? "",
                    discount ?? "", surcharge ?? "", Coded(receipt ? ReceiptExempt : Exempt, T(d, "IndExe")) ?? "", T(d, "MontoItem") ?? "",
                });
            }
            XmlUbl.Heading(sb, level, w.W("Items", "Detalle"));
            // a column that no line fills (the codes, the discounts, the exemption flag) is left out, so that a simple invoice reads as a simple table
            var keep = Enumerable.Range(0, header.Length).Where(c => c < 3 || c == header.Length - 1 || rows.Any(r => r[c].Length > 0)).ToList();
            var table = new List<IReadOnlyList<string>> { keep.Select(c => header[c]).ToList() };
            foreach (var r in rows) table.Add(keep.Select(c => r[c]).ToList());
            XmlUbl.Table(sb, table);
        }

        // The amount of a discount or surcharge of a line, and its percentage when the file gives it too: "8000 (10 %)".
        private static string Reduction(string amount, string percent) =>
            amount == null ? (percent == null ? null : percent + " %") : percent == null ? amount : amount + " (" + percent + " %)";

        // Discounts and surcharges of the whole document.
        private static void Global(StringBuilder sb, XElement body, Words w, int level)
        {
            var globals = XmlUbl.Children(body, "DscRcgGlobal").ToList();
            if (globals.Count == 0) return;
            XmlUbl.Heading(sb, level, w.W("Discounts and surcharges of the document", "Descuentos y recargos del documento"));
            var rows = new List<IReadOnlyList<string>> { new[] { "#", w.W("Movement", "Movimiento"), w.W("Description", "Glosa"), w.W("Value", "Valor"), w.W("Exemption", "Exención") } };
            foreach (var g in globals)
            {
                var movement = T(g, "TpoMov");
                var unit = T(g, "TpoValor");
                rows.Add(new[]
                {
                    T(g, "NroLinDR") ?? "", movement == "D" ? w.W("Discount", "Descuento") : movement == "R" ? w.W("Surcharge", "Recargo") : movement ?? "", T(g, "GlosaDR") ?? "",
                    (T(g, "ValorDR") ?? "") + (unit == "%" ? " %" : ""), Coded(GlobalExempt, T(g, "IndExeDR")) ?? "",
                });
            }
            XmlUbl.Table(sb, rows);
        }

        private static void Totals(StringBuilder sb, XElement totals, Words w, int level)
        {
            if (totals == null) return;
            var facts = new List<(string, string)>();
            Add(facts, w.W("Net amount", "Monto neto"), T(totals, "MntNeto"));
            Add(facts, w.W("Exempt amount", "Monto exento"), T(totals, "MntExe"));
            Add(facts, w.W("VAT rate", "Tasa de IVA"), T(totals, "TasaIVA") is { } rate ? rate + " %" : null);
            Add(facts, w.W("VAT", "IVA"), T(totals, "IVA"));
            foreach (var tax in XmlUbl.Children(totals, "ImptoReten"))
            {
                var code = T(tax, "TipoImp");
                var rate2 = T(tax, "TasaImp");
                Add(facts, w.W("Additional tax or withholding", "Impuesto o retención adicional") + " " + Coded(AdditionalTaxes, code), (rate2 == null ? "" : rate2 + " % → ") + T(tax, "MontoImp"));
            }
            Add(facts, w.W("VAT not withheld", "IVA no retenido"), T(totals, "IVANoRet"));
            Add(facts, w.W("Special credit of construction companies", "Crédito especial empresas constructoras"), T(totals, "CredEC"));
            Add(facts, w.W("Guarantee for deposit of containers", "Garantía por depósito de envases"), T(totals, "GrntDep"));
            if (XmlUbl.Child(totals, "Comisiones") is { } commissions)
            {
                Add(facts, w.W("Commissions, net", "Comisiones, neto"), T(commissions, "ValComNeto"));
                Add(facts, w.W("Commissions, exempt", "Comisiones, exento"), T(commissions, "ValComExe"));
                Add(facts, w.W("Commissions, VAT", "Comisiones, IVA"), T(commissions, "ValComIVA"));
            }
            Add(facts, w.W("Total", "Total"), T(totals, "MntTotal"));
            Add(facts, w.W("Amount not billable", "Monto no facturable"), T(totals, "MontoNF"));
            Add(facts, w.W("Total of the period", "Total del período"), T(totals, "MontoPeriodo") ?? T(totals, "TotalPeriodo"));
            Add(facts, w.W("Previous balance", "Saldo anterior"), T(totals, "SaldoAnterior"));
            Add(facts, w.W("Amount to pay", "Valor a pagar"), T(totals, "VlrPagar"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, level, w.W("Totals", "Totales"));
            XmlUbl.Facts(sb, facts);
        }

        private static void References(StringBuilder sb, XElement body, Words w, int level, bool receipt)
        {
            var references = XmlUbl.Children(body, "Referencia").ToList();
            if (references.Count == 0) return;
            XmlUbl.Heading(sb, level, w.W("Referenced documents", "Documentos referenciados"));
            var rows = new List<IReadOnlyList<string>> { new[] { "#", w.W("Document type", "Tipo de documento"), "Folio", w.W("Date", "Fecha"), w.W("Use", "Uso"), w.W("Reason", "Razón") } };
            foreach (var r in references)
            {
                var code = T(r, "CodRef");
                rows.Add(new[]
                {
                    T(r, "NroLinRef") ?? "", Coded(ReferenceTypes, T(r, "TpoDocRef")) ?? "", T(r, "FolioRef") ?? "", T(r, "FchRef") ?? "",
                    (receipt ? code : Coded(ReferenceUses, code)) ?? "", T(r, "RazonRef") ?? "",
                });
            }
            XmlUbl.Table(sb, rows);
        }

        // The commissions and other charges of a liquidation invoice.
        private static void Commissions(StringBuilder sb, XElement body, Words w, int level)
        {
            var commissions = XmlUbl.Children(body, "Comisiones").ToList();
            if (commissions.Count == 0) return;
            XmlUbl.Heading(sb, level, w.W("Commissions and other charges", "Comisiones y otros cargos"));
            var rows = new List<IReadOnlyList<string>> { new[] { "#", w.W("Kind", "Tipo"), w.W("Description", "Glosa"), w.W("Rate", "Tasa"), w.W("Net", "Neto"), w.W("Exempt", "Exento"), w.W("VAT", "IVA") } };
            foreach (var c in commissions)
            {
                var kind = T(c, "TipoMovim");
                rows.Add(new[]
                {
                    T(c, "NroLinCom") ?? "", kind == "C" ? w.W("Commission", "Comisión") : kind == "O" ? w.W("Other charge", "Otro cargo") : kind ?? "", T(c, "Glosa") ?? "",
                    T(c, "TasaComision") is { } rate ? rate + " %" : "", T(c, "ValComNeto") ?? "", T(c, "ValComExe") ?? "", T(c, "ValComIVA") ?? "",
                });
            }
            XmlUbl.Table(sb, rows);
        }

        // The electronic stamp (TED): what it says in the clear, and when the document was signed. The signatures are named, not printed.
        private static void Stamp(StringBuilder sb, XElement body, Words w, int level)
        {
            var ted = XmlUbl.Child(body, "TED");
            var signed = T(body, "TmstFirma");
            if (ted == null && signed == null) return;
            var facts = new List<(string, string)>();
            if (ted != null)
            {
                var dd = XmlUbl.Child(ted, "DD");
                Add(facts, w.W("Stamp issued at", "Timbre generado el"), T(dd, "TSTED"));
                var authorization = XmlUbl.Child(dd, "CAF/DA");
                if (authorization != null)
                {
                    var from = T(authorization, "RNG/D");
                    var to = T(authorization, "RNG/H");
                    Add(facts, w.W("Folios authorized", "Folios autorizados"), from == null && to == null ? null : (from ?? "…") + " — " + (to ?? "…"));
                    Add(facts, w.W("Folios authorized on", "Folios autorizados el"), T(authorization, "FA"));
                }
                // each signature is named only if the file has it
                if (XmlUbl.Child(ted, "FRMT") != null)
                    Add(facts, w.W("Signature of the stamp", "Firma del timbre"), w.W("digital signature (not printed)", "firma digital (no se imprime)"));
                if (XmlUbl.Child(dd, "CAF/FRMA") != null)
                    Add(facts, w.W("Signature of the SII over the folios", "Firma del SII sobre los folios"), w.W("digital signature (not printed)", "firma digital (no se imprime)"));
            }
            Add(facts, w.W("Document signed at", "Documento firmado el"), signed);
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, level, w.W("Electronic stamp", "Timbre electrónico"));
            XmlUbl.Facts(sb, facts);
        }

        // ---- the documents as rows of a table of invoices (InvoiceTable) --------------------------------------------------------------

        // One row per document of the file: an envelope has up to 2000. Dispatch notes (52) are returned with their type: the caller lists them
        // apart, as they are not invoices.
        internal static List<InvoiceRow> ReadRows(XElement root, string file)
        {
            var rows = new List<InvoiceRow>();
            foreach (var dte in Documents(root))
            {
                var body = Body(dte);
                if (body == null) continue;
                var head = XmlUbl.Child(body, "Encabezado");
                var id = XmlUbl.Child(head, "IdDoc");
                var issuer = XmlUbl.Child(head, "Emisor");
                var receiver = XmlUbl.Child(head, "Receptor");
                var totals = XmlUbl.Child(head, "Totales");
                var type = T(id, "TipoDTE");
                var folio = T(id, "Folio");
                var rut = T(issuer, "RUTEmisor");
                rows.Add(new InvoiceRow
                {
                    File = file,
                    Kind = "Dte",
                    TypeCode = type,
                    Number = folio,
                    Date = T(id, "FchEmis"),
                    Issuer = T(issuer, "RznSoc") ?? T(issuer, "RznSocEmisor"),
                    IssuerId = rut,
                    Customer = T(receiver, "RznSocRecep"),
                    CustomerId = T(receiver, "RUTRecep"),
                    // the amounts of a domestic document are in pesos (the schema says so); an export invoice says its currency
                    // (the customs table calls the Chilean peso "PESO CL": it is the same currency as CLP, so it is one group in the totals; the document
                    // itself still says what it says)
                    Currency = type is "110" or "111" or "112" ? (T(totals, "TpoMoneda") == "PESO CL" ? "CLP" : T(totals, "TpoMoneda")) : "CLP",
                    // a Chilean document has no unique code of its own in the clear: the RUT of the issuer, the type and the folio are what the SII
                    // identifies it by (the folios are authorized per issuer and type), so that is the code, and a copy of the file is counted once
                    Cuf = rut == null || type == null || folio == null ? null : rut + "-" + type + "-" + folio,
                    // before the taxes: the net amount (taxed) and the exempt amount, as the tax-exclusive amount of the other readers has both
                    Base = InvoiceTable.Sum(new[] { XmlUbl.Child(totals, "MntNeto"), XmlUbl.Child(totals, "MntExe") }),
                    // the VAT and the additional taxes that the schema names (a specific tax, an advance of VAT) are the taxes; what it names as a
                    // withholding ("IVA Retenido ...") is the withholdings; a code it does not name is in the total and in the summary, in no column
                    Taxes = InvoiceTable.Sum(new[] { XmlUbl.Child(totals, "IVA") }.Concat(Additional(totals, withheld: false))),
                    Withholdings = InvoiceTable.Sum(Additional(totals, withheld: true)),
                    Total = Amount(T(totals, "MntTotal")),
                    Payable = Amount(T(totals, "VlrPagar")),
                });
            }
            return rows;
        }

        // The MontoImp of the additional taxes and withholdings (ImptoReten) of a document that the schema names, the withheld ones or the others.
        private static IEnumerable<XElement> Additional(XElement totals, bool withheld) =>
            totals == null ? Enumerable.Empty<XElement>()
                : XmlUbl.Children(totals, "ImptoReten")
                    .Where(t => T(t, "TipoImp") is { } code && AdditionalTaxes.TryGetValue(code, out var name) && name.StartsWith("IVA Retenido", StringComparison.Ordinal) == withheld)
                    .Select(t => XmlUbl.Child(t, "MontoImp"));

        // The credit notes (61, and 112 of an export) are subtracted in the totals.
        internal static bool IsCreditNote(string typeCode) => typeCode is "61" or "112";

        // The dispatch note (52) moves goods and is not an invoice: its amounts must not be added to the totals of the invoices.
        internal static bool IsDispatchNote(string typeCode) => typeCode == "52";

        internal static string RowType(string typeCode, bool spanish) =>
            typeCode != null && DocumentTypes.TryGetValue(typeCode, out var name) ? (spanish ? name.Es : name.En) : null;

        private static InvoiceAmount Amount(string text) => new InvoiceAmount
        {
            Text = text ?? "",
            Value = decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null,
        };

        // ---- small helpers -----------------------------------------------------------------------------------------------------------

        private static string T(XElement parent, string path) => parent == null ? null : XmlUbl.Text(parent, path);

        private static void Add(List<(string, string)> facts, string label, string value)
        {
            if (!string.IsNullOrEmpty(value)) facts.Add((label, value));
        }

        // "33 — Factura Electrónica": the code and the name the SII gives it; the code alone when it is not in the list.
        private static string Coded(IReadOnlyDictionary<string, string> table, string code) =>
            code == null ? null : table.TryGetValue(code, out var name) ? code + " — " + name : code;

        private sealed class Words
        {
            public readonly bool Spanish;
            public Words(bool spanish) => Spanish = spanish;
            public string W(string en, string es) => Spanish ? es : en;
        }

        // ---- codes of the schemas and of the list of document codes of the SII ----------------------------------------------------------

        // The titles are in the language of the interface; the code in the summary is in the SII's words (Spanish).
        private static readonly Dictionary<string, (string En, string Es)> DocumentTypes = new()
        {
            ["33"] = ("Electronic invoice", "Factura Electrónica"),
            ["34"] = ("Electronic invoice, not subject to VAT or exempt", "Factura No Afecta o Exenta Electrónica"),
            ["39"] = ("Electronic receipt (boleta)", "Boleta Electrónica"),
            ["41"] = ("Electronic receipt (boleta), exempt", "Boleta Exenta Electrónica"),
            ["43"] = ("Electronic liquidation invoice", "Liquidación-Factura Electrónica"),
            ["46"] = ("Electronic purchase invoice", "Factura de Compra Electrónica"),
            ["52"] = ("Electronic dispatch note (guía de despacho)", "Guía de Despacho Electrónica"),
            ["56"] = ("Electronic debit note", "Nota de Débito Electrónica"),
            ["61"] = ("Electronic credit note", "Nota de Crédito Electrónica"),
            ["110"] = ("Electronic export invoice", "Factura de Exportación Electrónica"),
            ["111"] = ("Electronic export debit note", "Nota de Débito de Exportación Electrónica"),
            ["112"] = ("Electronic export credit note", "Nota de Crédito de Exportación Electrónica"),
        };

        private static readonly IReadOnlyDictionary<string, string> DocumentNames = DocumentTypes.ToDictionary(p => p.Key, p => p.Value.Es);

        // The document types that a reference can name: the electronic ones, and the paper ones of the SII's list of document codes (30, 32, 40, 45, 55, 60).
        private static readonly IReadOnlyDictionary<string, string> ReferenceTypes = new Dictionary<string, string>
        {
            ["30"] = "Factura", ["32"] = "Factura de ventas y servicios no afectos o exentos de IVA", ["33"] = "Factura Electrónica", ["34"] = "Factura No Afecta o Exenta Electrónica",
            ["39"] = "Boleta Electrónica", ["40"] = "Liquidación Factura", ["41"] = "Boleta Exenta Electrónica", ["43"] = "Liquidación-Factura Electrónica", ["45"] = "Factura de Compra",
            ["46"] = "Factura de Compra Electrónica", ["52"] = "Guía de Despacho Electrónica", ["55"] = "Nota de Débito", ["56"] = "Nota de Débito Electrónica", ["60"] = "Nota de Crédito",
            ["61"] = "Nota de Crédito Electrónica", ["110"] = "Factura de Exportación Electrónica", ["111"] = "Nota de Débito de Exportación Electrónica", ["112"] = "Nota de Crédito de Exportación Electrónica",
        };

        private static readonly IReadOnlyDictionary<string, string> ReferenceUses = new Dictionary<string, string>
        {
            ["1"] = "Anula Documento de Referencia", ["2"] = "Corrige Texto del Documento de Referencia", ["3"] = "Corrige Montos",
        };

        private static readonly IReadOnlyDictionary<string, string> PaymentForms = new Dictionary<string, string> { ["1"] = "Pago Contado", ["2"] = "Pago Crédito", ["3"] = "Sin Costo" };

        private static readonly IReadOnlyDictionary<string, string> PaymentMeans = new Dictionary<string, string>
        {
            ["CH"] = "Cheque", ["LT"] = "Letra", ["EF"] = "Efectivo", ["PE"] = "Pago a Cuenta Corriente", ["TC"] = "Tarjeta de Crédito", ["CF"] = "Cheque a Fecha", ["OT"] = "Otro",
        };

        private static readonly IReadOnlyDictionary<string, string> Services = new Dictionary<string, string>
        {
            ["1"] = "Facturación de Servicios Periódicos Domiciliarios", ["2"] = "Facturación de Otros Servicios Periódicos", ["3"] = "Factura de Servicio",
        };

        private static readonly IReadOnlyDictionary<string, string> ReceiptServices = new Dictionary<string, string>
        {
            ["1"] = "Boleta de Servicios Periódicos", ["2"] = "Boleta de Servicios Periódicos Domiciliarios", ["3"] = "Boleta de Ventas y Servicio", ["4"] = "Boleta de Espectáculo emitida por cuenta de Terceros",
        };

        private static readonly IReadOnlyDictionary<string, string> Transfers = new Dictionary<string, string>
        {
            ["1"] = "Operación Constituye Venta", ["2"] = "Venta por Efectuar", ["3"] = "Consignación", ["4"] = "Promoción o Donación (RUT Emisor = RUT Receptor)", ["5"] = "Traslado Interno",
            ["6"] = "Otros Traslados que no Constituyen Venta", ["7"] = "Guía de Devolución",
        };

        private static readonly IReadOnlyDictionary<string, string> Dispatches = new Dictionary<string, string>
        {
            ["1"] = "Despacho por Cuenta del Comprador", ["2"] = "Despacho por Cuenta del Emisor a Instalaciones del Comprador", ["3"] = "Despacho por Cuenta del Emisor a Otras Instalaciones",
        };

        private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
        {
            ["1"] = "El Producto o Servicio NO ESTÁ Afecto a IVA", ["2"] = "El Producto o Servicio NO ES Facturable", ["3"] = "Garantía por Depósito/Envase", ["4"] = "El Producto No Constituye Venta",
            ["5"] = "Ítem a Rebajar", ["6"] = "No facturables negativos",
        };

        private static readonly IReadOnlyDictionary<string, string> ReceiptExempt = new Dictionary<string, string>
        {
            ["1"] = "Producto o Servicio es Exento o No Afecto", ["2"] = "El Producto o Servicio NO ES Facturable",
        };

        private static readonly IReadOnlyDictionary<string, string> GlobalExempt = new Dictionary<string, string>
        {
            ["1"] = "Descuento/Recargo Global No Afecto", ["2"] = "Descuento/Recargo No Facturable",
        };

        // Additional taxes and withholdings (ImpAdicDTEType of SiiTypes_v10.xsd), as the schema names them; a code the schema does not name is printed as it is.
        private static readonly IReadOnlyDictionary<string, string> AdditionalTaxes = new Dictionary<string, string>
        {
            ["14"] = "IVA Margen Comercializacion (Factura Venta del Contribuyente)",
            ["15"] = "IVA Retenido Total (Factura Compra del Contribuyente)",
            ["16"] = "IVA Retenido Parcial (Factura Compra del Contribuyente)",
            ["17"] = "IVA Anticipado Faenamiento Carne",
            ["18"] = "IVA Anticipado Carne",
            ["19"] = "IVA Anticipado Harina",
            ["23"] = "Impuesto Adicional Productos Art. 37 a) b) c) Oro, Joyas, Pieles",
            ["24"] = "Impuesto Art. 42 a) Licores, Pisco, Destilados",
            ["25"] = "Impuesto Art. 42 c) Vinos",
            ["26"] = "Impuesto Art. 42 c) Cervezas y Bebidas Alcoholicas",
            ["27"] = "Impuesto Art. 42 d) y e) Bebidas Analcoholicas y Minerales",
            ["28"] = "Impuesto Especifico Diesel",
            ["30"] = "IVA Retenido Legumbres",
            ["31"] = "IVA Retenido Silvestres",
            ["32"] = "IVA Retenido Ganado",
            ["33"] = "IVA Retenido Madera",
            ["34"] = "IVA Retenido Trigo",
            ["35"] = "Impuesto Especifico Gasolina",
            ["36"] = "IVA Retenido Arroz",
            ["37"] = "IVA Retenido Hidrobiologicas",
            ["38"] = "IVA Retenido Chatarra",
            ["39"] = "IVA Retenido PPA",
            ["40"] = "IVA Retenido Opcional",
            ["41"] = "IVA Retenido Construccion",
            ["44"] = "Impuesto Adicional Productos Art. 37 e) h) i) l) 1ra Venta (Alfombras, C. Rodantes, Caviar, Armas)",
            ["45"] = "Impuesto Adicional Productos Art. 37 j) 1ra Venta (Pirotecnia)",
            ["271"] = "Bebidas analcohólicas y Minerales con elevado contenido de azúcares."
        };
    }
}
