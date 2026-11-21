using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // Mexico: an invoice paid later (MetodoPago PPD, "pago en parcialidades o diferido") needs a payment complement (a CFDI of type P, the "REP")
    // for every payment, and its customer cannot take the VAT until it is there. The complements of the folder are matched to the invoices by the
    // UUID they name (DoctoRelacionado/IdDocumento), and each PPD invoice says whether it has none, is paid in part (with the balance the last
    // complement gives) or is paid. A complement for an invoice that is not in the folder is listed too.
    internal sealed class CfdiPayment
    {
        // the UUID of the invoice paid, and the complement that says so (its UUID and its file)
        public string Invoice, Complement, File, Date, Currency;
        public int Installment;
        public decimal? Paid, Balance;
    }

    internal static partial class InvoiceTable
    {
        // The payments a complement of type P lists: Pagos 2.0 (CFDI 4.0) and Pagos 1.0 (CFDI 3.3) have the same names.
        internal static List<CfdiPayment> ReadPayments(XElement root, string file, string complementUuid)
        {
            var payments = new List<CfdiPayment>();
            var pagos = XmlUbl.Child(root, "Complemento/Pagos");
            if (pagos == null) return payments;
            foreach (var pago in XmlUbl.Children(pagos, "Pago"))
            {
                var date = (string)pago.Attribute("FechaPago");
                foreach (var docto in XmlUbl.Children(pago, "DoctoRelacionado"))
                {
                    var invoice = ((string)docto.Attribute("IdDocumento"))?.Trim();
                    if (string.IsNullOrEmpty(invoice)) continue;
                    payments.Add(new CfdiPayment
                    {
                        Invoice = invoice.ToUpperInvariant(),
                        Complement = complementUuid?.ToUpperInvariant(),
                        File = file,
                        Date = date != null && date.Length >= 10 ? date.Substring(0, 10) : date,
                        Currency = (string)docto.Attribute("MonedaDR"),
                        Installment = int.TryParse((string)docto.Attribute("NumParcialidad"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0,
                        Paid = Number((string)docto.Attribute("ImpPagado")),
                        Balance = Number((string)docto.Attribute("ImpSaldoInsoluto")),
                    });
                }
            }
            return payments;
        }

        private static decimal? Number(string text) =>
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

        // What a PPD invoice has been paid, from the complements of the folder (the same complement read twice counts once).
        internal sealed class PpdState
        {
            public InvoiceRow Row;
            public List<CfdiPayment> Payments = new();
            public decimal Paid;
            public decimal? Balance;
            public bool Uncertain;
            public bool Settled => Payments.Count > 0 && !Uncertain && Balance is { } b && b <= 0;
        }

        internal static List<PpdState> PpdStates(InvoiceTableResult result)
        {
            var byInvoice = Distinct(result.Payments).ToLookup(p => p.Invoice, StringComparer.OrdinalIgnoreCase);
            var states = new List<PpdState>();
            foreach (var row in result.Rows.Where(r => r.Ppd && !string.IsNullOrEmpty(r.Cuf)))
            {
                var state = new PpdState { Row = row, Payments = byInvoice[row.Cuf.Trim()].OrderBy(p => p.Installment).ThenBy(p => p.Date ?? "", StringComparer.Ordinal).ToList() };
                state.Paid = state.Payments.Sum(p => p.Paid ?? 0);
                // the balance the last complement gives (ImpSaldoInsoluto); without one, the total less what was paid
                var last = state.Payments.LastOrDefault();
                state.Balance = last?.Balance ?? (row.Total.Value is { } total && state.Payments.Count > 0 ? total - state.Paid : null);
                // A payment that the authority's list cancelled and that came before the last complement: the complement of a real payment, once
                // cancelled, is issued again for the same installment (NumParcialidad), and the balances after it count that replacement. When the
                // folder has the replacement, the last balance stands. When it does not, the folder cannot say whether the replacement exists
                // elsewhere (a folder can lack older complements), so the last balance is shown as it is and said to be uncertain, not changed.
                if (last?.Balance != null)
                {
                    var replaced = new HashSet<int>(state.Payments.Select(p => p.Installment));
                    state.Uncertain = Distinct(result.CancelledPayments)
                        .Any(c => string.Equals(c.Invoice, row.Cuf.Trim(), StringComparison.OrdinalIgnoreCase) && Before(c, last)
                                  && !(c.Installment > 0 && replaced.Contains(c.Installment)));
                }
                states.Add(state);
            }
            return states;
        }

        // Earlier than another payment of the same invoice: by installment, then by date.
        private static bool Before(CfdiPayment a, CfdiPayment b) =>
            a.Installment != b.Installment ? a.Installment < b.Installment : string.CompareOrdinal(a.Date ?? "", b.Date ?? "") < 0;

        // The complements whose invoice is not a row of the table.
        internal static List<CfdiPayment> PaymentsWithoutInvoice(InvoiceTableResult result)
        {
            var invoices = new HashSet<string>(result.Rows.Where(r => r.Kind == "Cfdi" && !string.IsNullOrEmpty(r.Cuf)).Select(r => r.Cuf.Trim()), StringComparer.OrdinalIgnoreCase);
            return Distinct(result.Payments).Where(p => !invoices.Contains(p.Invoice)).ToList();
        }

        private static IEnumerable<CfdiPayment> Distinct(IEnumerable<CfdiPayment> payments) =>
            payments.GroupBy(p => (p.Complement ?? p.File, p.Invoice, p.Installment)).Select(g => g.First());

        // Kept on each PPD invoice by Finish (which knows no language), and said in the answer column when the table is written.
        internal static void SetPpd(InvoiceTableResult result)
        {
            foreach (var row in result.Rows.Where(r => r.Ppd))
            {
                row.PpdComplements = 0;
                row.PpdPaid = 0;
                row.PpdBalance = null;
                row.PpdUncertain = false;
            }
            foreach (var state in PpdStates(result))
            {
                // complements, not payments: one complement can pay two installments of the same invoice
                state.Row.PpdComplements = state.Payments.Select(p => p.Complement ?? p.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                state.Row.PpdPaid = state.Paid;
                state.Row.PpdBalance = state.Balance;
                state.Row.PpdUncertain = state.Uncertain;
            }
        }

        // What the answer column says of a PPD invoice; null for any other row.
        internal static string PpdText(InvoiceRow row, XmlLabels l)
        {
            if (!row.Ppd || string.IsNullOrEmpty(row.Cuf)) return null;
            var p = PaymentLabels.For(l);
            if (row.PpdComplements == 0) return p.None;
            var count = row.PpdComplements == 1 ? p.OneComplement : string.Format(p.Complements_, row.PpdComplements);
            if (row.PpdUncertain) return string.Format(p.Uncertain, Money(row.PpdPaid), row.PpdBalance is { } said ? Money(said) : "?", count);
            if (row.PpdBalance is { } b && b <= 0) return string.Format(p.PaidInFull, count);
            return string.Format(p.Partial, Money(row.PpdPaid), row.PpdBalance is { } balance ? Money(balance) : "?", count);
        }

        private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        internal sealed class PaymentLabels
        {
            public string Title, Note, None, PaidInFull, Partial, Uncertain, OneComplement, Complements_, Paid, Balance, Complements, State, Orphans, OrphansNote, Invoice, Complement, PaymentDate, Installment;

            public static PaymentLabels For(XmlLabels l) => l.IsSpanish ? Spanish : l.IsPortuguese ? Portuguese : English;

            private static readonly PaymentLabels English = new()
            {
                Title = "Invoices paid later (PPD) and their payment complements",
                Note = "A PPD invoice is paid later, in one or more payments, and each payment needs a payment complement (a CFDI of type P, the REP); until it is there, the customer cannot take the VAT. The complements in the folder are matched to the invoices by the UUID they name; the balance is the one the last complement gives.",
                None = "PPD: no payment complement in the folder", PaidInFull = "PPD: paid in full ({0})", Partial = "PPD: paid {0}, balance {1} ({2})",
                Uncertain = "PPD: paid {0}; the last complement says balance {1} ({2}), but a payment complement before it was cancelled and the folder has no new one for that installment: check the balance with the issuer",
                OneComplement = "1 payment complement", Complements_ = "{0} payment complements",
                Paid = "Paid", Balance = "Balance", Complements = "Complements", State = "State",
                Orphans = "Payment complements of invoices that are not in the folder", OrphansNote = "These complements pay invoices whose XML is not in the folder.",
                Invoice = "UUID of the invoice", Complement = "Complement", PaymentDate = "Payment date", Installment = "Installment",
            };

            private static readonly PaymentLabels Spanish = new()
            {
                Title = "Facturas PPD y sus complementos de pago",
                Note = "Una factura PPD (pago en parcialidades o diferido) se paga después, en uno o varios pagos, y cada pago necesita un complemento de pago (un CFDI de tipo P, el REP); mientras no exista, el cliente no puede acreditar el IVA. Los complementos de la carpeta se relacionan con las facturas por el UUID que indican; el saldo es el que da el último complemento.",
                None = "PPD: sin complemento de pago en la carpeta", PaidInFull = "PPD: pagada ({0})", Partial = "PPD: pagado {0}, saldo {1} ({2})",
                Uncertain = "PPD: pagado {0}; el último complemento indica saldo {1} ({2}), pero un complemento de pago anterior fue cancelado y la carpeta no tiene uno nuevo para esa parcialidad: verifica el saldo con el emisor",
                OneComplement = "1 complemento de pago", Complements_ = "{0} complementos de pago",
                Paid = "Pagado", Balance = "Saldo", Complements = "Complementos", State = "Estado",
                Orphans = "Complementos de pago de facturas que no están en la carpeta", OrphansNote = "Estos complementos pagan facturas cuyo XML no está en la carpeta.",
                Invoice = "UUID de la factura", Complement = "Complemento", PaymentDate = "Fecha de pago", Installment = "Parcialidad",
            };

            private static readonly PaymentLabels Portuguese = new()
            {
                Title = "Faturas PPD (México) e seus complementos de pagamento",
                Note = "Uma fatura PPD (pagamento parcelado ou diferido) é paga depois, em um ou mais pagamentos, e cada pagamento precisa de um complemento de pagamento (um CFDI do tipo P, o REP); enquanto ele não existir, o cliente não pode creditar o IVA. Os complementos da pasta são ligados às faturas pelo UUID que indicam; o saldo é o que dá o último complemento.",
                None = "PPD: sem complemento de pagamento na pasta", PaidInFull = "PPD: paga ({0})", Partial = "PPD: pago {0}, saldo {1} ({2})",
                Uncertain = "PPD: pago {0}; o último complemento indica saldo {1} ({2}), mas um complemento de pagamento anterior foi cancelado e a pasta não tem um novo para essa parcela: confira o saldo com o emitente",
                OneComplement = "1 complemento de pagamento", Complements_ = "{0} complementos de pagamento",
                Paid = "Pago", Balance = "Saldo", Complements = "Complementos", State = "Situação",
                Orphans = "Complementos de pagamento de faturas que não estão na pasta", OrphansNote = "Estes complementos pagam faturas cujo XML não está na pasta.",
                Invoice = "UUID da fatura", Complement = "Complemento", PaymentDate = "Data do pagamento", Installment = "Parcela",
            };
        }

        // The section of the Markdown: every PPD invoice with what it was paid, then the complements without their invoice. Empty when there is none.
        internal static string PaymentsMarkdown(InvoiceTableResult result, XmlLabels l)
        {
            var states = PpdStates(result);
            var orphans = PaymentsWithoutInvoice(result);
            if (states.Count == 0 && orphans.Count == 0) return "";
            var p = PaymentLabels.For(l);
            var sb = new StringBuilder();
            sb.Append("## ").Append(p.Title).Append("\n\n").Append(p.Note).Append("\n\n");
            if (states.Count > 0)
            {
                var table = new List<IReadOnlyList<string>> { new[] { l.Date, l.Number, l.Issuer, l.Customer, l.TaxInclusive, p.Paid, p.Balance, p.Complements, p.State } };
                foreach (var s in states)
                {
                    var stateText = PpdText(s.Row, l);
                    table.Add(new[]
                    {
                        MdCell(s.Row.Date), MdCell(s.Row.Number), MdCell(s.Row.Issuer), MdCell(s.Row.Customer), MdCell(s.Row.Total.Text),
                        s.Payments.Count == 0 ? "" : Money(s.Paid), s.Balance is { } balance ? Money(balance) : "",
                        MdCell(string.Join(", ", s.Payments.Select(x => x.File))), MdCell(stateText),
                    });
                }
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
            }
            if (orphans.Count > 0)
            {
                sb.Append("### ").Append(p.Orphans).Append(" (").Append(orphans.Count).Append(")\n\n").Append(p.OrphansNote).Append("\n\n");
                var table = new List<IReadOnlyList<string>> { new[] { p.Invoice, p.PaymentDate, p.Installment, p.Paid, p.Balance, p.Complement } };
                table.AddRange(orphans.Select(o => (IReadOnlyList<string>)new[]
                {
                    MdCell(o.Invoice), MdCell(o.Date), o.Installment > 0 ? o.Installment.ToString(CultureInfo.InvariantCulture) : "",
                    o.Paid is { } paid ? Money(paid) : "", o.Balance is { } balance ? Money(balance) : "", MdCell(o.File),
                }));
                sb.Append(MarkdownText.Table(table)).Append("\n\n");
            }
            return sb.ToString();
        }
    }
}
