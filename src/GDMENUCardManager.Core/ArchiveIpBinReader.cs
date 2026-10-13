using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    internal static class ArchiveIpBinReader
    {
        private static readonly byte[] DreamcastSignature =
            Encoding.ASCII.GetBytes("SEGA SEGAKATANA SEGA ENTERPRISES");

        private static readonly Regex TrackOneNamePattern = new Regex(
            @"^(?:track0?1|.*\(track 0?1\))\.(?:bin|iso)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const int DataTrackReadBytes = 64 * 1024;
        private const int GdiTextReadBytes = 16 * 1024;
        private const int HighDensityAreaLba = 45000;

        /// <summary>
        /// Reads IP.BIN from inside an archive without extracting it. Null when the archive holds
        /// no readable data track.
        /// </summary>
        public static async Task<IpBin> TryReadAsync(
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedImage,
            ReadOnlyMemory<byte>? gdiManifestBytes = null)
        {
            if (string.IsNullOrEmpty(archivePath) ||
                archiveEntries == null ||
                archiveEntries.Count == 0 ||
                selectedImage == null)
                return null;

            try
            {
                var dataTrackEntries = await ResolveDataTrackEntriesAsync(
                    archivePath,
                    archiveEntries,
                    selectedImage,
                    gdiManifestBytes);
                foreach (var dataTrackEntry in dataTrackEntries)
                {
                    var ip = await TryReadDataTrackIpBinAsync(archivePath, dataTrackEntry);
                    if (ip != null)
                        return ip;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<IReadOnlyList<ArchiveEntryInfo>> ResolveDataTrackEntriesAsync(
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedImage,
            ReadOnlyMemory<byte>? gdiManifestBytes)
        {
            var selectedExtension = Path.GetExtension(selectedImage.FullName);

            if (selectedExtension.Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlyMemory<byte> gdiBytes;
                if (gdiManifestBytes.HasValue && !gdiManifestBytes.Value.IsEmpty)
                {
                    gdiBytes = gdiManifestBytes.Value;
                }
                else
                {
                    var readBytes = await Task.Run(() =>
                        Helper.DependencyManager.ReadArchiveEntryBytes(
                            archivePath,
                            selectedImage,
                            GdiTextReadBytes));

                    if (readBytes == null || readBytes.Length == 0)
                        return Array.Empty<ArchiveEntryInfo>();

                    gdiBytes = readBytes;
                }

                var referencedTrack = ParseGdiForHdDataTrack(
                    Encoding.ASCII.GetString(gdiBytes.Span));
                var dataTrackEntry = ArchiveEntryPath.FindRelativeEntry(
                    archiveEntries,
                    selectedImage,
                    referencedTrack);
                return dataTrackEntry == null
                    ? Array.Empty<ArchiveEntryInfo>()
                    : new[] { dataTrackEntry };
            }

            if (selectedExtension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                var validatedSelectedImage = FindSelectedEntry(archiveEntries, selectedImage);
                if (validatedSelectedImage == null)
                    return Array.Empty<ArchiveEntryInfo>();

                string selectedDirectory = ArchiveEntryPath.GetDirectoryKey(validatedSelectedImage.FullName);
                var trackOne = FindUniqueTrackOneEntryForValidatedSelection(
                    archiveEntries,
                    validatedSelectedImage);
                var fallback = archiveEntries
                    .Where(entry => IsDataEntryInSelectedDirectory(entry, selectedDirectory))
                    .OrderByDescending(entry => entry.Size)
                    .FirstOrDefault();
                var candidates = new List<ArchiveEntryInfo>();

                if (trackOne != null)
                    candidates.Add(trackOne);

                if (fallback != null && !HasSameDescriptorIdentity(trackOne, fallback))
                    candidates.Add(fallback);

                return candidates;
            }

            return Array.Empty<ArchiveEntryInfo>();
        }

        private static async Task<IpBin> TryReadDataTrackIpBinAsync(
            string archivePath,
            ArchiveEntryInfo dataTrackEntry)
        {
            try
            {
                var trackBytes = await Task.Run(() =>
                    Helper.DependencyManager.ReadArchiveEntryBytes(
                        archivePath,
                        dataTrackEntry,
                        DataTrackReadBytes));

                if (trackBytes == null || trackBytes.Length < 256)
                    return null;

                int sigOffset = FindSignature(trackBytes, DreamcastSignature);
                if (sigOffset < 0)
                    return null;

                int available = trackBytes.Length - sigOffset;
                if (available < 256)
                    return null;

                var ipBytes = new byte[Math.Min(512, available)];
                Array.Copy(trackBytes, sigOffset, ipBytes, 0, ipBytes.Length);

                return ImageHelper.GetIpData(ipBytes);
            }
            catch
            {
                return null;
            }
        }

        internal static ArchiveEntryInfo FindUniqueTrackOneEntry(
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedImage)
        {
            var selectedEntry = FindSelectedEntry(archiveEntries, selectedImage);
            return selectedEntry == null
                ? null
                : FindUniqueTrackOneEntryForValidatedSelection(archiveEntries, selectedEntry);
        }

        internal static async Task<IpBin> TryReadLateGdiTrackOneAsync(
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedImage)
        {
            var selectedEntry = FindSelectedEntry(archiveEntries, selectedImage);
            if (selectedEntry == null ||
                !Path.GetExtension(selectedEntry.FullName)
                    .Equals(".gdi", StringComparison.OrdinalIgnoreCase))
                return null;

            var trackOne = FindUniqueTrackOneEntryForValidatedSelection(
                archiveEntries,
                selectedEntry);
            if (trackOne == null || trackOne.Ordinal >= selectedEntry.Ordinal)
                return null;

            return await TryReadDataTrackIpBinAsync(archivePath, trackOne);
        }

        private static ArchiveEntryInfo FindSelectedEntry(
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedImage)
        {
            if (archiveEntries == null ||
                archiveEntries.Count == 0 ||
                selectedImage == null ||
                selectedImage.Ordinal >= archiveEntries.Count)
                return null;

            var listedEntry = archiveEntries[selectedImage.Ordinal];
            if (listedEntry == null ||
                listedEntry.Ordinal != selectedImage.Ordinal ||
                listedEntry.Size != selectedImage.Size ||
                !ArchiveEntryPath.HasSameIdentityKey(listedEntry.FullName, selectedImage.FullName))
                return null;

            try
            {
                ArchiveEntryPath.NormalizeKey(listedEntry.FullName);
                ArchiveEntryPath.NormalizeKey(selectedImage.FullName);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return listedEntry;
        }

        private static ArchiveEntryInfo FindUniqueTrackOneEntryForValidatedSelection(
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedEntry)
        {
            string selectedDirectory = ArchiveEntryPath.GetDirectoryKey(selectedEntry.FullName);
            ArchiveEntryInfo trackOneEntry = null;

            foreach (var entry in archiveEntries)
            {
                if (entry == null)
                    continue;

                string directory;
                string leafName;
                try
                {
                    directory = ArchiveEntryPath.GetDirectoryKey(entry.FullName);
                    leafName = ArchiveEntryPath.GetLeafName(entry.FullName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!string.Equals(directory, selectedDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ReferenceEquals(entry, selectedEntry) &&
                    Manager.supportedImageFormats.Any(extension => extension.Equals(
                        Path.GetExtension(leafName),
                        StringComparison.OrdinalIgnoreCase)))
                    return null;

                if (!TrackOneNamePattern.IsMatch(leafName))
                    continue;

                if (trackOneEntry != null)
                    return null;

                trackOneEntry = entry;
            }

            return trackOneEntry;
        }

        private static bool IsDataEntryInSelectedDirectory(
            ArchiveEntryInfo entry,
            string selectedDirectory)
        {
            if (entry == null)
                return false;

            try
            {
                string directory = ArchiveEntryPath.GetDirectoryKey(entry.FullName);
                string leafName = ArchiveEntryPath.GetLeafName(entry.FullName);
                string extension = Path.GetExtension(leafName);
                return string.Equals(directory, selectedDirectory, StringComparison.OrdinalIgnoreCase) &&
                    (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".iso", StringComparison.OrdinalIgnoreCase));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool HasSameDescriptorIdentity(
            ArchiveEntryInfo first,
            ArchiveEntryInfo second)
        {
            return first != null &&
                second != null &&
                first.Ordinal == second.Ordinal &&
                first.Size == second.Size &&
                ArchiveEntryPath.HasSameIdentityKey(first.FullName, second.FullName);
        }

        internal static string ParseGdiForHdDataTrack(string gdiText)
        {
            if (string.IsNullOrEmpty(gdiText))
                return null;

            var lines = gdiText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = SplitGdiLine(line);
                if (parts.Length < 5)
                    continue;

                if (!int.TryParse(parts[0], out _))
                    continue;
                if (!int.TryParse(parts[1], out int lba))
                    continue;
                if (!int.TryParse(parts[2], out int type))
                    continue;

                if (type == 4 && lba >= HighDensityAreaLba)
                    return parts[4];
            }

            return null;
        }

        private static string[] SplitGdiLine(string line)
        {
            var result = new List<string>();
            var cur = new StringBuilder();
            bool inQuotes = false;

            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && (c == ' ' || c == '\t'))
                {
                    if (cur.Length > 0)
                    {
                        result.Add(cur.ToString());
                        cur.Clear();
                    }
                }
                else
                {
                    cur.Append(c);
                }
            }

            if (cur.Length > 0)
                result.Add(cur.ToString());

            return result.ToArray();
        }

        private static int FindSignature(byte[] haystack, byte[] needle)
        {
            if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length)
                return -1;

            int limit = haystack.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }
    }
}
