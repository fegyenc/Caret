using System;
using System.Text;

namespace Typedown.WinUI.Services
{
    // New since the fork: a watermark (DRAFT, CONFIDENTIAL, any text), a header text, a footer text and page numbers on each page of an exported PDF.
    // The export prints the page of the editor with WebView2, so these are made of what a printed page can hold: an element fixed to the page, which
    // is repeated on every printed page, and the margin boxes of @page (@top-center, @bottom-left, @bottom-right, with counter(page) and counter(pages)).
    // They are added to the page for the print and taken away at once (MainWindow.xaml.cs, ExportPdfMenuItem_Click). Plain strings, offline, no AI.
    internal static class PdfExportMarks
    {
        public const string StyleId = "caret-pdf-marks";
        public const string WatermarkClass = "caret-pdf-watermark";

        // Room for the header and the footer: the margin boxes live in the page margins.
        public const double MarginInches = 0.7;

        public static bool Any(string watermark, string header, string footer, bool pageNumbers) =>
            !string.IsNullOrWhiteSpace(watermark) || !string.IsNullOrWhiteSpace(header) || !string.IsNullOrWhiteSpace(footer) || pageNumbers;

        // Whether there is something in the margins (the header, the footer, the page numbers): the page margins are made larger for it.
        public static bool InMargins(string header, string footer, bool pageNumbers) =>
            !string.IsNullOrWhiteSpace(header) || !string.IsNullOrWhiteSpace(footer) || pageNumbers;

        // A text as a CSS string between double quotes: a backslash, a quote and a line end must not end it or leave it.
        public static string CssString(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in (text ?? "").Trim())
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c is '\r' or '\n' or '\u2028' or '\u2029') sb.Append(' ');
                else if (char.IsControl(c)) continue;
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // The size of the watermark in CSS pixels: a short word is large, a long text smaller so that it still fits across the page.
        public static int WatermarkSize(string watermark) => Math.Clamp((int)(1350.0 / Math.Max(1, (watermark ?? "").Trim().Length)), 36, 130);

        public static string Css(string watermark, string header, string footer, bool pageNumbers)
        {
            var css = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(watermark))
                css.Append("@media print { .").Append(WatermarkClass).Append(" { position: fixed; top: 50%; left: 50%; transform: translate(-50%, -50%) rotate(-35deg); ")
                   .Append("font: 700 ").Append(WatermarkSize(watermark)).Append("px/1 'Segoe UI', Arial, sans-serif; letter-spacing: 0.05em; color: rgba(128, 128, 128, 0.2); ")
                   .Append("white-space: nowrap; z-index: 2147483647; pointer-events: none; -webkit-print-color-adjust: exact; print-color-adjust: exact; } }\n");
            if (InMargins(header, footer, pageNumbers))
            {
                const string font = "font: 9pt 'Segoe UI', Arial, sans-serif; color: #555;";
                css.Append("@page { margin: ").Append(MarginInches.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("in; ");
                if (!string.IsNullOrWhiteSpace(header)) css.Append("@top-center { content: ").Append(CssString(header)).Append("; ").Append(font).Append(" } ");
                if (!string.IsNullOrWhiteSpace(footer)) css.Append("@bottom-left { content: ").Append(CssString(footer)).Append("; ").Append(font).Append(" } ");
                if (pageNumbers) css.Append("@bottom-right { content: counter(page) \" / \" counter(pages); ").Append(font).Append(" } ");
                css.Append("}\n");
            }
            return css.ToString();
        }

        // The script that adds the style (and the watermark) to the page of the editor, and the one that takes them away.
        public static string AddScript(string watermark, string header, string footer, bool pageNumbers) =>
            "(function(){var s=document.createElement('style');s.id='" + StyleId + "';s.textContent=" + System.Text.Json.JsonSerializer.Serialize(Css(watermark, header, footer, pageNumbers)) + ";document.head.appendChild(s);" +
            (string.IsNullOrWhiteSpace(watermark) ? "" : "var d=document.createElement('div');d.className='" + WatermarkClass + "';d.textContent=" + System.Text.Json.JsonSerializer.Serialize(watermark.Trim()) + ";document.body.appendChild(d);") +
            "})()";

        public static string RemoveScript() =>
            "(function(){var s=document.getElementById('" + StyleId + "');if(s)s.remove();document.querySelectorAll('." + WatermarkClass + "').forEach(function(e){e.remove()});})()";
    }
}
