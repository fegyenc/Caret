using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // Invoices that arrive by e-mail: a folder where the mails were saved (.eml from any mail program, .msg from Outlook) or ZIP archives (a DIAN
    // invoice is sent as a ZIP of its XML and PDF; a supplier sends a month of invoices in one ZIP). The XML files inside are read as if they were
    // files of the folder, shown as "mail.eml › factura.xml", and what is inside them is opened too: a ZIP attached to a mail, a mail forwarded as an
    // attachment, a ZIP in a ZIP. Nothing is written to disk: every part is read in memory, within limits that stop a ZIP bomb.
    internal static partial class InvoiceTable
    {
        private enum SourceKind { None, Xml, Zip, Eml, Msg }

        private static SourceKind KindOf(string name) => (Path.GetExtension(name) ?? "").ToLowerInvariant() switch
        {
            ".xml" => SourceKind.Xml,
            ".zip" => SourceKind.Zip,
            ".eml" => SourceKind.Eml,
            ".msg" => SourceKind.Msg,
            _ => SourceKind.None,
        };

        // How far one file of the folder is opened: levels (a ZIP in a mail is 2), parts read, and the bytes they unpack to in all
        internal const int MaxNesting = 4;
        internal const int MaxParts = 20_000;
        internal const long MaxUnpacked = 1024L * 1024 * 1024;

        // what is inside a container, in the File column
        internal const string Inside = " › ";


        private sealed class Unpacking
        {
            public int Parts;
            public long Bytes;
            public long Limit = MaxUnpacked;
            public bool Stopped;
            public long Left => Math.Max(0, Limit - Bytes);
        }

        // One ZIP archive or e-mail of the folder. Its own size is what counts as read (the XML files in it are not counted again).
        private static void ReadContainerFile(InvoiceTableResult result, string path, string display, SourceKind kind, long maxUnpacked)
        {
            var bytesBefore = result.SourceBytes;
            long length = 0;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                length = stream.Length;
                // a mail is read whole into memory; a ZIP is read entry by entry, so only its entries are limited
                if (kind != SourceKind.Zip && length > XmlConverter.MaxBytes)
                {
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.TooBig });
                    return;
                }
                ReadContainer(result, stream, display, kind, 1, new Unpacking { Limit = maxUnpacked });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message });
            }
            finally
            {
                result.SourceBytes = bytesBefore + length;
            }
        }

        // A container read from a stream (a file, or a part of another container held in memory). One that holds no XML, ZIP or mail is listed.
        private static void ReadContainer(InvoiceTableResult result, Stream stream, string display, SourceKind kind, int depth, Unpacking unpacking)
        {
            int parts;
            try
            {
                parts = kind switch
                {
                    SourceKind.Zip => ReadZip(result, stream, display, depth, unpacking),
                    SourceKind.Eml => ReadMail(result, EmlReader.Read(stream, _ => "", rawAttachments: true), display, depth, unpacking),
                    _ => ReadMail(result, MsgFileReader.Read(stream, _ => ""), display, depth, unpacking),
                };
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // a damaged archive or mail, or a file that only has the name of one: the readers of the formats throw their own exceptions, and
                // one file that cannot be read must not end the table
                result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.Unreadable, Detail = ex.Message });
                return;
            }
            if (parts == 0) result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.NothingInside });
        }

        // The entries of a ZIP archive, in the order of their paths. Folders, PDFs and other files are passed over.
        private static int ReadZip(InvoiceTableResult result, Stream stream, string display, int depth, Unpacking unpacking)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var parts = 0;
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                var kind = KindOf(entry.Name);
                if (kind == SourceKind.None) continue;
                parts++;
                var inner = display + Inside + entry.FullName.Replace('\\', '/');
                if (!Within(result, unpacking, inner)) break;
                if (entry.IsEncrypted)
                {
                    SkipPart(result, inner, kind, InvoiceSkip.Encrypted);
                    continue;
                }
                MemoryStream copy;
                bool overBudget;
                try
                {
                    using var source = entry.Open();
                    copy = Copy(source, entry.Length, unpacking, out overBudget);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                {
                    SkipPart(result, inner, kind, InvoiceSkip.Unreadable, ex.Message);
                    continue;
                }
                if (overBudget)
                {
                    Stop(result, unpacking, inner);
                    break;
                }
                if (copy == null)
                {
                    SkipPart(result, inner, kind, InvoiceSkip.TooBig);
                    continue;
                }
                AddPart(result, copy, inner, kind, depth, unpacking);
            }
            return parts;
        }

        // The attachments of a mail: XML files, ZIP archives, mails (an .eml or .msg file, a forwarded message, an Outlook item attached to an Outlook
        // message). The text of the mail and its pictures are not read.
        private static int ReadMail(InvoiceTableResult result, EmailDocument mail, string display, int depth, Unpacking unpacking)
        {
            var parts = 0;
            foreach (var attachment in mail.Attachments)
            {
                var name = string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment" : attachment.FileName.Trim();
                var inner = display + Inside + name;
                if (attachment.Email != null)
                {
                    parts++;
                    if (!Within(result, unpacking, inner)) break;
                    if (depth >= MaxNesting)
                    {
                        result.Skipped.Add(new SkippedInvoiceFile { File = inner, Reason = InvoiceSkip.TooMuchInside });
                        continue;
                    }
                    if (ReadMail(result, attachment.Email, inner, depth + 1, unpacking) == 0)
                        result.Skipped.Add(new SkippedInvoiceFile { File = inner, Reason = InvoiceSkip.NothingInside });
                    continue;
                }
                var kind = KindOf(name);
                if (kind == SourceKind.None)
                    kind = attachment.ContentType switch
                    {
                        "application/xml" or "text/xml" => SourceKind.Xml,
                        "application/zip" or "application/x-zip-compressed" => SourceKind.Zip,
                        "message/rfc822" => SourceKind.Eml,
                        _ => SourceKind.None,
                    };
                if (kind == SourceKind.None || attachment.Data == null) continue;
                parts++;
                if (!Within(result, unpacking, inner)) break;
                if (attachment.Data.Length > XmlConverter.MaxBytes)
                {
                    SkipPart(result, inner, kind, InvoiceSkip.TooBig);
                    continue;
                }
                if (attachment.Data.Length > unpacking.Left)
                {
                    Stop(result, unpacking, inner);
                    break;
                }
                unpacking.Bytes += attachment.Data.Length;
                AddPart(result, new MemoryStream(attachment.Data, writable: false), inner, kind, depth, unpacking);
            }
            return parts;
        }

        // One part found inside a container: an XML file is read as a file of the folder, another container is opened one level deeper.
        private static void AddPart(InvoiceTableResult result, MemoryStream part, string display, SourceKind kind, int depth, Unpacking unpacking)
        {
            using (part)
            {
                if (kind == SourceKind.Xml)
                {
                    Add(result, part, display, fromContainer: true);
                    return;
                }
                if (depth >= MaxNesting)
                {
                    result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.TooMuchInside });
                    return;
                }
                ReadContainer(result, part, display, kind, depth + 1, unpacking);
            }
        }

        // A part that could not be read. An XML file is still one XML file looked at (Add counts the ones it reads), so that a folder whose only
        // invoices are in an archive protected by a password is not said to have no XML files.
        private static void SkipPart(InvoiceTableResult result, string display, SourceKind kind, InvoiceSkip reason, string detail = null)
        {
            if (kind == SourceKind.Xml) result.FilesSeen++;
            result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = reason, Detail = detail });
        }

        // One more part of the same file of the folder; false (said once, on the part where it stopped) past the limits.
        private static bool Within(InvoiceTableResult result, Unpacking unpacking, string display)
        {
            if (unpacking.Stopped) return false;
            if (++unpacking.Parts <= MaxParts && unpacking.Bytes < unpacking.Limit) return true;
            Stop(result, unpacking, display);
            return false;
        }

        private static void Stop(InvoiceTableResult result, Unpacking unpacking, string display)
        {
            unpacking.Stopped = true;
            result.Skipped.Add(new SkippedInvoiceFile { File = display, Reason = InvoiceSkip.TooMuchInside });
        }

        // An entry of a ZIP into memory, never more than the reader takes (the size an archive says can be false): null when it is bigger, and
        // null with overBudget when it would take the file of the folder past what it may unpack to in all.
        private static MemoryStream Copy(Stream source, long declared, Unpacking unpacking, out bool overBudget)
        {
            overBudget = false;
            if (declared > XmlConverter.MaxBytes) return null;
            if (declared > unpacking.Left)
            {
                overBudget = true;
                return null;
            }
            var copy = new MemoryStream((int)Math.Max(0, Math.Min(declared, XmlConverter.MaxBytes)));
            var buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                copy.Write(buffer, 0, read);
                unpacking.Bytes += read;
                if (unpacking.Bytes > unpacking.Limit) overBudget = true;
                if (overBudget || copy.Length > XmlConverter.MaxBytes)
                {
                    copy.Dispose();
                    return null;
                }
            }
            copy.Position = 0;
            return copy;
        }
    }
}
