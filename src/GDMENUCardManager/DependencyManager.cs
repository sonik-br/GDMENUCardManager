using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using SevenZip;

namespace GDMENUCardManager
{
    public class DependencyManager : IDependencyManager
    {
        private Window getMainWindow() => App.Current.MainWindow;

        public IProgressWindow CreateAndShowProgressWindow()
        {
            var p = new ProgressWindow() { Owner = getMainWindow() };
            p.Show();
            return p;
        }

        public ValueTask<GdItem[]> GdiShrinkWindowShowDialog(IEnumerable<GdItem> items, string title = null)
        {
            var w = new GdiShrinkWindow(items, title) { Owner = getMainWindow() };
            var picked = w.ShowDialog().GetValueOrDefault()
                ? w.List.Where(x => x.Value).Select(x => x.Key).ToArray()
                : null;
            return new ValueTask<GdItem[]>(picked);
        }

        public ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return new ValueTask<bool>(MessageBox.Show(getMainWindow(), text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        }

        public void ExtractArchive(string archivePath, string extractTo)
        {
            using (var extr = new SevenZipExtractor(archivePath))
            {
                extr.PreserveDirectoryStructure = false;
                extr.ExtractArchive(extractTo);
            }
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var compressedfile = new SevenZipExtractor(archivePath))
                foreach (var item in compressedfile.ArchiveFileData.Where(x => !x.IsDirectory))
                    if (!toReturn.ContainsKey(item.FileName))
                        toReturn.Add(item.FileName, (long)item.Size);
            return toReturn;
        }

        public IReadOnlyList<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
        {
            using var archive = new SevenZipExtractor(archivePath);
            return archive.ArchiveFileData
                .Where(item => !item.IsDirectory)
                .Select((item, ordinal) => new ArchiveEntryInfo(
                    ordinal,
                    item.FileName,
                    (long)item.Size))
                .ToArray();
        }

        // Decompression-work ceiling for one bounded read inside a solid
        // archive (bytes stored before the entry plus the prefix itself).
        private const long MaxSolidReadWorkBytes = 128L * 1024 * 1024;

        public byte[] ReadArchiveEntryBytes(
            string archivePath,
            ArchiveEntryInfo requestedEntry,
            long maxBytes)
        {
            if (string.IsNullOrEmpty(archivePath) || requestedEntry == null || maxBytes <= 0)
                return null;

            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(stream);
                if (archive.Type == SharpCompress.Common.ArchiveType.Rar && archive.IsSolid)
                    return null;

                var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
                var entry = entries.ElementAtOrDefault(requestedEntry.Ordinal);

                if (entry == null ||
                    entry.Key == null ||
                    entry.Size != requestedEntry.Size ||
                    !ArchiveEntryPath.HasSameIdentityKey(entry.Key, requestedEntry.FullName))
                    return null;

                long expectedBytes = Math.Min(requestedEntry.Size, maxBytes);

                // Reaching an entry inside a solid block first decompresses
                // everything stored before it. The byte cap alone does not
                // bound that work, so the read is skipped when it would
                // exceed the budget. SharpCompress only reports IsSolid for
                // RAR, so 7z is treated as solid outright (7-Zip archives
                // normally are).
                if (archive.Type == SharpCompress.Common.ArchiveType.SevenZip || archive.IsSolid)
                {
                    long precedingBytes = entries.Take(requestedEntry.Ordinal).Sum(e => e.Size);
                    if (precedingBytes + expectedBytes > MaxSolidReadWorkBytes)
                        return null;
                }

                using var entryStream = entry.OpenEntryStream();
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                while (output.Length < expectedBytes)
                {
                    int chunk = (int)Math.Min(
                        buffer.Length,
                        expectedBytes - output.Length);
                    int read = entryStream.Read(buffer, 0, chunk);
                    if (read <= 0)
                        break;

                    output.Write(buffer, 0, read);
                }

                return output.Length == expectedBytes
                    ? output.ToArray()
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
