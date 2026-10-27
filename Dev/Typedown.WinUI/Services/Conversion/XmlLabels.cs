using System;
using System.Collections.Generic;

namespace Typedown.WinUI.Services.Conversion
{
    // The words of the readable view of an XML file, in English and Spanish (the first format pack is Colombia's). Plain strings in code, not
    // resources, because the converters are plain .NET and tested without the app; the language comes in through ConversionOptions.Language
    // (the interface language). Another language falls back to English until it has its own set.
    internal sealed class XmlLabels
    {
        public string File, Namespace, AllFields, AllFieldsNote, SignaturePresent, EmbeddedXml, Characters;
        public string Date, Time, DueDate, Currency, Cuf, Environment, Note;
        public string Issuer, Customer, Name, TaxId, Address, Email, Phone, FiscalResponsibilities;
        public string Lines, Item, Description, Quantity, Unit, UnitPrice, LineTotal, Taxes, Tax, TaxableAmount, Rate, Amount;
        public string Totals, LineExtension, TaxExclusive, TaxInclusive, TotalTaxes, Payable, Discounts, Charges, Prepaid, Withholdings;
        public string PaymentMeans, PaymentCode, PaymentForm, Cash, Credit, Reference, References, RelatedInvoice, Reason;
        public string DianAnswer, Result, Approved, Rejected, Message, Code, Provider, Parts, Container, ContainerNote, Software;
        public string NotWellKnown, Responses, SignatureOf, TooDeep, MoreCharacters;
        // The overview at the top of the generic view (XmlStructure.cs)
        public string Structure, StructureLine, Element, Count, NumberingTitle, NumberingGap, NumberingRepeat;
        public bool IsSpanish;

        public static XmlLabels For(string language) =>
            language != null && language.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? Spanish : English;

        private static readonly XmlLabels English = new()
        {
            File = "File", Namespace = "Namespace", AllFields = "All fields of the file",
            AllFieldsNote = "The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.",
            SignaturePresent = "digital signature (not printed)", EmbeddedXml = "an XML document stored as text", Characters = "characters",
            Date = "Issue date", Time = "Issue time", DueDate = "Due date", Currency = "Currency", Cuf = "Unique code (CUFE / CUDE)", Environment = "Environment", Note = "Notes",
            Issuer = "Issuer", Customer = "Customer", Name = "Name", TaxId = "Tax ID", Address = "Address", Email = "Email", Phone = "Phone", FiscalResponsibilities = "Fiscal responsibilities",
            Lines = "Lines", Item = "#", Description = "Description", Quantity = "Quantity", Unit = "Unit", UnitPrice = "Unit price", LineTotal = "Line total",
            Taxes = "Taxes", Tax = "Tax", TaxableAmount = "Taxable amount", Rate = "Rate", Amount = "Amount",
            Totals = "Totals", LineExtension = "Total before taxes", TaxExclusive = "Taxable base", TaxInclusive = "Total with taxes", TotalTaxes = "Total taxes", Payable = "Total payable",
            Discounts = "Discounts", Charges = "Charges", Prepaid = "Prepaid", Withholdings = "Withholdings",
            PaymentMeans = "Payment", PaymentCode = "Payment means (code)", PaymentForm = "Form of payment", Cash = "Cash", Credit = "Credit", Reference = "Reference", References = "References", RelatedInvoice = "Related invoice", Reason = "Reason",
            DianAnswer = "Answer of the tax authority", Result = "Result", Approved = "Approved", Rejected = "Rejected", Message = "Message", Code = "Code", Provider = "Provider", Parts = "Parts of this file",
            Container = "Container of electronic documents", ContainerNote = "This file wraps an electronic document and the answer of the tax authority in one XML file.", Software = "Software",
            NotWellKnown = "(code not in this reader's list)", Responses = "Answers", SignatureOf = "Signature",
            TooDeep = "(nesting too deep to show; the rest of this branch is not listed)", MoreCharacters = "more characters",
            Structure = "Structure", StructureLine = "{0} elements, up to {1} levels deep. The elements that repeat most:", Element = "Element", Count = "Count",
            NumberingTitle = "Notes on the numbering", NumberingGap = "{0} ({1}): {2} is followed by {3}", NumberingRepeat = "{0} ({1}): {2} is there {3} times",
        };

