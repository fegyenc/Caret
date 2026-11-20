using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MimeKit;
using MimeKit.Utils;

namespace Typedown.WinUI.Services.Conversion
{
    // Reading .eml files (RFC 822 / MIME) with MimeKit. Which part is the body and which parts are
    // attachments follows Python's email.message rules (get_body / iter_attachments), which the plugin
    // relies on, so both tools pick the same parts.
    internal static class EmlReader
    {
        private static readonly string[] Preference = { "plain", "html" };

        // .NET knows UTF-8, UTF-16 and Latin-1 by itself; the other code pages a mail can name (windows-1250 and ISO-8859-2 for
        // Hungarian, Shift_JIS, KS C 5601, GB2312, ISO-2022-JP…) come from this provider, and without it they are read as Latin-1.
        static EmlReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // rawAttachments: every attachment as the bytes it was sent with, text too (an XML file declared as ISO-8859-1 must keep its bytes, or its
        // signature no longer matches); otherwise a text attachment is its text in UTF-8, as the conversion reads it.
        public static EmailDocument Read(Stream stream, Func<string, string> htmlToText, bool rawAttachments = false)
        {
            var message = MimeMessage.Load(stream);
            var email = new EmailDocument
            {
                Subject = HeaderText(message.Headers, HeaderId.Subject),
                Sender = message.Headers.Contains(HeaderId.From) ? EmailAddressParser.Parse(HeaderText(message.Headers, HeaderId.From)) : null,
                To = EmailAddressParser.ParseList(HeaderText(message.Headers, HeaderId.To)),
                Cc = EmailAddressParser.ParseList(HeaderText(message.Headers, HeaderId.Cc)),
                Bcc = EmailAddressParser.ParseList(HeaderText(message.Headers, HeaderId.Bcc)),
                Date = Date(message.Headers),
            };

            if (message.Body != null)
            {
                // An empty plain-text alternative next to the real HTML one: use the HTML
                var candidates = FindBody(message.Body, true).OrderBy(c => c.Priority).Select(c => c.Part).ToList();
                var body = candidates.FirstOrDefault();
                if (body != null && IsPlain(body) && string.IsNullOrWhiteSpace(body.Text))
                    body = candidates.FirstOrDefault(p => !IsPlain(p)) ?? body;
                if (body != null)
                {
                    var content = body.Text ?? "";
                    email.Body = body.ContentType.MediaSubtype.Equals("html", StringComparison.OrdinalIgnoreCase) ? htmlToText(content) : content;
                }

                foreach (var part in IterAttachments(message.Body, rawAttachments))
                    email.Attachments.Add(ToAttachment(part, rawAttachments));
                // a mail that is only its attachment (no text, the XML as the whole body): read as one by the invoice table, not by the conversion,
                // which has always shown such a body as the text of the mail
                if (rawAttachments && message.Body is not Multipart && IsAttachment(message.Body))
                    email.Attachments.Add(ToAttachment(message.Body, true));
            }
            return email;
        }

        // The header as its decoded, unfolded text — what Python's str(msg[name]) gives.
        private static string HeaderText(HeaderList headers, HeaderId id) => (headers[id] ?? "").Trim();

        private static string Date(HeaderList headers)
        {
            var raw = headers[HeaderId.Date];
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return DateUtils.TryParse(raw, out var date) ? EmailDates.Format(date.DateTime) : raw.Trim();
        }

        private static IEnumerable<(int Priority, TextPart Part)> FindBody(MimeEntity part, bool top)
        {
            if (IsAttachment(part)) yield break;
            var type = part.ContentType;
            if (part is TextPart text)
            {
                var index = Array.IndexOf(Preference, type.MediaSubtype.ToLowerInvariant());
                if (index >= 0) yield return (index, text);
                yield break;
            }
            if (part is not Multipart multipart) yield break;
            if (!type.MediaSubtype.Equals("related", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var child in multipart)
                    foreach (var found in FindBody(child, false))
                        yield return found;
                yield break;
            }
            var start = type.Parameters["start"];
            var candidate = string.IsNullOrEmpty(start) ? null : multipart.FirstOrDefault(p => SameContentId(p.Headers[HeaderId.ContentId], start));
            candidate ??= multipart.FirstOrDefault();
            if (candidate != null)
                foreach (var found in FindBody(candidate, false))
                    yield return found;
        }

