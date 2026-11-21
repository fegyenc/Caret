using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Typedown.WinUI.Services.Conversion;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Caret.ConverterTests
{
    // A folder of invoices compared with the list of the tax authority (Services/Conversion/InvoiceCheck.cs). The invoices are the invented
    // Colombian ones of InvoiceTableTests (issuer NIT 900000001, customer 1000000000) and the SAT's test-sealed CFDI; the lists are made here in
    // the layouts the authorities give: the SAT's metadata (~), a DIAN-like export (CSV and Excel).
    public class InvoiceCheckTests
    {
        private static string Invoice(string number, char cuf, string date, string issuerNit = "900000001", string customerNit = "1000000000") =>
            InvoiceTableTests.Invoice(number, InvoiceTableTests.Cuf(cuf), date, "Proveedor S.A.S.", "Cliente S.A.S.", "100.00", "19.00", "119.00")
                .Replace(">900000001<", ">" + issuerNit + "<").Replace(">1000000000<", ">" + customerNit + "<");

        private static string Cufe(char c) => InvoiceTableTests.Cuf(c).ToUpperInvariant();

        private static InvoiceTableResult Table(params (string Name, string Xml)[] files)
        {
            var result = new InvoiceTableResult();
            foreach (var (name, xml) in files)
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) InvoiceTable.Add(result, stream, name);
            InvoiceTable.Finish(result);
            return result;
        }

        private const string DianHeader = "Tipo de documento;CUFE/CUDE;Folio;Prefijo;Fecha Emisión;NIT Emisor;Nombre Emisor;NIT Receptor;Nombre Receptor;Total;Estado";

        private static string DianLine(char cuf, string folio, string date, string state, string issuer = "900000001") =>
            $"Factura electrónica;{InvoiceTableTests.Cuf(cuf)};{folio};SETP;{date};{issuer};Proveedor S.A.S.;1000000000;Cliente S.A.S.;119.00;{state}";

        private static AuthorityList Csv(InvoiceTableResult table, params string[] lines)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));
            return InvoiceCheck.FromRows(InvoiceCheck.TextRows(stream), "lista.csv", table);
        }

        [Fact]
        public void A_dian_list_finds_the_missing_xml_the_rejected_one_and_the_one_not_in_the_list()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")), ("F2.xml", Invoice("F2", 'b', "2026-06-05")), ("F3.xml", Invoice("F3", 'c', "2026-06-10")));
            var list = Csv(table, DianHeader,
                DianLine('a', "F1", "02-06-2026", "Aprobado"),
                DianLine('b', "F2", "05-06-2026", "Rechazado"),
                DianLine('d', "F4", "20-06-2026", "Aprobado con notificación"));
            Assert.Equal(1, list.CodeColumn);
            Assert.Equal(10, list.StatusColumn);
            Assert.Equal("2026-06-02", list.From);
            Assert.Equal("2026-06-20", list.To);

            var check = InvoiceCheck.Compare(table, list, "Cancelled ({0})");
            Assert.Equal(1, check.Matched);
            Assert.Equal(Cufe('d'), Assert.Single(check.Missing).Code);
            var (rejected, entry) = Assert.Single(check.Cancelled);
            Assert.Equal("F2", rejected.Number);
            Assert.Equal("Rechazado", entry.Status);
            Assert.Equal("F3", Assert.Single(check.NotInList).Number);

            // the rejected invoice stays in the table, says why, and leaves the totals
            Assert.True(rejected.Void);
            Assert.Contains("Cancelled (Rechazado)", rejected.Answer);
            Assert.Equal(200m, InvoiceTable.Totals(table).Single().Sums[0]);

            var md = InvoiceTable.ToMarkdown(table, XmlLabels.For("es"), "junio", check);
            Assert.Contains("## Comparación con lista.csv", md);
            Assert.Contains("### En la lista, pero sin XML en la carpeta (1)", md);
            Assert.Contains("### Cancelados o rechazados en la lista, pero en la carpeta (1)", md);
            Assert.Contains("### En la carpeta, pero no en la lista (1)", md);
            Assert.Contains(Cufe('d'), md);
        }

        [Fact]
        public void Documents_of_another_period_or_another_company_are_not_compared()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")),
                ("mayo.xml", Invoice("F0", 'e', "2026-05-20")),
                ("otra.xml", Invoice("X1", 'f', "2026-06-03", issuerNit: "800000002", customerNit: "800000003")));
            var list = Csv(table, DianHeader, DianLine('a', "F1", "02-06-2026", "Aprobado"), DianLine('g', "F5", "30-06-2026", "Aprobado"));
            Assert.Contains("1000000000", list.Parties);
            var check = InvoiceCheck.Compare(table, list, "{0}");
            Assert.Empty(check.NotInList);
            Assert.Equal(2, check.OutOfScope);
            Assert.Contains("2 documents of the folder are of another period or another company", InvoiceCheck.ToMarkdown(check, XmlLabels.For("en")));
        }

        [Fact]
        public void An_id_on_every_line_that_the_matched_documents_do_not_have_is_not_the_company_of_the_list()
        {
            // every line is of the same (other) issuer and the same client, but the folder's invoice is between two other companies: the matched
            // documents decide, so nothing is wrongly left out
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")), ("F9.xml", Invoice("F9", 'h', "2026-06-03")));
            var list = Csv(table, DianHeader,
                DianLine('a', "F1", "02-06-2026", "Aprobado", issuer: "700000007").Replace(";1000000000;", ";600000006;"),
                DianLine('b', "F8", "03-06-2026", "Aprobado", issuer: "700000007").Replace(";1000000000;", ";600000006;"));
            var check = InvoiceCheck.Compare(table, list, "{0}");
            Assert.Empty(list.Parties);
            Assert.Equal("F9", Assert.Single(check.NotInList).Number);
        }

        [Fact]
        public void The_metadata_of_the_sat_is_read_with_its_tildes_its_quotes_and_its_status_0_for_cancelled()
        {
            var cfdi = File.ReadAllText(Path.Combine(TestPaths.ProjectFolder, "sat-fixtures", "cfdi40-valid.xml"));
            var uuid = Regex.Match(cfdi, "UUID=\"([^\"]+)\"").Groups[1].Value;
            var fecha = Regex.Match(cfdi, " Fecha=\"([^\"]+)\"").Groups[1].Value.Replace('T', ' ');
            var table = Table(("cfdi.xml", cfdi));
            var metadata = string.Join("\r\n",
                "Uuid~RfcEmisor~NombreEmisor~RfcReceptor~NombreReceptor~RfcPac~FechaEmision~FechaCertificacionSat~Monto~EfectoComprobante~Estatus~FechaCancelacion",
                $"{uuid.ToLowerInvariant()}~EKU9003173C9~ESCUELA \"KEMPER\" URGATE~URE180429TM6~UNIVERSIDAD ROBOTICA ESPAÑOLA~SAT970701NN3~{fecha}~{fecha}~1000.00~I~0~{fecha}",
                $"11111111-2222-3333-4444-555555555555~EKU9003173C9~ESCUELA \"KEMPER\" URGATE~URE180429TM6~UNIVERSIDAD ROBOTICA ESPAÑOLA~SAT970701NN3~{fecha}~{fecha}~500.00~I~1~");
            AuthorityList list;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(metadata))) list = InvoiceCheck.FromRows(InvoiceCheck.TextRows(stream), "metadata.txt", table);
            Assert.Equal(0, list.CodeColumn);
            Assert.Equal(10, list.StatusColumn);
            Assert.Equal(2, list.Entries.Count);
            Assert.Equal("ESCUELA \"KEMPER\" URGATE", list.Entries[0].Cells[2]);

            var check = InvoiceCheck.Compare(table, list, "Cancelado ({0})");
            var (row, entry) = Assert.Single(check.Cancelled);
            Assert.Equal("0", entry.Status);
            Assert.True(row.Void);
            Assert.Equal("11111111-2222-3333-4444-555555555555", Assert.Single(check.Missing).Code);
        }

        [Fact]
        public void A_payment_complement_of_the_list_is_in_the_folder_and_a_cancelled_one_pays_nothing()
        {
            const string invoice = "AAAAAAAA-0000-4000-8000-000000000001";
            const string rep1 = "11111111-0000-4000-8000-00000000000A";
            const string rep2 = "22222222-0000-4000-8000-00000000000B";
            var table = Table(
                ("a.xml", InvoicePaymentsTests.Invoice(invoice, "1", "1000.00", "PPD")),
                ("rep1.xml", InvoicePaymentsTests.Rep(rep1, "2026-06-15", (invoice, 1, "1000.00", "400.00", "600.00"))),
                ("rep2.xml", InvoicePaymentsTests.Rep(rep2, "2026-06-20", (invoice, 2, "600.00", "600.00", "0.00"))));
            Assert.Equal(0m, Assert.Single(table.Rows).PpdBalance);

            // the SAT's metadata of the month: the invoice and the first complement valid, the second complement cancelled
            var metadata = string.Join("\r\n",
                "Uuid~RfcEmisor~NombreEmisor~RfcReceptor~NombreReceptor~RfcPac~FechaEmision~FechaCertificacionSat~Monto~EfectoComprobante~Estatus~FechaCancelacion",
                $"{invoice}~EKU9003173C9~PROVEEDOR~XAXX010101000~CLIENTE~SAT970701NN3~2026-06-01 10:00:00~2026-06-01 10:00:00~1000.00~I~1~",
                $"{rep1}~EKU9003173C9~PROVEEDOR~XAXX010101000~CLIENTE~SAT970701NN3~2026-06-15 12:00:00~2026-06-15 12:00:00~0.00~P~1~",
                $"{rep2}~EKU9003173C9~PROVEEDOR~XAXX010101000~CLIENTE~SAT970701NN3~2026-06-20 12:00:00~2026-06-20 12:00:00~0.00~P~0~2026-06-21 09:00:00");
            AuthorityList list;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(metadata))) list = InvoiceCheck.FromRows(InvoiceCheck.TextRows(stream), "metadata.txt", table);
            var check = InvoiceCheck.Compare(table, list, "{0}");

            // the complements are files of the folder, not rows: they are not "no XML", and the cancelled one is a finding with its file
            Assert.Empty(check.Missing);
            Assert.Equal(2, check.Matched);
            var (file, cancelledEntry) = Assert.Single(check.CancelledOthers);
            Assert.Equal("rep2.xml", file);
            Assert.Equal(rep2, cancelledEntry.Code);
            Assert.True(check.HasFindings);
            var md = InvoiceCheck.ToMarkdown(check, XmlLabels.For("en"));
            Assert.Contains("### Cancelled or rejected in the list, but in the folder (1)", md);
            Assert.Contains("rep2.xml", md);
            // the cancelled complement no longer pays: the invoice is paid in part again
            var row = Assert.Single(table.Rows);
            Assert.Equal(1, row.PpdComplements);
            Assert.Equal(600m, row.PpdBalance);
            Assert.Contains("PPD: paid 400.00, balance 600.00 (1 payment complement)", InvoiceTable.ToCsv(table, XmlLabels.For("en")));
        }

        [Fact]
        public void A_payment_complement_that_the_list_does_not_have_is_a_finding()
        {
            const string invoice = "AAAAAAAA-0000-4000-8000-000000000001";
            const string rep = "77777777-0000-4000-8000-000000000010";
            var table = Table(("a.xml", InvoicePaymentsTests.Invoice(invoice, "1", "1000.00", "PPD")),
                ("rep.xml", InvoicePaymentsTests.Rep(rep, "2026-06-15", (invoice, 1, "1000.00", "1000.00", "0.00"))));
            var list = InvoiceCheck.FromRows(new System.Collections.Generic.List<System.Collections.Generic.List<string>>
            {
                new() { "UUID", "FechaEmision", "Estatus" }, new() { invoice, "2026-06-01 10:00:00", "1" }, new() { "88888888-0000-4000-8000-000000000011", "2026-06-30 10:00:00", "1" },
            }, "metadata.txt", table);
            var check = InvoiceCheck.Compare(table, list, "{0}");
            var unlisted = Assert.Single(check.NotInList);
            Assert.Equal("rep.xml", unlisted.File);
            Assert.Equal(rep, unlisted.Cuf);
        }

        [Fact]
        public void The_column_named_for_the_code_is_taken_before_one_that_repeats_a_folder_code()
        {
            // three listed invoices that are not in the folder; a reference column repeats the code of the one that is
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var reference = InvoiceTableTests.Cuf('a');
            var list = Csv(table, "UUID;Reference;Company",
                InvoiceTableTests.Cuf('b') + ";" + reference + ";X", InvoiceTableTests.Cuf('c') + ";" + reference + ";X", InvoiceTableTests.Cuf('d') + ";" + reference + ";X");
            Assert.Equal(0, list.CodeColumn);
            var check = InvoiceCheck.Compare(table, list, "{0}");
            Assert.Equal(3, check.Missing.Count);
            Assert.Equal("F1", Assert.Single(check.NotInList).Number);
        }

        [Fact]
        public void The_own_code_column_is_taken_before_a_related_code_column()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var related = InvoiceTableTests.Cuf('a');
            var list = Csv(table, "UUID;Related UUID",
                InvoiceTableTests.Cuf('b') + ";" + related, InvoiceTableTests.Cuf('c') + ";" + related, InvoiceTableTests.Cuf('d') + ";" + related);
            Assert.Equal(0, list.CodeColumn);
            Assert.Equal(3, InvoiceCheck.Compare(table, list, "{0}").Missing.Count);
        }

        [Fact]
        public void A_company_written_with_and_without_its_check_digit_is_one_company()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var list = Csv(table, "CUFE;NIT Emisor;NIT Receptor",
                InvoiceTableTests.Cuf('a') + ";900000001;1000000000",
                InvoiceTableTests.Cuf('b') + ";9000000015;1000000099");
            Assert.Contains(list.Parties, p => p.StartsWith("900000001", System.StringComparison.Ordinal));
        }

        [Fact]
        public void A_matched_payment_complement_narrows_the_company_of_the_list()
        {
            // only a payment complement matches; the SAT's RfcPac is on every line, but the complement is not the PAC's, so the PAC is not the company
            const string rep = "77777777-0000-4000-8000-000000000020";
            const string invoice = "AAAAAAAA-0000-4000-8000-000000000021";
            var pacInvoice = InvoicePaymentsTests.Invoice("BBBBBBBB-0000-4000-8000-000000000022", "9", "100.00", "PUE")
                .Replace("Rfc=\"EKU9003173C9\"", "Rfc=\"SAT970701NN3\"").Replace("Rfc=\"XAXX010101000\"", "Rfc=\"AAA010101AAA\"");
            var table = Table(("rep.xml", InvoicePaymentsTests.Rep(rep, "2026-06-15", (invoice, 1, "100.00", "100.00", "0.00"))), ("pac.xml", pacInvoice));
            var list = InvoiceCheck.FromRows(new System.Collections.Generic.List<System.Collections.Generic.List<string>>
            {
                new() { "Uuid", "RfcEmisor", "RfcReceptor", "RfcPac", "FechaEmision", "Estatus" },
                new() { rep, "EKU9003173C9", "XAXX010101000", "SAT970701NN3", "2026-06-01 10:00:00", "1" },
                new() { invoice, "EKU9003173C9", "XAXX010101000", "SAT970701NN3", "2026-06-30 10:00:00", "1" },
            }, "metadata.txt", table);
            var check = InvoiceCheck.Compare(table, list, "{0}");
            Assert.DoesNotContain("SAT970701NN3", list.Parties);
            Assert.Empty(check.NotInList);
            Assert.Equal(1, check.OutOfScope);
        }

        [Fact]
        public void A_status_of_one_or_two_characters_is_said_with_its_column()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var list = Csv(table, "UUID;Estatus", InvoiceTableTests.Cuf('a') + ";0");
            var check = InvoiceCheck.Compare(table, list, "Cancelled ({0})");
            Assert.Contains("Cancelled (Estatus 0)", Assert.Single(table.Rows).Answer);
            Assert.Contains("Estatus 0", InvoiceCheck.ToMarkdown(check, XmlLabels.For("en")));
        }

        [Fact]
        public void An_excel_list_is_read_with_its_dates()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            byte[] xlsx;
            using (var buffer = new MemoryStream())
            {
                using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
                {
                    var workbook = document.AddWorkbookPart();
                    workbook.Workbook = new S.Workbook();
                    var styles = workbook.AddNewPart<WorkbookStylesPart>();
                    styles.Stylesheet = new S.Stylesheet(new S.Fonts(new S.Font()), new S.Fills(new S.Fill()), new S.Borders(new S.Border()),
                        new S.CellStyleFormats(new S.CellFormat()), new S.CellFormats(new S.CellFormat(), new S.CellFormat { NumberFormatId = 14U, ApplyNumberFormat = true }));
                    var sheet = workbook.AddNewPart<WorksheetPart>();
                    var data = new S.SheetData(
                        Row(1, Text("A1", "Reporte de documentos recibidos")),
                        Row(3, Text("A3", "CUFE/CUDE"), Text("B3", "Fecha Emisión"), Text("C3", "Estado")),
                        Row(4, Text("A4", InvoiceTableTests.Cuf('a')), new S.Cell { CellReference = "B4", StyleIndex = 1U, CellValue = new S.CellValue("46175") }, Text("C4", "Aprobado")),
                        Row(5, Text("A5", InvoiceTableTests.Cuf('f')), new S.Cell { CellReference = "B5", StyleIndex = 1U, CellValue = new S.CellValue("46180") }, Text("C5", "Aprobado")));
                    sheet.Worksheet = new S.Worksheet(data);
                    workbook.Workbook.AppendChild(new S.Sheets(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1U, Name = "Documentos" }));
                    workbook.Workbook.Save();
                }
                xlsx = buffer.ToArray();
            }
            var path = Path.Combine(TestPaths.NewTempFolder(), "dian.xlsx");
            try
            {
                File.WriteAllBytes(path, xlsx);
                var list = InvoiceCheck.Read(path, table);
                Assert.Equal(new[] { "CUFE/CUDE", "Fecha Emisión", "Estado" }, list.Headers);
                Assert.Equal("2026-06-02", list.From);
                Assert.Equal("2026-06-07", list.To);
                var check = InvoiceCheck.Compare(table, list, "{0}");
                Assert.Equal(1, check.Matched);
                Assert.Single(check.Missing);

                // the workbook of the table gets a sheet with the findings
                using var output = new MemoryStream();
                InvoiceTable.WriteXlsx(table, XmlLabels.For("en"), output, check);
                output.Position = 0;
                using var written = SpreadsheetDocument.Open(output, false);
                Assert.Equal("Check", written.WorkbookPart.Workbook.Sheets.Elements<S.Sheet>().Last().Name.Value);
            }
            finally { Directory.Delete(Path.GetDirectoryName(path), true); }
        }

        private static S.Row Row(uint index, params S.Cell[] cells) => new(cells) { RowIndex = index };

        private static S.Cell Text(string reference, string text) =>
            new() { CellReference = reference, DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(text)) };

        [Fact]
        public void A_list_of_one_quoted_column_is_read_as_csv()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var list = Csv(table, "\"UUID\"", "\"" + InvoiceTableTests.Cuf('a') + "\"");
            Assert.Equal(0, list.CodeColumn);
            Assert.Equal(1, InvoiceCheck.Compare(table, list, "{0}").Matched);
        }

        [Fact]
        public void Cancelable_is_not_cancelled()
        {
            // the SAT's "Cancelable sin aceptación" says the invoice can still be cancelled; "Cancelado" says it was
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")), ("F2.xml", Invoice("F2", 'b', "2026-06-03")));
            var list = Csv(table, "UUID;Estado", InvoiceTableTests.Cuf('a') + ";Cancelable sin aceptación", InvoiceTableTests.Cuf('b') + ";Cancelado");
            var check = InvoiceCheck.Compare(table, list, "{0}");
            Assert.Equal(1, check.Matched);
            Assert.Equal("F2", Assert.Single(check.Cancelled).Row.Number);
        }

        [Fact]
        public void A_list_without_unique_codes_says_that_nothing_was_compared()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var list = Csv(table, "Folio;Total", "F1;119.00");
            Assert.Equal(-1, list.CodeColumn);
            var check = InvoiceCheck.Compare(table, list, "{0}");
            Assert.False(check.HasFindings);
            Assert.Contains("No column of unique codes", InvoiceCheck.ToMarkdown(check, XmlLabels.For("en")));
        }

        [Fact]
        public void A_list_that_agrees_with_the_folder_says_so()
        {
            var table = Table(("F1.xml", Invoice("F1", 'a', "2026-06-02")));
            var check = InvoiceCheck.Compare(table, Csv(table, DianHeader, DianLine('a', "F1", "02-06-2026", "Aprobado")), "{0}");
            Assert.Contains("Todos los documentos de la lista están en la carpeta", InvoiceCheck.ToMarkdown(check, XmlLabels.For("es")));
        }

        [Fact]
        public void Codes_are_compared_without_case_spaces_or_the_prefix_of_an_access_key()
        {
            Assert.Equal("35260612345678000190550010000001231000001234", InvoiceCheck.Normalize("NFe35260612345678000190550010000001231000001234"));
            Assert.Equal("ABC-12", InvoiceCheck.Normalize(" abc-12 "));
            Assert.Equal("2026-06-01", InvoiceCheck.IsoDate("01/06/2026"));
            Assert.Equal("2026-06-01", InvoiceCheck.IsoDate("2026-06-01T10:00:00"));
            Assert.Null(InvoiceCheck.IsoDate("June 1"));
        }
    }
}
