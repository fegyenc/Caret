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
        // The table of a folder of invoices (InvoiceTable.cs)
        public string TableTitle, DocType, Number, IssuerTaxId, CustomerTaxId, AuthorityAnswer, TotalRow, TableSummary, TableNote, NotIncluded, NotIncludedNote;
        public string Invoice, InvoiceOfType, CreditNote, DebitNote;
        public string SkipNotInvoice, SkipAnswerOnly, SkipNotWellFormed, SkipTooBig, SkipUnreadable, SkipDuplicate, SkipOtherCfdi, SkipEventLinked, SkipEventOrphan, SkipOtherDte, SkipNfseNotIssued, SkipSaftOther, SkipManifest, VoidNote;
        public bool IsSpanish, IsPortuguese;

        public static XmlLabels For(string language) =>
            language != null && language.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? Spanish
            : language != null && language.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? Portuguese : English;

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
            NumberingTitle = "Notes on the numbering", NumberingGap = "The {1} of {0}: {2} is followed by {3}", NumberingRepeat = "The {1} of {0}: {2} appears {3} times",
            TableTitle = "Invoices table", DocType = "Type", Number = "Number", IssuerTaxId = "Issuer tax ID", CustomerTaxId = "Customer tax ID", AuthorityAnswer = "Answer of the tax authority",
            TotalRow = "Total (credit notes subtracted)", TableSummary = "{0} documents from {1} XML files",
            TableNote = "Every cell is copied from the files: nothing is checked, and the answer of the tax authority is shown as it was written. Totals are per currency, with credit notes subtracted.",
            NotIncluded = "Files not included", NotIncludedNote = "These XML files are not in the table above, and this is why.",
            Invoice = "Invoice", InvoiceOfType = "Invoice (type {0})", CreditNote = "Credit note", DebitNote = "Debit note",
            SkipNotInvoice = "Not an invoice, credit note or debit note that this reader knows", SkipAnswerOnly = "Only the answer of the tax authority, without the document",
            SkipNotWellFormed = "Not well-formed XML (line {0}, position {1})", SkipTooBig = "Too big to be read here (more than 60 MB)", SkipUnreadable = "Could not be opened", SkipDuplicate = "Same unique code as {0}, which is in the table",
            SkipOtherCfdi = "A CFDI of type {0} (transfer, payroll or payment): not an invoice or a credit note, so not added to the totals",
            SkipOtherDte = "A document of type {0} (a dispatch note): it moves goods and is not an invoice, so it is not added to the totals", SkipEventLinked = "An event of an invoice in the table: it is shown in the answer column of that invoice", SkipEventOrphan = "An event of an invoice that is not in this folder",
            SkipNfseNotIssued = "A declaration (DPS) or a request for an event that is not registered: not an issued NFS-e, so it is not added to the totals",
            SkipSaftOther = "Other documents of the SAF-T file ({0}): they are not sales invoices, so they are not in the table",
            SkipManifest = "A manifest of transport (MDF-e) or an event of one: it lists the documents that are carried and is not an invoice, so it is not in the table",
            VoidNote = "Documents with the status A (annulled), F (billed in another document) or R (a summary of documents of another application), and a CT-e that a CT-e of substitution replaces, are listed and are not added to the totals.",
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
            NumberingTitle = "Notas sobre la numeración", NumberingGap = "El {1} de {0}: después de {2} viene {3}", NumberingRepeat = "El {1} de {0}: {2} aparece {3} veces",
            TableTitle = "Tabla de facturas", DocType = "Tipo", Number = "Número", IssuerTaxId = "Identificación del emisor", CustomerTaxId = "Identificación del adquirente", AuthorityAnswer = "Respuesta de la DIAN",
            TotalRow = "Total (notas crédito restadas)", TableSummary = "{0} documentos de {1} archivos XML",
            TableNote = "Cada celda se copia de los archivos: no se verifica nada y la respuesta de la DIAN se muestra tal como fue escrita. Los totales son por moneda, con las notas crédito restadas.",
            NotIncluded = "Archivos no incluidos", NotIncludedNote = "Estos archivos XML no están en la tabla de arriba, y este es el motivo.",
            Invoice = "Factura", InvoiceOfType = "Factura (tipo {0})", CreditNote = "Nota crédito", DebitNote = "Nota débito",
            SkipNotInvoice = "No es una factura, nota crédito o nota débito que este lector conozca", SkipAnswerOnly = "Solo la respuesta de la DIAN, sin el documento",
            SkipNotWellFormed = "XML mal formado (línea {0}, posición {1})", SkipTooBig = "Demasiado grande para leerlo aquí (más de 60 MB)", SkipUnreadable = "No se pudo abrir", SkipDuplicate = "Mismo código único que {0}, que sí está en la tabla",
            SkipOtherCfdi = "Un CFDI de tipo {0} (traslado, nómina o pago): no es una factura ni una nota de crédito, por eso no se suma a los totales",
            SkipOtherDte = "Un documento del tipo {0} (una guía de despacho): mueve mercadería y no es una factura, por eso no se suma a los totales", SkipEventLinked = "Un evento de una factura de la tabla: se muestra en la columna de respuesta de esa factura", SkipEventOrphan = "Un evento de una factura que no está en esta carpeta",
            SkipNfseNotIssued = "Una declaración (DPS) o una solicitud de evento sin registrar: no es una NFS-e emitida, por eso no se suma a los totales",
            SkipSaftOther = "Otros documentos del archivo SAF-T ({0}): no son facturas de venta, por eso no están en la tabla",
            SkipManifest = "Un manifiesto de transporte (MDF-e) o un evento suyo: lista los documentos que se transportan y no es una factura, por eso no está en la tabla",
            VoidNote = "Los documentos con estado A (anulado), F (facturado en otro documento) o R (resumen de documentos de otra aplicación), y un CT-e que sustituye otro CT-e, se listan y no se suman a los totales.",
        };

        private static readonly XmlLabels Portuguese = new()
        {
            IsPortuguese = true,
            File = "Arquivo", Namespace = "Espaço de nomes", AllFields = "Todos os campos do arquivo",
            AllFieldsNote = "O resumo acima mostra o que é preciso para ler o documento; esta lista tem todos os campos do arquivo XML, na ordem, para que nada fique escondido.",
            SignaturePresent = "assinatura digital (não é impressa)", EmbeddedXml = "um documento XML guardado como texto", Characters = "caracteres",
            Date = "Data de emissão", Time = "Hora de emissão", DueDate = "Data de vencimento", Currency = "Moeda", Cuf = "Código único (CUFE / CUDE)", Environment = "Ambiente", Note = "Observações",
            Issuer = "Emitente", Customer = "Cliente", Name = "Nome", TaxId = "Identificação fiscal", Address = "Endereço", Email = "E-mail", Phone = "Telefone", FiscalResponsibilities = "Responsabilidades fiscais",
            Lines = "Itens", Item = "#", Description = "Descrição", Quantity = "Quantidade", Unit = "Unidade", UnitPrice = "Valor unitário", LineTotal = "Total do item",
            Taxes = "Impostos", Tax = "Imposto", TaxableAmount = "Base", Rate = "Alíquota", Amount = "Valor",
            Totals = "Totais", LineExtension = "Total antes dos impostos", TaxExclusive = "Base tributável", TaxInclusive = "Total com impostos", TotalTaxes = "Total de impostos", Payable = "Total a pagar",
            Discounts = "Descontos", Charges = "Encargos", Prepaid = "Adiantamentos", Withholdings = "Retenções",
            PaymentMeans = "Pagamento", PaymentCode = "Meio de pagamento (código)", PaymentForm = "Forma de pagamento", Cash = "À vista", Credit = "A prazo", Reference = "Referência", References = "Referências", RelatedInvoice = "Fatura relacionada", Reason = "Motivo",
            DianAnswer = "Resposta da autoridade fiscal", Result = "Resultado", Approved = "Aprovado", Rejected = "Rejeitado", Message = "Mensagem", Code = "Código", Provider = "Provedor", Parts = "Partes deste arquivo",
            Container = "Contêiner de documentos eletrônicos", ContainerNote = "Este arquivo reúne um documento eletrônico e a resposta da autoridade fiscal em um único arquivo XML.", Software = "Software",
            NotWellKnown = "(código que este leitor não conhece)", Responses = "Respostas", SignatureOf = "Assinatura",
            TooDeep = "(aninhamento profundo demais para mostrar; o restante deste ramo não é listado)", MoreCharacters = "caracteres a mais",
            Structure = "Estrutura", StructureLine = "{0} elementos, até {1} níveis de profundidade. Os elementos que mais se repetem:", Element = "Elemento", Count = "Quantidade",
            NumberingTitle = "Notas sobre a numeração", NumberingGap = "O {1} de {0}: depois de {2} vem {3}", NumberingRepeat = "O {1} de {0}: {2} aparece {3} vezes",
            TableTitle = "Tabela de notas fiscais", DocType = "Tipo", Number = "Número", IssuerTaxId = "Identificação fiscal do emitente", CustomerTaxId = "Identificação fiscal do destinatário", AuthorityAnswer = "Resposta da autoridade fiscal",
            TotalRow = "Total (notas de crédito subtraídas)", TableSummary = "{0} documentos de {1} arquivos XML",
            TableNote = "Cada célula é copiada dos arquivos: nada é verificado e a resposta da autoridade fiscal é mostrada como foi escrita. Os totais são por moeda, com as notas de crédito subtraídas.",
            NotIncluded = "Arquivos não incluídos", NotIncludedNote = "Estes arquivos XML não estão na tabela acima, e este é o motivo.",
            Invoice = "Fatura", InvoiceOfType = "Fatura (tipo {0})", CreditNote = "Nota de crédito", DebitNote = "Nota de débito",
            SkipNotInvoice = "Não é uma nota fiscal, fatura, nota de crédito ou nota de débito que este leitor conheça", SkipAnswerOnly = "Apenas a resposta da autoridade fiscal, sem o documento",
            SkipNotWellFormed = "XML malformado (linha {0}, posição {1})", SkipTooBig = "Grande demais para ser lido aqui (mais de 60 MB)", SkipUnreadable = "Não foi possível abrir", SkipDuplicate = "Mesmo código único que {0}, que está na tabela",
            SkipOtherCfdi = "Um CFDI do tipo {0} (traslado, folha de pagamento ou pagamento): não é uma fatura nem uma nota de crédito, por isso não entra nos totais",
            SkipOtherDte = "Um documento do tipo {0} (uma guia de despacho): movimenta mercadorias e não é uma fatura, por isso não entra nos totais", SkipEventLinked = "Um evento de uma nota que está na tabela: aparece na coluna de resposta dessa nota", SkipEventOrphan = "Um evento de uma nota que não está nesta pasta",
            SkipNfseNotIssued = "Uma declaração (DPS) ou um pedido de evento sem registro: não é uma NFS-e emitida, por isso não entra nos totais",
            SkipSaftOther = "Outros documentos do ficheiro SAF-T ({0}): não são faturas de venda, por isso não estão na tabela",
            SkipManifest = "Um manifesto de transporte (MDF-e) ou um evento dele: lista os documentos que são transportados e não é uma fatura, por isso não está na tabela",
            VoidNote = "Os documentos com o estado A (anulado), F (faturado noutro documento) ou R (resumo de documentos de outra aplicação), e um CT-e substituído por um CT-e de substituição, são listados e não entram nos totais.",
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
