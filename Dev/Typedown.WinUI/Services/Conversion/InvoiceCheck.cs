using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.Conversion
{
    // A folder of invoices compared with the list of the tax authority: the metadata the SAT gives for a period (a .txt separated by ~), the list
    // the DIAN's portal exports (Excel), or any list with a column of unique codes (CSV, TXT or Excel). The unique codes are compared, nothing else:
    // what is in the list and has no XML in the folder, what the list says was cancelled or rejected but is in the folder (it is then left out of the
    // totals), and what is in the folder and not in the list. Only the documents of the period and the company of the list are compared, so a
    // folder of the whole year can be compared with the list of one month. Offline: the list is the file the person downloaded.
    internal sealed class AuthorityEntry
    {
        public string Code, Status, Date;
        public bool Cancelled;
        public List<string> Cells;
        public int Line;
    }

    internal sealed class AuthorityList
    {
        public string Name;
        public List<string> Headers = new();
        public int CodeColumn = -1, StatusColumn = -1, DateColumn = -1;
        public List<AuthorityEntry> Entries = new();
        // the first and the last date of the list (yyyy-MM-dd), when it has a column of dates
        public string From, To;
        // the tax IDs that are on every line of the list (the company the list is about); empty when there is none
        public List<string> Parties = new();
        // which side of the documents the company of the list is on, when the list says it: "customer" for a register of purchases (each line names
        // the supplier), "issuer" for a register of sales (each line names the client); null when the list does not say
        public string Side;
        // the column of a register with the other side's RUT (the supplier, the client): a RUT there is never the company
        public int CounterpartyColumn = -1;
    }

    internal sealed class InvoiceCheckResult
    {
        public AuthorityList List;
        public int Matched, OutOfScope;
        public List<AuthorityEntry> Missing = new();
        public List<(InvoiceRow Row, AuthorityEntry Entry)> Cancelled = new();
        // the same for a document of the folder that is not a row (a payment complement): its code and its file
        public List<(string File, AuthorityEntry Entry)> CancelledOthers = new();
        public List<InvoiceRow> NotInList = new();
        // files of the folder that could not be read (locked, damaged, too big, protected by a password): an invoice of the list said to have no
        // XML may be one of them, so the comparison says it is incomplete
        public List<SkippedInvoiceFile> Unread = new();
        // a register whose documents in the folder are of different companies, and whose company could not be told: nothing was matched
        public bool Ambiguous;
        // lines of a register of sales for which the folder has documents of more than one company, while the company is not known: undecided,
        // not "no XML" (one of them may be the line's document)
        public List<AuthorityEntry> Unresolved = new();
        // a register whose company neither its lines nor the folder say: its lines without any XML are still "no XML", but no folder document is
        // said to be "not in the list" (one of them may be the company's own)
        public bool CompanyUnknown;
        public bool HasFindings => Missing.Count + Cancelled.Count + CancelledOthers.Count + NotInList.Count + Unresolved.Count > 0 || Ambiguous || CompanyUnknown;
    }

    internal static class InvoiceCheck
    {
        // ---- reading the list ---------------------------------------------------------------------------------------------------------

        internal const long MaxListBytes = 100L * 1024 * 1024;

        public static AuthorityList Read(string path, InvoiceTableResult table)
        {
            List<List<string>> rows;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length > MaxListBytes) throw new NotSupportedException("The list is bigger than 100 MB.");
                rows = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? ExcelConverter.SheetRows(stream) : TextRows(stream);
            }
            return FromRows(rows, Path.GetFileName(path), table);
        }

        // A text list: separated by ~ (the SAT's metadata), tab, semicolon, comma or |, in UTF-8 or the PC's code page.
        internal static List<List<string>> TextRows(Stream stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var text = Utilities.TextFileEncoding.Decode(buffer.ToArray()).Text;
            var delimiter = CsvConverter.DetectDelimiter(text, new[] { '~', '\t', ';', ',', '|' });
            // ~ only when the first line has one (the SAT's metadata, whose names can hold quotes); a list of one column is a CSV, quotes and all
            var tilde = delimiter == '~' && (text.Split('\n').FirstOrDefault() ?? "").Contains('~');
            if (delimiter == '~' && !tilde) delimiter = ',';
            return CsvConverter.Parse(text, delimiter, quotes: !tilde)
                .Select(r => r.Select(c => c.Trim()).ToList())
                .Where(r => r.Any(c => c.Length > 0))
                .ToList();
        }

        // A unique code as the folder and the lists write it: upper case, no spaces; an access key without its "NFe" or "CTe" in front.
        internal static string Normalize(string code)
        {
            var text = Regex.Replace(code ?? "", @"\s", "").ToUpperInvariant();
            var key = Regex.Match(text, @"^(?:NFE|CTE|NFSE|MDFE)(\d{44,50})$");
            return key.Success ? key.Groups[1].Value : text;
        }

        // What a unique code looks like: a UUID (CFDI), a CUFE or CUDE (SHA-384, 96 hex digits), an access key of 44 digits (NF-e, CT-e) or the 50
        // of a national NFS-e.
        private static readonly Regex CodeShape = new(@"^(?:[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}|[0-9A-F]{96}|\d{44}|\d{50}|(?:C\|)?\d{6,8}-[0-9K]-\d{2,3}-\d{1,10})$", RegexOptions.CultureInvariant);

        // Chile's Registro de Compras y Ventas (the CSV the SII gives): a document is named by three columns, the RUT of the other side, the type
        // and the folio, not by one code. Its columns are found by their names, not by their places.
        private static readonly Regex RcvType = new(@"^tipo\s*(de\s*)?doc", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RcvFolio = new(@"^folio\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RcvSupplier = new(@"^rut\s*(del\s*)?(proveedor|emisor)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RcvClient = new(@"^rut\s*(del\s*)?(cliente|receptor)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // When the rows are a Chilean register, a column with the code of each line is added at their end: for purchases RUT-type-folio of the
        // supplier (the issuer, as the folder's rows are coded), for sales C| and the client's RUT (the folder's rows have that code too). The side
        // of the company is kept: the customer in a register of purchases, the issuer in one of sales.
        private static List<List<string>> ChileanRegister(List<List<string>> rows, AuthorityList list)
        {
            for (var h = 0; h < Math.Min(rows.Count, 10); h++)
            {
                var header = rows[h];
                var type = header.FindIndex(c => RcvType.IsMatch(c));
                var folio = header.FindIndex(c => RcvFolio.IsMatch(c));
                var supplier = header.FindIndex(c => RcvSupplier.IsMatch(c));
                var client = header.FindIndex(c => RcvClient.IsMatch(c));
                var rut = supplier >= 0 ? supplier : client;
                if (type < 0 || folio < 0 || rut < 0) continue;
                list.Side = supplier >= 0 ? "customer" : "issuer";
                list.CounterpartyColumn = rut;
                var prefix = supplier >= 0 ? "" : "C|";
                var width = rows.Max(r => r.Count);
                var result = new List<List<string>>();
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i].Concat(Enumerable.Repeat("", width - rows[i].Count)).ToList();
                    if (i == h) row.Add("Código (RUT-Tipo-Folio)");
                    else if (i > h && Cell(row, rut) is { Length: > 0 } r && Cell(row, type) is { Length: > 0 } t && Cell(row, folio) is { Length: > 0 } f)
                        row.Add(prefix + Rut(r) + "-" + t.Trim() + "-" + f.Trim().TrimStart('0'));
                    else row.Add("");
                    result.Add(row);
                }
                return result;
            }
            return rows;
        }

        // A RUT as the DTE writes it: no dots, a hyphen before the check digit, K in capitals.
        internal static string Rut(string text)
        {
            var rut = new string((text ?? "").Where(c => char.IsDigit(c) || c is 'k' or 'K' or '-').ToArray()).ToUpperInvariant();
            if (!rut.Contains('-') && rut.Length > 1) rut = rut.Substring(0, rut.Length - 1) + "-" + rut[^1];
            return rut;
        }

        private static readonly Regex CodeHeader = new(@"uuid|cufe|cude|chave|folio\s*fiscal|clave\s*de\s*acceso|access\s*key|c[oó]digo\s*[uú]nico", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // A header that names the code of another document (the related CFDI, the one replaced, the reference): a code column, but not the list's own.
        private static readonly Regex RelatedHeader = new(@"relat|relacion|referen|origen|origin|sustitu|substitu|anterior|parent|vinculad|asociad|associad", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StatusHeader = new(@"^(estatus|estado|status|situa[cç][aã]o|situaci[oó]n|state)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DateHeader = new(@"fecha|data|date", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex IssueDateHeader = new(@"emisi[oó]n|emiss[aã]o|issue|docto|expedici[oó]n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A status that says the document is not valid at the authority: cancelled (SAT: 0), annulled, rejected, denied.
        // Whole words: "Cancelable" (the SAT's EsCancelable) says the document can still be cancelled, not that it was.
        private static readonly Regex CancelledStatus = new(@"^(0|cancelad[oa]s?|cancell?ed|anulad[oa]s?|annulled|rechazad[oa]s?|rejeitad[oa]s?|rejected|denegad[oa]s?|denied|inutilizad[oa]s?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static AuthorityList FromRows(List<List<string>> rows, string name, InvoiceTableResult table)
        {
            var list = new AuthorityList { Name = name };
            if (rows.Count == 0) return list;
            rows = ChileanRegister(rows, list);
            var known = FolderCodes(table);
            var width = rows.Max(r => r.Count);

            // The column of codes: the one whose cells are codes of the folder, or look like codes, the most. A column whose header names the code
            // of the document (UUID, CUFE, Chave...) comes first when its cells are codes: another column can repeat a code of the folder (a related
            // document, a reference) without being the list's own code.
            // header rank: 2 the document's own code (UUID, CUFE...), 1 the code of a related document (Related UUID, UUID relacionado), 0 none
            var best = -1;
            var bestScore = 0;
            var bestRank = -1;
            for (var c = 0; c < width; c++)
            {
                var score = 0;
                var rank = 0;
                foreach (var row in rows)
                {
                    if (c >= row.Count || row[c].Length == 0) continue;
                    var code = Normalize(row[c]);
                    if (known.ContainsKey(code)) score += 3;
                    else if (CodeShape.IsMatch(code)) score += 1;
                    else if (CodeHeader.IsMatch(row[c]) && row[c].Length < 60) rank = Math.Max(rank, RelatedHeader.IsMatch(row[c]) ? 1 : 2);
                }
                if (score == 0) continue;
                if (rank > bestRank || (rank == bestRank && score > bestScore)) { best = c; bestScore = score; bestRank = rank; }
            }
            if (best < 0) return list;
            list.CodeColumn = best;

            // the first line with a code is the first entry, and the line above it (when it is not one) holds the headers
            var first = rows.FindIndex(r => best < r.Count && IsCode(r[best], known));
            if (first < 0)
            {
                list.CodeColumn = -1;
                return list;
            }
            if (first > 0) list.Headers = rows[first - 1].ToList();
            list.StatusColumn = list.Headers.FindIndex(h => StatusHeader.IsMatch(h));
            list.DateColumn = list.Headers.FindIndex(h => DateHeader.IsMatch(h) && IssueDateHeader.IsMatch(h));
            if (list.DateColumn < 0) list.DateColumn = list.Headers.FindIndex(h => DateHeader.IsMatch(h));

            for (var i = first; i < rows.Count; i++)
            {
                var row = rows[i];
                if (best >= row.Count || !IsCode(row[best], known)) continue;
                var status = Cell(row, list.StatusColumn);
                list.Entries.Add(new AuthorityEntry
                {
                    Code = Normalize(row[best]),
                    Status = status,
                    Cancelled = status.Length > 0 && CancelledStatus.IsMatch(status),
                    Date = IsoDate(Cell(row, list.DateColumn)),
                    Cells = row,
                    Line = i + 1,
                });
            }
            var dates = list.Entries.Select(e => e.Date).Where(d => d != null).OrderBy(d => d, StringComparer.Ordinal).ToList();
            if (dates.Count > 0) { list.From = dates[0]; list.To = dates[^1]; }
            // the lines of a Chilean register name the other side (the supplier, the client): they never say who the company is
            list.Parties = list.Side != null ? new List<string>() : CommonIds(list);
            return list;
        }

        private static bool IsCode(string cell, Dictionary<string, InvoiceRow> known)
        {
            var code = Normalize(cell);
            return code.Length > 0 && (known.ContainsKey(code) || CodeShape.IsMatch(code));
        }

        private static string Cell(List<string> row, int column) => column >= 0 && column < row.Count ? row[column] : "";

        // yyyy-MM-dd from "2026-06-01", "2026-06-01 10:00:00", "2026-06-01T10:00:00" or "01/06/2026" (day first, as in Latin America); null otherwise.
        internal static string IsoDate(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var iso = Regex.Match(text, @"^\s*(\d{4})-(\d{2})-(\d{2})");
            if (iso.Success) return $"{iso.Groups[1].Value}-{iso.Groups[2].Value}-{iso.Groups[3].Value}";
            var dmy = Regex.Match(text, @"^\s*(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})");
            if (dmy.Success && int.Parse(dmy.Groups[2].Value, CultureInfo.InvariantCulture) is var month && month is >= 1 and <= 12)
                return $"{dmy.Groups[3].Value}-{month:00}-{int.Parse(dmy.Groups[1].Value, CultureInfo.InvariantCulture):00}";
            return null;
        }

        // A cell written like a tax ID: an RFC (Mexico), a NIT (Colombia, with or without dots and its check digit), a CNPJ or CPF (Brazil), a RUT
        // (Chile). A date or an amount is not one.
        private static readonly Regex IdShape = new(@"^(?:[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}|\d{1,3}(?:\.\d{3}){2,3}(?:-[\dK])?|\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}|\d{6,14}(?:-[\dK])?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // The tax ID of the company the list is about: a value written like an ID and on every line of the list, in whatever column (the receiver of
        // a list of received invoices, the issuer of a list of issued ones).
        private static List<string> CommonIds(AuthorityList list)
        {
            if (list.Entries.Count == 0) return new List<string>();
            List<string> common = null;
            foreach (var entry in list.Entries)
            {
                var ids = entry.Cells.Select((c, i) => i == list.CodeColumn || i == list.DateColumn || i == list.StatusColumn || !IdShape.IsMatch(c) ? "" : Id(c))
                    .Where(c => c.Length >= 6).Distinct().ToList();
                // the same company written with and without its check digit (900000001 and 9000000015) stays one candidate
                common = common == null ? ids : common.Where(c => ids.Any(i => SameId(c, i))).ToList();
                if (common.Count == 0) break;
            }
            return common?.OrderBy(c => c, StringComparer.Ordinal).ToList() ?? new List<string>();
        }

        // An ID with only its letters and digits: "900.000.001-5" and "9000000015" are the same NIT.
        private static string Id(string text) => new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        // The same company in a list: a Chilean RUT always has its check digit, so in a register only the same RUT is the same company (12345674-2
        // is not 1234567-4); elsewhere an ID with or without its check digit (a NIT) is.
        private static bool SameParty(AuthorityList list, string a, string b) => list.Side != null ? a.Length > 0 && a == b : SameId(a, b);

        // The same company: the same ID, or the same ID with its check digit written after it in one of the two (a NIT with or without its DV).
        private static bool SameId(string a, string b) =>
            a.Length > 0 && b.Length > 0 && (a == b || (a.Length == b.Length + 1 && a.StartsWith(b, StringComparison.Ordinal)) || (b.Length == a.Length + 1 && b.StartsWith(a, StringComparison.Ordinal)));

        // The rows of the folder by their unique codes (the code shown, and the key when another one says which document it is).
        private static Dictionary<string, InvoiceRow> FolderCodes(InvoiceTableResult table)
        {
            var codes = new Dictionary<string, InvoiceRow>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                if (!string.IsNullOrEmpty(row.Cuf)) codes.TryAdd(Normalize(row.Cuf), row);
                if (!string.IsNullOrEmpty(row.Key)) codes.TryAdd(Normalize(row.Key), row);
                // a Chilean document also as a register writes it (RUT without dots, folio without leading zeros), beside its own code
                if (row.Kind == "Dte" && ChileanCode(row) is { } chilean) codes.TryAdd(Normalize(chilean), row);
            }
            return codes;
        }

        // A Chilean document by the client's RUT, the type and the folio, as a register of sales names it. Two companies can sell to the same client
        // with the same type and folio, so every document with the key is kept, and the company of the register tells them apart.
        // Also a factura de compra (type 46) by its supplier, as a register of purchases names it: issued by the buyer, its supplier is its receiver,
        // so two buyers can have the same key too. And in a register of sales its client is the buyer, which is its issuer.
        private static Dictionary<string, List<InvoiceRow>> SalesCodes(InvoiceTableResult table)
        {
            var codes = new Dictionary<string, List<InvoiceRow>>(StringComparer.Ordinal);
            void Add(string key, InvoiceRow row)
            {
                key = Normalize(key);
                if (!codes.TryGetValue(key, out var rows)) codes[key] = rows = new List<InvoiceRow>();
                rows.Add(row);
            }
            foreach (var row in table.Rows.Where(r => r.Kind == "Dte" && r.TypeCode != null && r.Number != null))
            {
                var type = row.TypeCode.Trim();
                var folio = row.Number.Trim().TrimStart('0');
                var buyerIssues = type == "46";
                var client = buyerIssues ? row.IssuerId : row.CustomerId;
                if (!string.IsNullOrEmpty(client)) Add("C|" + Rut(client) + "-" + type + "-" + folio, row);
                if (buyerIssues && !string.IsNullOrEmpty(row.CustomerId)) Add(Rut(row.CustomerId) + "-46-" + folio, row);
            }
            return codes;
        }

        // The company of a register when none of its lines is in the folder: the RUT that every Chilean document of the folder within the period
        // has, as issuer or customer (a folder of one company's purchases and sales). None when there is no such RUT.
        private static List<string> FolderCompany(InvoiceTableResult table, AuthorityList list)
        {
            // the register names the other side: a RUT it names (a client of all the folder's sales) is never the company
            var others = new HashSet<string>(list.CounterpartyColumn < 0 ? Enumerable.Empty<string>()
                : list.Entries.Select(e => Id(Rut(Cell(e.Cells, list.CounterpartyColumn)))).Where(i => i.Length > 0));
            List<string> common = null;
            foreach (var row in table.Rows.Where(r => r.Kind == "Dte"))
            {
                var date = IsoDate(row.Date);
                if (list.From != null && date != null && (string.CompareOrdinal(date, list.From) < 0 || string.CompareOrdinal(date, list.To) > 0)) continue;
                var ids = new[] { Id(row.IssuerId), Id(row.CustomerId) }.Where(i => i.Length > 0).ToList();
                common = common == null ? ids : common.Where(c => ids.Contains(c)).ToList();
                if (common.Count == 0) break;
            }
            return (common ?? new List<string>()).Where(c => !others.Contains(c)).ToList();
        }

        // The company's party of a document on a register's side: the buyer of a purchase, the seller of a sale. Usually the customer of a purchase
        // and the issuer of a sale; a factura de compra (type 46) is issued by the buyer, so there the roles in the XML are the other way round.
        private static string SideParty(InvoiceRow row, AuthorityList list)
        {
            var buyerIssues = row.Kind == "Dte" && row.TypeCode?.Trim() == "46";
            return list.Side == "customer" ? (buyerIssues ? row.IssuerId : row.CustomerId) : (buyerIssues ? row.CustomerId : row.IssuerId);
        }

        // The code of a Chilean row as a register writes it: the issuer's RUT in the DTE's form, the type, the folio without leading zeros.
        private static string ChileanCode(InvoiceRow row) =>
            string.IsNullOrEmpty(row.IssuerId) || row.TypeCode == null || row.Number == null ? null
                : Rut(row.IssuerId) + "-" + row.TypeCode.Trim() + "-" + row.Number.Trim().TrimStart('0');

        // ---- the comparison -------------------------------------------------------------------------------------------------------------

        // Compares, and marks a document that the list says was cancelled or rejected: left out of the totals, with the reason in its answer column.
        public static InvoiceCheckResult Compare(InvoiceTableResult table, AuthorityList list, string cancelledNote)
        {
            var result = new InvoiceCheckResult { List = list };
            if (list.CodeColumn < 0) return result;
            var codes = FolderCodes(table);
            var sales = SalesCodes(table);
            var ambiguous = false;
            // The company of a register, when the folder itself establishes it (the one RUT on every Chilean document of the folder that the register
            // does not name as the other side): the only evidence that may decide between two companies' documents for one line.
            var companyFromFolder = false;
            if (list.Side != null && FolderCompany(table, list) is { Count: 1 } established)
            {
                list.Parties = established;
                companyFromFolder = true;
            }
            // Every document a code can name: its own code, and the keys that do not name the issuer (the client of a sale, the supplier of a
            // factura de compra), which can be another document's own code too (A's and S's facturas de compra to each other with one folio).
            List<InvoiceRow> Candidates(string code)
            {
                var found = new List<InvoiceRow>();
                if (codes.TryGetValue(code, out var row)) found.Add(row);
                if (sales.TryGetValue(code, out var shared)) found.AddRange(shared.Where(s => !found.Contains(s)));
                return found;
            }
            InvoiceRow Find(string code)
            {
                if (ambiguous) return null;
                var candidates = Candidates(code);
                if (candidates.Count == 0) return null;
                // documents of more than one company for one code: only a company the folder establishes decides between them (one inferred from
                // the register's other lines could be the wrong one); otherwise the line stays undecided
                if (Several(candidates) && !companyFromFolder) return null;
                var own = candidates.Where(OnSide).ToList();
                return own.Count == 1 ? own[0] : null;
            }
            bool Several(List<InvoiceRow> candidates) => candidates.Select(c => Id(list.Side == null ? c.IssuerId : SideParty(c, list))).Distinct().Count() > 1;
            // a document of a register is the company's when the company is on the register's side of it (once the company is known)
            bool OnSide(InvoiceRow row) => list.Side == null || list.Parties.Count == 0
                || list.Parties.Any(p => SameParty(list, p, Id(SideParty(row, list))));
            // the documents of the folder that are not rows (a payment complement...) are there too: never "no XML"
            var others = new Dictionary<string, InvoiceRow>(StringComparer.Ordinal);
            foreach (var document in table.OtherDocuments) others.TryAdd(Normalize(document.Cuf), document);
            // an ID that is on every line of the list but not on the documents of the folder that the list has is not the company of the list (a
            // list of one supplier and one client both ways, the SAT's RfcPac): only the ones that every matched document has are kept, the
            // documents that are not rows counted too
            var matched = list.Entries.Select(e => Find(e.Code) ?? (others.TryGetValue(e.Code, out var o) ? o : null)).Where(r => r != null).ToList();
            if (list.Side != null)
            {
                // a register says the company's side: unless the folder established it, its company is the one every matched document has on that
                // side; when they are of different companies (or more than one company is left) it is not known whose register this is, and nothing
                // is matched
                if (!companyFromFolder && matched.Count > 0)
                {
                    var sides = matched.Select(r => Id(SideParty(r, list))).Where(i => i.Length > 0).Distinct().ToList();
                    list.Parties = sides.Where(p => matched.All(r => SameParty(list, p, Id(SideParty(r, list))))).ToList();
                    if (list.Parties.Count == 0 && sides.Count > 1) ambiguous = result.Ambiguous = true;
                }
                if (list.Parties.Count > 1) ambiguous = result.Ambiguous = true;
                else if (list.Parties.Count == 0 && !ambiguous) result.CompanyUnknown = true;
            }
            else if (matched.Count > 0)
                list.Parties = list.Parties.Where(p => matched.All(r => SameId(p, Id(r.IssuerId)) || SameId(p, Id(r.CustomerId)))).ToList();
            // the documents of an undecided line: they are in the folder, so none of them is "not in the list" either
            var undecided = new HashSet<InvoiceRow>();
            var listed = new HashSet<string>(list.Entries.Select(e => e.Code), StringComparer.Ordinal);
            var found = new HashSet<InvoiceRow>();
            var missing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in list.Entries)
            {
                // the company is not known: no line is "no XML" (its document may be in the folder), the ambiguity is the finding
                if (ambiguous) break;
                if (others.TryGetValue(entry.Code, out var other))
                {
                    if (entry.Cancelled) result.CancelledOthers.Add((other.File, entry));
                    else result.Matched++;
                    continue;
                }
                var row = Find(entry.Code);
                if (row != null)
                {
                    if (!found.Add(row)) continue;
                    if (entry.Cancelled)
                    {
                        result.Cancelled.Add((row, entry));
                        if (!row.Void)
                        {
                            row.Void = true;
                            var note = string.Format(cancelledNote, StatusText(list, entry));
                            row.Answer = string.IsNullOrEmpty(row.Answer) ? note : row.Answer + " · " + note;
                        }
                    }
                    else result.Matched++;
                }
                // more than one company's document for a line of a register of sales, and the company not known: undecided, not missing
                // (a cancelled line too: one of its documents may be the cancelled one, so none of them is "not in the list")
                else if (Candidates(entry.Code) is { Count: > 1 } several && (Several(several) && !companyFromFolder || list.Parties.Count == 0))
                {
                    result.Unresolved.Add(entry);
                    undecided.UnionWith(several);
                }
                // a document cancelled at the authority that is not in the folder is as it should be
                else if (!entry.Cancelled && missing.Add(entry.Code)) result.Missing.Add(entry);
            }
            // a payment complement that the list says was cancelled pays nothing: what the PPD invoices were paid is made again without it
            var cancelled = new HashSet<string>(list.Entries.Where(e => e.Cancelled).Select(e => e.Code), StringComparer.Ordinal);
            var voided = table.Payments.Where(p => p.Complement != null && cancelled.Contains(Normalize(p.Complement))).ToList();
            if (voided.Count > 0)
            {
                table.Payments.RemoveAll(voided.Contains);
                table.CancelledPayments.AddRange(voided);
                InvoiceTable.SetPpd(table);
            }
            // the other way: what the folder has and the list does not, the documents that are not rows too (a payment complement the authority does
            // not know is as much a finding as an invoice), within the period and the company of the list
            foreach (var row in table.Rows.Concat(others.Values))
            {
                // nor is any document of the folder "not in the list" while it is not known whose register this is
                if (ambiguous || result.CompanyUnknown) break;
                if (found.Contains(row) || undecided.Contains(row)) continue;
                if (!InScope(row, list))
                {
                    result.OutOfScope++;
                    continue;
                }
                var cuf = string.IsNullOrEmpty(row.Cuf) ? null : Normalize(row.Cuf);
                var key = string.IsNullOrEmpty(row.Key) ? null : Normalize(row.Key);
                if ((cuf != null && listed.Contains(cuf)) || (key != null && listed.Contains(key))) continue;
                result.NotInList.Add(row);
            }
            result.Unread = table.Skipped.Where(s => s.Reason is InvoiceSkip.Unreadable or InvoiceSkip.TooBig or InvoiceSkip.NotWellFormed
                or InvoiceSkip.Encrypted or InvoiceSkip.TooMuchInside).ToList();
            return result;
        }

        // A document of the folder is compared when it is of the period of the list and of its company (when the list says them).
        private static bool InScope(InvoiceRow row, AuthorityList list)
        {
            var date = IsoDate(row.Date);
            if (list.From != null && date != null && (string.CompareOrdinal(date, list.From) < 0 || string.CompareOrdinal(date, list.To) > 0)) return false;
            if (list.Parties.Count == 0) return true;
            var issuer = Id(row.IssuerId);
            var customer = Id(row.CustomerId);
            // a register of purchases is about what the company received, one of sales about what it issued
            if (list.Side != null) return list.Parties.Any(p => SameParty(list, p, Id(SideParty(row, list))));
            return list.Parties.Any(p => SameId(p, issuer) || SameId(p, customer));
        }

        // ---- what is said -----------------------------------------------------------------------------------------------------------------

        internal sealed class Labels
        {
            public string Title, Summary, Period, Scope, NoCodes, NoFindings, Missing, MissingNote, CancelledTitle, CancelledNote, NotInList, NotInListNote,
                Code, Status, InList, Line, Finding, FindingMissing, FindingCancelled, FindingNotInList, SheetName, CancelledAnswer, Incomplete, FindingUnread, Ambiguous,
                UnresolvedTitle, UnresolvedNote, FindingUnresolved, FindingAmbiguous, CompanyUnknown, FindingCompanyUnknown;

            public static Labels For(XmlLabels l) => l.IsSpanish ? Spanish : l.IsPortuguese ? Portuguese : English;

            private static readonly Labels English = new()
            {
                Title = "Compared with {0}",
                Summary = "{0} documents of the list are in the folder. The list has {1} documents{2}.",
                Period = ", from {0} to {1}",
                Scope = "{0} documents of the folder are of another period or another company than the list, and were not compared.",
                NoCodes = "No column of unique codes (UUID, CUFE, access key) was found in {0}, so nothing was compared.",
                NoFindings = "Every document of the list is in the folder, and every document of the folder is in the list.",
                Missing = "In the list, but no XML in the folder", MissingNote = "Ask the issuer for these XML files, or download them from the portal of the tax authority.",
                CancelledTitle = "Cancelled or rejected in the list, but in the folder", CancelledNote = "An invoice among them stays in the table above and is not added to its totals; a payment complement among them no longer counts as a payment.",
                NotInList = "In the folder, but not in the list", NotInListNote = "The list may be of another date or another kind (received or issued), or the document may not be valid at the tax authority: check it on its portal.",
                Code = "Unique code", Status = "Status in the list", InList = "Line of the list", Line = "Line",
                Finding = "Finding", FindingMissing = "No XML in the folder", FindingCancelled = "Cancelled or rejected in the list", FindingNotInList = "Not in the list",
                Incomplete = "{0} files of the folder could not be read (see Files not included below): an invoice said here to have no XML may be one of them.",
                FindingUnread = "Could not be read: may be an invoice of the list",
                UnresolvedTitle = "In the list, with documents of more than one company in the folder",
                UnresolvedNote = "For these lines the folder has documents of different companies with the same client, type and folio, and the folder does not say which company is yours: one of them may be the line's document.",
                FindingUnresolved = "More than one company's document in the folder",
                FindingAmbiguous = "Nothing matched: the register's company is not known",
                CompanyUnknown = "Neither this register nor the folder says which company the register is of, so no document of the folder is said to be \"not in the list\" (one of them may be the company's own). Compare a folder of one company's documents.",
                FindingCompanyUnknown = "Not compared the other way: the register's company is not known",
                Ambiguous = "The documents of the folder that this register names are of different companies, and the folder does not say which one is yours: nothing was matched. Compare a folder of one company's documents.",
                SheetName = "Check", CancelledAnswer = "Cancelled or rejected in the list of the tax authority ({0})",
            };

            private static readonly Labels Spanish = new()
            {
                Title = "Comparación con {0}",
                Summary = "{0} documentos de la lista están en la carpeta. La lista tiene {1} documentos{2}.",
                Period = ", del {0} al {1}",
                Scope = "{0} documentos de la carpeta son de otro periodo o de otra empresa que la lista, y no se compararon.",
                NoCodes = "No se encontró en {0} ninguna columna de códigos únicos (UUID, CUFE, clave de acceso), así que no se comparó nada.",
                NoFindings = "Todos los documentos de la lista están en la carpeta, y todos los documentos de la carpeta están en la lista.",
                Missing = "En la lista, pero sin XML en la carpeta", MissingNote = "Pide estos XML al emisor o descárgalos del portal de la autoridad tributaria.",
                CancelledTitle = "Cancelados o rechazados en la lista, pero en la carpeta", CancelledNote = "Una factura entre ellos sigue en la tabla de arriba y no se suma a sus totales; un complemento de pago entre ellos ya no cuenta como pago.",
                NotInList = "En la carpeta, pero no en la lista", NotInListNote = "La lista puede ser de otra fecha o de otro tipo (recibidos o emitidos), o el documento puede no ser válido ante la autoridad tributaria: verifícalo en su portal.",
                Code = "Código único", Status = "Estado en la lista", InList = "Línea de la lista", Line = "Línea",
                Finding = "Hallazgo", FindingMissing = "Sin XML en la carpeta", FindingCancelled = "Cancelado o rechazado en la lista", FindingNotInList = "No está en la lista",
                Incomplete = "{0} archivos de la carpeta no se pudieron leer (ver Archivos no incluidos más abajo): una factura que aquí figura sin XML puede ser uno de ellos.",
                FindingUnread = "No se pudo leer: puede ser una factura de la lista",
                UnresolvedTitle = "En la lista, con documentos de más de una empresa en la carpeta",
                UnresolvedNote = "Para estas líneas la carpeta tiene documentos de empresas distintas con el mismo cliente, tipo y folio, y la carpeta no dice cuál es tu empresa: uno de ellos puede ser el documento de la línea.",
                FindingUnresolved = "Documentos de más de una empresa en la carpeta",
                FindingAmbiguous = "Nada relacionado: no se sabe de qué empresa es el registro",
                CompanyUnknown = "Ni este registro ni la carpeta dicen de qué empresa es el registro, así que ningún documento de la carpeta figura como «no está en la lista» (uno de ellos puede ser de la propia empresa). Compara una carpeta con documentos de una sola empresa.",
                FindingCompanyUnknown = "Sin comparar en sentido inverso: no se sabe de qué empresa es el registro",
                Ambiguous = "Los documentos de la carpeta que este registro nombra son de empresas distintas, y la carpeta no dice cuál es la tuya: no se relacionó nada. Compara una carpeta con documentos de una sola empresa.",
                SheetName = "Comparación", CancelledAnswer = "Cancelado o rechazado en la lista de la autoridad tributaria ({0})",
            };

            private static readonly Labels Portuguese = new()
            {
                Title = "Comparação com {0}",
                Summary = "{0} documentos da lista estão na pasta. A lista tem {1} documentos{2}.",
                Period = ", de {0} a {1}",
                Scope = "{0} documentos da pasta são de outro período ou de outra empresa que a lista, e não foram comparados.",
                NoCodes = "Nenhuma coluna de códigos únicos (UUID, CUFE, chave de acesso) foi encontrada em {0}, então nada foi comparado.",
                NoFindings = "Todos os documentos da lista estão na pasta, e todos os documentos da pasta estão na lista.",
                Missing = "Na lista, mas sem XML na pasta", MissingNote = "Peça estes XML ao emitente ou baixe-os do portal da autoridade fiscal.",
                CancelledTitle = "Cancelados ou rejeitados na lista, mas na pasta", CancelledNote = "Uma fatura entre eles continua na tabela acima e não entra nos seus totais; um complemento de pagamento entre eles não conta mais como pagamento.",
                NotInList = "Na pasta, mas não na lista", NotInListNote = "A lista pode ser de outra data ou de outro tipo (recebidos ou emitidos), ou o documento pode não ser válido na autoridade fiscal: confira no portal dela.",
                Code = "Código único", Status = "Situação na lista", InList = "Linha da lista", Line = "Linha",
                Finding = "Constatação", FindingMissing = "Sem XML na pasta", FindingCancelled = "Cancelado ou rejeitado na lista", FindingNotInList = "Não está na lista",
                Incomplete = "{0} arquivos da pasta não puderam ser lidos (ver Arquivos não incluídos abaixo): uma fatura que aqui aparece sem XML pode ser um deles.",
                FindingUnread = "Não foi possível ler: pode ser uma fatura da lista",
                UnresolvedTitle = "Na lista, com documentos de mais de uma empresa na pasta",
                UnresolvedNote = "Para estas linhas a pasta tem documentos de empresas diferentes com o mesmo cliente, tipo e folio, e a pasta não diz qual é a sua empresa: um deles pode ser o documento da linha.",
                FindingUnresolved = "Documentos de mais de uma empresa na pasta",
                FindingAmbiguous = "Nada relacionado: não se sabe de que empresa é o registro",
                CompanyUnknown = "Nem este registro nem a pasta dizem de que empresa é o registro, então nenhum documento da pasta aparece como “não está na lista” (um deles pode ser da própria empresa). Compare uma pasta com documentos de uma só empresa.",
                FindingCompanyUnknown = "Sem comparar no sentido inverso: não se sabe de que empresa é o registro",
                Ambiguous = "Os documentos da pasta que este registro cita são de empresas diferentes, e a pasta não diz qual é a sua: nada foi relacionado. Compare uma pasta com documentos de uma só empresa.",
                SheetName = "Comparação", CancelledAnswer = "Cancelado ou rejeitado na lista da autoridade fiscal ({0})",
            };
        }

        // The status as the list writes it, with the name of its column when the value alone says little (the SAT's Estatus is 0 or 1).
        internal static string StatusText(AuthorityList list, AuthorityEntry entry)
        {
            var header = list.StatusColumn >= 0 && list.StatusColumn < list.Headers.Count ? list.Headers[list.StatusColumn] : "";
            return header.Length > 0 && entry.Status.Length <= 2 ? header + " " + entry.Status : entry.Status;
        }

        // A line of the list in a few words: its cells, without the code, joined, and cut at 200 characters.
        private static string ListText(AuthorityEntry entry, AuthorityList list)
        {
            var text = string.Join(" · ", entry.Cells.Where((c, i) => i != list.CodeColumn && c.Length > 0));
            return text.Length > 200 ? text.Substring(0, 199) + "…" : text;
        }

        private static string Md(string text) => MarkdownText.EscapeInline(XmlConverter.OneLine(text ?? ""));

        public static string ToMarkdown(InvoiceCheckResult check, XmlLabels l)
        {
            var c = Labels.For(l);
            var list = check.List;
            var sb = new StringBuilder();
            sb.Append("## ").Append(Md(string.Format(c.Title, list.Name))).Append("\n\n");
            if (list.CodeColumn < 0)
            {
                sb.Append(Md(string.Format(c.NoCodes, list.Name))).Append("\n\n");
                return sb.ToString();
            }
            var period = list.From == null ? "" : string.Format(c.Period, list.From, list.To);
            sb.Append(string.Format(c.Summary, check.Matched + check.Cancelled.Count + check.CancelledOthers.Count, list.Entries.Count, period));
            if (check.OutOfScope > 0) sb.Append(' ').Append(string.Format(c.Scope, check.OutOfScope));
            sb.Append("\n\n");
            if (check.Ambiguous) sb.Append(c.Ambiguous).Append("\n\n");
            if (check.CompanyUnknown) sb.Append(c.CompanyUnknown).Append("\n\n");
            if (!check.HasFindings && !check.Ambiguous)
            {
                sb.Append(c.NoFindings).Append("\n\n");
                return sb.ToString();
            }
            if (check.Missing.Count > 0)
            {
                sb.Append("### ").Append(c.Missing).Append(" (").Append(check.Missing.Count).Append(")\n\n").Append(c.MissingNote).Append("\n\n");
                if (check.Unread.Count > 0) sb.Append(string.Format(c.Incomplete, check.Unread.Count)).Append("\n\n");
                var table = new List<IReadOnlyList<string>> { new[] { c.Code, l.Date, c.InList, c.Line } };
                table.AddRange(check.Missing.Select(e => (IReadOnlyList<string>)new[] { Md(e.Code), Md(e.Date ?? ""), Md(ListText(e, list)), e.Line.ToString(CultureInfo.InvariantCulture) }));
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
            }
            if (check.Unresolved.Count > 0)
            {
                sb.Append("### ").Append(c.UnresolvedTitle).Append(" (").Append(check.Unresolved.Count).Append(")\n\n").Append(c.UnresolvedNote).Append("\n\n");
                var table = new List<IReadOnlyList<string>> { new[] { c.Code, l.Date, c.InList, c.Line } };
                table.AddRange(check.Unresolved.Select(e => (IReadOnlyList<string>)new[] { Md(e.Code), Md(e.Date ?? ""), Md(ListText(e, list)), e.Line.ToString(CultureInfo.InvariantCulture) }));
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
            }
            if (check.Cancelled.Count + check.CancelledOthers.Count > 0)
            {
                sb.Append("### ").Append(c.CancelledTitle).Append(" (").Append(check.Cancelled.Count + check.CancelledOthers.Count).Append(")\n\n").Append(c.CancelledNote).Append("\n\n");
                var table = new List<IReadOnlyList<string>> { new[] { c.Code, c.Status, l.Date, l.Number, l.Issuer, l.TaxInclusive, l.File } };
                table.AddRange(check.Cancelled.Select(x => (IReadOnlyList<string>)new[] { Md(x.Entry.Code), Md(StatusText(list, x.Entry)), Md(x.Row.Date), Md(x.Row.Number), Md(x.Row.Issuer), Md(x.Row.Total.Text), Md(x.Row.File) }));
                // a payment complement: no row, so only what the list says of it, and its file
                table.AddRange(check.CancelledOthers.Select(x => (IReadOnlyList<string>)new[] { Md(x.Entry.Code), Md(StatusText(list, x.Entry)), Md(x.Entry.Date), "", "", "", Md(x.File) }));
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
            }
            if (check.NotInList.Count > 0)
            {
                sb.Append("### ").Append(c.NotInList).Append(" (").Append(check.NotInList.Count).Append(")\n\n").Append(c.NotInListNote).Append("\n\n");
                var table = new List<IReadOnlyList<string>> { new[] { l.Date, l.Number, l.Issuer, l.Customer, l.TaxInclusive, c.Code, l.File } };
                table.AddRange(check.NotInList.Select(r => (IReadOnlyList<string>)new[] { Md(r.Date), Md(r.Number), Md(r.Issuer), Md(r.Customer), Md(r.Total.Text), Md(r.Cuf ?? r.Key), Md(r.File) }));
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
            }
            return sb.ToString();
        }

        // The findings as one table (the sheet of the workbook): what, the code, and what is known of the document.
        internal static List<string[]> FindingRows(InvoiceCheckResult check, XmlLabels l, out string[] headers)
        {
            var c = Labels.For(l);
            headers = new[] { c.Finding, c.Code, l.Date, l.Number, l.Issuer, l.Customer, l.TaxInclusive, c.Status, c.InList, l.File };
            var rows = new List<string[]>();
            // a register whose company could not be told: one line that says why nothing was matched
            if (check.Ambiguous)
                rows.Add(new[] { c.FindingAmbiguous, "", "", "", "", "", "", "", c.Ambiguous, "" });
            if (check.CompanyUnknown)
                rows.Add(new[] { c.FindingCompanyUnknown, "", "", "", "", "", "", "", c.CompanyUnknown, "" });
            foreach (var e in check.Missing)
                rows.Add(new[] { c.FindingMissing, e.Code, e.Date ?? "", "", "", "", "", StatusText(check.List, e), ListText(e, check.List), "" });
            foreach (var e in check.Unresolved)
                rows.Add(new[] { c.FindingUnresolved, e.Code, e.Date ?? "", "", "", "", "", StatusText(check.List, e), ListText(e, check.List), "" });
            foreach (var (r, e) in check.Cancelled)
                rows.Add(new[] { c.FindingCancelled, e.Code, r.Date ?? "", r.Number ?? "", r.Issuer ?? "", r.Customer ?? "", r.Total.Text, StatusText(check.List, e), ListText(e, check.List), r.File });
            foreach (var (file, e) in check.CancelledOthers)
                rows.Add(new[] { c.FindingCancelled, e.Code, e.Date ?? "", "", "", "", "", StatusText(check.List, e), ListText(e, check.List), file });
            foreach (var r in check.NotInList)
                rows.Add(new[] { c.FindingNotInList, r.Cuf ?? r.Key ?? "", r.Date ?? "", r.Number ?? "", r.Issuer ?? "", r.Customer ?? "", r.Total.Text, "", "", r.File });
            // only beside invoices said to have no XML: then each unread file could be one of them
            if (check.Missing.Count > 0)
                foreach (var s in check.Unread)
                    rows.Add(new[] { c.FindingUnread, "", "", "", "", "", "", "", "", s.File });
            return rows;
        }
    }
}
