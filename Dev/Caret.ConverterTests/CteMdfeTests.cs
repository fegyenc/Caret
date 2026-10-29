using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Brazil's CT-e 4.00 (the electronic transport document, model 57) and MDF-e 3.00 (the electronic manifest, model 58), and their events
    // (Services/Conversion/XmlCte.cs, XmlMdfe.cs), read from the official schema packages. The samples in samples/ are invented and valid against
    // those schemas.
    public class CteMdfeTests
    {
        private const string CteKey = "35261011222333000181570010000004561000000019";
        private const string SubstituteKey = "35261011222333000181570010000004571000000026";
        private const string MdfeKey = "35261011222333000181580010000001231000000073";

        private static string Sample(string name, string language = "en") =>
            DocumentConverter.Convert(Path.Combine(TestPaths.Samples, name), new ConversionOptions { Language = language }).Markdown;

        private static InvoiceTableResult Table(params string[] samples)
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                foreach (var name in samples) File.Copy(Path.Combine(TestPaths.Samples, name), Path.Combine(folder, name));
                return InvoiceTable.ReadFolder(folder);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void The_processed_CTe_reads_as_a_summary_with_the_codes_of_the_schema_and_the_answer_of_the_authority()
        {
            var md = Sample("br-cte-autorizada.xml");
            Assert.StartsWith("# CT-e (electronic transport document) 456 — series 1", md);
            Assert.Contains("- **Access key:** " + CteKey, md);
            Assert.Contains("- **Model:** 57 — CT-e", md);
            Assert.Contains("- **Type:** 0 — CT-e Normal", md);
            Assert.Contains("- **Type of service:** 0 — Normal", md);
            Assert.Contains("- **Mode of transport:** 01 — Rodoviário", md);
            Assert.Contains("- **Who pays (taker of the service):** 3 — Destinatário", md);
            Assert.Contains("- **The service starts in:** SAO PAULO/SP", md);
            Assert.Contains("- **The service ends in:** RIO DE JANEIRO/RJ", md);
            Assert.Contains("- **Total value of the service:** 1200.00", md);
            Assert.Contains("| Pedagio | 200.00 |", md);
            Assert.Contains("- **Tax situation (CST):** 00 — Tributação normal do ICMS", md);
            Assert.Contains("- **ICMS:** 144.00", md);
            Assert.Contains("| PESO BRUTO | 01 — KG | 850.0000 |", md);   // the measure is free text in this file: printed as it is
            Assert.Contains("| NF-e | 35261012345678000195550010000001231000000019 |", md);
            Assert.Contains("- **RNTRC (registry of road carriers):** 12345678", md);
            Assert.Contains("- **Pedido:** PED-2026-0042", md);
            // the answer of the authority as it was written, not judged
            Assert.Contains("- **Result:** 100: Autorizado o uso do CT-e", md);
            Assert.Contains("## All fields of the file", md);
        }

        [Fact]
        public void The_CTe_reads_in_Portuguese_and_in_Spanish()
        {
            var pt = Sample("br-cte-autorizada.xml", "pt");
            Assert.StartsWith("# CT-e (conhecimento de transporte eletrônico) 456 — série 1", pt);
            Assert.Contains("- **Chave de acesso:** " + CteKey, pt);
            Assert.Contains("- **Quem paga (tomador do serviço):** 3 — Destinatário", pt);
            Assert.Contains("## Resposta da autoridade fiscal", pt);
            var es = Sample("br-cte-autorizada.xml", "es");
            Assert.StartsWith("# CT-e (documento de transporte electrónico) 456 — serie 1", es);
            Assert.Contains("- **Quién paga (tomador del servicio):** 3 — Destinatário", es);
        }

        [Fact]
        public void A_CTe_of_substitution_names_the_one_it_replaces()
        {
            var md = Sample("br-cte-substituicao.xml");
            Assert.Contains("- **Type:** 3 — CT-e de Substituição", md);
            Assert.Contains("- **Replaces the CT-e:** " + CteKey, md);
        }

        [Fact]
        public void The_event_of_a_CTe_reads_with_its_justification_and_the_answer()
        {
            var md = Sample("br-cte-evento-cancelamento.xml");
            Assert.StartsWith("# Event of a CT-e: Cancelamento", md);
            Assert.Contains("- **Access key:** " + CteKey, md);
            Assert.Contains("- **Justification:** Servico cancelado a pedido do cliente antes da coleta", md);
            Assert.Contains("- **Result:** 135: Evento registrado e vinculado a CT-e", md);
        }

        [Fact]
        public void The_processed_MDFe_reads_as_a_summary_with_the_vehicles_the_documents_and_the_cargo()
        {
            var md = Sample("br-mdfe-autorizado.xml");
            Assert.StartsWith("# MDF-e (electronic manifest of fiscal documents) 123 — series 1", md);
            Assert.Contains("- **Access key:** " + MdfeKey, md);
            Assert.Contains("- **Mode of transport:** 1 — Rodoviário", md);
            Assert.Contains("- **Type of issuer:** 1 — Prestador de serviço de transporte", md);
            Assert.Contains("- **Type of carrier:** 1 — ETC", md);
            Assert.Contains("- **State of loading:** SP", md);
            Assert.Contains("- **Route (states):** MG", md);
            Assert.Contains("| Tractor | ABC1D23 | 9000 | 25000 |  | 03 — Cavalo Mecânico | 00 — não aplicável | SP |  |", md);
            Assert.Contains("| Trailer | XYZ9A87 | 6000 | 30000 |  |  | 02 — Fechada/Baú | SP |  |", md);
            Assert.Contains("| Motorista Inventado | 12345678909 |", md);
            Assert.Contains("## Unloading: RIO DE JANEIRO", md);
            Assert.Contains("| CT-e | " + CteKey + " |", md);
            Assert.Contains("| CT-e | " + SubstituteKey + " |", md);
            Assert.Contains("- **Type of cargo:** 05 — Carga Geral", md);
            Assert.Contains("- **Gross weight:** 1700.0000 KG", md);
            Assert.Contains("- **Seals:** 00012345", md);
            Assert.Contains("- **Result:** 100: Autorizado o uso do MDF-e", md);
        }

        [Fact]
        public void The_closing_of_a_trip_reads_as_an_event_of_the_manifest()
        {
            var md = Sample("br-mdfe-evento-encerramento.xml");
            Assert.StartsWith("# Event of an MDF-e: Encerramento", md);
            Assert.Contains("- **Access key:** " + MdfeKey, md);
            Assert.Contains("- **Closed on:** 2026-10-21", md);
            Assert.Contains("- **Closed in (municipality · state):** 3304557 · 33", md);
            Assert.Contains("- **Result:** 135: Evento registrado e vinculado a MDF-e", md);
        }

        [Fact]
        public void An_unknown_element_of_the_modal_is_not_lost_the_generic_view_follows_the_summary()
        {
            var md = Sample("br-mdfe-autorizado.xml");
            Assert.Contains("## All fields of the file", md);
            Assert.Contains("**RNTRC**: 12345678", md);
        }

        [Fact]
        public void A_CTe_is_a_row_of_the_table_for_the_taker_and_its_cancellation_is_said_in_the_answer()
        {
            var result = Table("br-cte-autorizada.xml", "br-cte-evento-cancelamento.xml");
            var row = Assert.Single(result.Rows);
            Assert.Equal("Cte", row.Kind);
            Assert.Equal("456/1", row.Number);
            Assert.Equal("2026-10-20T10:30:00-03:00", row.Date);
            Assert.Equal("Transportadora Exemplo Ltda", row.Issuer);
            Assert.Equal("11222333000181", row.IssuerId);
            Assert.Equal("Loja Cliente Exemplo Ltda", row.Customer);   // the taker of the service, who pays it
            Assert.Equal("07526557000100", row.CustomerId);
            Assert.Equal("BRL", row.Currency);
            Assert.Equal(CteKey, row.Cuf);
            Assert.Equal(1200.00m, row.Total.Value);
            Assert.Equal(1200.00m, row.Payable.Value);
            Assert.Equal(144.00m, row.Taxes.Value);
            Assert.StartsWith("100: Autorizado o uso do CT-e", row.Answer);
            Assert.Contains("Cancelamento", row.Answer);
            Assert.Contains(result.Skipped, s => s.File == "br-cte-evento-cancelamento.xml" && s.Reason == InvoiceSkip.NfeEvent && s.Detail == "linked");
            Assert.Equal("CT-e", XmlCte.RowType(row.TypeCode));
        }

        [Fact]
        public void A_CTe_that_a_CTe_of_substitution_replaces_is_listed_and_not_added()
        {
            var result = Table("br-cte-autorizada.xml", "br-cte-substituicao.xml");
            Assert.Equal(2, result.Rows.Count);
            var original = result.Rows.Single(r => r.Cuf == CteKey);
            var substitute = result.Rows.Single(r => r.Cuf == SubstituteKey);
            Assert.True(original.Void);
            Assert.False(substitute.Void);
            Assert.Contains("Substituído pelo CT-e 457/1", original.Answer);
            Assert.Equal("CT-e — CT-e de Substituição", XmlCte.RowType(substitute.TypeCode));
            var total = Assert.Single(InvoiceTable.Totals(result));
            Assert.Equal(1250.00m, total.Sums[3]);   // the replacement; the 1200.00 of the replaced one is not added
            var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "cte");
            Assert.Contains("replaces", md);
        }

        [Fact]
        public void A_manifest_and_its_events_are_listed_as_not_invoices_and_the_CTe_it_carries_is_still_a_row()
        {
            var result = Table("br-cte-autorizada.xml", "br-mdfe-autorizado.xml", "br-mdfe-evento-encerramento.xml");
            Assert.Single(result.Rows);
            var manifests = result.Skipped.Where(s => s.Reason == InvoiceSkip.Manifest).Select(s => s.File).OrderBy(f => f).ToList();
            Assert.Equal(new[] { "br-mdfe-autorizado.xml", "br-mdfe-evento-encerramento.xml" }, manifests);
            var csv = InvoiceTable.ToCsv(result, XmlLabels.For("pt"));
            Assert.Contains("CT-e", csv);
        }
    }
}
