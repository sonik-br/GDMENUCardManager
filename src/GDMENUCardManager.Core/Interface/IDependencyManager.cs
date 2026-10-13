using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core.Interface
{
    public interface IDependencyManager
    {
        public ValueTask<GdItem[]> GdiShrinkWindowShowDialog(System.Collections.Generic.IEnumerable<GdItem> items, string title = null);
        public IProgressWindow CreateAndShowProgressWindow();
        public ValueTask<bool> ShowYesNoDialog(string caption, string text);

        public void ExtractArchive(string archivePath, string extractTo);
        public Dictionary<string, long> GetArchiveFiles(string archivePath);

        public IReadOnlyList<ArchiveEntryInfo> GetArchiveEntries(string archivePath);
        public byte[] ReadArchiveEntryBytes(string archivePath, ArchiveEntryInfo requestedEntry, long maxBytes);
    }

    public interface IProgressWindow
    {
        public void Close();
        public bool IsInitialized { get; }
        public bool IsVisible { get; }
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public string TextContent { get; set; }
    }
}