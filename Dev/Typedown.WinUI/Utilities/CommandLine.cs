using System.Linq;

namespace Typedown.WinUI.Utilities
{
    // Ported verbatim from Typedown.Core\Utilities\CommandLine.cs — no UWP dependencies.
    public static class CommandLine
    {
        public static string GetOpenFilePath(string[] commandLineArgs)
        {
            return commandLineArgs?.Where(FileTypeHelper.IsMarkdownFile).FirstOrDefault();
        }
    }
}
