using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.XmlEditing
{
    // How an XML file was written, kept so that a save writes it the same way: the encoding, the byte-order mark and the line ends. An XML file is
    // data other programs read, so unlike a note it must not be rewritten as UTF-8 with other line ends just because a person fixed a value in it.
    internal sealed class XmlTextFormat
    {
        public Encoding Encoding { get; init; } = new UTF8Encoding(false);
        public bool Bom { get; init; }
        public string NewLine { get; init; } = "\n";
        // the file had more than one kind of line end: the most frequent one is kept, which changes the others
        public bool MixedLineEndings { get; init; }
        // the bytes were not valid in the declared encoding (UTF-8 when none is declared): the system's ANSI code page was used to read them
        public bool Legacy { get; init; }

        public string EncodingName => Encoding switch
        {
            UTF8Encoding => Bom ? "UTF-8 (BOM)" : "UTF-8",
            UnicodeEncoding u => u.Preamble.Length > 0 || Bom ? (IsBigEndian(u) ? "UTF-16 BE" : "UTF-16 LE") : "UTF-16",
            UTF32Encoding => "UTF-32",
            _ => Encoding.WebName.ToUpperInvariant(),
        };

        public string NewLineName => NewLine switch { "\r\n" => "CRLF", "\r" => "CR", _ => "LF" };

        private static bool IsBigEndian(UnicodeEncoding encoding) => encoding.GetBytes("A")[0] == 0;
    }

    // A character that the encoding of the file cannot hold, or an encoding that is not known: the file is not written (nothing is changed
    // silently), and the message says where and what to do.
    internal sealed class XmlEncodingException : Exception
    {
        public XmlEncodingException(string message) : base(message) { }

        // what the message says, apart, so that the app can say it in the language of the person
        public string UnknownEncoding { get; init; }
        public string Character { get; init; }
        public string EncodingName { get; init; }
        public int Line { get; init; }
        public int Position { get; init; }
    }

    internal static class XmlTextFile
    {
        private static readonly Regex Declaration = new(@"^\s*<\?xml\s[^>]*?\bencoding\s*=\s*[""']([A-Za-z][A-Za-z0-9._\-]*)[""']", RegexOptions.CultureInvariant);

        // The text of the file with "\n" line ends (as the editor holds it) and the format to save it back in.
        public static (string Text, XmlTextFormat Format) Decode(byte[] bytes)
        {
            Encoding encoding;
            var skip = 0;
            var bom = true;
            var legacy = false;
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
            else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { encoding = new UTF8Encoding(false, true); skip = 3; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
            else
            {
                bom = false;
                // UTF-16 without a byte-order mark: the "<" of the first tag is followed (or preceded) by a zero byte
                if (bytes.Length >= 4 && bytes[0] == 0x3C && bytes[1] == 0 && bytes[3] == 0 && bytes[2] != 0) encoding = new UnicodeEncoding(false, false, true);
                else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0x3C && bytes[2] == 0 && bytes[3] != 0) encoding = new UnicodeEncoding(true, false, true);
                else
                {
                    var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 300));
                    var declared = Declaration.Match(head);
                    encoding = (declared.Success ? Resolve(declared.Groups[1].Value) : null) ?? new UTF8Encoding(false, true);
                }
            }
            string text;
            try
            {
                text = encoding.GetString(bytes, skip, bytes.Length - skip);
            }
            catch (DecoderFallbackException)
            {
                // not what it says it is (or invalid UTF-8 with no declaration): read it with the system's code page, say so, and keep the
                // declaration as it is, so that the person sees the mismatch in the text
                encoding = Utilities.TextFileEncoding.LegacyEncoding();
                text = encoding.GetString(bytes);
                legacy = true;
                bom = false;
            }
            var (newLine, mixed) = LineEnds(text);
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return (normalized, new XmlTextFormat { Encoding = encoding, Bom = bom, NewLine = newLine, MixedLineEndings = mixed, Legacy = legacy });
        }

        // The bytes to write for a text from the editor ("\n" line ends), in the format of the file it came from. The encoding is the one the
        // text declares in its first line (so that a person who changes it gets what they wrote), otherwise the one of the file when it was a
        // Unicode one, otherwise UTF-8. A character the encoding cannot hold, or an encoding that is not known, is an XmlEncodingException.
        public static byte[] Encode(string text, XmlTextFormat format, out XmlTextFormat written)
        {
            format ??= new XmlTextFormat();
            var declared = DeclaredEncoding(text);
            Encoding encoding;
            bool bom;
            if (declared != null)
            {
                encoding = Resolve(declared) ?? throw new XmlEncodingException($"The encoding \"{declared}\" in the first line is not known. Use UTF-8, or another encoding the system has.") { UnknownEncoding = declared };
                bom = encoding is UnicodeEncoding or UTF32Encoding || (encoding is UTF8Encoding && format.Bom && format.Encoding is UTF8Encoding);
                // UTF-16 in the declaration is written with a mark (the standard asks for one), as little-endian when the file did not say
                if (encoding is UnicodeEncoding && format.Encoding is UnicodeEncoding original) encoding = new UnicodeEncoding(IsBigEndian(original), false, true);
            }
            else if (format.Encoding is UnicodeEncoding or UTF32Encoding)
            {
                encoding = format.Encoding is UnicodeEncoding u ? new UnicodeEncoding(IsBigEndian(u), false, true) : format.Encoding;
                bom = format.Bom;
            }
            else
            {
                encoding = new UTF8Encoding(false, true);
                bom = format.Bom && format.Encoding is UTF8Encoding;
            }

            var withLineEnds = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (format.NewLine != "\n") withLineEnds = withLineEnds.Replace("\n", format.NewLine);
            byte[] body;
            try
            {
                body = encoding.GetBytes(withLineEnds);
            }
            catch (EncoderFallbackException ex)
            {
                var at = ex.Index;
                var line = 1 + text.Take(Math.Min(at, text.Length)).Count(c => c == '\n');
                var column = at - (text.LastIndexOf('\n', Math.Max(0, Math.Min(at, text.Length) - 1)) + 1) + 1;
                var shown = at < withLineEnds.Length ? char.ConvertFromUtf32(char.ConvertToUtf32(withLineEnds, at)) : "?";
                throw new XmlEncodingException($"The character \"{shown}\" (line {line}, position {Math.Max(1, column)}) cannot be saved in the encoding {encoding.WebName.ToUpperInvariant()} that the file declares. Remove it, or change the encoding in the first line to UTF-8.")
                {
                    Character = shown, EncodingName = encoding.WebName.ToUpperInvariant(), Line = line, Position = Math.Max(1, column),
                };
            }
            var preamble = bom ? PreambleOf(encoding) : Array.Empty<byte>();
            var result = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
            written = new XmlTextFormat { Encoding = encoding, Bom = bom, NewLine = format.NewLine };
            return result;
        }

        // The encoding named in the declaration of the text (<?xml version="1.0" encoding="ISO-8859-1"?>), or null.
        public static string DeclaredEncoding(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var head = text.Length > 300 ? text.Substring(0, 300) : text;
            var match = Declaration.Match(head.TrimStart('﻿'));
            return match.Success ? match.Groups[1].Value : null;
        }

        // An encoding that fails loudly on a byte or a character it cannot map (a silent "?" in a data file is a change nobody asked for).
        internal static Encoding Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var key = name.Trim().ToLowerInvariant();
            switch (key)
            {
                case "utf-8":
                case "utf8":
                    return new UTF8Encoding(false, true);
                case "utf-16":
                case "utf16":
                case "utf-16le":
                    return new UnicodeEncoding(false, false, true);
                case "utf-16be":
                    return new UnicodeEncoding(true, false, true);
                case "utf-32":
                case "utf32":
                case "utf-32le":
                    return new UTF32Encoding(false, false, true);
                case "utf-32be":
                    return new UTF32Encoding(true, false, true);
            }
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(key, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static byte[] PreambleOf(Encoding encoding) => encoding switch
        {
            UTF8Encoding => new byte[] { 0xEF, 0xBB, 0xBF },
            UnicodeEncoding u => IsBigEndian(u) ? new byte[] { 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE },
            UTF32Encoding f => f.GetBytes("A")[0] == 0 ? new byte[] { 0, 0, 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE, 0, 0 },
            _ => Array.Empty<byte>(),
        };

        private static bool IsBigEndian(UnicodeEncoding encoding) => encoding.GetBytes("A")[0] == 0;

        // The line end the file mostly has, and whether it has others.
        private static (string, bool) LineEnds(string text)
        {
            var crlf = 0;
            var lf = 0;
            var cr = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                    else cr++;
                }
                else if (text[i] == '\n') lf++;
            }
            var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
            var newLine = crlf >= lf && crlf >= cr && crlf > 0 ? "\r\n" : cr > lf ? "\r" : "\n";
            return (newLine, kinds > 1);
        }
    }
}
