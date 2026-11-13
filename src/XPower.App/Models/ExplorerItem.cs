using Windows.Storage;
using XPower.Storage;

namespace XPower.Models
{
    public enum ExplorerItemKind { StorageRoot, Folder, File }

    /// <summary>UI-facing entry with the WinRT handle required for supported storage operations.</summary>
    public sealed class ExplorerItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Glyph { get; set; }
        public string Trailing { get; set; }
        public StorageFolder Folder { get; set; }
        public StorageFile File { get; set; }
        public ExplorerItemKind Kind { get; set; }
        public StorageRootKind? RootKind { get; set; }
    }
}
