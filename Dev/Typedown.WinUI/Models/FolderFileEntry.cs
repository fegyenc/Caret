namespace Typedown.WinUI.Models
{
    // Not present in the original as a standalone type — Typedown.Core's FolderPage built a real
    // ExplorerItem tree (lazy-loaded children, drag-drop, rename/delete context menu) bound through a
    // full AppViewModel. This is the flat equivalent for our flat ListView (see MainWindow.xaml's
    // FolderListView): a pre-scanned list of markdown files under the chosen folder, shown by relative
    // path instead of a real expand/collapse tree.
    public class FolderFileEntry
    {
        public string RelativePath { get; set; }
        public string FullPath { get; set; }
    }
}
