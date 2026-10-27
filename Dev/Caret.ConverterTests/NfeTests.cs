using System;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Brazil's electronic invoice, the NF-e and NFC-e 4.00, and the events of an invoice (Services/Conversion/XmlNfe.cs), read from the national
    // schema. The samples in samples/ are invented.
    public class NfeTests
    {
        private const string Key = "35260312345678000195550010000001231000000019";

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
        public void The_processed_invoice_reads_as_a_summary_with_the_codes_of_the_schema_and_the_answer_of_the_authority()
        {
            var md = Sample("br-nfe-autorizada.xml");
            Assert.StartsWith("# NF-e (electronic invoice) 123 — series 1", md);
            Assert.Contains("- **Access key:** " + Key, md);
            Assert.Contains("- **Model:** 55 — NF-e", md);
            Assert.Contains("- **Type:** 1 — Saída", md);
            Assert.Contains("- **Tax regime (CRT):** 3 — Regime Normal", md);
            Assert.Contains("| 17 — Pagamento instantâneo (PIX) |  | 460.00 |", md);
            Assert.Contains("- **Total of the invoice:** 460.00", md);
            // the answer of the authority as it was written, not judged
            Assert.Contains("- **Result:** 100: Autorizado o uso da NF-e", md);
            // a pipe and asterisks in a description do not break the table
            Assert.Contains("Caderno espiral \\| \\*capa dura\\*", md);
        }

        [Fact]
        public void It_reads_in_Portuguese_and_the_generic_words_are_Portuguese_too()
        {
            var md = Sample("br-nfe-autorizada.xml", "pt");
            Assert.StartsWith("# NF-e (nota fiscal eletrônica) 123 — série 1", md);
            Assert.Contains("- **Chave de acesso:** " + Key, md);
            Assert.Contains("- **Natureza da operação:** Venda de mercadoria", md);
            Assert.Contains("## Resposta da autoridade fiscal", md);
            Assert.Contains("## Todos os campos do arquivo", md);
        }

        [Fact]
        public void It_reads_in_Spanish_and_does_not_depend_on_the_regional_settings()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
                var brazilian = Sample("br-nfe-autorizada.xml", "es");
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
                var american = Sample("br-nfe-autorizada.xml", "es");
                Assert.Equal(american, brazilian);
                Assert.Contains("- **Clave de acceso:** " + Key, american);
                Assert.Contains("- **Total de la factura:** 460.00", american);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = original; }
        }

        [Fact]
        public void The_signature_is_named_and_the_value_is_not_printed()
        {
            var md = Sample("br-nfe-autorizada.xml");
            Assert.DoesNotContain("SYNTHETIC-SIGNATURE", md);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.ASCII.GetBytes("SYNTHETIC-SIGNATURE-NOT-REAL-")).Substring(0, 30), md);
            Assert.Contains("digital signature (not printed)", md);
        }

        [Fact]
        public void A_code_that_is_not_in_the_list_is_printed_as_it_is()
        {
            var md = Read(File.ReadAllText(Path.Combine(TestPaths.Samples, "br-nfe-autorizada.xml")).Replace("<tPag>17</tPag>", "<tPag>77</tPag>").Replace("<finNFe>1</finNFe>", "<finNFe>8</finNFe>"));
            Assert.Contains("| 77 |  | 460.00 |", md);
            Assert.Contains("- **Purpose of the issue:** 8\n", md);
        }

        [Fact]
        public void A_consumer_invoice_is_an_NFC_e_and_a_bare_NFe_has_no_authorization_section()
        {
            var xml = File.ReadAllText(Path.Combine(TestPaths.Samples, "br-nfe-autorizada.xml"));
            var bare = xml.Substring(xml.IndexOf("<NFe>", StringComparison.Ordinal), xml.IndexOf("</NFe>", StringComparison.Ordinal) + 6 - xml.IndexOf("<NFe>", StringComparison.Ordinal))
                .Replace("<NFe>", "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">").Replace("<mod>55</mod>", "<mod>65</mod>");
            var md = Read(bare);
            Assert.StartsWith("# NFC-e (electronic consumer invoice) 123", md);
            Assert.DoesNotContain("## Answer of the tax authority", md);
        }

        [Fact]
        public void A_CNPJ_with_letters_is_text_and_stays_as_it_is()
        {
            var md = Read(File.ReadAllText(Path.Combine(TestPaths.Samples, "br-nfe-autorizada.xml")).Replace("<CNPJ>12345678000195</CNPJ><xNome>EMPRESA", "<CNPJ>12ABC34501DE35</CNPJ><xNome>EMPRESA"));
            Assert.Contains("- **Tax ID (CNPJ / CPF):** 12ABC34501DE35", md);
        }

        [Fact]
        public void The_event_of_a_cancellation_is_read_with_its_type_justification_and_the_answer()
        {
            var md = Sample("br-evento-cancelamento.xml");
            Assert.StartsWith("# Event of an NF-e: Cancelamento", md);
            Assert.Contains("- **Type of event:** 110111 — Cancelamento", md);
            Assert.Contains("- **Justification:** Pedido cancelado a pedido do cliente (texto de exemplo)", md);
            Assert.Contains("- **Result:** 135: Evento registrado e vinculado a NF-e", md);
            Assert.DoesNotContain("SYNTHETIC-SIGNATURE", md);
        }

        [Fact]
        public void A_Nfe_in_another_namespace_is_not_taken_for_one()
        {
            var md = Read("<nfeProc xmlns=\"http://example.com/other\"><NFe><infNFe Id=\"NFe1\"><ide><nNF>1</nNF></ide></infNFe></NFe></nfeProc>");
            Assert.StartsWith("# nfeProc", md);
            Assert.DoesNotContain("NF-e (electronic invoice)", md);
        }

        // ---- the folder table: the invoices and their events -----------------------------------------------------------------------------

        [Fact]
        public void The_folder_table_reads_the_invoice_as_a_row_and_shows_the_cancellation_event_in_its_answer_without_asking_anyone()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.Copy(Path.Combine(TestPaths.Samples, "br-nfe-autorizada.xml"), Path.Combine(folder, "nota.xml"));
                File.Copy(Path.Combine(TestPaths.Samples, "br-evento-cancelamento.xml"), Path.Combine(folder, "evento-cancelamento.xml"));
                var other = File.ReadAllText(Path.Combine(TestPaths.Samples, "br-evento-cancelamento.xml")).Replace(Key, "35260399999999000199550010000009991000000012");
                File.WriteAllText(Path.Combine(folder, "evento-de-outra-nota.xml"), other);
                var result = InvoiceTable.ReadFolder(folder);
                var row = Assert.Single(result.Rows);
                Assert.Equal("123/1", row.Number);
                Assert.Equal("12345678000195", row.IssuerId);
                Assert.Equal("98765432000110", row.CustomerId);
                Assert.Equal("BRL", row.Currency);
                Assert.Equal("460.00", row.Total.Text);
                Assert.Equal(Key, row.Cuf);
                Assert.Equal("100: Autorizado o uso da NF-e · Cancelamento (2026-03-10T09:00:00-03:00): 135: Evento registrado e vinculado a NF-e", row.Answer);
                var reasons = result.Skipped.ToDictionary(s => s.File, s => s.Detail);
                Assert.Equal("linked", reasons["evento-cancelamento.xml"]);
                Assert.Equal("", reasons["evento-de-outra-nota.xml"]);
                var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("pt"), "");
                Assert.Contains("Um evento de uma nota que está na tabela", md);
                Assert.Contains("Um evento de uma nota que não está nesta pasta", md);
                Assert.Contains("NF-e", md);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void A_credit_note_of_the_purpose_5_is_subtracted_and_a_return_is_named_in_its_type()
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                var xml = File.ReadAllText(Path.Combine(TestPaths.Samples, "br-nfe-autorizada.xml"));
                File.WriteAllText(Path.Combine(folder, "normal.xml"), xml);
                var credit = xml.Replace("<finNFe>1</finNFe>", "<finNFe>5</finNFe>").Replace("<nNF>123</nNF>", "<nNF>124</nNF>").Replace("Id=\"NFe" + Key, "Id=\"NFe35260312345678000195550010000001241000000012").Replace("<vNF>460.00</vNF>", "<vNF>60.00</vNF>");
                File.WriteAllText(Path.Combine(folder, "credito.xml"), credit);
                var returned = xml.Replace("<finNFe>1</finNFe>", "<finNFe>4</finNFe>").Replace("<nNF>123</nNF>", "<nNF>125</nNF>").Replace("Id=\"NFe" + Key, "Id=\"NFe35260312345678000195550010000001251000000013").Replace("<vNF>460.00</vNF>", "<vNF>10.00</vNF>");
                File.WriteAllText(Path.Combine(folder, "devolucao.xml"), returned);
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Equal(3, result.Rows.Count);
                // 460.00 + 10.00 (a return is an invoice of its own) - 60.00 (a credit note)
                Assert.Equal(410.00m, InvoiceTable.Totals(result).Single().Sums[3]);
                var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "");
                Assert.Contains("NF-e — Devolução/Retorno", md);
                Assert.Contains("NF-e — Nota de crédito", md);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
