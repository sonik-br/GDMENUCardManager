using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    internal readonly struct ArchiveGdiInspectionResult
    {
        public ArchiveGdiInspectionResult(bool readSucceeded, bool isLegacy, ReadOnlyMemory<byte> manifestBytes)
        {
            ReadSucceeded = readSucceeded;
            IsLegacy = isLegacy;
            ManifestBytes = manifestBytes;
        }

        public bool ReadSucceeded { get; }
        public bool IsLegacy { get; }
        public ReadOnlyMemory<byte> ManifestBytes { get; }
    }

    /// <summary>
    /// Thrown when a disc image is in a format the application refuses to load.
    /// </summary>
    public class UnsupportedDiscFormatException : Exception
    {
        public UnsupportedDiscFormatException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Detects disc images in Redump's old, short-lived GDI format.
    /// </summary>
    public static class LegacyRedumpGdiDetector
    {
        public const string ShortMessage = "Redump's old, short-lived GDI format is not supported.";

        private const string SupportedFormatsText =
            "GD MENU Card Manager supports TOSEC GDI and modern Redump CUE/BIN disc images (both of which can be compressed as CHD), " +
            "as well as CDI, MDS/MDF, and CCD/IMG. All of these formats can also be added inside 7Z/RAR/ZIP archives.";

        private const int GdiTextReadBytes = 16 * 1024;

        /// <summary>
        /// Builds the info box text shown when one or more legacy Redump GDIs are rejected.
        /// </summary>
        public static string BuildMessage(IReadOnlyCollection<string> fileNames)
        {
            if (fileNames == null || fileNames.Count <= 1)
            {
                return "This disc image is in Redump's old, short-lived GDI format, which is not supported."
                    + "\n\n" + SupportedFormatsText;
            }

            return "These disc images are in Redump's old, short-lived GDI format, which is not supported:"
                + "\n\n" + string.Join(Environment.NewLine, fileNames)
                + "\n\n" + SupportedFormatsText;
        }

        // Redump's old GDI format keeps the pregap inside each track file, so its
        // track 2 LBA equals track 1's sector count exactly. A normal GDI puts
        // track 2 at track 1's sector count plus the 150-sector pregap.
        public static bool IsLegacyRedumpGdi(string gdiPath)
        {
            try
            {
                var lines = File.ReadAllLines(gdiPath);
                var gdiFolder = Path.GetDirectoryName(gdiPath) ?? string.Empty;

                return CheckTrackLayout(lines, trackFileName =>
                {
                    var file = new FileInfo(Helper.ResolveActualPath(Path.Combine(gdiFolder, trackFileName)));
                    return file.Exists ? file.Length : (long?)null;
                });
            }
            catch
            {
                // Unreadable .gdi, let the normal loading paths report it.
                return false;
            }
        }

        internal static async Task<ArchiveGdiInspectionResult> InspectGdiInArchiveAsync(
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedGdi)
        {
            try
            {
                if (archiveEntries == null || selectedGdi == null ||
                    !Path.GetExtension(selectedGdi.FullName)
                        .Equals(".gdi", StringComparison.OrdinalIgnoreCase))
                    return default;

                var gdiBytes = await Task.Run(() =>
                    Helper.DependencyManager.ReadArchiveEntryBytes(
                        archivePath,
                        selectedGdi,
                        GdiTextReadBytes));

                if (gdiBytes == null || gdiBytes.Length == 0)
                    return default;

                var lines = Encoding.ASCII.GetString(gdiBytes)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var isLegacy = CheckTrackLayout(lines, trackFileName =>
                {
                    return ArchiveEntryPath.FindRelativeEntry(
                        archiveEntries,
                        selectedGdi,
                        trackFileName)?.Size;
                });

                return new ArchiveGdiInspectionResult(true, isLegacy, gdiBytes);
            }
            catch
            {
                return default;
            }
        }

        private static bool CheckTrackLayout(IEnumerable<string> lines, Func<string, long?> trackFileSizeLookup)
        {
            var track1 = default(GdiTrackLine);
            var track2 = default(GdiTrackLine);
            bool hasTrack1 = false, hasTrack2 = false;
            bool hasAudioTrackFile = false;

            foreach (var line in lines)
            {
                if (!TryParseTrackLine(line, out var track))
                    continue;

                if (Path.GetExtension(track.FileName).Equals(".raw", StringComparison.OrdinalIgnoreCase))
                    hasAudioTrackFile = true;

                if (track.TrackNumber == 1)
                {
                    track1 = track;
                    hasTrack1 = true;
                }
                else if (track.TrackNumber == 2)
                {
                    track2 = track;
                    hasTrack2 = true;
                }
            }

            // The old format names every track .bin. A set with .raw audio tracks
            // came from somewhere else, whatever its track layout looks like.
            if (hasAudioTrackFile)
                return false;

            if (!hasTrack1 || !hasTrack2 || track1.SectorSize <= 0)
                return false;

            var track1FileSize = trackFileSizeLookup(track1.FileName);
            if (track1FileSize == null)
                return false;

            long track1Sectors = track1FileSize.Value / track1.SectorSize;
            return track2.Lba == track1Sectors;
        }

        private struct GdiTrackLine
        {
            public int TrackNumber;
            public int Lba;
            public int SectorSize;
            public string FileName;
        }

        // One track line of a .gdi file:
        //   <track#> <lba> <type> <sector size> <filename> <offset>
        private static bool TryParseTrackLine(string line, out GdiTrackLine track)
        {
            track = default;

            var tokens = Tokenize(line);
            if (tokens.Count < 5)
                return false;
            if (!int.TryParse(tokens[0], out track.TrackNumber))
                return false;
            if (!int.TryParse(tokens[1], out track.Lba))
                return false;
            if (!int.TryParse(tokens[2], out _))
                return false;
            if (!int.TryParse(tokens[3], out track.SectorSize))
                return false;
            if (string.IsNullOrEmpty(tokens[4]))
                return false;

            track.FileName = tokens[4];
            return true;
        }

        // Splits on whitespace, keeping quoted filenames (with their spaces) as a
        // single token and stripping the quotes.
        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }
    }
}
