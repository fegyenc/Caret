using System;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Chile's electronic tax documents, the DTE of the SII (Services/Conversion/XmlChile.cs), read from the schemas the SII publishes. The samples
    // in samples/ are invented.
    public class ChileTests
    {
        private static string Sample(string name, string language = "en") =>
            DocumentConverter.Convert(Path.Combine(TestPaths.Samples, name), new ConversionOptions { Language = language }).Markdown;

        private static string Read(string xml, string language = "en", Encoding encoding = null)
        {
            var work = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(work, "test.xml");
                File.WriteAllText(path, xml, encoding ?? new UTF8Encoding(false));
                return DocumentConverter.Convert(path, new ConversionOptions { Language = language }).Markdown;
            }
            finally { Directory.Delete(work, true); }
        }

        private const string Ns = "http://www.sii.cl/SiiDte";

        // A bare DTE with the given IdDoc, issuer, receiver and totals in it; the rest as little as the schema needs.
        private static string Dte(string type, string idExtra = "", string totals = "<MntTotal>1190</MntTotal>", string tail = "", string issuerName = "RznSoc") =>
            "<DTE xmlns=\"" + Ns + "\" version=\"1.0\"><Documento ID=\"X\"><Encabezado><IdDoc><TipoDTE>" + type + "</TipoDTE><Folio>5</Folio><FchEmis>2026-03-04</FchEmis>" + idExtra + "</IdDoc>"
            + "<Emisor><RUTEmisor>76000001-1</RUTEmisor><" + issuerName + ">Emisor de prueba</" + issuerName + "></Emisor>"
            + "<Receptor><RUTRecep>76000002-K</RUTRecep><RznSocRecep>Receptor de prueba</RznSocRecep></Receptor>"
            + "<Totales>" + totals + "</Totales></Encabezado>"
            + "<Detalle><NroLinDet>1</NroLinDet><NmbItem>Cosa</NmbItem><MontoItem>1000</MontoItem></Detalle>" + tail + "</Documento></DTE>";

        [Fact]
        public void An_envelope_reads_as_a_summary_of_each_document_with_the_codes_of_the_SII()
        {
            var md = Sample("cl-factura.xml");
            Assert.StartsWith("# Electronic tax documents (EnvioDTE)", md);
            Assert.Contains("- **Issuer RUT:** 76000001-1", md);
            Assert.Contains("- **Documents of type 33:** 1", md);
            Assert.Contains("### Electronic invoice 1234", md);
            Assert.Contains("### Electronic invoice, not subject to VAT or exempt 77", md);
            Assert.Contains("- **Type:** 33 — Factura Electrónica", md);
            Assert.Contains("- **Form of payment:** 2 — Pago Crédito", md);
            Assert.Contains("- **Means of payment:** EF — Efectivo", md);
            Assert.Contains("- **Currency:** CLP (Chilean pesos)", md);
            Assert.Contains("- **RUT:** 76000002-K", md);
            // the additional tax is named as the schema names it, with its rate and amount as the file has them
            Assert.Contains("24 — Impuesto Art. 42 a) Licores, Pisco, Destilados:** 31.5 % → 1000", md);
            // a line with a discount in both forms, and the exemption flag, in columns that exist only because a line uses them
            Assert.Contains("| 1 | INT1 CUA-01 | Cuaderno de muestra |  | 10 | UN | 8000 | 8000 (10 %) |  | 72000 |", md);
            Assert.Contains("1 — El Producto o Servicio NO ESTÁ Afecto a IVA", md);
            Assert.Contains("| 1 |  | Servicio exento de ejemplo | 50000 |", md);
            // a reference type that is not in the list is printed as it is
            Assert.Contains("| 1 | 801 | OC-555 | 2026-03-01 |  |  |", md);
            Assert.Contains("- **Folios authorized:** 1200 — 1300", md);
        }

        [Fact]
        public void The_signatures_and_the_signatures_of_the_stamp_are_named_and_not_printed()
        {
            var md = Sample("cl-factura.xml");
            Assert.DoesNotContain("SAMPLESTAMPSIGNATURE", md);
            Assert.DoesNotContain("SAMPLESIIFOLIOSIGNATURE", md);
            Assert.DoesNotContain("SAMPLEDOCSIGNATURE", md);
            Assert.DoesNotContain("SAMPLEENVELOPESIGNATURE", md);
            Assert.Contains("- **FRMT**: digital signature (not printed)", md);
            Assert.Contains("- **FRMA**: digital signature (not printed)", md);
            Assert.Contains("- **Signature of the stamp:** digital signature (not printed)", md);
            Assert.Contains("- **Signature of the SII over the folios:** digital signature (not printed)", md);
            // a stamp without the authorization of the folios (the credit note sample) has only its own signature, and says only that
            var note = Sample("cl-nota-credito.xml");
            Assert.Contains("- **Signature of the stamp:** digital signature (not printed)", note);
            Assert.DoesNotContain("Signature of the SII", note);
        }

        [Fact]
        public void The_same_envelope_in_Spanish_says_it_in_Spanish()
        {
            var md = Sample("cl-factura.xml", "es");
            Assert.StartsWith("# Documentos tributarios electrónicos (EnvioDTE)", md);
            Assert.Contains("### Factura Electrónica 1234", md);
            Assert.Contains("- **Forma de pago:** 2 — Pago Crédito", md);
            Assert.Contains("- **Moneda:** CLP (pesos chilenos)", md);
            Assert.Contains("#### Timbre electrónico", md);
        }

        [Fact]
        public void A_credit_note_names_the_document_it_corrects_and_the_use_of_the_reference()
        {
            var md = Sample("cl-nota-credito.xml");
            Assert.StartsWith("# Electronic credit note 9", md);
            Assert.Contains("| 1 | 33 — Factura Electrónica | 1234 | 2026-03-04 | 3 — Corrige Montos | Corrige montos de la factura (texto de ejemplo) |", md);
        }

        [Fact]
        public void A_code_that_the_schema_does_not_explain_is_printed_as_it_is()
        {
            var md = Read(Dte("33", "<FmaPago>9</FmaPago>", "<ImptoReten><TipoImp>999</TipoImp><MontoImp>5</MontoImp></ImptoReten><MntTotal>1190</MntTotal>"));
            Assert.Contains("- **Form of payment:** 9", md);
            Assert.DoesNotContain("9 —", md);
            Assert.Contains("Additional tax or withholding 999:** 5", md);
            // an unknown type of document is a document of that type, not a made-up name
            Assert.StartsWith("# Electronic tax document (type 77) 5", Read(Dte("77")));
        }

        [Fact]
        public void A_file_in_ISO_8859_1_keeps_its_accents()
        {
            var xml = Dte("33").Replace("Emisor de prueba", "Peñalolén Ñuñoa Ltda.");
            var md = Read("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + xml, "en", Encoding.GetEncoding("ISO-8859-1"));
            Assert.Contains("- **Name:** Peñalolén Ñuñoa Ltda.", md);
        }

        [Fact]
        public void An_electronic_receipt_is_read_with_the_names_of_its_own_schema()
        {
            // EnvioBOLETA: the issuer's fields have other names (RznSocEmisor, GiroEmisor) and the service indicator other meanings
            var xml = "<EnvioBOLETA xmlns=\"" + Ns + "\" version=\"1.0\"><SetDTE ID=\"S\"><Caratula version=\"1.0\"><RutEmisor>76000001-1</RutEmisor><RutEnvia>11111111-1</RutEnvia><RutReceptor>60803000-K</RutReceptor>"
                + "<FchResol>2026-01-05</FchResol><NroResol>0</NroResol><TmstFirmaEnv>2026-03-04T10:15:30</TmstFirmaEnv><SubTotDTE><TpoDTE>39</TpoDTE><NroDTE>1</NroDTE></SubTotDTE></Caratula>"
                + "<DTE version=\"1.0\"><Documento ID=\"B\"><Encabezado><IdDoc><TipoDTE>39</TipoDTE><Folio>15</Folio><FchEmis>2026-03-04</FchEmis><IndServicio>3</IndServicio></IdDoc>"
                + "<Emisor><RUTEmisor>76000001-1</RUTEmisor><RznSocEmisor>Tienda de prueba</RznSocEmisor><GiroEmisor>Venta al por menor</GiroEmisor></Emisor>"
                + "<Receptor><RUTRecep>66666666-6</RUTRecep></Receptor><Totales><MntNeto>1000</MntNeto><IVA>190</IVA><MntTotal>1190</MntTotal></Totales></Encabezado>"
                + "<Detalle><NroLinDet>1</NroLinDet><NmbItem>Cosa</NmbItem><MontoItem>1190</MontoItem></Detalle></Documento></DTE></SetDTE></EnvioBOLETA>";
            var md = Read(xml);
            Assert.StartsWith("# Electronic receipts (EnvioBOLETA)", md);
            Assert.Contains("### Electronic receipt (boleta) 15", md);
            Assert.Contains("- **Kind of service:** 3 — Boleta de Ventas y Servicio", md);
            Assert.Contains("- **Name:** Tienda de prueba", md);
            Assert.Contains("- **Line of business:** Venta al por menor", md);
        }

        [Fact]
        public void An_export_invoice_says_its_currency_and_a_liquidation_invoice_its_commissions()
        {
            var export = Read(Dte("110", "", "<TpoMoneda>DOLAR USA</TpoMoneda><MntExe>5000</MntExe><MntTotal>5000</MntTotal>"));
            Assert.StartsWith("# Electronic export invoice 5", export);
            Assert.Contains("- **Currency:** DOLAR USA", export);
            Assert.Contains("- **Type:** 110 — Factura de Exportación Electrónica", export);
            var liquidation = Read(Dte("43", "", "<MntNeto>1000</MntNeto><MntTotal>1190</MntTotal>", "<Comisiones><NroLinCom>1</NroLinCom><TipoMovim>C</TipoMovim><Glosa>Comisión de prueba</Glosa><TasaComision>5</TasaComision><ValComNeto>50</ValComNeto><ValComExe>0</ValComExe></Comisiones>").Replace("<Documento", "<Liquidacion").Replace("</Documento>", "</Liquidacion>"));
            Assert.StartsWith("# Electronic liquidation invoice 5", liquidation);
            Assert.Contains("| 1 | Commission | Comisión de prueba | 5 % | 50 | 0 |  |", liquidation);
        }

        [Fact]
        public void A_DTE_outside_the_namespace_of_the_SII_is_not_given_a_fiscal_summary()
        {
            var md = Read("<DTE><Documento><Encabezado><IdDoc><TipoDTE>33</TipoDTE></IdDoc></Encabezado></Documento></DTE>");
            Assert.StartsWith("# DTE", md);
            Assert.DoesNotContain("Factura Electrónica", md);
        }

        [Fact]
        public void The_folder_table_has_a_row_per_document_of_an_envelope_with_a_unique_code_and_subtracts_credit_notes()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.Copy(Path.Combine(TestPaths.Samples, "cl-factura.xml"), Path.Combine(folder, "envio.xml"));
                // a copy of the same envelope, and the credit note
                File.Copy(Path.Combine(TestPaths.Samples, "cl-factura.xml"), Path.Combine(folder, "copy of envio.xml"));
                File.Copy(Path.Combine(TestPaths.Samples, "cl-nota-credito.xml"), Path.Combine(folder, "nc.xml"));
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Equal(3, result.Rows.Count);
                var invoice = result.Rows.Single(r => r.Number == "1234");
                Assert.Equal("76000001-1-33-1234", invoice.Cuf);
                Assert.Equal("76000001-1", invoice.IssuerId);
                Assert.Equal("76000002-K", invoice.CustomerId);
                Assert.Equal("CLP", invoice.Currency);
                Assert.Equal("100000", invoice.Base.Text);
                // the VAT (19000) and the additional tax (24, 1000) are the taxes
                Assert.Equal("20000", invoice.Taxes.Text);
                Assert.True(invoice.Withholdings.IsEmpty);
                Assert.Equal("140000", invoice.Total.Text);
                Assert.True(result.Rows.Single(r => r.Number == "9").IsCreditNote);
                // the copy: two documents, both a duplicate of one already in the table
                Assert.Equal(2, result.Skipped.Count(s => s.Reason == InvoiceSkip.Duplicate));
                var clp = InvoiceTable.Totals(result).Single();
                Assert.Equal("CLP", clp.Currency);
                Assert.Equal(90000m, clp.Sums[0]);
                Assert.Equal(18100m, clp.Sums[1]);
                Assert.Equal(178100m, clp.Sums[3]);
                var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("es"), "");
                Assert.Contains("Factura Electrónica", md);
                Assert.Contains("Nota de Crédito Electrónica", md);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void The_additional_taxes_the_schema_names_go_to_the_taxes_or_the_withholdings_and_those_it_does_not_name_to_neither()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                var totals = "<MntNeto>1000</MntNeto><IVA>190</IVA><ImptoReten><TipoImp>15</TipoImp><TasaImp>19</TasaImp><MontoImp>190</MontoImp></ImptoReten>"
                    + "<ImptoReten><TipoImp>35</TipoImp><MontoImp>40</MontoImp></ImptoReten><ImptoReten><TipoImp>999</TipoImp><MontoImp>7</MontoImp></ImptoReten><MntTotal>1040</MntTotal>";
                File.WriteAllText(Path.Combine(folder, "46.xml"), Dte("46", "", totals), new UTF8Encoding(false));
                var row = Assert.Single(InvoiceTable.ReadFolder(folder).Rows);
                // 15 is "IVA Retenido Total" (a withholding); 35 "Impuesto Especifico Gasolina" (a tax, with the VAT); 999 is not in the schema: in the total only
                Assert.Equal("190", row.Withholdings.Text);
                Assert.Equal("230", row.Taxes.Text);
                Assert.Equal("1040", row.Total.Text);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void A_dispatch_note_is_listed_apart_and_not_added_to_the_totals()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "guia.xml"), Dte("52", "<IndTraslado>1</IndTraslado>", "<MntTotal>9999</MntTotal>"), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "factura.xml"), Dte("33"), new UTF8Encoding(false));
                var result = InvoiceTable.ReadFolder(folder);
                var row = Assert.Single(result.Rows);
                Assert.Equal("33", row.TypeCode);
                var skipped = Assert.Single(result.Skipped);
                Assert.Equal(InvoiceSkip.OtherDte, skipped.Reason);
                Assert.Equal("52", skipped.Detail);
                Assert.Equal(1190m, InvoiceTable.Totals(result).Single().Sums[3]);
                Assert.Contains("a dispatch note", InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), ""));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void An_export_credit_note_is_subtracted_in_its_own_currency()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "110.xml"), Dte("110", "", "<TpoMoneda>DOLAR USA</TpoMoneda><MntTotal>5000</MntTotal>"), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "112.xml"), Dte("112", "", "<TpoMoneda>DOLAR USA</TpoMoneda><MntTotal>800</MntTotal>").Replace("<Folio>5</Folio>", "<Folio>6</Folio>"), new UTF8Encoding(false));
                var result = InvoiceTable.ReadFolder(folder);
                var usd = InvoiceTable.Totals(result).Single();
                Assert.Equal("DOLAR USA", usd.Currency);
                Assert.Equal(4200m, usd.Sums[3]);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