        private static readonly XmlLabels Spanish = new()
        {
            IsSpanish = true,
            File = "Archivo", Namespace = "Espacio de nombres", AllFields = "Todos los campos del archivo",
            AllFieldsNote = "El resumen de arriba muestra lo que hace falta para leer el documento; esta lista tiene todos los campos del archivo XML, en orden, para que no se oculte nada.",
            SignaturePresent = "firma digital (no se imprime)", EmbeddedXml = "un documento XML guardado como texto", Characters = "caracteres",
            Date = "Fecha de emisión", Time = "Hora de emisión", DueDate = "Fecha de vencimiento", Currency = "Moneda", Cuf = "Código único (CUFE / CUDE)", Environment = "Ambiente", Note = "Notas",
            Issuer = "Emisor", Customer = "Adquirente", Name = "Nombre", TaxId = "Identificación", Address = "Dirección", Email = "Correo electrónico", Phone = "Teléfono", FiscalResponsibilities = "Responsabilidades fiscales",
            Lines = "Detalle", Item = "#", Description = "Descripción", Quantity = "Cantidad", Unit = "Unidad", UnitPrice = "Precio unitario", LineTotal = "Total línea",
            Taxes = "Impuestos", Tax = "Impuesto", TaxableAmount = "Base", Rate = "Tarifa", Amount = "Valor",
            Totals = "Totales", LineExtension = "Total antes de impuestos", TaxExclusive = "Base imponible", TaxInclusive = "Total con impuestos", TotalTaxes = "Total impuestos", Payable = "Total a pagar",
            Discounts = "Descuentos", Charges = "Cargos", Prepaid = "Anticipos", Withholdings = "Retenciones",
            PaymentMeans = "Pago", PaymentCode = "Medio de pago (código)", PaymentForm = "Forma de pago", Cash = "Contado", Credit = "Crédito", Reference = "Referencia", References = "Referencias", RelatedInvoice = "Factura relacionada", Reason = "Motivo",
            DianAnswer = "Respuesta de la DIAN", Result = "Resultado", Approved = "Aprobado", Rejected = "Rechazado", Message = "Mensaje", Code = "Código", Provider = "Proveedor", Parts = "Partes de este archivo",
            Container = "Contenedor de documentos electrónicos", ContainerNote = "Este archivo reúne un documento electrónico y la respuesta de la autoridad tributaria en un solo archivo XML.", Software = "Software",
            NotWellKnown = "(código que este lector no conoce)", Responses = "Respuestas", SignatureOf = "Firma",
            TooDeep = "(anidamiento demasiado profundo para mostrarlo; el resto de esta rama no se lista)", MoreCharacters = "caracteres más",
            Structure = "Estructura", StructureLine = "{0} elementos, hasta {1} niveles de profundidad. Los elementos que más se repiten:", Element = "Elemento", Count = "Cantidad",
            NumberingTitle = "Notas sobre la numeración", NumberingGap = "{0} ({1}): después de {2} viene {3}", NumberingRepeat = "{0} ({1}): {2} está {3} veces",
        };

        // Codes of the DIAN tables that are certain. A code that is not here is shown as it is, never guessed (the full tables are in the DIAN's
        // "caja de herramientas", in Excel; docs/xml-reader-design.md says how they are to be added).
        public static string InvoiceType(string code, bool spanish) => (code, spanish) switch
        {
            ("01", true) => "Factura electrónica de venta",
            ("01", false) => "Electronic sales invoice",
            ("02", true) => "Factura electrónica de exportación",
            ("02", false) => "Electronic export invoice",
            ("03", true) => "Factura electrónica de venta, contingencia del facturador (tipo 03)",
            ("03", false) => "Electronic sales invoice, issuer contingency (type 03)",
            ("04", true) => "Factura electrónica de venta, contingencia de la DIAN (tipo 04)",
            ("04", false) => "Electronic sales invoice, tax authority contingency (type 04)",
            _ => null,
        };

        public static readonly IReadOnlyDictionary<string, string> TaxNamesEs = new Dictionary<string, string>
        {
            ["01"] = "IVA", ["02"] = "IC", ["03"] = "ICA", ["04"] = "INC", ["05"] = "ReteIVA", ["06"] = "ReteFuente", ["07"] = "ReteICA", ["ZZ"] = "Otro tributo",
        };

        public static readonly IReadOnlyDictionary<string, string> IdTypesEs = new Dictionary<string, string>
        {
            ["13"] = "Cédula de ciudadanía", ["22"] = "Cédula de extranjería", ["31"] = "NIT", ["41"] = "Pasaporte", ["42"] = "Documento de identificación extranjero",
        };
    }
}
