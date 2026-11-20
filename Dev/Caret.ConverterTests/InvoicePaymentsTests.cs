using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Mexico: PPD invoices and their payment complements in the folder table (Services/Conversion/InvoiceTable.Payments.cs). The CFDIs are
    // invented here (unsealed: only what the table reads), with the SAT's generic RFCs.
    public class InvoicePaymentsTests
    {
        private const string Head = "<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" xmlns:tfd=\"http://www.sat.gob.mx/TimbreFiscalDigital\" xmlns:pago20=\"http://www.sat.gob.mx/Pagos20\" Version=\"4.0\"";

        internal static string Invoice(string uuid, string folio, string total, string method) =>
            $"{Head} Serie=\"A\" Folio=\"{folio}\" Fecha=\"2026-06-01T10:00:00\" SubTotal=\"{total}\" Moneda=\"MXN\" Total=\"{total}\" TipoDeComprobante=\"I\" MetodoPago=\"{method}\">" +
            "<cfdi:Emisor Rfc=\"EKU9003173C9\" Nombre=\"PROVEEDOR\"/><cfdi:Receptor Rfc=\"XAXX010101000\" Nombre=\"CLIENTE\"/>" +
            $"<cfdi:Complemento><tfd:TimbreFiscalDigital Version=\"1.1\" UUID=\"{uuid}\"/></cfdi:Complemento></cfdi:Comprobante>";

        internal static string Rep(string uuid, string date, params (string Invoice, int Installment, string Before, string Paid, string After)[] docs) =>
            $"{Head} Serie=\"P\" Folio=\"{uuid.Substring(0, 4)}\" Fecha=\"{date}T12:00:00\" SubTotal=\"0\" Moneda=\"XXX\" Total=\"0\" TipoDeComprobante=\"P\">" +
            "<cfdi:Emisor Rfc=\"EKU9003173C9\" Nombre=\"PROVEEDOR\"/><cfdi:Receptor Rfc=\"XAXX010101000\" Nombre=\"CLIENTE\"/>" +
            $"<cfdi:Complemento><pago20:Pagos Version=\"2.0\"><pago20:Pago FechaPago=\"{date}T12:00:00\" MonedaP=\"MXN\" Monto=\"0\">" +
            string.Concat(docs.Select(d => $"<pago20:DoctoRelacionado IdDocumento=\"{d.Invoice.ToLowerInvariant()}\" MonedaDR=\"MXN\" NumParcialidad=\"{d.Installment}\" ImpSaldoAnt=\"{d.Before}\" ImpPagado=\"{d.Paid}\" ImpSaldoInsoluto=\"{d.After}\"/>")) +
            $"</pago20:Pago></pago20:Pagos><tfd:TimbreFiscalDigital Version=\"1.1\" UUID=\"{uuid}\"/></cfdi:Complemento></cfdi:Comprobante>";

        private const string A = "AAAAAAAA-0000-4000-8000-000000000001";
        private const string B = "BBBBBBBB-0000-4000-8000-000000000002";
        private const string C = "CCCCCCCC-0000-4000-8000-000000000003";
        private const string D = "DDDDDDDD-0000-4000-8000-000000000004";
        private const string Missing = "EEEEEEEE-0000-4000-8000-000000000005";

        private static InvoiceTableResult Table(params (string Name, string Xml)[] files)
        {
            var result = new InvoiceTableResult();
            foreach (var (name, xml) in files)
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) InvoiceTable.Add(result, stream, name);
            InvoiceTable.Finish(result);
            return result;
        }

        [Fact]
        public void Each_ppd_invoice_says_what_its_complements_paid()
        {
            var result = Table(
                ("a.xml", Invoice(A, "1", "1000.00", "PPD")),
                ("b.xml", Invoice(B, "2", "500.00", "PPD")),
                ("c.xml", Invoice(C, "3", "300.00", "PPD")),
                ("d.xml", Invoice(D, "4", "200.00", "PUE")),
                ("rep1.xml", Rep("11111111-0000-4000-8000-00000000000A", "2026-06-15", (A, 1, "1000.00", "400.00", "600.00"), (C, 1, "300.00", "300.00", "0.00"))),
                ("rep2.xml", Rep("22222222-0000-4000-8000-00000000000B", "2026-07-15", (A, 2, "600.00", "100.00", "500.00"), (Missing, 1, "50.00", "50.00", "0.00"))),
                // the same complement twice (a copy in another folder) counts once
                ("copia/rep2.xml", Rep("22222222-0000-4000-8000-00000000000B", "2026-07-15", (A, 2, "600.00", "100.00", "500.00"), (Missing, 1, "50.00", "50.00", "0.00"))));
            Assert.Equal(4, result.Rows.Count);
            var l = XmlLabels.For("es");
            var csv = InvoiceTable.ToCsv(result, l);
            Assert.Contains("PPD: pagado 500.00, saldo 500.00 (2 complementos de pago)", csv);
            Assert.Contains("PPD: sin complemento de pago en la carpeta", csv);
            Assert.Contains("PPD: pagada (1 complemento de pago)", csv);
            // a PUE invoice is paid when it is issued: nothing is said
            Assert.Equal(3, csv.Split('\n').Count(line => line.Contains("PPD:")));

            var md = InvoiceTable.ToMarkdown(result, l, "junio");
            Assert.Contains("## Facturas PPD y sus complementos de pago", md);
            Assert.Contains("### Complementos de pago de facturas que no están en la carpeta (1)", md);
            Assert.Contains(Missing, md);
            // the complements are listed as files that are not invoices, not added to the totals
            Assert.Equal(3, result.Skipped.Count(s => s.Reason == InvoiceSkip.OtherCfdiType && s.Detail == "P"));
        }

        [Fact]
        public void Without_ppd_invoices_or_complements_there_is_no_section()
        {
            var result = Table(("d.xml", Invoice(D, "4", "200.00", "PUE")));
            Assert.DoesNotContain("PPD", InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "x"));
        }

        [Fact]
        public void Without_the_balance_the_total_less_what_was_paid_is_used()
        {
            var rep = Rep("33333333-0000-4000-8000-00000000000C", "2026-06-20", (A, 1, "1000.00", "250.00", "750.00")).Replace(" ImpSaldoInsoluto=\"750.00\"", "");
            var result = Table(("a.xml", Invoice(A, "1", "1000.00", "PPD")), ("rep.xml", rep));
            var row = Assert.Single(result.Rows);
            Assert.Equal(750m, row.PpdBalance);
            Assert.Contains("PPD: paid 250.00, balance 750.00 (1 payment complement)", InvoiceTable.ToCsv(result, XmlLabels.For("en")));
        }
    }
}
