using System.Collections.Generic;
using System.IO;

namespace Typedown.WinUI.Utilities
{
    // Ported verbatim from Typedown.Core\Utilities\FileTypeHelper.cs — no UWP dependencies.
    public static class FileTypeHelper
    {
        public static HashSet<string> Markdown { get; } = new() { ".md", ".markdown", ".mdown", ".mkdn", ".mkd", ".mdwn", ".txt", ".mdtxt", ".text", ".mdtext", ".rmd" };

        // New since the fork: XML files are opened for editing as they are (Services/XmlEditing), not as notes.
        public static HashSet<string> Xml { get; } = new() { ".xml", ".xsd", ".xsl", ".xslt" };

        public static HashSet<string> Image = new() { ".jpeg", ".jpg", ".png", ".gif", ".svg", ".webp", ".jfif" };

        public enum FileType
        {
            Unknown,
            Markdown,
            Image,
            Xml
        }

        public static FileType GetFileType(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName) && fileName.Contains('.'))
            {
                var ex = Path.GetExtension(fileName).ToLower();
                if (Markdown.Contains(ex))
                    return FileType.Markdown;
                if (Image.Contains(ex))
                    return FileType.Image;
                if (Xml.Contains(ex))
                    return FileType.Xml;
            }
            return FileType.Unknown;
        }

        public static bool IsMarkdownFile(string fileName)
        {
            return GetFileType(fileName) == FileType.Markdown;
        }

        public static bool IsXmlFile(string fileName)
        {
            return GetFileType(fileName) == FileType.Xml;
        }

        // A file Caret opens as a document: a note, or an XML file.
        public static bool IsEditableFile(string fileName)
        {
            return GetFileType(fileName) is FileType.Markdown or FileType.Xml;
        }

        public static bool IsImageFile(string fileName)
        {
            return GetFileType(fileName) == FileType.Image;
        }
    }
}
