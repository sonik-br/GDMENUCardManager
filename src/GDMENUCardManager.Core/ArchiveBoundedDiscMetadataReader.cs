using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    internal readonly record struct CcdArchiveBundle(
        ArchiveEntryInfo Ccd,
        ArchiveEntryInfo Img,
        ArchiveEntryInfo Sub);

    internal readonly record struct MdsArchiveBundle(
        ArchiveEntryInfo Mds,
        ArchiveEntryInfo Mdf);

    internal static class ArchiveBoundedDiscMetadataReader
    {
        internal const long MaxImageBytes = 128L * 1024 * 1024;
        internal const long InitialImageBytes = 4L * 1024 * 1024;
        internal const long MaxDescriptorBytes = 1024 * 1024;

        private static readonly byte[] DreamcastSignature =
            Encoding.ASCII.GetBytes("SEGA SEGAKATANA SEGA ENTERPRISES");

        internal static async Task<IpBin> TryReadAsync(
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo selectedImage)
        {
            if (string.IsNullOrEmpty(archivePath) ||
                entries == null ||
                entries.Count == 0 ||
                selectedImage == null)
                return null;

            try
            {
                string extension = Path.GetExtension(selectedImage.FullName);
                if (extension.Equals(".cdi", StringComparison.OrdinalIgnoreCase))
                {
                    ArchiveEntryInfo selected = FindSelectedEntry(entries, selectedImage);
                    if (selected == null)
                        return null;

                    return await ReadImageAsync(archivePath, selected);
                }

                if (extension.Equals(".ccd", StringComparison.OrdinalIgnoreCase))
                {
                    CcdArchiveBundle? bundle = FindCcdBundle(entries, selectedImage);
                    if (!bundle.HasValue)
                        return null;

                    byte[] descriptor = await ReadEntryAsync(
                        archivePath,
                        bundle.Value.Ccd,
                        MaxDescriptorBytes);
                    if (!IsCcdDescriptor(descriptor))
                        return null;

                    return await ReadImageAsync(archivePath, bundle.Value.Img);
                }

                if (extension.Equals(".mds", StringComparison.OrdinalIgnoreCase))
                {
                    MdsArchiveBundle? bundle = FindMdsBundle(entries, selectedImage);
                    if (!bundle.HasValue)
                        return null;

                    byte[] descriptor = await ReadEntryAsync(
                        archivePath,
                        bundle.Value.Mds,
                        MaxDescriptorBytes);
                    if (!IsMdsDescriptor(descriptor))
                        return null;

                    return await ReadImageAsync(archivePath, bundle.Value.Mdf);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        internal static IpBin TryParseDreamcastPrefix(ReadOnlySpan<byte> bytes)
        {
            int offset = 0;
            while (offset <= bytes.Length - DreamcastSignature.Length)
            {
                int relative = bytes.Slice(offset).IndexOf(DreamcastSignature);
                if (relative < 0)
                    return null;

                int candidateOffset = offset + relative;
                int available = bytes.Length - candidateOffset;
                if (available >= 256)
                {
                    int length = Math.Min(512, available);
                    IpBin ip = ImageHelper.GetIpData(
                        bytes.Slice(candidateOffset, length).ToArray());
                    if (IsComplete(ip))
                        return ip;
                }

                offset = candidateOffset + 1;
            }

            return null;
        }

        internal static CcdArchiveBundle? FindCcdBundle(
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo selectedCcd)
        {
            ArchiveEntryInfo selected = FindSelectedEntry(entries, selectedCcd);
            if (selected == null ||
                !Path.GetExtension(selected.FullName)
                    .Equals(".ccd", StringComparison.OrdinalIgnoreCase))
                return null;

            string directory;
            string baseName;
            try
            {
                directory = ArchiveEntryPath.GetDirectoryKey(selected.FullName);
                baseName = Path.GetFileNameWithoutExtension(
                    ArchiveEntryPath.GetLeafName(selected.FullName));
            }
            catch (ArgumentException)
            {
                return null;
            }

            var ccdEntries = new List<ArchiveEntryInfo>();
            var imgEntries = new List<ArchiveEntryInfo>();
            var subEntries = new List<ArchiveEntryInfo>();

            foreach (ArchiveEntryInfo entry in entries)
            {
                if (entry == null)
                    continue;

                try
                {
                    if (!string.Equals(
                            ArchiveEntryPath.GetDirectoryKey(entry.FullName),
                            directory,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    string leaf = ArchiveEntryPath.GetLeafName(entry.FullName);
                    if (!string.Equals(
                            Path.GetFileNameWithoutExtension(leaf),
                            baseName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    switch (Path.GetExtension(leaf).ToLowerInvariant())
                    {
                        case ".ccd": ccdEntries.Add(entry); break;
                        case ".img": imgEntries.Add(entry); break;
                        case ".sub": subEntries.Add(entry); break;
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            if (ccdEntries.Count != 1 ||
                imgEntries.Count != 1 ||
                subEntries.Count != 1 ||
                !ReferenceEquals(ccdEntries[0], selected))
                return null;

            return new CcdArchiveBundle(
                ccdEntries[0],
                imgEntries[0],
                subEntries[0]);
        }

        internal static MdsArchiveBundle? FindMdsBundle(
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo selectedMds)
        {
            ArchiveEntryInfo selected = FindSelectedEntry(entries, selectedMds);
            if (selected == null ||
                !Path.GetExtension(selected.FullName)
                    .Equals(".mds", StringComparison.OrdinalIgnoreCase))
                return null;

            string directory;
            string baseName;
            try
            {
                directory = ArchiveEntryPath.GetDirectoryKey(selected.FullName);
                baseName = Path.GetFileNameWithoutExtension(
                    ArchiveEntryPath.GetLeafName(selected.FullName));
            }
            catch (ArgumentException)
            {
                return null;
            }

            var mdsEntries = new List<ArchiveEntryInfo>();
            var mdfEntries = new List<ArchiveEntryInfo>();
            foreach (ArchiveEntryInfo entry in entries)
            {
                if (entry == null)
                    continue;

                try
                {
                    if (!string.Equals(
                            ArchiveEntryPath.GetDirectoryKey(entry.FullName),
                            directory,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    string leaf = ArchiveEntryPath.GetLeafName(entry.FullName);
                    if (!string.Equals(
                            Path.GetFileNameWithoutExtension(leaf),
                            baseName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    switch (Path.GetExtension(leaf).ToLowerInvariant())
                    {
                        case ".mds": mdsEntries.Add(entry); break;
                        case ".mdf": mdfEntries.Add(entry); break;
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            if (mdsEntries.Count != 1 ||
                mdfEntries.Count != 1 ||
                !ReferenceEquals(mdsEntries[0], selected))
                return null;

            return new MdsArchiveBundle(mdsEntries[0], mdfEntries[0]);
        }

        private static ArchiveEntryInfo FindSelectedEntry(
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo selected)
        {
            if (selected.Ordinal < 0 || selected.Ordinal >= entries.Count)
                return null;

            ArchiveEntryInfo listed = entries[selected.Ordinal];
            if (listed == null ||
                listed.Ordinal != selected.Ordinal ||
                listed.Size != selected.Size ||
                !ArchiveEntryPath.HasSameIdentityKey(
                    listed.FullName,
                    selected.FullName))
                return null;

            try
            {
                ArchiveEntryPath.NormalizeKey(listed.FullName);
                ArchiveEntryPath.NormalizeKey(selected.FullName);
                return listed;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool IsCcdDescriptor(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return false;

            string text = Encoding.ASCII.GetString(bytes);
            return text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n')
                .StartsWith("[CloneCD]", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMdsDescriptor(byte[] bytes)
        {
            if (bytes == null)
                return false;

            ReadOnlySpan<byte> signature = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");
            return bytes.AsSpan().StartsWith(signature);
        }

        private static async Task<byte[]> ReadEntryAsync(
            string archivePath,
            ArchiveEntryInfo entry,
            long maxBytes)
        {
            if (entry.Size < 0 || maxBytes <= 0)
                return null;

            byte[] bytes = await Task.Run(() => Helper.DependencyManager.ReadArchiveEntryBytes(
                archivePath,
                entry,
                maxBytes));
            long expectedLength = Math.Min(entry.Size, maxBytes);
            return bytes != null && bytes.LongLength == expectedLength
                ? bytes
                : null;
        }

        private static async Task<IpBin> ReadImageAsync(
            string archivePath,
            ArchiveEntryInfo entry)
        {
            byte[] initial = await ReadEntryAsync(
                archivePath,
                entry,
                InitialImageBytes);
            if (initial == null)
                return null;

            IpBin ip = TryParseDreamcastPrefix(initial);
            if (ip != null || entry.Size <= InitialImageBytes)
                return ip;

            byte[] expanded = await ReadEntryAsync(
                archivePath,
                entry,
                MaxImageBytes - InitialImageBytes);
            return expanded == null ? null : TryParseDreamcastPrefix(expanded);
        }

        private static bool IsComplete(IpBin ip)
        {
            return ip != null &&
                !string.IsNullOrWhiteSpace(ip.Name) &&
                !string.IsNullOrWhiteSpace(ip.ProductNumber) &&
                !string.IsNullOrWhiteSpace(ip.Disc) &&
                !string.IsNullOrWhiteSpace(ip.Region);
        }
    }
}