        // raw (the invoice table): a multipart part inside the mail (a multipart/mixed in a multipart/signed) is gone through for its own
        // attachments, which the conversion has always shown as one part
        private static IEnumerable<MimeEntity> IterAttachments(MimeEntity root, bool raw = false)
        {
            if (root is not Multipart multipart) yield break;
            var subtype = multipart.ContentType.MediaSubtype.ToLowerInvariant();
            if (subtype == "alternative") yield break;
            var parts = multipart.ToList();
            if (subtype == "related")
            {
                var start = multipart.ContentType.Parameters["start"];
                if (!string.IsNullOrEmpty(start) && parts.Any(p => SameContentId(p.Headers[HeaderId.ContentId], start)))
                {
                    foreach (var p in parts.Where(p => !SameContentId(p.Headers[HeaderId.ContentId], start)))
                        yield return p;
                    yield break;
                }
                foreach (var p in parts.Skip(1))
                    yield return p;
                yield break;
            }
            var seen = new List<string>();
            foreach (var part in parts)
            {
                var type = part.ContentType.MediaType.ToLowerInvariant();
                var sub = part.ContentType.MediaSubtype.ToLowerInvariant();
                var bodyType = (type == "text" && (sub == "plain" || sub == "html")) || (type == "multipart" && (sub == "related" || sub == "alternative"));
                if (bodyType && !IsAttachment(part) && !seen.Contains(sub))
                {
                    seen.Add(sub);
                    continue;
                }
                if (raw && part is Multipart)
                {
                    foreach (var inner in IterAttachments(part, raw))
                        yield return inner;
                    continue;
                }
                yield return part;
            }
        }

        private static EmailAttachment ToAttachment(MimeEntity part, bool raw)
        {
            var filename = part.ContentDisposition?.FileName ?? part.ContentType.Name ?? "";
            var contentType = part.ContentType.MimeType.ToLowerInvariant();
            byte[] data = null;
            if (part is MessagePart messagePart)
            {
                if (messagePart.Message != null)
                {
                    using var buffer = new MemoryStream();
                    messagePart.Message.WriteTo(buffer);
                    data = buffer.ToArray();
                }
                if (filename.Length == 0)
                {
                    filename = (data != null ? messagePart.Message.Subject?.Trim() : "") ?? "";
                    if (filename.Length == 0) filename = "attached message";
                    filename += ".eml";
                }
            }
            else if (part is TextPart text && !raw)
                data = Encoding.UTF8.GetBytes(text.Text ?? "");
            else if (part is MimePart mime && mime.Content != null)
            {
                using var buffer = new MemoryStream();
                mime.Content.DecodeTo(buffer);
                data = buffer.ToArray();
            }
            var inline = (part.ContentDisposition?.Disposition ?? "").Equals("inline", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(part.Headers[HeaderId.ContentId]) && contentType.StartsWith("image/", StringComparison.Ordinal));
            return new EmailAttachment { FileName = filename.Length > 0 ? filename : "attachment", Data = data, ContentType = contentType, Inline = inline };
        }

        private static bool IsPlain(TextPart part) => part.ContentType.MediaSubtype.Equals("plain", StringComparison.OrdinalIgnoreCase);

        private static bool IsAttachment(MimeEntity part) => part.ContentDisposition?.IsAttachment == true;

        private static bool SameContentId(string contentId, string start) =>
            !string.IsNullOrEmpty(contentId) && contentId.Trim().Trim('<', '>') == start.Trim().Trim('<', '>');
    }
}
