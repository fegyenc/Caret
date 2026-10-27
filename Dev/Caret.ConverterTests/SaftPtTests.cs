using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Portugal's SAF-T (PT) 1.04_01 (Services/Conversion/XmlSaftPt.cs): the summary and the checks. The sample in samples/ is invented, with a few deliberate faults.
    public class SaftPtTests
    {
        private const string Open = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><AuditFile xmlns=\"urn:OECD:StandardAuditFile-Tax:PT_1.04_01\">";

        private static string Header(string extra = "") =>
            "<Header><AuditFileVersion>1.04_01</AuditFileVersion><TaxRegistrationNumber>500000018</TaxRegistrationNumber><TaxAccountingBasis>F</TaxAccountingBasis><CompanyName>Teste, Lda.</CompanyName>" +
            "<FiscalYear>2026</FiscalYear><StartDate>2026-02-01</StartDate><EndDate>2026-02-28</EndDate><CurrencyCode>EUR</CurrencyCode>" + extra + "</Header>";

        private static string Invoice(string no, string date, string customer, string credit, string tax, string gross, string atcud = "AB-1", string status = "N", string type = "FT") =>
            $"<Invoice><InvoiceNo>{no}</InvoiceNo><ATCUD>{atcud}</ATCUD><DocumentStatus><InvoiceStatus>{status}</InvoiceStatus></DocumentStatus><InvoiceDate>{date}</InvoiceDate><InvoiceType>{type}</InvoiceType><CustomerID>{customer}</CustomerID>" +
            $"<Line><LineNumber>1</LineNumber><CreditAmount>{credit}</CreditAmount><Tax><TaxType>IVA</TaxType><TaxCode>NOR</TaxCode><TaxPercentage>23</TaxPercentage></Tax></Line>" +
            $"<DocumentTotals><TaxPayable>{tax}</TaxPayable><NetTotal>{credit}</NetTotal><GrossTotal>{gross}</GrossTotal></DocumentTotals></Invoice>";

        private static string Customers = "<MasterFiles><Customer><CustomerID>C1</CustomerID><CustomerTaxID>500000026</CustomerTaxID><CompanyName>Cliente</CompanyName><BillingAddress><Country>PT</Country></BillingAddress></Customer></MasterFiles>";

        private static string Sales(string entries, string credit, params string[] invoices) =>
            $"<SourceDocuments><SalesInvoices><NumberOfEntries>{entries}</NumberOfEntries><TotalDebit>0.00</TotalDebit><TotalCredit>{credit}</TotalCredit>{string.Concat(invoices)}</SalesInvoices></SourceDocuments>";

        private static string Read(string xml, string language = "en")
        {
            var work = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(work, "saft.xml");
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                return DocumentConverter.Convert(path, new ConversionOptions { Language = language }).Markdown;
            }
            finally { Directory.Delete(work, true); }
        }

        private static string Sample(string language = "en") =>
            DocumentConverter.Convert(Path.Combine(TestPaths.Samples, "pt-saft-faturacao.xml"), new ConversionOptions { Language = language }).Markdown;

        [Fact]
        public void The_summary_says_whose_file_it_is_the_period_the_software_and_what_it_holds()
        {
            var md = Sample();
            Assert.StartsWith("# SAF-T (PT) · Empresa de Exemplo, Lda. · 2026", md);
            Assert.Contains("> File: pt-saft-faturacao.xml · 1.04_01 · F — Invoicing", md);
            Assert.Contains("- **NIF:** 500000018", md);
            Assert.Contains("- **Period:** 2026-02-01 → 2026-02-28", md);
            Assert.Contains("- **Software:** Faturador Exemplo/Software de Teste, Lda. 1.0", md);
            Assert.Contains("| Customers | 3 |", md);
            Assert.Contains("| Sales documents | 7 |", md);
            // no list of all the fields: a file with thousands of documents would not be readable
            Assert.DoesNotContain("All fields of the file", md);
        }

        [Fact]
        public void The_sales_are_added_by_type_with_credit_notes_subtracted_and_voided_documents_left_out()
        {
            var md = Sample();
            Assert.Contains("| FT — Invoice | 4 |  | 440.00 | 71.40 | 513.40 |", md);
            Assert.Contains("| FS — Simplified invoice | 2 | 1 | 10.00 | 2.30 | 12.30 |", md);
            Assert.Contains("| NC — Credit note | 1 |  | 50.00 | 11.50 | 61.50 |", md);
            Assert.Contains("| 7 | 1 | 400.00 | 62.20 | 464.20 |", md);
            Assert.Contains("- **Documents to the final consumer (NIF 999999990):** 1 · 12.30", md);
            // by VAT rate, from the lines, the credit note subtracted from the base
            Assert.Contains("| IVA PT NOR | 23 % | 260.00 | 59.80 |", md);
            Assert.Contains("| IVA PT RED | 6 % | 40.00 | 2.40 |", md);
            Assert.Contains("| Cliente Exemplo, S.A. | 500000026 | 3 | 226.90 |", md);
        }

        [Fact]
        public void The_checks_find_what_the_sample_has_wrong_and_say_it_in_plain_words()
        {
            var md = Sample();
            Assert.Contains("- Customers with a Portuguese address and an invalid NIF check digit (1): 200000013 (Cliente Inventado, Unipessoal Lda.).", md);
            Assert.Contains("- The header of the sales documents says 8 entries; the file has 7 documents (the header counts every one, the voided ones too).", md);
            Assert.Contains("(1): FT 2026A/5: 125.00 ≠ 100.00 + 23.00.", md);
            Assert.Contains("- Documents dated outside the period of the file (2026-02-01 to 2026-02-28) (1): FT 2026A/5 (2026-03-02).", md);
            Assert.Contains("- Numbers missing in the numbering of a series (1): FT 2026A: 3.", md);
            Assert.Contains("- Documents dated 2023 or later with no ATCUD, or an ATCUD of 0 (1): the first is FT 2026A/5.", md);
            Assert.Contains("- Customer codes used in documents but not in the customer list (1): C009.", md);
            Assert.Contains("- Lines with 0 % VAT and no exemption code or reason (1).", md);
            // the totals of the header fit the lines without the voided document: no finding for them
            Assert.DoesNotContain("TotalCredit in the header", md);
            Assert.DoesNotContain("TotalDebit in the header", md);
            Assert.Contains("not the validator of the Autoridade Tributária", md);
        }

        [Fact]
        public void A_file_with_nothing_wrong_says_so_and_still_says_what_the_checks_are_not()
        {
            var xml = Open + Header() + Customers + Sales("2", "200.00",
                Invoice("FT A/1", "2026-02-03", "C1", "100.00", "23.00", "123.00"),
                Invoice("FT A/2", "2026-02-04", "C1", "100.00", "23.00", "123.00")) + "</AuditFile>";
            var md = Read(xml);
            Assert.Contains("The checks below found nothing.", md);
            Assert.Contains("a file with nothing here can still be refused", md);
        }

        [Fact]
        public void The_header_counts_every_document_and_its_totals_leave_out_those_with_status_A_or_F()
        {
            // Portaria 302/2016 (4.1.1 to 4.1.3): NumberOfEntries includes the voided (A) and the invoiced (F) documents, TotalCredit and TotalDebit exclude them
            string File(string entries, string credit) => Open + Header() + Customers + Sales(entries, credit,
                Invoice("FT A/1", "2026-02-03", "C1", "100.00", "23.00", "123.00"),
                Invoice("FT A/2", "2026-02-04", "C1", "50.00", "11.50", "61.50", status: "A"),
                Invoice("FT A/3", "2026-02-05", "C1", "30.00", "6.90", "36.90", status: "F")) + "</AuditFile>";
            var right = Read(File("3", "100.00"));
            Assert.Contains("found nothing", right);
            Assert.Contains("- **Documents with status F (already invoiced in another document), left out of the totals:** 1", right);
            // the sum of all the lines is not what the header has to say, and neither is the count without the voided ones
            var wrong = Read(File("1", "180.00"));
            Assert.Contains("The header of the sales documents says 1 entries; the file has 3 documents (the header counts every one, the voided ones too).", wrong);
            Assert.Contains("TotalCredit in the header of the sales documents is 180.00; the lines of the documents without status A or F add up to 100.00.", wrong);
        }

        [Fact]
        public void A_gap_is_a_number_missing_between_two_that_are_there_and_a_voided_document_is_not_one()
        {
            string No(int n) => Invoice("FT A/" + n, "2026-02-03", "C1", "10.00", "2.30", "12.30");
            var xml = Open + Header() + Customers + Sales("6", "50.00", No(1), No(2), No(5), No(6), No(9)) + "</AuditFile>";
            var md = Read(xml);
            Assert.Contains("Numbers missing in the numbering of a series (4): FT A: 3–4, 7–8.", md);
            var voided = Open + Header() + Customers + Sales("3", "20.00", No(1), Invoice("FT A/2", "2026-02-03", "C1", "10.00", "2.30", "12.30", status: "A"), No(3)) + "</AuditFile>";
            Assert.DoesNotContain("Numbers missing", Read(voided));
            // the same number twice
            var twice = Open + Header() + Customers + Sales("2", "20.00", No(1), No(1)) + "</AuditFile>";
            Assert.Contains("Document numbers that appear more than once (1): FT A/1.", Read(twice));
        }

        [Fact]
        public void The_entries_of_working_documents_and_payments_count_the_voided_ones_too()
        {
            string Work(string no, string status) => $"<WorkDocument><DocumentNumber>OR A/{no}</DocumentNumber><DocumentStatus><WorkStatus>{status}</WorkStatus></DocumentStatus><WorkDate>2026-02-03</WorkDate><WorkType>OR</WorkType><CustomerID>C1</CustomerID><DocumentTotals><TaxPayable>0</TaxPayable><NetTotal>10.00</NetTotal><GrossTotal>10.00</GrossTotal></DocumentTotals></WorkDocument>";
            string File(string entries) => Open + Header() + Customers + $"<SourceDocuments><WorkingDocuments><NumberOfEntries>{entries}</NumberOfEntries><TotalDebit>0</TotalDebit><TotalCredit>0</TotalCredit>{Work("1", "N")}{Work("2", "A")}</WorkingDocuments></SourceDocuments></AuditFile>";
            Assert.Contains("found nothing", Read(File("2")));
            var wrong = Read(File("1"));   // the count without the voided one
            Assert.Contains("The header of the working documents says 1 entries; the file has 2.", wrong);
            Assert.Contains("| OR — Quote | 2 | 1 | 10.00 |", wrong);
        }

        [Fact]
        public void Every_repeated_number_is_counted_and_a_huge_declared_count_does_not_stop_the_summary()
        {
            string No(int n) => Invoice("FT A/" + n, "2026-02-03", "C1", "10.00", "2.30", "12.30");
            // seven numbers, each twice: seven repeats, five of them named; the header declares more entries than a number of 32 bits holds
            var invoices = string.Concat(Enumerable.Range(1, 7).Select(n => No(n) + No(n)));
            var md = Read(Open + Header() + Customers + Sales("99999999999999", "140.00", invoices) + "</AuditFile>");
            Assert.Contains("Document numbers that appear more than once (7): FT A/1, FT A/2, FT A/3, FT A/4, FT A/5, ….", md);
            Assert.Contains("The header of the sales documents says 99999999999999 entries; the file has 14 documents", md);
            var fraction = Read(Open + Header() + Customers + Sales("8.5", "10.00", No(1)) + "</AuditFile>");
            Assert.Contains("says 8.5 entries; the file has 1 documents", fraction);
            // a small fraction is not rounded to a zero that looks equal to the real count
            var tiny = Read(Open + Header() + Customers + "<SourceDocuments><SalesInvoices><NumberOfEntries>0.001</NumberOfEntries><TotalDebit>0</TotalDebit><TotalCredit>0</TotalCredit></SalesInvoices></SourceDocuments></AuditFile>");
            Assert.Contains("says 0.001 entries; the file has 0 documents", tiny);
        }

        [Fact]
        public void The_check_digit_of_a_Portuguese_tax_number_is_the_modulus_11_one()
        {
            Assert.True(XmlSaftPt.IsValidNif("500000018"));
            Assert.True(XmlSaftPt.IsValidNif("500000026"));
            Assert.False(XmlSaftPt.IsValidNif("500000019"));
            Assert.False(XmlSaftPt.IsValidNif("200000013"));
            Assert.False(XmlSaftPt.IsValidNif("400000018"));     // 4 is not a first digit of a tax number (45 is)
            Assert.False(XmlSaftPt.IsValidNif("50000001"));      // eight digits
            Assert.False(XmlSaftPt.IsValidNif("5000000AB"));
            Assert.False(XmlSaftPt.IsValidNif(null));
            // the company's own number is checked too
            var md = Read(Open + Header().Replace("500000018", "500000019") + "</AuditFile>");
            Assert.Contains("The tax number (NIF) of the company, 500000019, does not have a valid check digit.", md);
        }

        [Fact]
        public void An_accounting_file_is_checked_for_entries_that_do_not_balance()
        {
            string Tx(string id, string debit, string credit) =>
                $"<Transaction><TransactionID>{id}</TransactionID><Lines><DebitLine><AccountID>11</AccountID><DebitAmount>{debit}</DebitAmount></DebitLine><CreditLine><AccountID>71</AccountID><CreditAmount>{credit}</CreditAmount></CreditLine></Lines></Transaction>";
            var xml = Open + Header().Replace("<TaxAccountingBasis>F", "<TaxAccountingBasis>C") +
                "<GeneralLedgerEntries><NumberOfEntries>2</NumberOfEntries><TotalDebit>150.00</TotalDebit><TotalCredit>140.00</TotalCredit><Journal><JournalID>1</JournalID>" +
                Tx("2026-02-01 1/1", "100.00", "100.00") + Tx("2026-02-02 1/2", "50.00", "40.00") + "</Journal></GeneralLedgerEntries></AuditFile>";
            var md = Read(xml);
            Assert.Contains("> File: saft.xml · 1.04_01 · C — Accounting", md);
            Assert.Contains("| Ledger transactions | 2 |", md);
            Assert.Contains("Ledger transactions where debit and credit are not equal (1): 2026-02-02 1/2 (50.00 ≠ 40.00).", md);
            Assert.DoesNotContain("TotalDebit in the header", md);   // 150 is the sum of the debits
        }

        [Fact]
        public void The_words_are_in_Portuguese_of_Portugal_and_in_Spanish_with_the_same_findings()
        {
            var pt = Sample("pt");
            Assert.Contains("## O ficheiro", pt);
            Assert.Contains("- **Tipo de ficheiro:** F — Faturação", pt);
            Assert.Contains("| FT — Fatura | 4 |", pt);
            Assert.Contains("- Números em falta na numeração de uma série (1): FT 2026A: 3.", pt);
            Assert.Contains("- Clientes com morada em Portugal e um NIF com dígito de controlo inválido (1): 200000013", pt);
            Assert.Contains("não o validador da Autoridade Tributária", pt);
            var es = Sample("es");
            Assert.Contains("## El archivo", es);
            Assert.Contains("| FT — Factura | 4 |", es);
            Assert.Contains("- Números que faltan en la numeración de una serie (1): FT 2026A: 3.", es);
            Assert.Contains("no el validador de la Autoridade Tributária", es);
            Assert.DoesNotContain("omprob", es);
            Assert.DoesNotContain("omprueb", es);
        }

        [Fact]
        public void Another_countrys_audit_file_is_not_taken_for_a_portuguese_one()
        {
            var md = Read("<AuditFile xmlns=\"urn:StandardAuditFile-Taxation-financial:NO\"><Header><CompanyName>X</CompanyName></Header></AuditFile>");
            Assert.DoesNotContain("SAF-T (PT)", md);
        }
    }
}
