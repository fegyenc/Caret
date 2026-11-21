using System.Runtime.CompilerServices;
using System.Text;

namespace Caret.ConverterTests
{
    internal static class TestSetup
    {
        // The Windows code pages (Shift_JIS, Windows-1250...) for every test, before any of them runs. The app registers them when a reader starts
        // (EmlReader, TextFileEncoding); a test that builds a mail with MimeKit first would otherwise make MimeKit take those charsets for unknown,
        // and a later test of a Japanese mail would fail or pass depending on the order the tests run in.
        [ModuleInitializer]
        internal static void RegisterCodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
