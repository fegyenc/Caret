using System;
using System.IO;

namespace Typedown.WinUI.Services
{
    // Caret's small local log for diagnosing problems: a text file in the Windows temp folder, never sent anywhere.
    //
    // Three rules, because the log runs inside every window event:
    // - it never throws: a full disk, a file another Caret process holds open or a temp folder that is gone must not
    //   break what was being logged (an exception there used to escape from the editor's message handler and close the app);
    // - it stays small: past MaxBytes the file moves aside to .old (one generation is kept), so it can't fill a disk;
    // - the traffic between the window and its editor (Traffic) is only written in a development build or when asked
    //   for (CARET_VERBOSE_LOG=1): those messages carry the note's own text and come on every scroll and key, so in
    //   the Store app they were the user's documents in a temp file and a disk write on the UI thread many times a second.
    public static class DiagLog
    {
        public const long MaxBytes = 1024 * 1024;

        private static readonly object gate = new();

        public static string FilePath { get; set; } = Path.Combine(Path.GetTempPath(), "caret_winui_probe.log");

        // Set at startup (App): on for the unpackaged development build, otherwise only with CARET_VERBOSE_LOG=1.
        public static bool Verbose { get; set; }

        public static bool VerboseRequested => Environment.GetEnvironmentVariable("CARET_VERBOSE_LOG") == "1";

        public static void Write(string message)
        {
            try
            {
                lock (gate)
                {
                    RollIfFull();
                    File.AppendAllText(FilePath, $"{DateTime.Now:O} {message}\n");
                }
            }
            catch
            {
                // a log that can't be written is not worth a failure of what it was logging
            }
        }

        // A message between the window and the editor: only in a verbose log (see above).
        public static void Traffic(string message)
        {
            if (Verbose) Write(message);
        }

        public static void Error(string where, Exception ex) => Write($"{where}: {ex}");

        private static void RollIfFull()
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length < MaxBytes) return;
            File.Move(FilePath, FilePath + ".old", overwrite: true);
        }
    }
}
