using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GDMENUCardManager.Core
{
    public class CueTrack
    {
        public int TrackNumber { get; set; }
        public string DataType { get; set; } = string.Empty; // AUDIO, MODE1/2352, MODE2/2352, etc.
        public string BinFilename { get; set; } = string.Empty;
        public List<string> Comments { get; set; } = new List<string>();
        public int Index0Frames { get; set; } = -1; // Pregap start in frames (-1 if not present)
        public int Index1Frames { get; set; } = 0;  // Track start in frames

        public bool IsAudio => DataType.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
        public bool IsData => !IsAudio;
        public bool IsHighDensityArea => Comments.Any(c => c.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase));
    }

    public class CueSheetParser
    {
        public const int SectorSize = 2352;
        public const int IpBinSize = 256; // We only need the header portion

        public List<CueTrack> Tracks { get; private set; } = new List<CueTrack>();
        public string CueFilePath { get; private set; } = string.Empty;
        public string CueDirectory { get; private set; } = string.Empty;
        public bool IsGdRom { get; private set; }
        public bool IsCdRom => !IsGdRom;

        /// <summary>
        /// Populates Tracks and IsGdRom. Throws if the file is missing.
        /// </summary>
        public void Parse(string cuePath)
        {
            if (!File.Exists(cuePath))
                throw new FileNotFoundException("CUE file not found", cuePath);

            CueFilePath = cuePath;
            CueDirectory = Path.GetDirectoryName(cuePath) ?? string.Empty;
            Tracks.Clear();

            var lines = File.ReadAllLines(cuePath);
            string currentBinFile = string.Empty;
            CueTrack currentTrack = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = SplitCueLine(line);
                if (parts.Length == 0)
                    continue;

                var command = parts[0].ToUpperInvariant();

                switch (command)
                {
                    case "FILE":
                        if (parts.Length >= 2)
                        {
                            currentBinFile = Helper.ResolveActualFileName(CueDirectory, parts[1]);
                        }
                        break;

                    case "TRACK":
                        if (parts.Length >= 3 && int.TryParse(parts[1], out int trackNum))
                        {
                            currentTrack = new CueTrack
                            {
                                TrackNumber = trackNum,
                                DataType = parts[2],
                                BinFilename = currentBinFile
                            };
                            Tracks.Add(currentTrack);
                        }
                        break;

                    case "INDEX":
                        if (currentTrack != null && parts.Length >= 3 && int.TryParse(parts[1], out int indexNum))
                        {
                            int frames = ParseMsfToFrames(parts[2]);

                            if (indexNum == 0)
                                currentTrack.Index0Frames = frames;
                            else if (indexNum == 1)
                                currentTrack.Index1Frames = frames;
                        }
                        break;

                    case "REM":
                        if (currentTrack != null && parts.Length >= 2)
                        {
                            currentTrack.Comments.Add(string.Join(" ", parts.Skip(1)));
                        }
                        break;
                }
            }

            IsGdRom = Tracks.Any(t => t.IsHighDensityArea);
        }

        // Splits on spaces, but a quoted filename stays one token.
        private string[] SplitCueLine(string line)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                parts.Add(current.ToString());

            return parts.ToArray();
        }

        private int ParseMsfToFrames(string msf)
        {
            var parts = msf.Split(':');
            if (parts.Length != 3)
                return 0;

            if (!int.TryParse(parts[0], out int minutes) ||
                !int.TryParse(parts[1], out int seconds) ||
                !int.TryParse(parts[2], out int frames))
                return 0;

            return (minutes * 60 * 75) + (seconds * 75) + frames;
        }

        /// <summary>
        /// The HD-area data track on a GD-ROM, otherwise the first data track.
        /// </summary>
        public CueTrack GetPrimaryDataTrack()
        {
            if (IsGdRom)
            {
                return Tracks.FirstOrDefault(t => t.IsHighDensityArea && t.IsData)
                    ?? Tracks.FirstOrDefault(t => t.IsData);
            }
            else
            {
                // For CD-ROM, use the first data track
                return Tracks.FirstOrDefault(t => t.IsData);
            }
        }

        // Dreamcast IP.BIN signature to search for (same as ImageHelper.katanachar)
        private static readonly byte[] DreamcastSignature = Encoding.ASCII.GetBytes("SEGA SEGAKATANA SEGA ENTERPRISES");

        /// <summary>
        /// Scans the first 100 sectors for the SEGA SEGAKATANA signature rather than trusting a
        /// fixed offset, since sector formats vary.
        /// </summary>
        public byte[] ReadIpBin()
        {
            var dataTrack = GetPrimaryDataTrack();
            if (dataTrack == null)
                throw new Exception("No data track found in CUE sheet");

            var binPath = Path.Combine(CueDirectory, dataTrack.BinFilename);
            if (!File.Exists(binPath))
                throw new FileNotFoundException("BIN file not found", binPath);

            using var fs = new FileStream(binPath, FileMode.Open, FileAccess.Read);

            // Offset varies by sector format, so search rather than seek.
            long signatureOffset = FindSignature(fs, DreamcastSignature, Math.Min(fs.Length, SectorSize * 100));

            if (signatureOffset < 0)
                throw new Exception("Dreamcast signature not found - this may not be a Dreamcast disc");

            fs.Seek(signatureOffset, SeekOrigin.Begin);

            // Read 512 bytes for IP.BIN parsing
            byte[] buffer = new byte[512];
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            if (bytesRead < IpBinSize)
                throw new Exception("Could not read enough data for IP.BIN");

            return buffer;
        }

        private static long FindSignature(Stream stream, byte[] signature, long maxSearchLength)
        {
            stream.Seek(0, SeekOrigin.Begin);
            int matchIndex = 0;
            long position = 0;

            while (position < maxSearchLength)
            {
                int b = stream.ReadByte();
                if (b == -1)
                    break;

                if (b == signature[matchIndex])
                {
                    matchIndex++;
                    if (matchIndex == signature.Length)
                    {
                        return position - signature.Length + 1;
                    }
                }
                else
                {
                    // Reset match but check if current byte starts a new match
                    matchIndex = (b == signature[0]) ? 1 : 0;
                }

                position++;
            }

            return -1; // Not found
        }

        public IpBin TryParseIpBin()
        {
            try
            {
                var ipData = ReadIpBin();
                return ImageHelper.GetIpData(ipData);
            }
            catch
            {
                return null;
            }
        }

        public long GetTotalBinSize()
        {
            long total = 0;
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in Tracks)
            {
                if (!string.IsNullOrEmpty(track.BinFilename) && !processedFiles.Contains(track.BinFilename))
                {
                    var binPath = Path.Combine(CueDirectory, track.BinFilename);
                    if (File.Exists(binPath))
                    {
                        total += new FileInfo(binPath).Length;
                        processedFiles.Add(track.BinFilename);
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// Get all BIN files referenced by this CUE sheet.
        /// </summary>
        public List<string> GetAllBinFiles()
        {
            var files = new List<string>();
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in Tracks)
            {
                if (!string.IsNullOrEmpty(track.BinFilename) && !processedFiles.Contains(track.BinFilename))
                {
                    files.Add(track.BinFilename);
                    processedFiles.Add(track.BinFilename);
                }
            }

            return files;
        }
    }
}
