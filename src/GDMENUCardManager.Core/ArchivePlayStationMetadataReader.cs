using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    internal static class ArchivePlayStationMetadataReader
    {
        internal const long MaxCueBytes = 1024 * 1024;
        internal const long MaxDataBytes = 128L * 1024 * 1024;

        internal static async Task<IpBin> TryReadAsync(
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo selectedCue)
        {
            if (string.IsNullOrEmpty(archivePath) ||
                entries == null ||
                entries.Count == 0 ||
                selectedCue == null)
                return null;

            try
            {
                ArchiveEntryInfo selected = FindSelectedEntry(entries, selectedCue);
                if (selected == null ||
                    !Path.GetExtension(selected.FullName)
                        .Equals(".cue", StringComparison.OrdinalIgnoreCase))
                    return null;

                byte[] cueBytes = await ReadEntryAsync(
                    archivePath,
                    selected,
                    MaxCueBytes);
                if (cueBytes == null ||
                    !TryFindPrimaryDataTrack(
                        Encoding.UTF8.GetString(cueBytes),
                        out string referencedName,
                        out string trackMode))
                    return null;

                ArchiveEntryInfo dataTrack = FindUniqueRelativeEntry(
                    entries,
                    selected,
                    referencedName);
                if (dataTrack == null)
                    return null;

                byte[] dataBytes = await ReadEntryAsync(
                    archivePath,
                    dataTrack,
                    MaxDataBytes);
                return dataBytes == null
                    ? null
                    : TryParseDataTrackPrefix(dataBytes, trackMode);
            }
            catch
            {
                return null;
            }
        }

        internal static IpBin TryParseDataTrackPrefix(
            ReadOnlySpan<byte> bytes,
            string cueTrackMode)
        {
            if (!TryGetSectorLayout(
                    cueTrackMode,
                    out int sectorSize,
                    out int dataOffset))
                return null;

            if (!TryGetLogicalSector(bytes, 16, sectorSize, dataOffset, out var pvd) ||
                pvd[0] != 1 ||
                !pvd.Slice(1, 5).SequenceEqual(Encoding.ASCII.GetBytes("CD001")) ||
                pvd[6] != 1 ||
                !Encoding.ASCII.GetString(pvd.Slice(8, 32)).TrimEnd('\0', ' ')
                    .Equals("PLAYSTATION", StringComparison.OrdinalIgnoreCase))
                return null;

            int rootRecordLength = pvd[156];
            if (rootRecordLength < 34 || 156 + rootRecordLength > pvd.Length)
                return null;

            int rootSector = BinaryPrimitives.ReadInt32LittleEndian(pvd.Slice(158, 4));
            int rootLength = BinaryPrimitives.ReadInt32LittleEndian(pvd.Slice(166, 4));
            if (rootSector < 0 || rootLength <= 0 || rootLength > 4 * 1024 * 1024)
                return null;

            byte[] root = ReadLogicalBytes(
                bytes,
                rootSector,
                rootLength,
                sectorSize,
                dataOffset);
            if (root == null)
                return null;

            int systemCnfSector = -1;
            int systemCnfLength = 0;
            int position = 0;
            int matches = 0;
            while (position < rootLength)
            {
                int recordLength = root[position];
                if (recordLength == 0)
                {
                    position = ((position / 2048) + 1) * 2048;
                    continue;
                }

                if (recordLength < 34 || position + recordLength > root.Length)
                    return null;

                int nameLength = root[position + 32];
                if (nameLength > 0 && position + 33 + nameLength <= root.Length)
                {
                    string name = Encoding.ASCII.GetString(
                        root,
                        position + 33,
                        nameLength);
                    if (name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SYSTEM.CNF;1", StringComparison.OrdinalIgnoreCase))
                    {
                        matches++;
                        systemCnfSector = BinaryPrimitives.ReadInt32LittleEndian(
                            root.AsSpan(position + 2, 4));
                        systemCnfLength = BinaryPrimitives.ReadInt32LittleEndian(
                            root.AsSpan(position + 10, 4));
                    }
                }

                position += recordLength;
            }

            if (matches != 1 ||
                systemCnfSector < 0 ||
                systemCnfLength <= 0 ||
                systemCnfLength > 4096)
                return null;

            byte[] systemCnf = ReadLogicalBytes(
                bytes,
                systemCnfSector,
                systemCnfLength,
                sectorSize,
                dataOffset);
            if (systemCnf == null)
                return null;

            string firstLine;
            using (var stream = new MemoryStream(systemCnf))
            using (var reader = new StreamReader(stream, Encoding.ASCII))
                firstLine = reader.ReadLine();

            string serial = ParseBootSerial(firstLine);
            if (serial == null)
                return null;

            PSDBEntry databaseEntry = PlayStationDB.FindBySerial(serial);
            string releaseDate = DateOnly.TryParse(
                databaseEntry?.releaseDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsedDate)
                    ? parsedDate.ToString("yyyyMMdd")
                    : "19990909";

            return new IpBin
            {
                Name = databaseEntry?.name ?? serial,
                ProductNumber = serial,
                Disc = "1/1",
                Region = "JUE",
                Vga = true,
                ReleaseDate = releaseDate,
                SpecialDisc = SpecialDisc.BleemGame
            };
        }

        private static bool TryFindPrimaryDataTrack(
            string cueText,
            out string referencedName,
            out string trackMode)
        {
            referencedName = null;
            trackMode = null;
            if (string.IsNullOrWhiteSpace(cueText))
                return false;

            string currentFile = null;
            foreach (string rawLine in cueText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = SplitCueLine(rawLine.Trim());
                if (parts.Length == 0)
                    continue;

                if (parts[0].Equals("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    currentFile = parts.Length >= 2 ? parts[1] : null;
                    continue;
                }

                if (!parts[0].Equals("TRACK", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (parts.Length < 3 ||
                    !int.TryParse(parts[1], out _) ||
                    string.IsNullOrWhiteSpace(currentFile))
                    return false;

                if (parts[2].Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryGetSectorLayout(parts[2], out _, out _))
                    return false;

                referencedName = currentFile;
                trackMode = parts[2];
                return true;
            }

            return false;
        }

        private static string[] SplitCueLine(string line)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            foreach (char value in line)
            {
                if (value == '"')
                {
                    quoted = !quoted;
                }
                else if (char.IsWhiteSpace(value) && !quoted)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(value);
                }
            }

            if (quoted)
                return Array.Empty<string>();
            if (current.Length > 0)
                parts.Add(current.ToString());
            return parts.ToArray();
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
                !ArchiveEntryPath.HasSameIdentityKey(listed.FullName, selected.FullName))
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

        private static ArchiveEntryInfo FindUniqueRelativeEntry(
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo owner,
            string referencedName)
        {
            if (string.IsNullOrWhiteSpace(referencedName))
                return null;

            string reference = referencedName.Replace('\\', '/');
            if (reference.StartsWith("/", StringComparison.Ordinal) ||
                (reference.Length >= 2 && reference[1] == ':'))
                return null;

            string expected;
            try
            {
                string directory = ArchiveEntryPath.GetDirectoryKey(owner.FullName);
                expected = ArchiveEntryPath.NormalizeKey(
                    string.IsNullOrEmpty(directory)
                        ? reference
                        : directory + "/" + reference);
            }
            catch (ArgumentException)
            {
                return null;
            }

            ArchiveEntryInfo match = null;
            foreach (ArchiveEntryInfo entry in entries)
            {
                if (entry == null)
                    continue;

                string normalized;
                try
                {
                    normalized = ArchiveEntryPath.NormalizeKey(entry.FullName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match != null)
                    return null;
                match = entry;
            }

            return match;
        }

        private static async Task<byte[]> ReadEntryAsync(
            string archivePath,
            ArchiveEntryInfo entry,
            long maxBytes)
        {
            byte[] bytes = await Task.Run(() => Helper.DependencyManager.ReadArchiveEntryBytes(
                archivePath,
                entry,
                maxBytes));
            long expectedLength = Math.Min(entry.Size, maxBytes);
            return bytes != null && bytes.LongLength == expectedLength
                ? bytes
                : null;
        }

        private static bool TryGetSectorLayout(
            string trackMode,
            out int sectorSize,
            out int dataOffset)
        {
            sectorSize = 0;
            dataOffset = 0;
            if (trackMode == null)
                return false;

            switch (trackMode.ToUpperInvariant())
            {
                case "MODE1/2048":
                    sectorSize = 2048;
                    return true;
                case "MODE1/2352":
                    sectorSize = 2352;
                    dataOffset = 16;
                    return true;
                case "MODE2/2352":
                    sectorSize = 2352;
                    dataOffset = 24;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetLogicalSector(
            ReadOnlySpan<byte> bytes,
            int sector,
            int sectorSize,
            int dataOffset,
            out ReadOnlySpan<byte> data)
        {
            long offset = (long)sector * sectorSize + dataOffset;
            if (sector < 0 || offset < 0 || offset + 2048 > bytes.Length)
            {
                data = default;
                return false;
            }

            data = bytes.Slice((int)offset, 2048);
            return true;
        }

        private static byte[] ReadLogicalBytes(
            ReadOnlySpan<byte> bytes,
            int startSector,
            int length,
            int sectorSize,
            int dataOffset)
        {
            var result = new byte[length];
            int copied = 0;
            int sector = startSector;
            while (copied < length)
            {
                if (!TryGetLogicalSector(
                        bytes,
                        sector,
                        sectorSize,
                        dataOffset,
                        out var logicalSector))
                    return null;

                int count = Math.Min(logicalSector.Length, length - copied);
                logicalSector.Slice(0, count).CopyTo(result.AsSpan(copied));
                copied += count;
                sector++;
            }

            return result;
        }

        private static string ParseBootSerial(string firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine))
                return null;

            int separator = firstLine.LastIndexOf('\\');
            if (separator < 0 || separator == firstLine.Length - 1)
                return null;

            string serial = firstLine.Substring(separator + 1).Trim();
            int terminator = serial.IndexOf(';');
            if (terminator >= 0)
                serial = serial.Substring(0, terminator);
            int whitespace = serial.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            if (whitespace >= 0)
                serial = serial.Substring(0, whitespace);
            serial = serial.Replace('_', '-').Replace(".", string.Empty).ToUpperInvariant();

            return Regex.IsMatch(serial, "^[A-Z]{4}-[0-9]{5}$", RegexOptions.CultureInvariant)
                ? serial
                : null;
        }
    }
}
