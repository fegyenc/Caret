using System;
using System.IO;
using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    // DiagLog is static: the tests that change its file run one after another, never in parallel with each other.
    [Collection(nameof(DiagLogTests))]
    public class DiagLogTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "caret-diaglog-" + Guid.NewGuid().ToString("N"));
        private readonly string previousPath = DiagLog.FilePath;
        private readonly bool previousVerbose = DiagLog.Verbose;

        public DiagLogTests()
        {
            Directory.CreateDirectory(folder);
            DiagLog.FilePath = Path.Combine(folder, "probe.log");
        }

        public void Dispose()
        {
            DiagLog.FilePath = previousPath;
            DiagLog.Verbose = previousVerbose;
            try { Directory.Delete(folder, true); } catch { }
        }

        [Fact]
        public void Traffic_is_left_out_unless_verbose()
        {
            DiagLog.Verbose = false;
            DiagLog.Traffic("WebMessage: the note's text");
            DiagLog.Write("Save: done");
            var log = File.ReadAllText(DiagLog.FilePath);
            Assert.DoesNotContain("the note's text", log);
            Assert.Contains("Save: done", log);

            DiagLog.Verbose = true;
            DiagLog.Traffic("WebMessage: scrolled");
            Assert.Contains("WebMessage: scrolled", File.ReadAllText(DiagLog.FilePath));
        }

        [Fact]
        public void A_log_that_cannot_be_written_does_not_throw()
        {
            DiagLog.FilePath = Path.Combine(folder, "no such folder", "probe.log");
            DiagLog.Write("anything");
            DiagLog.Error("somewhere", new InvalidOperationException("boom"));

            DiagLog.FilePath = Path.Combine(folder, "held.log");
            using (new FileStream(DiagLog.FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                DiagLog.Write("while another process holds the file");
        }

        [Fact]
        public void A_full_log_moves_aside_and_starts_again()
        {
            File.WriteAllText(DiagLog.FilePath, new string('x', (int)DiagLog.MaxBytes));
            DiagLog.Write("first line of the new log");

            Assert.True(File.Exists(DiagLog.FilePath + ".old"));
            Assert.True(new FileInfo(DiagLog.FilePath).Length < 200);
            Assert.Contains("first line of the new log", File.ReadAllText(DiagLog.FilePath));
        }
    }
}
