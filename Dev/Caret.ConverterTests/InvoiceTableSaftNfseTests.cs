using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // The SAF-T (PT) and NFS-e Nacional files in the folder table (Services/Conversion/InvoiceTable.cs). The files are the synthetic samples in samples/.
    public class InvoiceTableSaftNfseTests
    {
        private static string Sample(string name) => File.ReadAllText(Path.Combine(TestPaths.Samples, name));

        private static InvoiceTableResult Read(params (string Name, string Xml)[] files)
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                foreach (var (name, xml) in files) File.WriteAllText(Path.Combine(folder, name), xml, new UTF8Encoding(false));
                return InvoiceTable.ReadFolder(folder);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void A_SAFT_file_gives_a_row_per_sales_document_and_the_annulled_one_is_left_out_of_the_totals()
        {
            var result = Read(("saft.xml", Sample("pt-saft-faturacao.xml")));
            Assert.Equal(7, result.Rows.Count);
            var annulled = result.Rows.Single(r => r.Number == "FS 2026B/2");
            Assert.True(annulled.Void);
            Assert.Equal("A — Anulado", annulled.Answer);
            Assert.All(result.Rows.Where(r => r.Number != "FS 2026B/2"), r => Assert.False(r.Void));

            // 123.00 + 165.40 + 12.30 - 61.50 (credit note) + 100.00 + 125.00; the annulled 24.60 is not added
            var total = Assert.Single(InvoiceTable.Totals(result));
            Assert.Equal("EUR", total.Currency);
            Assert.Equal(400.00m, total.Sums[0]);   // net
            Assert.Equal(62.20m, total.Sums[1]);    // tax
            Assert.Null(total.Sums[2]);             // nobody filled in the withholdings
            Assert.Equal(464.20m, total.Sums[3]);   // gross
            Assert.Null(total.Sums[4]);             // the file has no "payable"

            var invoice = result.Rows.Single(r => r.Number == "FT 2026A/1");
            Assert.Equal("2026-02-03", invoice.Date);
            Assert.Equal("Empresa de Exemplo, Lda.", invoice.Issuer);
            Assert.Equal("500000018", invoice.IssuerId);
            Assert.Equal("Cliente Exemplo, S.A.", invoice.Customer);
            Assert.Equal("500000026", invoice.CustomerId);
            Assert.Equal("CSDF7T5H-1", invoice.Cuf);
        }

        [Fact]
        public void The_withholdings_of_a_SAFT_document_are_added_wherever_the_file_puts_them()
        {
            var xml = Sample("pt-saft-faturacao.xml")
                .Replace("<GrossTotal>123.00</GrossTotal></DocumentTotals>", "<GrossTotal>123.00</GrossTotal></DocumentTotals><WithholdingTax><WithholdingTaxType>IRS</WithholdingTaxType><WithholdingTaxAmount>5.00</WithholdingTaxAmount></WithholdingTax><WithholdingTax><WithholdingTaxType>IS</WithholdingTaxType><WithholdingTaxAmount>1.50</WithholdingTaxAmount></WithholdingTax>")
                .Replace("<GrossTotal>165.40</GrossTotal>", "<GrossTotal>165.40</GrossTotal><WithholdingTax><WithholdingTaxAmount>2.00</WithholdingTaxAmount></WithholdingTax>");
            var result = Read(("saft.xml", xml));
            Assert.Equal(6.50m, result.Rows.Single(r => r.Number == "FT 2026A/1").Withholdings.Value);
            Assert.Equal(2.00m, result.Rows.Single(r => r.Number == "FT 2026A/2").Withholdings.Value);
            Assert.Equal(8.50m, Assert.Single(InvoiceTable.Totals(result)).Sums[2]);
        }

        [Fact]
        public void The_amounts_of_a_foreign_currency_invoice_stay_in_the_currency_of_the_header()
        {
            var xml = Sample("pt-saft-faturacao.xml")
                .Replace("<GrossTotal>123.00</GrossTotal></DocumentTotals>", "<GrossTotal>123.00</GrossTotal><Currency><CurrencyCode>USD</CurrencyCode><CurrencyAmount>134.00</CurrencyAmount></Currency></DocumentTotals>");
            var result = Read(("saft.xml", xml));
            Assert.Equal("EUR", result.Rows.Single(r => r.Number == "FT 2026A/1").Currency);
            Assert.Equal(464.20m, Assert.Single(InvoiceTable.Totals(result)).Sums[3]);
        }

        [Fact]
        public void A_summary_of_another_application_is_listed_and_not_added()
        {
            var xml = Sample("pt-saft-faturacao.xml").Replace("<InvoiceStatus>N</InvoiceStatus><InvoiceStatusDate>2026-02-03", "<InvoiceStatus>R</InvoiceStatus><InvoiceStatusDate>2026-02-03");
            var result = Read(("saft.xml", xml));
            var row = result.Rows.Single(r => r.Number == "FT 2026A/1");
            Assert.True(row.Void);
            Assert.StartsWith("R — ", row.Answer);
            Assert.Equal(341.20m, Assert.Single(InvoiceTable.Totals(result)).Sums[3]);   // 464.20 without the 123.00
        }

        [Fact]
        public void A_SAFT_file_without_sales_documents_is_listed_as_not_an_invoice()
        {
            var xml = Sample("pt-saft-faturacao.xml");
            var start = xml.IndexOf("<SalesInvoices>");
            var end = xml.IndexOf("</SalesInvoices>") + "</SalesInvoices>".Length;
            Assert.True(start > 0 && end > start);
            var result = Read(("accounts.xml", xml.Remove(start, end - start)));
            Assert.Empty(result.Rows);
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(InvoiceSkip.NotInvoice, skipped.Reason);
        }

        [Fact]
        public void An_ATCUD_of_zero_is_not_a_code_and_a_customer_that_is_not_in_the_master_file_keeps_its_id()
        {
            var result = Read(("saft.xml", Sample("pt-saft-faturacao.xml")));
            var row = result.Rows.Single(r => r.Number == "FT 2026A/5");
            Assert.Null(row.Cuf);
            Assert.Equal("C009", row.CustomerId);
            Assert.Null(row.Customer);
        }

        [Fact]
        public void The_same_SAFT_document_in_two_files_is_counted_once_and_the_credit_note_is_subtracted_in_the_markdown_too()
        {
            var xml = Sample("pt-saft-faturacao.xml");
            var result = Read(("a.xml", xml), ("b.xml", xml));
            Assert.Equal(7, result.Rows.Count);
            Assert.Equal(7, result.Skipped.Count(s => s.Reason == InvoiceSkip.Duplicate));
            var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "saft");
            Assert.Contains("464.2", md);
            Assert.Contains("annulled", md);   // the note that the A and F documents are listed and not added
            Assert.Contains("NC — Credit note", md);
        }

        [Fact]
        public void The_same_ATCUD_of_two_companies_does_not_make_one_document_of_two()
        {
            var xml = Sample("pt-saft-faturacao.xml");
            var other = xml.Replace("<TaxRegistrationNumber>500000018</TaxRegistrationNumber>", "<TaxRegistrationNumber>500000042</TaxRegistrationNumber>");
            Assert.NotEqual(xml, other);
            var result = Read(("a.xml", xml), ("b.xml", other));
            Assert.Equal(14, result.Rows.Count);
            Assert.DoesNotContain(result.Skipped, s => s.Reason == InvoiceSkip.Duplicate);
        }

        [Fact]
        public void The_other_documents_of_a_SAFT_file_are_listed_by_name_and_not_added()
        {
            var xml = Sample("pt-saft-faturacao.xml").Replace("</SourceDocuments>", "<Payments><NumberOfEntries>0</NumberOfEntries></Payments></SourceDocuments>");
            var result = Read(("saft.xml", xml));
            Assert.Equal(7, result.Rows.Count);
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(InvoiceSkip.SaftOther, skipped.Reason);
            Assert.Equal("Payments", skipped.Detail);
        }

        [Fact]
        public void An_NFSe_is_a_row_with_its_cancellation_in_the_answer_and_a_declaration_alone_is_listed()
        {
            var result = Read(("nfse.xml", Sample("br-nfse-nacional.xml")), ("evento.xml", Sample("br-nfse-evento-cancelamento.xml")), ("dps.xml", Sample("br-dps-substituicao.xml")));
            var row = Assert.Single(result.Rows);
            Assert.Equal("Nfse", row.Kind);
            Assert.Equal("456", row.Number);
            Assert.Equal("2026-10-27T09:15:00-03:00", row.Date);
            Assert.Equal("Empresa de Servicos Exemplo Ltda", row.Issuer);
            Assert.Equal("11222333000181", row.IssuerId);
            Assert.Equal("Cliente Inventado S.A.", row.Customer);
            Assert.Equal("45997418000153", row.CustomerId);
            Assert.Equal("BRL", row.Currency);
            Assert.Equal("35503082211222333000181000000000045626101234567890", row.Cuf);
            Assert.Equal(1500.00m, row.Total.Value);
            Assert.Equal(30.00m, row.Taxes.Value);
            Assert.Equal(0.00m, row.Withholdings.Value);
            Assert.Equal(1500.00m, row.Payable.Value);
            Assert.StartsWith("100 — ", row.Answer);
            Assert.Contains("Cancelamento de NFS-e", row.Answer);

            Assert.Contains(result.Skipped, s => s.File == "dps.xml" && s.Reason == InvoiceSkip.NfseNotIssued);
            Assert.Contains(result.Skipped, s => s.File == "evento.xml" && s.Reason == InvoiceSkip.NfeEvent && s.Detail == "linked");
        }

        [Fact]
        public void An_event_of_an_NFSe_that_is_not_in_the_folder_is_listed_as_orphan()
        {
            var result = Read(("evento.xml", Sample("br-nfse-evento-cancelamento.xml")));
            Assert.Empty(result.Rows);
            Assert.Contains(result.Skipped, s => s.File == "evento.xml" && s.Reason == InvoiceSkip.NfeEvent && s.Detail == "");
        }

        [Fact]
        public void The_table_of_both_countries_has_a_total_per_currency()
        {
            var result = Read(("saft.xml", Sample("pt-saft-faturacao.xml")), ("nfse.xml", Sample("br-nfse-nacional.xml")));
            var totals = InvoiceTable.Totals(result).ToDictionary(t => t.Currency);
            Assert.Equal(464.20m, totals["EUR"].Sums[3]);
            Assert.Equal(1500.00m, totals["BRL"].Sums[3]);
            var csv = InvoiceTable.ToCsv(result, XmlLabels.For("pt"));
            Assert.Contains("NFS-e", csv);
            Assert.Contains("FT — Fatura", csv);
        }
    }
}
