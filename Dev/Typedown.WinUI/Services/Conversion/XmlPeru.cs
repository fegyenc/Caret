using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // Peru's electronic invoice (SUNAT, UBL 2.1): the Invoice, CreditNote and DebitNote documents are the same UBL documents the Colombian pack reads, so
    // the structure is read by XmlUbl; what is Peruvian is the meaning of some attributes and the codes. The main difference: the attribute schemeID of
    // a party's ID is the TYPE of the identity document (catalog 06: 6 is the RUC, 1 the DNI), not a check digit as in Colombia, so it must not be
    // printed as "20100113612-6".
    //
    // The code tables are those of the annex of Resolución de Superintendencia 245-2017/SUNAT (catalogs 01, 05, 06, 07, 09, 10, 51 and 52), read from
    // the document, as it is on the SUNAT site; a code that is not here is printed as it is, never guessed.
    //
    // A document is Peruvian when the file says so in its header or about its issuer: a list or scheme attribute with the agency "PE:SUNAT" or a
    // URI of "urn:pe:gob:sunat:..." (every code in the guide of SUNAT has them) on the type of operation, the type of document, the currency, the
    // reason of a note, the document a note corrects, or the ID of the issuer. The customer is not evidence (a Colombian invoice can name a
    // Peruvian customer, and it is still a Colombian invoice). A file without these marks is read like any other UBL file.
    internal static class XmlPeru
    {
        public static bool IsPeruvian(XElement root)
        {
            foreach (var header in new[] { "ProfileID", "InvoiceTypeCode", "DocumentCurrencyCode" })
                if (Marked(XmlUbl.Child(root, header))) return true;
            foreach (var response in XmlUbl.Children(root, "DiscrepancyResponse"))
                if (Marked(XmlUbl.Child(response, "ResponseCode"))) return true;
            foreach (var billing in XmlUbl.Children(root, "BillingReference"))
                if (Marked(XmlUbl.Child(billing, "InvoiceDocumentReference/DocumentTypeCode"))) return true;
            var issuer = XmlUbl.Child(root, "AccountingSupplierParty/Party");
            if (issuer != null)
                foreach (var path in new[] { "PartyIdentification/ID", "PartyTaxScheme/CompanyID", "PartyLegalEntity/CompanyID" })
                    if (Marked(XmlUbl.Child(issuer, path))) return true;
            return false;
        }

        private static bool Marked(XElement element)
        {
            if (element == null) return false;
            foreach (var attribute in element.Attributes())
            {
                var value = attribute.Value.Trim();
                if (value == "PE:SUNAT" || value.StartsWith("urn:pe:gob:sunat", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // The type of identity document of a party (catalog 06) is the schemeID of its ID.
        public static string IdType(XElement id) => id?.Attribute("schemeID")?.Value.Trim();

        // ---- the words of the view, which the generic labels do not have --------------------------------------------------------------------

        public static string OperationLabel(bool spanish) => spanish ? "Tipo de operación" : "Type of operation";
        public static string TaxTreatmentLabel(bool spanish) => spanish ? "Afectación del IGV" : "IGV treatment";
        public static string LegendLabel(bool spanish) => spanish ? "Leyenda" : "Legend";

        // ---- the tables -----------------------------------------------------------------------------------------------------------------

        private static string Pick(Dictionary<string, (string Es, string En)> table, string code, bool spanish) =>
            code != null && table.TryGetValue(code.Trim(), out var name) ? (spanish ? name.Es : name.En) : null;

        // Catalog 01, the kinds of document an Invoice carries (01, 03) and those a note refers to (07, 08).
        private static readonly Dictionary<string, (string Es, string En)> DocumentTypes = new()
        {
            ["01"] = ("Factura electrónica", "Electronic invoice"),
            ["03"] = ("Boleta de venta electrónica", "Electronic sales receipt (boleta de venta)"),
            ["07"] = ("Nota de crédito electrónica", "Electronic credit note"),
            ["08"] = ("Nota de débito electrónica", "Electronic debit note"),
        };

        public static string DocumentType(string code, bool spanish) => Pick(DocumentTypes, code, spanish);

        // Catalog 06, the types of identity document.
        private static readonly Dictionary<string, (string Es, string En)> IdTypes = new()
        {
            ["0"] = ("Documento tributario no domiciliado sin RUC", "Tax document of a non-resident without RUC"),
            ["1"] = ("DNI", "DNI"),
            ["4"] = ("Carnet de extranjería", "Foreigner's card (carnet de extranjería)"),
            ["6"] = ("RUC", "RUC"),
            ["7"] = ("Pasaporte", "Passport"),
            ["A"] = ("Cédula diplomática de identidad", "Diplomatic identity card"),
            ["B"] = ("Documento de identidad del país de residencia (no domiciliado)", "Identity document of the country of residence (non-resident)"),
            ["C"] = ("TIN, documento tributario de persona natural", "TIN, tax document of a natural person"),
            ["D"] = ("IN, documento tributario de persona jurídica", "IN, tax document of a legal person"),
        };

        public static string IdTypeName(string code, bool spanish) => Pick(IdTypes, code, spanish);

        // Catalog 05, the types of tax.
        private static readonly Dictionary<string, (string Es, string En)> TaxTypes = new()
        {
            ["1000"] = ("IGV (impuesto general a las ventas)", "IGV (general sales tax)"),
            ["2000"] = ("ISC (impuesto selectivo al consumo)", "ISC (selective consumption tax)"),
            ["9995"] = ("Exportación", "Export"),
            ["9996"] = ("Gratuito", "Free of charge"),
            ["9997"] = ("Exonerado", "Exonerated"),
            ["9998"] = ("Inafecto", "Not subject"),
            ["9999"] = ("Otros conceptos de pago", "Other payment concepts"),
        };

        public static string TaxType(string code, bool spanish) => Pick(TaxTypes, code, spanish);

        // Catalog 07, the kinds of affectation of the IGV (a line).
        private static readonly Dictionary<string, (string Es, string En)> Affectations = new()
        {
            ["10"] = ("Gravado - operación onerosa", "Taxed - onerous operation"),
            ["11"] = ("Gravado - retiro por premio", "Taxed - withdrawal as a prize"),
            ["12"] = ("Gravado - retiro por donación", "Taxed - withdrawal as a donation"),
            ["13"] = ("Gravado - retiro", "Taxed - withdrawal"),
            ["14"] = ("Gravado - retiro por publicidad", "Taxed - withdrawal for advertising"),
            ["15"] = ("Gravado - bonificaciones", "Taxed - bonuses"),
            ["16"] = ("Gravado - retiro por entrega a trabajadores", "Taxed - withdrawal for delivery to workers"),
            ["17"] = ("Gravado - IVAP", "Taxed - IVAP"),
            ["20"] = ("Exonerado - operación onerosa", "Exonerated - onerous operation"),
            ["21"] = ("Exonerado - transferencia gratuita", "Exonerated - free transfer"),
            ["30"] = ("Inafecto - operación onerosa", "Not subject - onerous operation"),
            ["31"] = ("Inafecto - retiro por bonificación", "Not subject - withdrawal as a bonus"),
            ["32"] = ("Inafecto - retiro", "Not subject - withdrawal"),
            ["33"] = ("Inafecto - retiro por muestras médicas", "Not subject - withdrawal for medical samples"),
            ["34"] = ("Inafecto - retiro por convenio colectivo", "Not subject - withdrawal by collective agreement"),
            ["35"] = ("Inafecto - retiro por premio", "Not subject - withdrawal as a prize"),
            ["36"] = ("Inafecto - retiro por publicidad", "Not subject - withdrawal for advertising"),
            ["40"] = ("Exportación", "Export"),
        };

        public static string Affectation(string code, bool spanish) => Pick(Affectations, code, spanish);

        // Catalogs 09 and 10, the reasons of a credit note and of a debit note.
        private static readonly Dictionary<string, (string Es, string En)> CreditNoteReasons = new()
        {
            ["01"] = ("Anulación de la operación", "Cancellation of the operation"),
            ["02"] = ("Anulación por error en el RUC", "Cancellation because of an error in the RUC"),
            ["03"] = ("Corrección por error en la descripción", "Correction of an error in the description"),
            ["04"] = ("Descuento global", "Global discount"),
            ["05"] = ("Descuento por ítem", "Discount per item"),
            ["06"] = ("Devolución total", "Total return"),
            ["07"] = ("Devolución por ítem", "Return per item"),
            ["08"] = ("Bonificación", "Bonus"),
            ["09"] = ("Disminución en el valor", "Decrease in value"),
            ["10"] = ("Otros conceptos", "Other concepts"),
        };

        private static readonly Dictionary<string, (string Es, string En)> DebitNoteReasons = new()
        {
            ["01"] = ("Intereses por mora", "Interest for late payment"),
            ["02"] = ("Aumento en el valor", "Increase in value"),
            ["03"] = ("Penalidades / otros conceptos", "Penalties / other concepts"),
        };

        public static string NoteReason(string kind, string code, bool spanish) =>
            Pick(kind == "DebitNote" ? DebitNoteReasons : CreditNoteReasons, code, spanish);

        // Catalog 51, the types of operation of an invoice (the ProfileID of the file).
        private static readonly Dictionary<string, (string Es, string En)> Operations = new()
        {
            ["0101"] = ("Venta interna", "Domestic sale"),
            ["0102"] = ("Exportación", "Export"),
            ["0103"] = ("No domiciliados", "Non-residents"),
            ["0104"] = ("Venta interna - anticipos", "Domestic sale - advance payments"),
            ["0105"] = ("Venta itinerante", "Itinerant sale"),
            ["0106"] = ("Factura guía", "Invoice with delivery note"),
            ["0107"] = ("Venta arroz pilado", "Sale of milled rice"),
            ["0108"] = ("Factura - comprobante de percepción", "Invoice - perception receipt"),
            ["0110"] = ("Factura - guía remitente", "Invoice - sender's delivery note"),
            ["0111"] = ("Factura - guía transportista", "Invoice - carrier's delivery note"),
        };

        public static string Operation(string code, bool spanish) => Pick(Operations, code, spanish);

        // Catalog 52, the legends (the languageLocaleID of a Note).
        private static readonly Dictionary<string, (string Es, string En)> Legends = new()
        {
            ["1000"] = ("Monto en letras", "Amount in words"),
            ["1002"] = ("Transferencia gratuita de un bien y/o servicio prestado gratuitamente", "Free transfer of a good and/or service provided free of charge"),
            ["2000"] = ("Comprobante de percepción", "Perception receipt"),
            ["2001"] = ("Bienes transferidos en la Amazonía región selva para ser consumidos en la misma", "Goods transferred in the Amazon jungle region to be consumed there"),
            ["2002"] = ("Servicios prestados en la Amazonía región selva para ser consumidos en la misma", "Services provided in the Amazon jungle region to be consumed there"),
            ["2003"] = ("Contratos de construcción ejecutados en la Amazonía región selva", "Construction contracts carried out in the Amazon jungle region"),
            ["2004"] = ("Agencia de viaje - paquete turístico", "Travel agency - tourist package"),
            ["2005"] = ("Venta realizada por emisor itinerante", "Sale made by an itinerant issuer"),
            ["2006"] = ("Operación sujeta a detracción", "Operation subject to detraction"),
            ["3000"] = ("Código interno generado por el software de facturación", "Internal code generated by the invoicing software"),
        };

        public static string Legend(string code, bool spanish) => Pick(Legends, code, spanish);
    }
}
