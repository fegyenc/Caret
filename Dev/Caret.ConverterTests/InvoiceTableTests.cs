using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // A folder of Colombian electronic invoices as one table (Services/Conversion/InvoiceTable.cs). The invoices are made here from invented
    // data; the container and the credit note are the synthetic samples in samples/.
    public class InvoiceTableTests
    {
        private const string Ubl = "urn:oasis:names:specification:ubl:schema:xsd:";

        private static string Invoice(string number, string cuf, string date, string issuer, string customer, string taxExclusive, string tax, string total,
            string currency = "COP", string root = "Invoice", string extraTax = "") =>
            $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<{root} xmlns=""{Ubl}{root}-2"" xmlns:cac=""{Ubl}CommonAggregateComponents-2"" xmlns:cbc=""{Ubl}CommonBasicComponents-2"">
  <cbc:ID>{number}</cbc:ID>
  {(cuf == null ? "" : $@"<cbc:UUID schemeName=""CUFE-SHA384"">{cuf}</cbc:UUID>")}
  <cbc:IssueDate>{date}</cbc:IssueDate>
  {(root == "Invoice" ? "<cbc:InvoiceTypeCode>01</cbc:InvoiceTypeCode>" : "")}
  <cbc:DocumentCurrencyCode>{currency}</cbc:DocumentCurrencyCode>
  <cac:AccountingSupplierParty><cac:Party><cac:PartyTaxScheme><cbc:RegistrationName>{issuer}</cbc:RegistrationName><cbc:CompanyID schemeID=""1"" schemeName=""31"">900000001</cbc:CompanyID></cac:PartyTaxScheme></cac:Party></cac:AccountingSupplierParty>
  <cac:AccountingCustomerParty><cac:Party><cac:PartyTaxScheme><cbc:RegistrationName>{customer}</cbc:RegistrationName><cbc:CompanyID schemeName=""13"">1000000000</cbc:CompanyID></cac:PartyTaxScheme></cac:Party></cac:AccountingCustomerParty>
  <cac:TaxTotal><cbc:TaxAmount currencyID=""{currency}"">{tax}</cbc:TaxAmount></cac:TaxTotal>{extraTax}
  <cac:LegalMonetaryTotal>
    <cbc:TaxExclusiveAmount currencyID=""{currency}"">{taxExclusive}</cbc:TaxExclusiveAmount>
    <cbc:TaxInclusiveAmount currencyID=""{currency}"">{total}</cbc:TaxInclusiveAmount>
    <cbc:PayableAmount currencyID=""{currency}"">{total}</cbc:PayableAmount>
  </cac:LegalMonetaryTotal>
</{root}>";

        private static string Cuf(char c) => new string(c, 96);

        private static string Folder(params (string Name, string Xml)[] files)
        {
            var folder = TestPaths.NewTempFolder();
            foreach (var (name, xml) in files)
            {
                var path = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, xml, new UTF8Encoding(false));
            }
            return folder;
        }

        private static InvoiceTableResult Read(params (string Name, string Xml)[] files)
        {
            var folder = Folder(files);
            try { return InvoiceTable.ReadFolder(folder); }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Each_invoice_is_a_row_sorted_by_date_and_the_container_gives_the_invoice_and_the_answer()
        {
            var result = Read(
                ("b.xml", Invoice("F2", Cuf('b'), "2026-02-01", "Proveedor Uno", "Cliente", "200.00", "38.00", "238.00")),
                ("a.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor Uno", "Cliente", "100.00", "19.00", "119.00")),
                ("sub/c.xml", File.ReadAllText(Path.Combine(TestPaths.Samples, "co-factura-adjunta.xml"))));
            Assert.Equal(new[] { "F2", "F1", "SETP990000001" }, result.Rows.Select(r => r.Number));
            Assert.Empty(result.Skipped);
            var container = result.Rows.Single(r => r.Number == "SETP990000001");
            Assert.Equal("900000001-1", container.IssuerId);
            Assert.Equal("Cliente de Prueba Ltda.", container.Customer);
            Assert.Equal("1000000.00", container.Base.Text);
            Assert.Equal("1190000.00", container.Total.Text);
            Assert.Equal("COP", container.Currency);
            Assert.Equal("02: Documento validado por la DIAN (texto de ejemplo)", container.Answer);
            Assert.Equal("sub/c.xml", container.File);
        }

        [Fact]
        public void The_same_unique_code_twice_is_one_document_and_the_second_file_is_listed()
        {
            var xml = Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor", "Cliente", "100.00", "19.00", "119.00");
            var result = Read(("a.xml", xml), ("copy of a.xml", xml));
            Assert.Single(result.Rows);
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(InvoiceSkip.Duplicate, skipped.Reason);
            Assert.Equal("copy of a.xml", skipped.File);
            Assert.Equal("a.xml", skipped.Detail);
        }

        [Fact]
        public void Invoices_without_a_unique_code_are_never_taken_for_duplicates()
        {
            var xml = Invoice("F1", null, "2026-03-01", "Proveedor", "Cliente", "100.00", "19.00", "119.00");
            var result = Read(("a.xml", xml), ("b.xml", xml));
            Assert.Equal(2, result.Rows.Count);
        }

        [Fact]
        public void Files_that_are_not_in_the_table_are_listed_with_their_reason()
        {
            var result = Read(
                ("good.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor", "Cliente", "100.00", "19.00", "119.00")),
                ("broken.xml", "<Invoice><oops></Invoice>"),
                ("config.xml", "<settings><a>1</a></settings>"),
                ("other-namespace.xml", "<Invoice xmlns=\"http://example.com/not-ubl\"><ID>1</ID></Invoice>"),
                ("answer.xml", $"<ApplicationResponse xmlns=\"{Ubl}ApplicationResponse-2\"><ID xmlns=\"{Ubl}CommonBasicComponents-2\">1</ID></ApplicationResponse>"));
            Assert.Single(result.Rows);
            Assert.Equal(5, result.FilesSeen);
            var reasons = result.Skipped.ToDictionary(s => s.File, s => s.Reason);
            Assert.Equal(InvoiceSkip.NotWellFormed, reasons["broken.xml"]);
            Assert.Equal(InvoiceSkip.NotInvoice, reasons["config.xml"]);
            Assert.Equal(InvoiceSkip.NotInvoice, reasons["other-namespace.xml"]);
            Assert.Equal(InvoiceSkip.AnswerOnly, reasons["answer.xml"]);
            var broken = result.Skipped.Single(s => s.File == "broken.xml");
            Assert.Equal(1, broken.Line);
            Assert.True(broken.Position > 0);
        }

        [Fact]
        public void An_xml_file_that_is_not_text_at_all_is_listed_and_does_not_stop_the_rest()
        {
            var folder = Folder(("good.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor", "Cliente", "100.00", "19.00", "119.00")));
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "binary.xml"), new byte[] { 0, 1, 2, 255, 254, 0 });
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Single(result.Rows);
                Assert.Equal(InvoiceSkip.NotWellFormed, Assert.Single(result.Skipped).Reason);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Totals_are_per_currency_with_credit_notes_subtracted_and_the_taxes_of_several_blocks_added()
        {
            var result = Read(
                ("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "P", "C", "100.00", "19.00", "119.00",
                    extraTax: "<cac:TaxTotal><cbc:TaxAmount currencyID=\"COP\">8.00</cbc:TaxAmount></cac:TaxTotal>")),
                ("2.xml", Invoice("F2", Cuf('b'), "2026-03-02", "P", "C", "50.50", "9.50", "60.00")),
                ("3.xml", Invoice("NC1", Cuf('c'), "2026-03-03", "P", "C", "20.00", "3.80", "23.80", root: "CreditNote")),
                ("4.xml", Invoice("U1", Cuf('d'), "2026-03-04", "P", "C", "10.00", "0.00", "10.00", currency: "USD")));
            Assert.Equal("27.00", result.Rows.Single(r => r.Number == "F1").Taxes.Text);
            var totals = InvoiceTable.Totals(result);
            var cop = totals.Single(t => t.Currency == "COP");
            Assert.Equal(130.50m, cop.Sums[0]);          // 100.00 + 50.50 - 20.00
            Assert.Equal(32.70m, cop.Sums[1]);           // 27.00 + 9.50 - 3.80
            Assert.Equal(155.20m, cop.Sums[3]);          // 119.00 + 60.00 - 23.80
            Assert.Equal(10.00m, totals.Single(t => t.Currency == "USD").Sums[0]);
        }

        [Fact]
        public void An_amount_that_is_not_a_plain_number_is_kept_as_text_and_has_no_total()
        {
            var result = Read(
                ("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "P", "C", "100.00", "19.00", "119.00")),
                ("2.xml", Invoice("F2", Cuf('b'), "2026-03-02", "P", "C", "1,000.50", "19.00", "119.00")));
            Assert.Equal("1,000.50", result.Rows.Single(r => r.Number == "F2").Base.Text);
            var total = InvoiceTable.Totals(result).Single();
            Assert.Null(total.Sums[0]);
            Assert.Equal(238.00m, total.Sums[3]);
            // nobody has withholdings: no total for that column (not a total of zero)
            Assert.Null(total.Sums[2]);
        }

        [Fact]
        public void A_file_that_is_not_well_formed_says_where_in_the_language_of_the_table()
        {
            var result = Read(("sub/broken.xml", "<a>\n<b></a>"));
            Assert.Contains("| sub/broken.xml | Not well-formed XML (line 2, position", InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), ""));
            Assert.Contains("XML mal formado (línea 2, posición", InvoiceTable.ToMarkdown(result, XmlLabels.For("es"), ""));
        }

        [Fact]
        public void The_markdown_has_the_table_the_totals_and_the_files_that_are_not_included()
        {
            var result = Read(
                ("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor | Uno", "Cliente", "100.00", "19.00", "119.00")),
                ("x.xml", "<a/>"));
            var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "April");
            Assert.StartsWith("# Invoices table", md);
            Assert.Contains("1 documents from 2 XML files · April", md);
            Assert.Contains("| Issue date | Type | Number |", md);
            Assert.Contains("Electronic sales invoice", md);
            Assert.Contains("Proveedor \\| Uno", md);
            Assert.Contains("**Total (credit notes subtracted) COP**", md);
            Assert.Contains("**119.00**", md);
            Assert.Contains("## Files not included", md);
            Assert.Contains("| x.xml | Not an invoice, credit note or debit note that this reader knows |", md);
        }

        [Fact]
        public void The_markdown_reads_in_Spanish_and_does_not_depend_on_the_regional_settings()
        {
            var result = Read(("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor", "Cliente", "1000.50", "190.10", "1190.60")));
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-CO");
                var md = InvoiceTable.ToMarkdown(result, XmlLabels.For("es"), "");
                Assert.StartsWith("# Tabla de facturas", md);
                Assert.Contains("Factura electrónica de venta", md);
                Assert.Contains("Respuesta de la DIAN", md);
                Assert.Contains("**1190.60**", md);
                Assert.Contains("**Total (notas crédito restadas) COP**", md);
                Assert.Contains("1000.50", InvoiceTable.ToCsv(result, XmlLabels.For("es")));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Fact]
        public void The_csv_quotes_what_needs_it_and_does_not_let_a_text_run_as_a_formula()
        {
            var result = Read(
                ("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "=HYPERLINK(&quot;http://example.com&quot;)", "Pérez, \"Juan\"", "100.00", "19.00", "119.00")),
                ("2.xml", Invoice("-5+3", Cuf('b'), "2026-03-02", "Normal", "Cliente", "-20.00", "0.00", "-20.00")));
            var csv = InvoiceTable.ToCsv(result, XmlLabels.For("en"));
            var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Issue date,Type,Number,", lines[0]);
            Assert.Contains("'=HYPERLINK(\"\"http://example.com\"\")", lines[1]);
            Assert.Contains("\"Pérez, \"\"Juan\"\"\"", lines[1]);
            // a negative amount is a number, not a formula
            Assert.Contains(",-20.00,", lines[2]);
            Assert.DoesNotContain("'-20.00", lines[2]);
            // an identifier that would run as a formula is text and gets the apostrophe
            Assert.Contains(",'-5+3,", lines[2]);
        }

        [Fact]
        public void The_excel_file_has_the_rows_as_text_and_the_amounts_as_numbers()
        {
            var result = Read(
                ("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor", "Cliente", "100.00", "19.00", "119.00")),
                ("2.xml", Invoice("NC1", Cuf('b'), "2026-03-02", "Proveedor", "Cliente", "20.00", "3.80", "23.80", root: "CreditNote")));
            var folder = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(folder, "table.xlsx");
                using (var stream = File.Create(path)) InvoiceTable.WriteXlsx(result, XmlLabels.For("en"), stream);

                // it opens like any workbook: the converter that reads Excel files reads it back
                var md = DocumentConverter.Convert(path, new ConversionOptions()).Markdown;
                Assert.Contains("## Invoices table", md);
                Assert.Contains("F1", md);
                Assert.Contains("Credit note", md);
                Assert.Contains("Total (credit notes subtracted) COP", md);

                // and the amounts are numbers (not text) in the cells
                using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false);
                // Excel is stricter than the converter: the file follows the Open XML schema
                var problems = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).Select(e => e.Description).ToList();
                Assert.True(problems.Count == 0, string.Join(" | ", problems));
                var cells = document.WorkbookPart.WorksheetParts.Single().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().ToList();
                var baseOfInvoice = cells.Single(c => c.CellReference == "I2");
                Assert.Null(baseOfInvoice.DataType);
                Assert.Equal("100.00", baseOfInvoice.CellValue.Text);
                var totalBase = cells.Single(c => c.CellReference == "I4");
                Assert.Equal("80.00", totalBase.CellValue.Text);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void The_excel_file_can_be_written_to_a_file_opened_for_writing_only()
        {
            var result = Read(("1.xml", Invoice("F1", Cuf('a'), "2026-03-01", "Proveedor", "Cliente", "100.00", "19.00", "119.00")));
            var folder = TestPaths.NewTempFolder();
            try
            {
                var path = Path.Combine(folder, "table.xlsx");
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) InvoiceTable.WriteXlsx(result, XmlLabels.For("en"), stream);
                Assert.Contains("F1", DocumentConverter.Convert(path, new ConversionOptions()).Markdown);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Hidden_and_system_files_are_read_too_and_counted()
        {
            var folder = Folder(("visible.xml", Invoice("F1", Cuf('a'), "2026-03-01", "P", "C", "100.00", "19.00", "119.00")),
                ("hidden.xml", Invoice("F2", Cuf('b'), "2026-03-02", "P", "C", "50.00", "9.50", "59.50")));
            try
            {
                File.SetAttributes(Path.Combine(folder, "hidden.xml"), FileAttributes.Hidden);
                Directory.CreateDirectory(Path.Combine(folder, "system-folder"));
                File.WriteAllText(Path.Combine(folder, "system-folder", "inside.xml"), Invoice("F3", Cuf('c'), "2026-03-03", "P", "C", "10.00", "1.90", "11.90"));
                File.SetAttributes(Path.Combine(folder, "system-folder"), FileAttributes.Hidden | FileAttributes.System);
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Equal(3, result.FilesSeen);
                Assert.Equal(new[] { "F1", "F2", "F3" }, result.Rows.Select(r => r.Number));
            }
            finally
            {
                File.SetAttributes(Path.Combine(folder, "system-folder"), FileAttributes.Normal);
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void A_link_to_another_folder_is_listed_and_not_followed()
        {
            var folder = Folder(("a.xml", Invoice("F1", Cuf('a'), "2026-03-01", "P", "C", "100.00", "19.00", "119.00")));
            var target = Folder(("elsewhere.xml", Invoice("F9", Cuf('z'), "2026-03-09", "P", "C", "1.00", "0.19", "1.19")));
            var link = Path.Combine(folder, "linked");
            try
            {
                // a junction needs no privilege (a symbolic link does)
                var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false });
                made.WaitForExit();
                Assert.True(Directory.Exists(link), "the test could not make a junction");
                var result = InvoiceTable.ReadFolder(folder);
                Assert.Equal(new[] { "F1" }, result.Rows.Select(r => r.Number));
                var skipped = Assert.Single(result.Skipped);
                Assert.Equal("linked/", skipped.File);
                Assert.Equal(InvoiceSkip.Unreadable, skipped.Reason);
            }
            finally
            {
                if (Directory.Exists(link)) Directory.Delete(link);   // the link only, not what it points to
                Directory.Delete(folder, true);
                Directory.Delete(target, true);
            }
        }

        // A stream that opens and then fails while it is read, as a network drive does when it goes away.
        private sealed class FailingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set { } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new IOException("the network name is no longer available");
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) { }
        }

        [Fact]
        public void A_file_that_cannot_be_read_to_its_end_is_listed_and_counted_once()
        {
            var result = new InvoiceTableResult();
            InvoiceTable.Add(result, new FailingStream(), "gone.xml");
            Assert.Equal(1, result.FilesSeen);
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(InvoiceSkip.Unreadable, skipped.Reason);
            Assert.Contains("no longer available", skipped.Detail);
        }

        [Fact]
        public void An_empty_folder_gives_an_empty_result_that_still_writes_valid_files()
        {
            var result = Read();
            Assert.Empty(result.Rows);
            Assert.Equal(0, result.FilesSeen);
            using var xlsx = new MemoryStream();
            InvoiceTable.WriteXlsx(result, XmlLabels.For("en"), xlsx);
            Assert.True(xlsx.Length > 0);
            Assert.StartsWith("# Invoices table", InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), ""));
        }
    }
}
