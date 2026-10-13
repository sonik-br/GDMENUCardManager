using Avalonia.Controls;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace GDMENUCardManager
{
    public class DependencyManager : IDependencyManager
    {
        private Window getMainWindow() => ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).MainWindow;

        public IProgressWindow CreateAndShowProgressWindow()
        {
            var p = new ProgressWindow();
            p.Show(getMainWindow());
            return p;
        }

        public async ValueTask<GdItem[]> GdiShrinkWindowShowDialog(System.Collections.Generic.IEnumerable<GdItem> items, string title = null)
        {
            var dialog = new GdiShrinkWindow(items, title);
            await dialog.ShowDialog(getMainWindow());
            return dialog.Result;
        }

        public async ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return await MessageBoxManager.GetMessageBoxStandard(caption, text, ButtonEnum.YesNo).ShowWindowDialogAsync(getMainWindow()) == ButtonResult.Yes;
        }


        public void ExtractArchive(string archivePath, string extractTo)
        {
            var extOptions = new ExtractionOptions()
            {
                ExtractFullPath = false,
                Overwrite = true
            };

            using (var stream = File.OpenRead(archivePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            archive.WriteToDirectory(extractTo, extOptions);
            //using (var reader = archive.ExtractAllEntries())
            //    reader.WriteAllToDirectory(extractTo, extOptions);
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var stream = File.OpenRead(archivePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
                foreach (var item in archive.Entries)
                    if (!item.IsDirectory && !toReturn.ContainsKey(item.Key))
                        toReturn.Add(item.Key, item.Size);
            return toReturn;
        }

        public IReadOnlyList<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = ArchiveFactory.OpenArchive(stream);
            return archive.Entries
                .Where(entry => !entry.IsDirectory)
                .Select((entry, ordinal) => new ArchiveEntryInfo(
                    ordinal,
                    entry.Key,
                    entry.Size))
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
                using var archive = ArchiveFactory.OpenArchive(stream);
                if (archive.Type == ArchiveType.Rar && archive.IsSolid)
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
                if (archive.Type == ArchiveType.SevenZip || archive.IsSolid)
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
