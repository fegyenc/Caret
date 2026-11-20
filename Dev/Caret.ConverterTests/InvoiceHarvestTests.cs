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
        public void A_file_of_the_folder_is_kept_before_its_copy_in_an_archive_whatever_the_names()
        {
            var plain = Utf8(Invoice("F13", 'm', withCuf: false));
            // the archive comes first by name: its copy still yields to the two files of the folder, which both stay (the person put them there)
            var result = Read(("a.zip", Zip(("F13.xml", plain))), ("b.xml", plain), ("c.xml", plain));
            Assert.Equal(new[] { "b.xml", "c.xml" }, result.Rows.Select(r => r.File).OrderBy(f => f).ToArray());
            var same = Assert.Single(result.Skipped, s => s.Reason == InvoiceSkip.SameFile);
            Assert.Equal("a.zip" + Sep + "F13.xml", same.File);
        }

        [Fact]
        public void A_name_with_the_arrow_in_it_is_still_a_file_of_the_folder()
        {
            var plain = Utf8(Invoice("F14", 'n', withCuf: false));
            var result = Read(("a" + Sep + "original.xml", plain), ("b.xml", plain));
            Assert.Equal(2, result.Rows.Count);
            Assert.All(result.Rows, r => Assert.False(r.FromContainer));
        }

        [Fact]
        public void The_part_that_would_go_past_the_budget_is_not_read_and_the_limit_is_said()
        {
            var one = Utf8(Invoice("F15", 'a'));
            var two = Utf8(Invoice("F16", 'b'));
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "lote.zip"), Zip(("1.xml", one), ("2.xml", two)));
                // room for the first entry only: the second (the last one) would cross the budget, and that is said on it
                var result = InvoiceTable.ReadFolder(folder, one.Length + two.Length / 2);
                Assert.Equal("F15", Assert.Single(result.Rows).Number);
                var stopped = Assert.Single(result.Skipped, s => s.Reason == InvoiceSkip.TooMuchInside);
                Assert.Equal("lote.zip" + Sep + "2.xml", stopped.File);

                // the same for a mail: the attachment's size is checked before it is read
                File.Delete(Path.Combine(folder, "lote.zip"));
                File.WriteAllBytes(Path.Combine(folder, "correo.eml"), Eml(("1.xml", one, "application/xml"), ("2.xml", two, "application/xml")));
                result = InvoiceTable.ReadFolder(folder, one.Length + two.Length / 2);
                Assert.Single(result.Rows);
                Assert.Equal("correo.eml" + Sep + "2.xml", Assert.Single(result.Skipped, s => s.Reason == InvoiceSkip.TooMuchInside).File);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void An_entry_that_says_a_smaller_size_than_it_has_is_stopped_at_the_budget()
        {
            // a stored entry of 2,000 bytes whose headers say 10: the reader must stop at the budget, not take a whole buffer past it
            var data = Utf8(Invoice("F18", 'd') + new string(' ', 2000));
            byte[] zip;
            using (var buffer = new MemoryStream())
            {
                using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                using (var stream = archive.CreateEntry("F18.xml", CompressionLevel.NoCompression).Open())
                    stream.Write(data, 0, data.Length);
                zip = buffer.ToArray();
            }
            SetSize(zip, 0x04034b50, 22, 10);
            SetSize(zip, 0x02014b50, 24, 10);
            var folder = TestPaths.NewTempFolder();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "corto.zip"), zip);
                var result = InvoiceTable.ReadFolder(folder, 500);
                Assert.Empty(result.Rows);
                var skipped = Assert.Single(result.Skipped);
                Assert.Equal("corto.zip" + Sep + "F18.xml", skipped.File);
                Assert.Equal(InvoiceSkip.TooMuchInside, skipped.Reason);
            }
            finally { Directory.Delete(folder, true); }
        }

        // The uncompressed size of the first entry, in its local header (offset 22) or in the central directory (offset 24).
        private static void SetSize(byte[] zip, uint signature, int offset, uint size)
        {
            for (var i = 0; i + 4 <= zip.Length; i++)
                if (BitConverter.ToUInt32(zip, i) == signature)
                {
                    BitConverter.GetBytes(size).CopyTo(zip, i + offset);
                    return;
                }
            throw new InvalidOperationException("signature not found");
        }

        [Fact]
        public void The_attachments_of_a_signed_mail_are_read()
        {
            // multipart/signed around multipart/mixed (text, invoice): the invoice is one level down
            var mixed = new Multipart("mixed")
            {
                new TextPart("plain") { Text = "Adjuntamos la factura." },
                new MimePart("application", "xml")
                {
                    Content = new MimeContent(new MemoryStream(Utf8(Invoice("F19", 'e')))),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = "F19.xml",
                },
            };
            var signed = new Multipart("signed")
            {
                mixed,
                new MimePart("application", "pkcs7-signature")
                {
                    Content = new MimeContent(new MemoryStream(new byte[] { 48, 130, 1, 2 })),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    FileName = "smime.p7s",
                },
            };
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Proveedor", "facturas@proveedor.example"));
            message.Subject = "Factura firmada";
            message.Body = signed;
            using var buffer = new MemoryStream();
            message.WriteTo(buffer);
            var row = Assert.Single(Read(("firmado.eml", buffer.ToArray())).Rows);
            Assert.Equal("firmado.eml" + Sep + "F19.xml", row.File);
        }

        [Fact]
        public void An_outlook_item_deeper_than_the_reader_goes_is_listed()
        {
            // four Outlook items, each attached to the one before: the Outlook reader reads three levels, the fourth is listed, not taken for empty
            byte[] msg;
            using (var buffer = new MemoryStream())
            {
                using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
                {
                    Write(root, "__substg1.0_0037001F", Encoding.Unicode.GetBytes("Reenvío"));
                    Storage parent = root;
                    for (var level = 1; level <= 4; level++)
                    {
                        var attachment = parent.CreateStorage("__attach_version1.0_#00000000");
                        Write(attachment, "__substg1.0_3707001F", Encoding.Unicode.GetBytes(level + ".msg"));
                        parent = attachment.CreateStorage("__substg1.0_3701000D");
                        Write(parent, "__substg1.0_0037001F", Encoding.Unicode.GetBytes("Nivel " + level));
                    }
                    var xml = parent.CreateStorage("__attach_version1.0_#00000000");
                    Write(xml, "__substg1.0_3707001F", Encoding.Unicode.GetBytes("F20.xml"));
                    Write(xml, "__substg1.0_37010102", Utf8(Invoice("F20", 'f')));
                }
                msg = buffer.ToArray();
            }
            var result = Read(("hondo.msg", msg));
            Assert.Empty(result.Rows);
            var deep = Assert.Single(result.Skipped);
            Assert.Equal(InvoiceSkip.TooMuchInside, deep.Reason);
            Assert.Equal("hondo.msg" + Sep + "1.msg" + Sep + "2.msg" + Sep + "3.msg" + Sep + "4.msg", deep.File);
        }

        [Fact]
        public void A_mail_that_is_only_its_xml_attachment_is_read()
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Proveedor", "facturas@proveedor.example"));
            message.Subject = "Factura";
            message.Body = new MimePart("application", "xml")
            {
                Content = new MimeContent(new MemoryStream(Utf8(Invoice("F17", 'c')))),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = "F17.xml",
            };
            using var buffer = new MemoryStream();
            message.WriteTo(buffer);
            var row = Assert.Single(Read(("solo.eml", buffer.ToArray())).Rows);
            Assert.Equal("solo.eml" + Sep + "F17.xml", row.File);
        }

        [Fact]
        public void When_nothing_could_be_read_the_reasons_are_said_in_a_line()
        {
            var result = Read(("roto.zip", Utf8("not a zip")), ("vacio.eml", Eml(("nota.txt", Utf8("hola"), "text/plain"))));
            Assert.Equal(0, result.FilesSeen);
            var line = InvoiceTable.SkipSummary(result, XmlLabels.For("en"), 3);
            Assert.Contains("roto.zip (Could not be opened", line);
            Assert.Contains("vacio.eml (An e-mail or ZIP archive with no XML file in it)", line);
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
