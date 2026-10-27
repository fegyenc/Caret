using System;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Mexico's electronic invoice, the CFDI 4.0 and 3.3 (Services/Conversion/XmlCfdi.cs), read from the SAT's schemas. The samples in samples/
    // are invented (the RFCs are the SAT's published test taxpayers).
    public class CfdiTests
    {
        private static string Sample(string name, string language = "en") =>
            DocumentConverter.Convert(Path.Combine(TestPaths.Samples, name), new ConversionOptions { Language = language }).Markdown;

        private static string Read(string xml, string language = "en")
        {
            var work = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(work, "test.xml");
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                return DocumentConverter.Convert(path, new ConversionOptions { Language = language }).Markdown;
            }
            finally { Directory.Delete(work, true); }
        }

        [Fact]
        public void The_invoice_of_income_reads_as_a_summary_with_the_codes_the_SAT_names()
        {
            var md = Sample("mx-cfdi40-ingreso.xml");
            Assert.StartsWith("# CFDI of income (invoice) A 1001", md);
            Assert.Contains("- **Form of payment:** 03 — Transferencia electrónica de fondos", md);
            Assert.Contains("- **Method of payment:** PUE — Pago en una sola exhibición", md);
            Assert.Contains("- **Tax regime:** 601 — General de Ley Personas Morales", md);
            Assert.Contains("- **Use of the CFDI:** G03 — Gastos en general", md);
            Assert.Contains("| 002 — IVA | 900.00 | Tasa | 0.160000 | 144.00 |", md);
            Assert.Contains("- **UUID:** 6128396f-c09b-4ec6-8699-43c5f7e3b230", md);
            // a pipe and asterisks in a description do not break the table
            Assert.Contains("Servicio de instalación \\| \\*ejemplo\\*", md);
        }

        [Fact]
        public void The_digital_seals_and_the_certificate_are_named_and_not_printed_anywhere()
        {
            var source = File.ReadAllText(Path.Combine(TestPaths.Samples, "mx-cfdi40-ingreso.xml"));
            var sealStart = source.Substring(source.IndexOf("Sello=\"", StringComparison.Ordinal) + 7, 40);
            var md = Sample("mx-cfdi40-ingreso.xml");
            Assert.DoesNotContain(sealStart, md);
            Assert.Contains("Sello=digital seal of 180 characters (not printed)", md);
            Assert.Contains("SelloSAT=digital seal of 180 characters (not printed)", md);
            // the certificate is a certificate, not a seal
            Assert.Contains("Certificado=certificate of 180 characters (not printed)", md);
            Assert.Contains("sello digital de 180 caracteres (no se imprime)", Sample("mx-cfdi40-ingreso.xml", "es"));
            Assert.Contains("certificado de 180 caracteres (no se imprime)", Sample("mx-cfdi40-ingreso.xml", "es"));
        }

        [Fact]
        public void The_seal_and_the_certificate_of_a_payment_complement_are_not_printed_either()
        {
            var md = Read("<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Version=\"4.0\" Total=\"0\" TipoDeComprobante=\"P\"><cfdi:Complemento>"
                + "<pago20:Pagos xmlns:pago20=\"http://www.sat.gob.mx/Pagos20\" Version=\"2.0\"><pago20:Pago FechaPago=\"2026-03-01T12:00:00\" FormaDePagoP=\"03\" MonedaP=\"MXN\" Monto=\"10.00\" SelloPago=\"SECRETSEALVALUE\" CertPago=\"SECRETCERTVALUE\"/></pago20:Pagos>"
                + "</cfdi:Complemento></cfdi:Comprobante>");
            Assert.DoesNotContain("SECRETSEALVALUE", md);
            Assert.DoesNotContain("SECRETCERTVALUE", md);
            Assert.Contains("SelloPago=digital seal of 15 characters (not printed)", md);
            Assert.Contains("CertPago=certificate of 15 characters (not printed)", md);
        }

        [Fact]
        public void A_short_seal_is_not_printed_either()
        {
            var md = Read("<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Version=\"4.0\" Sello=\"SHORTSEAL\" Certificado=\"SHORTCERT\" Total=\"1\" TipoDeComprobante=\"I\"/>");
            Assert.DoesNotContain("SHORTSEAL", md);
            Assert.DoesNotContain("SHORTCERT", md);
            Assert.Contains("Sello=digital seal of 9 characters (not printed)", md);
        }

        [Fact]
        public void A_code_that_is_not_in_the_list_is_printed_as_it_is_and_a_complement_is_only_named()
        {
            var md = Sample("mx-cfdi40-egreso.xml");
            Assert.Contains("- **Export:** 04\n", md);
            Assert.Contains("- **Type of relation:** 01 — Nota de crédito de los documentos relacionados", md);
            Assert.Contains("## Complements", md);
            Assert.Contains("- Detalle", md);
            // and the field of the complement is in the list of all fields, so nothing is hidden
            Assert.Contains("**Detalle** (Nota=complemento inventado)", md);
        }

        [Fact]
        public void The_summary_reads_in_Spanish_and_does_not_depend_on_the_regional_settings()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("es-MX");
                var md = Sample("mx-cfdi40-ingreso.xml", "es");
                Assert.StartsWith("# CFDI de ingreso (factura) A 1001", md);
                Assert.Contains("- **Fecha de expedición:** 2026-03-04T10:15:30", md);
                Assert.Contains("- **Total:** 1044.00", md);
                Assert.Contains("## Timbre fiscal digital", md);
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
                Assert.Equal(md, Sample("mx-cfdi40-ingreso.xml", "es"));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = original; }
        }

        [Fact]
        public void A_Comprobante_that_is_not_in_the_SAT_namespace_is_not_taken_for_a_CFDI()
        {
            var md = Read("<Comprobante xmlns=\"http://example.com/other\" Total=\"5\"><x>1</x></Comprobante>");
            Assert.DoesNotContain("CFDI", md);
            Assert.StartsWith("# Comprobante", md);
        }

        [Fact]
        public void A_version_3_3_invoice_is_read_with_the_same_summary()
        {
            var md = Read("<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/3\" Version=\"3.3\" Serie=\"B\" Folio=\"9\" Fecha=\"2019-05-01T12:00:00\" SubTotal=\"10.00\" Moneda=\"MXN\" Total=\"11.60\" TipoDeComprobante=\"I\" MetodoPago=\"PPD\" FormaPago=\"99\">"
                + "<cfdi:Emisor Rfc=\"EKU9003173C9\" Nombre=\"EMISOR\" RegimenFiscal=\"601\"/><cfdi:Receptor Rfc=\"URE180429TM6\" Nombre=\"RECEPTOR\" UsoCFDI=\"G01\"/>"
                + "<cfdi:Conceptos><cfdi:Concepto ClaveProdServ=\"01010101\" Cantidad=\"1\" ClaveUnidad=\"ACT\" Descripcion=\"x\" ValorUnitario=\"10.00\" Importe=\"10.00\"/></cfdi:Conceptos></cfdi:Comprobante>");
            Assert.StartsWith("# CFDI of income (invoice) B 9", md);
            Assert.Contains("> CFDI 3.3", md);
            Assert.Contains("- **Method of payment:** PPD — Pago en parcialidades o diferido", md);
            Assert.Contains("- **Use of the CFDI:** G01 — Adquisición de mercancías", md);
        }

        // ---- a folder of invoices from both countries --------------------------------------------------------------------------------

        [Fact]
        public void A_payroll_a_payment_and_a_transfer_CFDI_are_listed_and_not_added_to_the_totals()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.Copy(Path.Combine(TestPaths.Samples, "mx-cfdi40-ingreso.xml"), Path.Combine(folder, "invoice.xml"));
                var income = File.ReadAllText(Path.Combine(TestPaths.Samples, "mx-cfdi40-ingreso.xml"));
                var uuid = "6128396f-c09b-4ec6-8699-43c5f7e3b230";
                File.WriteAllText(Path.Combine(folder, "payroll.xml"), income.Replace("TipoDeComprobante=\"I\"", "TipoDeComprobante=\"N\"").Replace(uuid, "11111111-c09b-4ec6-8699-43c5f7e3b230"));
                File.WriteAllText(Path.Combine(folder, "payment.xml"), income.Replace("TipoDeComprobante=\"I\"", "TipoDeComprobante=\"P\"").Replace(uuid, "22222222-c09b-4ec6-8699-43c5f7e3b230"));
                File.WriteAllText(Path.Combine(folder, "transfer.xml"), income.Replace("TipoDeComprobante=\"I\"", "TipoDeComprobante=\"T\"").Replace(uuid, "33333333-c09b-4ec6-8699-43c5f7e3b230"));
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Equal("A 1001", Assert.Single(result.Rows).Number);
                Assert.Equal(new[] { "payment.xml", "payroll.xml", "transfer.xml" }, result.Skipped.Select(s => s.File));
                Assert.All(result.Skipped, s => Assert.Equal(InvoiceSkip.OtherCfdiType, s.Reason));
                Assert.Equal(1044.00m, InvoiceTable.Totals(result).Single().Sums[3]);
                var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "");
                Assert.Contains("| payroll.xml | A CFDI of type N (transfer, payroll or payment): not an invoice or a credit note, so not added to the totals |", md);
                Assert.Contains("Un CFDI de tipo P", InvoiceTable.ToMarkdown(result, XmlLabels.For("es"), ""));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void The_folder_table_reads_the_CFDI_as_rows_subtracts_the_expense_ones_and_counts_one_UUID_once()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.Copy(Path.Combine(TestPaths.Samples, "mx-cfdi40-ingreso.xml"), Path.Combine(folder, "a.xml"));
                File.Copy(Path.Combine(TestPaths.Samples, "mx-cfdi40-ingreso.xml"), Path.Combine(folder, "a copy.xml"));
                File.Copy(Path.Combine(TestPaths.Samples, "mx-cfdi40-egreso.xml"), Path.Combine(folder, "b.xml"));
                File.Copy(Path.Combine(TestPaths.Samples, "co-factura-adjunta.xml"), Path.Combine(folder, "c.xml"));
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Equal(3, result.Rows.Count);
                var income = result.Rows.Single(r => r.Number == "A 1001");
                Assert.Equal("EKU9003173C9", income.IssuerId);
                Assert.Equal("URE180429TM6", income.CustomerId);
                Assert.Equal("900.00", income.Base.Text);          // SubTotal 1000.00 less Descuento 100.00
                Assert.Equal("144.00", income.Taxes.Text);
                Assert.Equal("1044.00", income.Total.Text);
                Assert.Equal("6128396f-c09b-4ec6-8699-43c5f7e3b230", income.Cuf);
                var duplicate = Assert.Single(result.Skipped);
                Assert.Equal(InvoiceSkip.Duplicate, duplicate.Reason);
                // MXN: 1044.00 income less 116.00 expense
                var mxn = InvoiceTable.Totals(result).Single(t => t.Currency == "MXN");
                Assert.Equal(928.00m, mxn.Sums[3]);
                // and the Colombian invoice of the same folder is its own group (COP), not lost or mixed with the pesos of Mexico
                var cop = InvoiceTable.Totals(result).Single(t => t.Currency == "COP");
                Assert.Equal(1190000.00m, cop.Sums[3]);
                Assert.Equal(1000000.00m, cop.Sums[0]);
                Assert.Equal(800.00m, mxn.Sums[0]);                // 900.00 - 100.00
                var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("es"), "");
                Assert.Contains("CFDI de egreso", md);
                Assert.Contains("CFDI de ingreso (factura)", md);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
