using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // The second format pack of the XML reader: Mexico's electronic invoice, the CFDI (Comprobante Fiscal Digital por Internet), versions 4.0
    // and 3.3 (the Comprobante element of the SAT's namespace). Read from the SAT's own schemas (cfdv40.xsd, catCFDI.xsd,
    // TimbreFiscalDigitalv11.xsd): the structure and the names of the fields come from there.
    //
    // The same principles as the Colombian pack: a code is explained only when it is certain (the names below are the SAT's own, in Spanish
    // whatever the language of the interface, and a code that is not here is printed as it is: the full catalogs are in the SAT's Excel file
    // catCFDI.xls); amounts and dates are printed as the file has them; nothing is checked or judged (that is the SAT's work, and the stamp
    // of the certification provider is shown, not verified); the digital seals are named, not printed; and the caller adds every field of the
    // file after this summary. The complements (payments, payroll, foreign trade...) are not summarised yet: they are in the list of all
    // fields, and their names are said.
    internal static class XmlCfdi
    {
        private static readonly string[] Namespaces = { "http://www.sat.gob.mx/cfd/4", "http://www.sat.gob.mx/cfd/3" };

        // A Comprobante is a CFDI when its name and its namespace go together (a Comprobante of another namespace is not one).
        internal static bool IsCfdi(XElement root) => root.Name.LocalName == "Comprobante" && Namespaces.Contains(root.Name.NamespaceName);

        // The digital seals and the certificate: Base64 texts that nobody reads; they are named, not printed (also in the list of all fields),
        // whatever their length.
        internal static bool IsSeal(XAttribute attribute) =>
            (attribute.Name.LocalName is "Sello" or "SelloCFD" or "SelloSAT" or "Certificado")
            && attribute.Parent != null && attribute.Parent.Name.NamespaceName.StartsWith("http://www.sat.gob.mx", StringComparison.Ordinal);

        // "digital seal of 180 characters (not printed)"; the certificate is called a certificate.
        internal static string SealText(bool spanish, int length, string attributeName)
        {
            var certificate = attributeName == "Certificado";
            var what = spanish ? (certificate ? "certificado de " : "sello digital de ") : (certificate ? "certificate of " : "digital seal of ");
            return what + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + (spanish ? " caracteres (no se imprime)" : " characters (not printed)");
        }

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            if (!IsCfdi(root)) return false;
            var l = CfdiLabels.For(xl.IsSpanish);
            var type = Attr(root, "TipoDeComprobante");
            var number = Number(root);
            XmlUbl.Heading(sb, 1, l.KindTitle(type) + (number.Length > 0 ? " " + number : ""));
            if (!string.IsNullOrEmpty(context.SourceName)) sb.Append("> ").Append(xl.File).Append(": ").Append(XmlUbl.Safe(context.SourceName)).Append('\n');
            var version = Attr(root, "Version");
            if (version != null) sb.Append("> CFDI ").Append(XmlUbl.Safe(version)).Append('\n');
            sb.Append('\n');

            var facts = new List<(string, string)>();
            Add(facts, l.Date, Attr(root, "Fecha"));
            Add(facts, l.Series, Attr(root, "Serie"));
            Add(facts, l.Folio, Attr(root, "Folio"));
            Add(facts, l.Currency, Attr(root, "Moneda"));
            Add(facts, l.ExchangeRate, Attr(root, "TipoCambio"));
            Add(facts, l.PlaceOfIssue, Attr(root, "LugarExpedicion"));
            Add(facts, l.PaymentForm, Coded(PaymentForms, Attr(root, "FormaPago")));
            Add(facts, l.PaymentMethod, Coded(PaymentMethods, Attr(root, "MetodoPago")));
            Add(facts, l.PaymentTerms, Attr(root, "CondicionesDePago"));
            Add(facts, l.Export, Coded(Exports, Attr(root, "Exportacion")));
            Add(facts, l.Confirmation, Attr(root, "Confirmacion"));
            XmlUbl.Facts(sb, facts);

            var global = XmlUbl.Child(root, "InformacionGlobal");
            if (global != null)
            {
                XmlUbl.Heading(sb, 2, l.GlobalInformation);
                var g = new List<(string, string)>();
                Add(g, l.Periodicity, Coded(Periodicities, Attr(global, "Periodicidad")));
                Add(g, l.Months, Attr(global, "Meses"));
                Add(g, l.Year, Attr(global, "Año"));
                XmlUbl.Facts(sb, g);
            }

            foreach (var related in XmlUbl.Children(root, "CfdiRelacionados"))
            {
                XmlUbl.Heading(sb, 2, l.RelatedCfdi);
                var r = new List<(string, string)> ();
                Add(r, l.RelationType, Coded(RelationTypes, Attr(related, "TipoRelacion")));
                foreach (var uuid in XmlUbl.Children(related, "CfdiRelacionado")) Add(r, l.Uuid, Attr(uuid, "UUID"));
                XmlUbl.Facts(sb, r);
            }

            Party(sb, XmlUbl.Child(root, "Emisor"), l.Issuer, l, issuer: true);
            Party(sb, XmlUbl.Child(root, "Receptor"), l.Receiver, l, issuer: false);
            Lines(sb, root, l);
            TaxSection(sb, XmlUbl.Child(root, "Impuestos"), l);
            Totals(sb, root, l);
            Stamp(sb, root, l);
            Complements(sb, root, l);
            return true;
        }

        // ---- the parts ---------------------------------------------------------------------------------------------------------------

        private static void Party(StringBuilder sb, XElement party, string title, CfdiLabels l, bool issuer)
        {
            if (party == null) return;
            var facts = new List<(string, string)>();
            Add(facts, l.Name, Attr(party, "Nombre"));
            Add(facts, l.Rfc, Attr(party, "Rfc"));
            Add(facts, l.TaxRegime, Coded(TaxRegimes, Attr(party, issuer ? "RegimenFiscal" : "RegimenFiscalReceptor")));
            if (!issuer)
            {
                Add(facts, l.FiscalPostalCode, Attr(party, "DomicilioFiscalReceptor"));
                Add(facts, l.CfdiUse, Coded(CfdiUses, Attr(party, "UsoCFDI")));
                Add(facts, l.TaxResidence, Attr(party, "ResidenciaFiscal"));
                Add(facts, l.ForeignTaxId, Attr(party, "NumRegIdTrib"));
            }
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, title);
            XmlUbl.Facts(sb, facts);
        }

        private static void Lines(StringBuilder sb, XElement root, CfdiLabels l)
        {
            var lines = XmlUbl.Children(XmlUbl.Child(root, "Conceptos") ?? root, "Concepto").ToList();
            if (lines.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Lines);
            var rows = new List<IReadOnlyList<string>> { new[] { "#", l.ProductKey, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.Discount, l.Amount, l.TaxObject } };
            var n = 0;
            foreach (var line in lines)
            {
                n++;
                var unit = string.Join(" · ", new[] { Attr(line, "ClaveUnidad"), Attr(line, "Unidad") }.Where(s => !string.IsNullOrEmpty(s)));
                rows.Add(new[]
                {
                    n.ToString(System.Globalization.CultureInfo.InvariantCulture), Attr(line, "ClaveProdServ") ?? "", Attr(line, "Descripcion") ?? "", Attr(line, "Cantidad") ?? "",
                    unit, Attr(line, "ValorUnitario") ?? "", Attr(line, "Descuento") ?? "", Attr(line, "Importe") ?? "", Attr(line, "ObjetoImp") ?? "",
                });
            }
            XmlUbl.Table(sb, rows);
        }

        private static void TaxSection(StringBuilder sb, XElement taxes, CfdiLabels l)
        {
            if (taxes == null) return;
            var transferred = XmlUbl.Children(XmlUbl.Child(taxes, "Traslados") ?? taxes, "Traslado").ToList();
            var withheld = XmlUbl.Children(XmlUbl.Child(taxes, "Retenciones") ?? taxes, "Retencion").ToList();
            var totals = new List<(string, string)>();
            Add(totals, l.TotalTransferred, Attr(taxes, "TotalImpuestosTrasladados"));
            Add(totals, l.TotalWithheld, Attr(taxes, "TotalImpuestosRetenidos"));
            if (transferred.Count == 0 && withheld.Count == 0 && totals.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Taxes);
            XmlUbl.Facts(sb, totals);
            if (transferred.Count > 0)
            {
                sb.Append("**").Append(l.Transferred).Append("**\n\n");
                var rows = new List<IReadOnlyList<string>> { new[] { l.Tax, l.Base, l.Factor, l.Rate, l.Amount } };
                foreach (var t in transferred)
                    rows.Add(new[] { Coded(Taxes, Attr(t, "Impuesto")) ?? "", Attr(t, "Base") ?? "", Attr(t, "TipoFactor") ?? "", Attr(t, "TasaOCuota") ?? "", Attr(t, "Importe") ?? "" });
                XmlUbl.Table(sb, rows);
            }
            if (withheld.Count > 0)
            {
                sb.Append("**").Append(l.Withheld).Append("**\n\n");
                var rows = new List<IReadOnlyList<string>> { new[] { l.Tax, l.Amount } };
                foreach (var t in withheld) rows.Add(new[] { Coded(Taxes, Attr(t, "Impuesto")) ?? "", Attr(t, "Importe") ?? "" });
                XmlUbl.Table(sb, rows);
            }
        }

        private static void Totals(StringBuilder sb, XElement root, CfdiLabels l)
        {
            var facts = new List<(string, string)>();
            Add(facts, l.Subtotal, Attr(root, "SubTotal"));
            Add(facts, l.Discount, Attr(root, "Descuento"));
            Add(facts, l.Total, Attr(root, "Total"));
            if (facts.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Totals);
            XmlUbl.Facts(sb, facts);
        }

        // The stamp of the certification provider (Timbre Fiscal Digital): what it says, not whether it is right.
        private static void Stamp(StringBuilder sb, XElement root, CfdiLabels l)
        {
            var stamp = XmlUbl.Child(root, "Complemento/TimbreFiscalDigital");
            if (stamp == null) return;
            XmlUbl.Heading(sb, 2, l.Stamp);
            var facts = new List<(string, string)>();
            Add(facts, l.Uuid, Attr(stamp, "UUID"));
            Add(facts, l.StampDate, Attr(stamp, "FechaTimbrado"));
            Add(facts, l.Provider, Attr(stamp, "RfcProvCertif"));
            Add(facts, l.SatCertificate, Attr(stamp, "NoCertificadoSAT"));
            Add(facts, l.Legend, Attr(stamp, "Leyenda"));
            XmlUbl.Facts(sb, facts);
        }

        // The other complements (payments, payroll, foreign trade...) are said by name; their fields are in the list of all fields.
        private static void Complements(StringBuilder sb, XElement root, CfdiLabels l)
        {
            var names = (XmlUbl.Child(root, "Complemento")?.Elements() ?? Enumerable.Empty<XElement>())
                .Where(e => e.Name.LocalName != "TimbreFiscalDigital").Select(e => e.Name.LocalName).Distinct().ToList();
            if (names.Count == 0) return;
            XmlUbl.Heading(sb, 2, l.Complements);
            sb.Append(l.ComplementsNote).Append("\n\n");
            foreach (var name in names) sb.Append("- ").Append(XmlUbl.Safe(name)).Append('\n');
            sb.Append('\n');
        }

        // ---- reading a CFDI as one row of a table of invoices (InvoiceTable) ------------------------------------------------------------

        // The fields a table of invoices needs. Null when the file is not a CFDI. The amounts are as the file has them; the base is the SubTotal
        // less the Descuento when there is one (both are in the file: this is the amount before taxes, after discounts, like the DIAN's).
        internal static InvoiceRow ReadRow(XElement root, string file)
        {
            if (!IsCfdi(root)) return null;
            var stamp = XmlUbl.Child(root, "Complemento/TimbreFiscalDigital");
            var emisor = XmlUbl.Child(root, "Emisor");
            var receptor = XmlUbl.Child(root, "Receptor");
            var taxes = XmlUbl.Child(root, "Impuestos");
            var row = new InvoiceRow
            {
                File = file,
                Kind = "Cfdi",
                TypeCode = Attr(root, "TipoDeComprobante"),
                Number = Number(root),
                Date = Attr(root, "Fecha"),
                Issuer = emisor == null ? null : Attr(emisor, "Nombre"),
                IssuerId = emisor == null ? null : Attr(emisor, "Rfc"),
                Customer = receptor == null ? null : Attr(receptor, "Nombre"),
                CustomerId = receptor == null ? null : Attr(receptor, "Rfc"),
                Currency = Attr(root, "Moneda"),
                Cuf = stamp == null ? null : Attr(stamp, "UUID"),
                Taxes = Amount(taxes == null ? null : Attr(taxes, "TotalImpuestosTrasladados")),
                Withholdings = Amount(taxes == null ? null : Attr(taxes, "TotalImpuestosRetenidos")),
                Total = Amount(Attr(root, "Total")),
                Payable = new InvoiceAmount(),
            };
            var subtotal = Attr(root, "SubTotal");
            var discount = Attr(root, "Descuento");
            row.Base = BeforeTaxes(subtotal, discount);
            return row;
        }

        private static InvoiceAmount Amount(string text) => new InvoiceAmount
        {
            Text = text ?? "",
            Value = decimal.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null,
        };

        private static InvoiceAmount BeforeTaxes(string subtotal, string discount)
        {
            var sub = Amount(subtotal);
            if (string.IsNullOrEmpty(discount)) return sub;
            var off = Amount(discount);
            if (sub.Value == null || off.Value == null) return sub.IsEmpty ? sub : new InvoiceAmount { Text = subtotal + " − " + discount };
            var result = sub.Value.Value - off.Value.Value;
            return new InvoiceAmount { Text = result.ToString(System.Globalization.CultureInfo.InvariantCulture), Value = result };
        }

        // ---- small helpers -----------------------------------------------------------------------------------------------------------

        private static string Number(XElement root) =>
            string.Join(" ", new[] { Attr(root, "Serie"), Attr(root, "Folio") }.Where(s => !string.IsNullOrEmpty(s)));

        private static string Attr(XElement element, string name)
        {
            var value = element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && a.Name.LocalName == name)?.Value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static void Add(List<(string, string)> facts, string label, string value)
        {
            if (!string.IsNullOrEmpty(value)) facts.Add((label, value));
        }

        // "01 — Efectivo": the code and the SAT's name for it when the code is one of those below; the code alone otherwise.
        private static string Coded(IReadOnlyDictionary<string, string> table, string code) =>
            code == null ? null : table.TryGetValue(code, out var name) ? code + " — " + name : code;

        // Codes of the SAT's catalogs (catCFDI) that are certain: the codes were read from the SAT's schema, the names are the catalog's.
        // A code that is not here is shown as it is.
        private static readonly IReadOnlyDictionary<string, string> PaymentForms = new Dictionary<string, string>
        {
            ["01"] = "Efectivo", ["02"] = "Cheque nominativo", ["03"] = "Transferencia electrónica de fondos", ["04"] = "Tarjeta de crédito",
            ["05"] = "Monedero electrónico", ["06"] = "Dinero electrónico", ["08"] = "Vales de despensa", ["12"] = "Dación en pago",
            ["13"] = "Pago por subrogación", ["14"] = "Pago por consignación", ["15"] = "Condonación", ["17"] = "Compensación", ["23"] = "Novación",
            ["24"] = "Confusión", ["25"] = "Remisión de deuda", ["26"] = "Prescripción o caducidad", ["27"] = "A satisfacción del acreedor",
            ["28"] = "Tarjeta de débito", ["29"] = "Tarjeta de servicios", ["30"] = "Aplicación de anticipos", ["31"] = "Intermediario pagos", ["99"] = "Por definir",
        };

        private static readonly IReadOnlyDictionary<string, string> PaymentMethods = new Dictionary<string, string>
        {
            ["PUE"] = "Pago en una sola exhibición", ["PPD"] = "Pago en parcialidades o diferido",
        };

        private static readonly IReadOnlyDictionary<string, string> Exports = new Dictionary<string, string>
        {
            ["01"] = "No aplica", ["02"] = "Definitiva con clave A1", ["03"] = "Temporal",
        };

        private static readonly IReadOnlyDictionary<string, string> Taxes = new Dictionary<string, string>
        {
            ["001"] = "ISR", ["002"] = "IVA", ["003"] = "IEPS",
        };

        private static readonly IReadOnlyDictionary<string, string> Periodicities = new Dictionary<string, string>
        {
            ["01"] = "Diario", ["02"] = "Semanal", ["03"] = "Quincenal", ["04"] = "Mensual", ["05"] = "Bimestral",
        };

        private static readonly IReadOnlyDictionary<string, string> RelationTypes = new Dictionary<string, string>
        {
            ["01"] = "Nota de crédito de los documentos relacionados", ["02"] = "Nota de débito de los documentos relacionados",
            ["03"] = "Devolución de mercancía sobre facturas o traslados previos", ["04"] = "Sustitución de los CFDI previos",
            ["05"] = "Traslados de mercancías facturados previamente", ["06"] = "Factura generada por los traslados previos",
            ["07"] = "CFDI por aplicación de anticipo",
        };

        private static readonly IReadOnlyDictionary<string, string> TaxRegimes = new Dictionary<string, string>
        {
            ["601"] = "General de Ley Personas Morales", ["603"] = "Personas Morales con Fines no Lucrativos",
            ["605"] = "Sueldos y Salarios e Ingresos Asimilados a Salarios", ["606"] = "Arrendamiento",
            ["607"] = "Régimen de Enajenación o Adquisición de Bienes", ["608"] = "Demás ingresos",
            ["610"] = "Residentes en el Extranjero sin Establecimiento Permanente en México", ["611"] = "Ingresos por Dividendos (socios y accionistas)",
            ["612"] = "Personas Físicas con Actividades Empresariales y Profesionales", ["614"] = "Ingresos por intereses",
            ["615"] = "Régimen de los ingresos por obtención de premios", ["616"] = "Sin obligaciones fiscales",
            ["620"] = "Sociedades Cooperativas de Producción que optan por diferir sus ingresos", ["621"] = "Incorporación Fiscal",
            ["622"] = "Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras", ["623"] = "Opcional para Grupos de Sociedades", ["624"] = "Coordinados",
            ["625"] = "Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas", ["626"] = "Régimen Simplificado de Confianza",
        };

        private static readonly IReadOnlyDictionary<string, string> CfdiUses = new Dictionary<string, string>
        {
            ["G01"] = "Adquisición de mercancías", ["G02"] = "Devoluciones, descuentos o bonificaciones", ["G03"] = "Gastos en general",
            ["I01"] = "Construcciones", ["I02"] = "Mobiliario y equipo de oficina por inversiones", ["I03"] = "Equipo de transporte",
            ["I04"] = "Equipo de computo y accesorios", ["I05"] = "Dados, troqueles, moldes, matrices y herramental", ["I06"] = "Comunicaciones telefónicas",
            ["I07"] = "Comunicaciones satelitales", ["I08"] = "Otra maquinaria y equipo",
            ["D01"] = "Honorarios médicos, dentales y gastos hospitalarios", ["D02"] = "Gastos médicos por incapacidad o discapacidad", ["D03"] = "Gastos funerales",
            ["D04"] = "Donativos", ["D05"] = "Intereses reales efectivamente pagados por créditos hipotecarios (casa habitación)",
            ["D06"] = "Aportaciones voluntarias al SAR", ["D07"] = "Primas por seguros de gastos médicos", ["D08"] = "Gastos de transportación escolar obligatoria",
            ["D09"] = "Depósitos en cuentas para el ahorro, primas que tengan como base planes de pensiones", ["D10"] = "Pagos por servicios educativos (colegiaturas)",
            ["S01"] = "Sin efectos fiscales", ["CP01"] = "Pagos",
        };
    }

    // The words of the readable view of a CFDI, in Spanish and English. The names of the SAT's catalogs (above) are the SAT's, in Spanish.
    internal sealed class CfdiLabels
    {
        public string Date, Series, Folio, Currency, ExchangeRate, PlaceOfIssue, PaymentForm, PaymentMethod, PaymentTerms, Export, Confirmation;
        public string GlobalInformation, Periodicity, Months, Year, RelatedCfdi, RelationType, Uuid;
        public string Issuer, Receiver, Name, Rfc, TaxRegime, FiscalPostalCode, CfdiUse, TaxResidence, ForeignTaxId;
        public string Lines, ProductKey, Description, Quantity, Unit, UnitPrice, Discount, Amount, TaxObject;
        public string Taxes, TotalTransferred, TotalWithheld, Transferred, Withheld, Tax, Base, Factor, Rate;
        public string Totals, Subtotal, Total, Stamp, StampDate, Provider, SatCertificate, Legend, Complements, ComplementsNote;
        public string KindIncome, KindExpense, KindTransfer, KindPayroll, KindPayment, KindOther;

        public string KindTitle(string type) => type switch
        {
            "I" => KindIncome, "E" => KindExpense, "T" => KindTransfer, "N" => KindPayroll, "P" => KindPayment,
            _ => type == null ? KindOther : KindOther + " (" + type + ")",
        };

        public static CfdiLabels For(bool spanish) => spanish ? Spanish : English;

        private static readonly CfdiLabels English = new()
        {
            Date = "Issue date", Series = "Series", Folio = "Folio", Currency = "Currency", ExchangeRate = "Exchange rate", PlaceOfIssue = "Place of issue (postal code)",
            PaymentForm = "Form of payment", PaymentMethod = "Method of payment", PaymentTerms = "Payment terms", Export = "Export", Confirmation = "Confirmation code",
            GlobalInformation = "Global information", Periodicity = "Periodicity", Months = "Months", Year = "Year", RelatedCfdi = "Related CFDI", RelationType = "Type of relation", Uuid = "UUID",
            Issuer = "Issuer", Receiver = "Receiver", Name = "Name", Rfc = "RFC", TaxRegime = "Tax regime", FiscalPostalCode = "Fiscal postal code", CfdiUse = "Use of the CFDI",
            TaxResidence = "Country of tax residence", ForeignTaxId = "Foreign tax registration number",
            Lines = "Lines", ProductKey = "Product/service key", Description = "Description", Quantity = "Quantity", Unit = "Unit", UnitPrice = "Unit price", Discount = "Discount", Amount = "Amount", TaxObject = "Tax object",
            Taxes = "Taxes", TotalTransferred = "Total transferred taxes", TotalWithheld = "Total withheld taxes", Transferred = "Transferred", Withheld = "Withheld", Tax = "Tax", Base = "Base", Factor = "Factor", Rate = "Rate",
            Totals = "Totals", Subtotal = "Subtotal", Total = "Total", Stamp = "Digital stamp (Timbre Fiscal Digital)", StampDate = "Stamp date", Provider = "Certification provider (RFC)",
            SatCertificate = "SAT certificate number", Legend = "Legend", Complements = "Complements",
            ComplementsNote = "These complements are in the file; their fields are in the list of all fields below.",
            KindIncome = "CFDI of income (invoice)", KindExpense = "CFDI of expense", KindTransfer = "CFDI of transfer", KindPayroll = "CFDI of payroll", KindPayment = "CFDI of payment", KindOther = "CFDI",
        };

        private static readonly CfdiLabels Spanish = new()
        {
            Date = "Fecha de expedición", Series = "Serie", Folio = "Folio", Currency = "Moneda", ExchangeRate = "Tipo de cambio", PlaceOfIssue = "Lugar de expedición (código postal)",
            PaymentForm = "Forma de pago", PaymentMethod = "Método de pago", PaymentTerms = "Condiciones de pago", Export = "Exportación", Confirmation = "Clave de confirmación",
            GlobalInformation = "Información global", Periodicity = "Periodicidad", Months = "Meses", Year = "Año", RelatedCfdi = "CFDI relacionados", RelationType = "Tipo de relación", Uuid = "UUID",
            Issuer = "Emisor", Receiver = "Receptor", Name = "Nombre", Rfc = "RFC", TaxRegime = "Régimen fiscal", FiscalPostalCode = "Domicilio fiscal (código postal)", CfdiUse = "Uso del CFDI",
            TaxResidence = "Residencia fiscal", ForeignTaxId = "Número de registro de identidad tributaria",
            Lines = "Conceptos", ProductKey = "Clave de producto o servicio", Description = "Descripción", Quantity = "Cantidad", Unit = "Unidad", UnitPrice = "Valor unitario", Discount = "Descuento", Amount = "Importe", TaxObject = "Objeto de impuesto",
            Taxes = "Impuestos", TotalTransferred = "Total de impuestos trasladados", TotalWithheld = "Total de impuestos retenidos", Transferred = "Trasladados", Withheld = "Retenidos", Tax = "Impuesto", Base = "Base", Factor = "Tipo de factor", Rate = "Tasa o cuota",
            Totals = "Totales", Subtotal = "Subtotal", Total = "Total", Stamp = "Timbre fiscal digital", StampDate = "Fecha de timbrado", Provider = "Proveedor de certificación (RFC)",
            SatCertificate = "Número de certificado del SAT", Legend = "Leyenda", Complements = "Complementos",
            ComplementsNote = "Estos complementos están en el archivo; sus campos están en la lista de todos los campos, más abajo.",
            KindIncome = "CFDI de ingreso (factura)", KindExpense = "CFDI de egreso", KindTransfer = "CFDI de traslado", KindPayroll = "CFDI de nómina", KindPayment = "CFDI de pago", KindOther = "CFDI",
        };
    }
}
