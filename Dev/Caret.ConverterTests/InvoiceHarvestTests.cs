using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MimeKit;
using OpenMcdf;
using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Invoices inside ZIP archives and saved e-mails, read into the folder table (Services/Conversion/InvoiceTable.Containers.cs). The archives and
    // mails are made here; the invoices are the invented Colombian ones of InvoiceTableTests and the SAT's test-sealed CFDI.
    public class InvoiceHarvestTests
    {
        private const string Sep = InvoiceTable.Inside;

        private static string Invoice(string number, char cuf, string issuer = "Proveedor S.A.S.", bool withCuf = true, string encoding = "UTF-8") =>
            InvoiceTableTests.Invoice(number, withCuf ? InvoiceTableTests.Cuf(cuf) : null, "2026-06-0" + (number.Last() % 9 + 1), issuer, "Cliente S.A.S.", "100.00", "19.00", "119.00")
                .Replace("encoding=\"UTF-8\"", "encoding=\"" + encoding + "\"");

        private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

        private static byte[] Zip(params (string Name, byte[] Data)[] entries)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, data) in entries)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    stream.Write(data, 0, data.Length);
                }
            return buffer.ToArray();
        }

        private static byte[] Eml(params (string Name, byte[] Data, string Type)[] attachments)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Proveedor", "facturas@proveedor.example"));
            message.To.Add(new MailboxAddress("Contabilidad", "contabilidad@cliente.example"));
            message.Subject = "Factura electrónica";
            var body = new BodyBuilder { TextBody = "Adjuntamos la factura." };
            foreach (var (name, data, type) in attachments) body.Attachments.Add(name, data, ContentType.Parse(type));
            message.Body = body.ToMessageBody();
            using var buffer = new MemoryStream();
            message.WriteTo(buffer);
            return buffer.ToArray();
        }

        // The least an Outlook message needs for the reader: a subject and attachment storages with a long file name and the data.
        private static byte[] Msg(params (string Name, byte[] Data)[] attachments)
        {
            using var buffer = new MemoryStream();
            using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
            {
                Write(root, "__substg1.0_0037001F", Encoding.Unicode.GetBytes("Factura"));
                for (var i = 0; i < attachments.Length; i++)
                {
                    var storage = root.CreateStorage($"__attach_version1.0_#{i:X8}");
                    Write(storage, "__substg1.0_3707001F", Encoding.Unicode.GetBytes(attachments[i].Name));
                    Write(storage, "__substg1.0_37010102", attachments[i].Data);
                }
            }
            return buffer.ToArray();
        }

        private static void Write(Storage storage, string name, byte[] data)
        {
            using var stream = storage.CreateStream(name);
            stream.Write(data, 0, data.Length);
        }

        private static InvoiceTableResult Read(params (string Name, byte[] Data)[] files)
        {
            var folder = TestPaths.NewTempFolder();
            try
            {
                foreach (var (name, data) in files)
                {
                    var path = Path.Combine(folder, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, data);
                }
                return InvoiceTable.ReadFolder(folder);
            }
            finally { Directory.Delete(folder, true); }
        }

        private static string Fixture() => File.ReadAllText(Path.Combine(TestPaths.ProjectFolder, "sat-fixtures", "cfdi40-valid.xml"));

        [Fact]
        public void The_xml_files_of_a_zip_archive_are_rows_and_its_other_files_are_passed_over()
        {
            var zip = Zip(("F1.xml", Utf8(Invoice("F1", 'a'))), ("junio/F2.xml", Utf8(Invoice("F2", 'b'))), ("F1.pdf", new byte[] { 37, 80, 68, 70 }));
            var result = Read(("correo/junio.zip", zip));
            Assert.Equal(2, result.Rows.Count);
            Assert.Contains(result.Rows, r => r.File == "correo/junio.zip" + Sep + "F1.xml" && r.Number == "F1");
            Assert.Contains(result.Rows, r => r.File == "correo/junio.zip" + Sep + "junio/F2.xml" && r.Number == "F2");
            Assert.Empty(result.Skipped);
            Assert.Equal(2, result.FilesSeen);
            // what was read is the archive, not its contents counted again
            Assert.Equal(zip.Length, result.SourceBytes);
        }

        [Fact]
        public void An_eml_gives_its_xml_attachments_with_their_own_bytes_and_the_zip_attached_to_it()
        {
            // an XML written in ISO-8859-1 and sent as text/xml: the bytes must arrive as they were sent, or the accents break
            var latin = Encoding.Latin1.GetBytes(Invoice("F3", 'c', issuer: "Compañía Ñandú S.A.S.", encoding: "ISO-8859-1"));
            var mail = Eml(("F3.xml", latin, "text/xml"), ("lote.zip", Zip(("F4.xml", Utf8(Invoice("F4", 'd')))), "application/zip"),
                ("logo.png", new byte[] { 137, 80, 78, 71 }, "image/png"));
            var result = Read(("factura.eml", mail));
            Assert.Equal(2, result.Rows.Count);
            var f3 = Assert.Single(result.Rows, r => r.Number == "F3");
            Assert.Equal("Compañía Ñandú S.A.S.", f3.Issuer);
            Assert.Equal("factura.eml" + Sep + "F3.xml", f3.File);
            Assert.Contains(result.Rows, r => r.File == "factura.eml" + Sep + "lote.zip" + Sep + "F4.xml");
        }

        [Fact]
        public void An_outlook_message_gives_its_xml_attachments()
        {
            var result = Read(("factura.msg", Msg(("F5.xml", Utf8(Invoice("F5", 'e'))), ("F5.pdf", new byte[] { 37, 80, 68, 70 }))));
            var row = Assert.Single(result.Rows);
            Assert.Equal("F5", row.Number);
            Assert.Equal("factura.msg" + Sep + "F5.xml", row.File);
        }

        [Fact]
        public void The_same_invoice_in_the_folder_and_in_a_mail_is_counted_once()
        {
            var keyed = Invoice("F6", 'f');
            var plain = Invoice("F7", 'g', withCuf: false);
            var result = Read(("F6.xml", Utf8(keyed)), ("F7.xml", Utf8(plain)),
                ("hilo/respuesta.eml", Eml(("F6.xml", Utf8(keyed), "application/xml"), ("F7.xml", Utf8(plain), "application/xml"))));
            Assert.Equal(2, result.Rows.Count);
            // with a unique code: the same code; without one: the same bytes
            Assert.Contains(result.Skipped, s => s.Reason == InvoiceSkip.Duplicate && s.File == "hilo/respuesta.eml" + Sep + "F6.xml");
            var same = Assert.Single(result.Skipped, s => s.Reason == InvoiceSkip.SameFile);
            Assert.Equal("hilo/respuesta.eml" + Sep + "F7.xml", same.File);
            Assert.Equal("F7.xml", same.Detail);
            Assert.Contains("F7.xml", InvoiceTable.ToMarkdown(result, XmlLabels.For("en"), "x"));
        }

        [Fact]
        public void Two_different_invoices_without_a_code_are_both_rows()
        {
            var result = Read(("a.xml", Utf8(Invoice("F8", 'h', withCuf: false))), ("b.xml", Utf8(Invoice("F9", 'i', withCuf: false))));
            Assert.Equal(2, result.Rows.Count);
            Assert.DoesNotContain(result.Skipped, s => s.Reason == InvoiceSkip.SameFile);
        }

        [Fact]
        public void A_mail_or_archive_without_xml_is_listed_and_a_damaged_archive_is_unreadable()
        {
            var result = Read(("solo-texto.eml", Eml(("nota.txt", Utf8("hola"), "text/plain"))), ("vacio.zip", Zip(("leeme.txt", Utf8("x")))),
                ("roto.zip", Utf8("this is not a zip archive")));
            Assert.Empty(result.Rows);
            Assert.Contains(result.Skipped, s => s.File == "solo-texto.eml" && s.Reason == InvoiceSkip.NothingInside);
            Assert.Contains(result.Skipped, s => s.File == "vacio.zip" && s.Reason == InvoiceSkip.NothingInside);
            Assert.Contains(result.Skipped, s => s.File == "roto.zip" && s.Reason == InvoiceSkip.Unreadable);
            Assert.Equal(0, result.FilesSeen);
            var text = InvoiceTable.ToMarkdown(result, XmlLabels.For("es"), "x");
            Assert.Contains("Un correo o archivo ZIP sin ningún archivo XML", text);
        }

        [Fact]
        public void Archives_nested_deeper_than_the_limit_are_listed_not_opened()
        {
            var data = Zip(("F10.xml", Utf8(Invoice("F10", 'j'))));
            for (var i = 0; i < InvoiceTable.MaxNesting; i++) data = Zip(($"nivel{i}.zip", data));
            var result = Read(("profundo.zip", data));
            Assert.Empty(result.Rows);
            Assert.Single(result.Skipped, s => s.Reason == InvoiceSkip.TooMuchInside);

            // one level less is read
            data = Zip(("F10.xml", Utf8(Invoice("F10", 'j'))));
            for (var i = 0; i < InvoiceTable.MaxNesting - 1; i++) data = Zip(($"nivel{i}.zip", data));
            Assert.Single(Read(("profundo.zip", data)).Rows);
        }

        [Fact]
        public void An_entry_protected_by_a_password_is_said_as_such()
        {
            var zip = Zip(("F11.xml", Utf8(Invoice("F11", 'k'))));
            // the flag of an encrypted entry, in its local header and in the central directory
            SetEncrypted(zip, 0x04034b50, 6);
            SetEncrypted(zip, 0x02014b50, 8);
            var result = Read(("clave.zip", zip));
            Assert.Empty(result.Rows);
            Assert.Contains(result.Skipped, s => s.File == "clave.zip" + Sep + "F11.xml" && s.Reason == InvoiceSkip.Encrypted);
            // an XML file was there, only it could not be read: the folder is not said to have none
            Assert.Equal(1, result.FilesSeen);
        }

        private static void SetEncrypted(byte[] zip, uint signature, int offset)
        {
            for (var i = 0; i + 4 <= zip.Length; i++)
                if (BitConverter.ToUInt32(zip, i) == signature)
                {
                    zip[i + offset] |= 1;
                    return;
                }
            throw new InvalidOperationException("signature not found");
        }

        [Fact]
        public void The_seal_of_a_cfdi_is_checked_inside_a_zip_and_a_mail()
        {
            var cfdi = Utf8(Fixture());
            var result = Read(("sat.zip", Zip(("cfdi.xml", cfdi))));
            Assert.Equal("valid", Assert.Single(result.Rows).Signature);
            result = Read(("sat.eml", Eml(("cfdi.xml", cfdi, "text/xml"))));
            Assert.Equal("valid", Assert.Single(result.Rows).Signature);
        }

        [Fact]
        public void A_forwarded_mail_inside_a_mail_is_opened()
        {
            var inner = Eml(("F12.xml", Utf8(Invoice("F12", 'l')), "application/xml"));
            var outer = Eml(("reenviado.eml", inner, "message/rfc822"));
            var row = Assert.Single(Read(("reenvio.eml", outer)).Rows);
            Assert.Equal("F12", row.Number);
            Assert.StartsWith("reenvio.eml" + Sep, row.File);
            Assert.EndsWith(Sep + "F12.xml", row.File);
        }
    }
}
