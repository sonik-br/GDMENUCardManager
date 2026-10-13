using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{

    /// <summary>
    /// Handles MODE1/2352, MODE1/2048, MODE2/2352, MODE2/2336, CDI/2352, CDI/2336, CDG and AUDIO
    /// tracks, from BINARY or WAVE sources.
    /// </summary>
    public static class Cue2CcdConverter
    {
        public static async Task<bool> ConvertAsync(string cueFilePath, string destinationDir,
            IProgress<string> progress = null)
        {
            return await Task.Run(() =>
            {
                string baseName = Path.GetFileNameWithoutExtension(cueFilePath);
                Directory.CreateDirectory(destinationDir);
                string ccdPath = Path.Combine(destinationDir, baseName + ".ccd");
                string imgPath = Path.Combine(destinationDir, baseName + ".img");
                string subPath = Path.Combine(destinationDir, baseName + ".sub");

                progress?.Report($"Converting {baseName} from CUE to CCD...");

                var cue = CueParser.Parse(cueFilePath);
                var disc = DiscLayoutBuilder.Build(cue);

                string ccdContent = CcdFileWriter.Generate(disc);
                File.WriteAllText(ccdPath, ccdContent, new UTF8Encoding(false));

                ImgFileWriter.Generate(disc, imgPath);
                SubFileWriter.Generate(disc, subPath);

                return true;
            });
        }

        #region CUE Parser

        private class CueSheet
        {
            public List<CueFile> Files { get; } = new();
            public string Catalog { get; set; }
        }

        private record CueFile(string Name, string FullPath, string Type)
        {
            public List<CueTrack> Tracks { get; } = new();
        }

        private record CueIndex(int Number, int Position);

        private class CueTrack
        {
            public int Number { get; }
            public string CueType { get; }
            public CueFile File { get; }
            public List<CueIndex> Indices { get; } = new();
            public int Pregap { get; set; }
            public int Postgap { get; set; }

            public CueTrack(int number, string cueType, CueFile file)
            {
                Number = number;
                CueType = cueType;
                File = file;
            }

            public bool IsAudio => CueType == "AUDIO" || CueType == "CDG";

            public int SourceSectorSize => CueType switch
            {
                "MODE1/2048" => 2048,
                "MODE1/2352" => 2352,
                "MODE2/2352" => 2352,
                "MODE2/2336" => 2336,
                "CDI/2336" => 2336,
                "CDI/2352" => 2352,
                "CDG" => 2448,
                "AUDIO" => 2352,
                _ => 2352
            };

            public int CcdMode => CueType switch
            {
                var t when t.StartsWith("MODE1") => 1,
                var t when t.StartsWith("MODE2") || t.StartsWith("CDI") => 2,
                _ => 0
            };

            public int Control => IsAudio ? 0x00 : 0x04;
        }

        private static class CueParser
        {
            public static CueSheet Parse(string path)
            {
                var sheet = new CueSheet();
                CueFile curFile = null;
                CueTrack curTrack = null;
                string dir = Path.GetDirectoryName(path) ?? ".";

                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string upper = line.ToUpperInvariant();

                    if (upper.StartsWith("FILE "))
                    {
                        int q1 = line.IndexOf('"');
                        int q2 = line.LastIndexOf('"');
                        if (q1 < 0 || q2 <= q1)
                            throw new FormatException($"Bad FILE line: {line}");
                        string name = line[(q1 + 1)..q2];
                        string type = line[(q2 + 1)..].Trim().ToUpperInvariant();
                        curFile = new CueFile(name, Helper.ResolveActualPath(Path.Combine(dir, name)), type);
                        sheet.Files.Add(curFile);
                    }
                    else if (upper.StartsWith("TRACK "))
                    {
                        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        curTrack = new CueTrack(int.Parse(p[1]), p[2].ToUpperInvariant(), curFile!);
                        curFile!.Tracks.Add(curTrack);
                    }
                    else if (upper.StartsWith("INDEX "))
                    {
                        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        curTrack!.Indices.Add(new CueIndex(int.Parse(p[1]), ParseMsf(p[2])));
                    }
                    else if (upper.StartsWith("PREGAP "))
                    {
                        curTrack!.Pregap = ParseMsf(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                    }
                    else if (upper.StartsWith("POSTGAP "))
                    {
                        curTrack!.Postgap = ParseMsf(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                    }
                    else if (upper.StartsWith("CATALOG "))
                    {
                        sheet.Catalog = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Trim();
                    }
                }

                return sheet;
            }

            private static int ParseMsf(string msf)
            {
                var p = msf.Split(':');
                return int.Parse(p[0]) * 75 * 60 + int.Parse(p[1]) * 75 + int.Parse(p[2]);
            }
        }

        #endregion

        #region Disc Layout

        private class DiscLayout
        {
            public List<DiscTrack> Tracks { get; } = new();
            public long TotalSectors { get; set; }
            public string Catalog { get; set; }
        }

        private class DiscTrack
        {
            public int Number;
            public string CueType = "";
            public bool IsAudio;
            public int CcdMode;
            public int ControlField;
            public int SourceSectorSize;
            public string SourceFilePath = "";
            public string SourceFileType = "";
            public long SourceOffsetInFile;
            public int SectorCount;
            public int PregapLength;
            public int PostgapLength;
            public int GeneratedPregap;
            public long AbsoluteStart;
            public long? AbsoluteIndex00;
            public bool HasIndex00;
            public bool IsCdg;
            public List<(int Number, long Position)> AdditionalIndices = new();
        }

        private static class DiscLayoutBuilder
        {
            public static DiscLayout Build(CueSheet cue)
            {
                var layout = new DiscLayout();
                layout.Catalog = cue.Catalog;
                long absoluteOffset = 0;
                string prevFileName = null;
                long prevFileSectors = 0;

                var allTracks = cue.Files.SelectMany(f => f.Tracks).ToList();

                for (int ti = 0; ti < allTracks.Count; ti++)
                {
                    var ct = allTracks[ti];
                    bool newFile = ct.File.Name != prevFileName;

                    if (newFile && prevFileName != null)
                        absoluteOffset = prevFileSectors;

                    long fileSectors;
                    if (ct.File.Type == "WAVE")
                    {
                        long dataSize = WavFileReader.GetDataSize(ct.File.FullPath);
                        fileSectors = dataSize / 2352;
                    }
                    else
                    {
                        long fileSize = new FileInfo(ct.File.FullPath).Length;
                        fileSectors = fileSize / ct.SourceSectorSize;
                    }

                    long fileBaseOffset;
                    if (newFile)
                    {
                        fileBaseOffset = absoluteOffset;
                        prevFileName = ct.File.Name;
                        prevFileSectors = absoluteOffset + fileSectors;
                    }
                    else
                    {
                        fileBaseOffset = absoluteOffset;
                    }

                    var idx01 = ct.Indices.FirstOrDefault(i => i.Number == 1);
                    var idx00 = ct.Indices.FirstOrDefault(i => i.Number == 0);
                    int trackStartInFile = idx01?.Position ?? 0;

                    long trackSectorCount;
                    var nextInFile = allTracks.Skip(ti + 1).FirstOrDefault(t => t.File.Name == ct.File.Name);
                    if (nextInFile != null)
                    {
                        var nextIdx00 = nextInFile.Indices.FirstOrDefault(i => i.Number == 0);
                        var nextIdx01 = nextInFile.Indices.FirstOrDefault(i => i.Number == 1);
                        int nextStart = nextIdx00?.Position ?? nextIdx01?.Position ?? 0;
                        trackSectorCount = nextStart - trackStartInFile;
                    }
                    else
                    {
                        trackSectorCount = fileSectors - trackStartInFile;
                    }

                    var dt = new DiscTrack
                    {
                        Number = ct.Number,
                        CueType = ct.CueType,
                        IsAudio = ct.IsAudio,
                        CcdMode = ct.CcdMode,
                        ControlField = ct.Control,
                        SourceSectorSize = ct.SourceSectorSize,
                        SourceFilePath = ct.File.FullPath,
                        SourceFileType = ct.File.Type,
                        SourceOffsetInFile = (long)trackStartInFile * ct.SourceSectorSize,
                        SectorCount = (int)trackSectorCount,
                        PregapLength = ct.Pregap,
                        PostgapLength = ct.Postgap,
                        IsCdg = ct.CueType == "CDG",
                    };

                    if (idx00 != null)
                    {
                        dt.AbsoluteIndex00 = fileBaseOffset + idx00.Position;
                        dt.AbsoluteStart = fileBaseOffset + trackStartInFile;
                        dt.HasIndex00 = true;
                        int embeddedPregap = trackStartInFile - idx00.Position;
                        dt.SectorCount += embeddedPregap;
                        dt.SourceOffsetInFile = (long)idx00.Position * ct.SourceSectorSize;
                    }
                    else if (ct.Pregap > 0)
                    {
                        dt.AbsoluteIndex00 = fileBaseOffset + trackStartInFile;
                        dt.AbsoluteStart = fileBaseOffset + trackStartInFile + ct.Pregap;
                        dt.HasIndex00 = true;
                        dt.GeneratedPregap = ct.Pregap;
                    }
                    else
                    {
                        dt.AbsoluteStart = fileBaseOffset + trackStartInFile;
                    }

                    foreach (var idx in ct.Indices.Where(i => i.Number > 1))
                        dt.AdditionalIndices.Add((idx.Number, fileBaseOffset + idx.Position));

                    layout.Tracks.Add(dt);
                }

                if (layout.Tracks.Count > 0)
                    RecalculateAbsolutePositions(layout);

                return layout;
            }

            private static void RecalculateAbsolutePositions(DiscLayout layout)
            {
                bool singleFile = layout.Tracks.Select(t => t.SourceFilePath).Distinct().Count() == 1;
                bool hasGeneratedGaps = layout.Tracks.Any(t => t.GeneratedPregap > 0 || t.PostgapLength > 0);

                if (singleFile && !hasGeneratedGaps)
                {
                    var last = layout.Tracks.Last();
                    long lastEnd = last.HasIndex00 ? last.AbsoluteIndex00!.Value + last.SectorCount
                                                   : last.AbsoluteStart + last.SectorCount;
                    layout.TotalSectors = lastEnd;
                    return;
                }

                long currentSector = 0;
                for (int i = 0; i < layout.Tracks.Count; i++)
                {
                    var t = layout.Tracks[i];

                    if (t.GeneratedPregap > 0)
                    {
                        t.AbsoluteIndex00 = currentSector;
                        currentSector += t.GeneratedPregap;
                        t.AbsoluteStart = currentSector;
                        currentSector += t.SectorCount;
                    }
                    else if (t.HasIndex00 && i > 0)
                    {
                        long pregapLen = t.AbsoluteStart - t.AbsoluteIndex00!.Value;
                        t.AbsoluteIndex00 = currentSector;
                        currentSector += pregapLen;
                        t.AbsoluteStart = currentSector;
                        currentSector += t.SectorCount - pregapLen;
                    }
                    else
                    {
                        t.AbsoluteStart = currentSector;
                        currentSector += t.SectorCount;
                    }

                    currentSector += t.PostgapLength;
                }

                layout.TotalSectors = currentSector;
            }
        }

        #endregion

        #region WAV Reader

        private static class WavFileReader
        {
            public static long GetDataSize(string path)
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                return FindDataChunk(fs);
            }

            public static (Stream Stream, long DataSize) OpenData(string path)
            {
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                long size = FindDataChunk(fs);
                return (fs, size);
            }

            private static long FindDataChunk(Stream s)
            {
                var r = new BinaryReader(s);
                string riff = new string(r.ReadChars(4));
                if (riff != "RIFF")
                    throw new FormatException("Not a RIFF file");
                r.ReadInt32();
                string wave = new string(r.ReadChars(4));
                if (wave != "WAVE")
                    throw new FormatException("Not a WAVE file");

                while (s.Position < s.Length - 8)
                {
                    string chunkId = new string(r.ReadChars(4));
                    int chunkSize = r.ReadInt32();
                    if (chunkId == "data")
                        return chunkSize;
                    s.Seek(chunkSize, SeekOrigin.Current);
                }

                throw new FormatException("No data chunk found in WAV file");
            }
        }

        #endregion

        #region CCD Writer

        private static class CcdFileWriter
        {
            public static string Generate(DiscLayout disc)
            {
                var sb = new StringBuilder();
                int trackCount = disc.Tracks.Count;
                int tocEntries = trackCount + 3;

                var first = disc.Tracks[0];
                var last = disc.Tracks[^1];

                sb.AppendLine("[CloneCD]");
                sb.AppendLine("Version=3");
                sb.AppendLine("[Disc]");
                sb.AppendLine($"TocEntries={tocEntries}");
                sb.AppendLine("Sessions=1");
                sb.AppendLine("DataTracksScrambled=0");
                sb.AppendLine("CDTextLength=0");
                sb.AppendLine("[Session 1]");
                sb.AppendLine($"PreGapMode={first.CcdMode}");
                sb.AppendLine("PreGapSubC=1");

                int discType = disc.Tracks.Any(t => t.CcdMode == 2) ? 0x20 : 0x00;
                WriteEntry(sb, 0, 0xA0, first.ControlField, first.Number, discType, 0,
                    (first.Number * 60 + discType) * 75 - 150);
                WriteEntry(sb, 1, 0xA1, last.ControlField, last.Number, 0, 0,
                    last.Number * 4500 - 150);

                var (loM, loS, loF) = LbaToMsf(disc.TotalSectors + 150);
                WriteEntry(sb, 2, 0xA2, last.ControlField, loM, loS, loF, disc.TotalSectors);

                for (int i = 0; i < trackCount; i++)
                {
                    var t = disc.Tracks[i];
                    var (m, s, f) = LbaToMsf(t.AbsoluteStart + 150);
                    WriteEntry(sb, 3 + i, t.Number, t.ControlField, m, s, f, t.AbsoluteStart);
                }

                for (int i = 0; i < trackCount; i++)
                {
                    var t = disc.Tracks[i];
                    sb.AppendLine($"[TRACK {t.Number}]");
                    sb.AppendLine($"MODE={t.CcdMode}");
                    if (t.HasIndex00)
                        sb.AppendLine($"INDEX 0={t.AbsoluteIndex00}");
                    sb.AppendLine($"INDEX 1={t.AbsoluteStart}");
                    foreach (var (num, pos) in t.AdditionalIndices)
                        sb.AppendLine($"INDEX {num}={pos}");
                }

                return sb.ToString();
            }

            private static void WriteEntry(StringBuilder sb, int entryNum, int point, int control,
                int pMin, int pSec, int pFrame, long plba)
            {
                sb.AppendLine($"[Entry {entryNum}]");
                sb.AppendLine("Session=1");
                sb.AppendLine($"Point=0x{point:x2}");
                sb.AppendLine("ADR=0x01");
                sb.AppendLine($"Control=0x{control:x2}");
                sb.AppendLine("TrackNo=0");
                sb.AppendLine("AMin=0");
                sb.AppendLine("ASec=0");
                sb.AppendLine("AFrame=0");
                sb.AppendLine("ALBA=-150");
                sb.AppendLine("Zero=0");
                sb.AppendLine($"PMin={pMin}");
                sb.AppendLine($"PSec={pSec}");
                sb.AppendLine($"PFrame={pFrame}");
                sb.AppendLine($"PLBA={plba}");
            }

            private static (int M, int S, int F) LbaToMsf(long lba)
            {
                int m = (int)(lba / 4500);
                int s = (int)(lba / 75 % 60);
                int f = (int)(lba % 75);
                return (m, s, f);
            }
        }

        #endregion

        #region IMG Writer

        private static class ImgFileWriter
        {
            public static void Generate(DiscLayout disc, string outputPath)
            {
                using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.SequentialScan);

                long sectorNum = 0;

                foreach (var track in disc.Tracks)
                {
                    if (track.GeneratedPregap > 0)
                        WriteGapSectors(output, track.GeneratedPregap, track.IsAudio, track.CcdMode, ref sectorNum);

                    WriteTrackData(output, track, ref sectorNum);

                    if (track.PostgapLength > 0)
                        WriteGapSectors(output, track.PostgapLength, track.IsAudio, track.CcdMode, ref sectorNum);
                }
            }

            private static void WriteGapSectors(FileStream output, int count, bool isAudio, int ccdMode, ref long sectorNum)
            {
                if (isAudio)
                {
                    byte[] silence = new byte[2352];
                    for (int i = 0; i < count; i++)
                    {
                        output.Write(silence);
                        sectorNum++;
                    }
                }
                else if (ccdMode == 2)
                {
                    byte[] zeroData = new byte[2336];
                    for (int i = 0; i < count; i++)
                    {
                        byte[] sector = Mode2SectorBuilder.Build(sectorNum, zeroData);
                        output.Write(sector);
                        sectorNum++;
                    }
                }
                else
                {
                    byte[] zeroData = new byte[2048];
                    for (int i = 0; i < count; i++)
                    {
                        byte[] sector = Mode1SectorBuilder.Build(sectorNum, zeroData);
                        output.Write(sector);
                        sectorNum++;
                    }
                }
            }

            private static void WriteTrackData(FileStream output, DiscTrack track, ref long sectorNum)
            {
                bool isWav = track.SourceFileType == "WAVE";
                Stream source;

                if (isWav)
                {
                    var (s, _) = WavFileReader.OpenData(track.SourceFilePath);
                    source = s;
                }
                else
                {
                    source = new FileStream(track.SourceFilePath, FileMode.Open, FileAccess.Read);
                    source.Seek(track.SourceOffsetInFile, SeekOrigin.Begin);
                }

                using (source)
                {
                    int srcSectorSize = track.SourceSectorSize;
                    bool needsMode1Wrap = (srcSectorSize == 2048);
                    bool needsMode2Wrap = (srcSectorSize == 2336);
                    byte[] readBuf = new byte[srcSectorSize];

                    for (int i = 0; i < track.SectorCount; i++)
                    {
                        int bytesRead = ReadFull(source, readBuf, 0, srcSectorSize);
                        if (bytesRead < srcSectorSize)
                            Array.Clear(readBuf, bytesRead, srcSectorSize - bytesRead);

                        if (needsMode1Wrap)
                        {
                            byte[] sector = Mode1SectorBuilder.Build(sectorNum, readBuf);
                            output.Write(sector);
                        }
                        else if (needsMode2Wrap)
                        {
                            byte[] sector = Mode2SectorBuilder.Build(sectorNum, readBuf);
                            output.Write(sector);
                        }
                        else
                        {
                            output.Write(readBuf, 0, 2352);
                        }

                        sectorNum++;
                    }
                }
            }

            private static int ReadFull(Stream s, byte[] buf, int offset, int count)
            {
                int total = 0;
                while (total < count)
                {
                    int n = s.Read(buf, offset + total, count - total);
                    if (n == 0) break;
                    total += n;
                }
                return total;
            }
        }

        #endregion

        #region Mode 1 Sector Builder (2048 to 2352)

        private static class Mode1SectorBuilder
        {
            private static readonly byte[] SyncPattern =
            {
            0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0x00
        };

            private static readonly uint[] EdcTable;
            private static readonly byte[] EccFTable;
            private static readonly byte[] EccBTable;

            static Mode1SectorBuilder()
            {
                EdcTable = new uint[256];
                for (int i = 0; i < 256; i++)
                {
                    uint edc = (uint)i;
                    for (int j = 0; j < 8; j++)
                        edc = (edc >> 1) ^ ((edc & 1) != 0 ? 0xD8018001u : 0u);
                    EdcTable[i] = edc;
                }

                EccFTable = new byte[256];
                EccBTable = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    int j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
                    EccFTable[i] = (byte)j;
                    EccBTable[i ^ j] = (byte)i;
                }
            }

            public static byte[] Build(long lba, byte[] userData)
            {
                byte[] sector = new byte[2352];

                Array.Copy(SyncPattern, 0, sector, 0, 12);

                long absolute = lba + 150;
                sector[12] = ToBcd((int)(absolute / 4500));
                sector[13] = ToBcd((int)(absolute / 75 % 60));
                sector[14] = ToBcd((int)(absolute % 75));
                sector[15] = 0x01;

                Array.Copy(userData, 0, sector, 16, Math.Min(userData.Length, 2048));

                uint edc = 0;
                for (int i = 0; i < 2064; i++)
                    edc = EdcTable[(edc ^ sector[i]) & 0xFF] ^ (edc >> 8);
                sector[2064] = (byte)(edc & 0xFF);
                sector[2065] = (byte)((edc >> 8) & 0xFF);
                sector[2066] = (byte)((edc >> 16) & 0xFF);
                sector[2067] = (byte)((edc >> 24) & 0xFF);

                ComputeEcc(sector, 12, 86, 24, 2, 86, sector, 2076);
                ComputeEcc(sector, 12, 52, 43, 86, 88, sector, 2248);

                return sector;
            }

            private static void ComputeEcc(byte[] src, int srcOffset, int majorCount, int minorCount,
                int majorMult, int minorInc, byte[] dest, int destOffset)
            {
                int size = majorCount * minorCount;
                for (int major = 0; major < majorCount; major++)
                {
                    int index = (major >> 1) * majorMult + (major & 1);
                    byte eccA = 0, eccB = 0;
                    for (int minor = 0; minor < minorCount; minor++)
                    {
                        byte temp = src[srcOffset + index];
                        index += minorInc;
                        if (index >= size) index -= size;
                        eccA ^= temp;
                        eccB ^= temp;
                        eccA = EccFTable[eccA];
                    }
                    eccA = EccBTable[(byte)(EccFTable[eccA] ^ eccB)];
                    dest[destOffset + major] = eccA;
                    dest[destOffset + major + majorCount] = (byte)(eccA ^ eccB);
                }
            }

            private static byte ToBcd(int val) => (byte)(((val / 10) << 4) | (val % 10));
        }

        #endregion

        #region Mode 2 Sector Builder (2336 to 2352)

        private static class Mode2SectorBuilder
        {
            private static readonly byte[] SyncPattern =
            {
                0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0x00
            };

            public static byte[] Build(long lba, byte[] data)
            {
                byte[] sector = new byte[2352];

                Array.Copy(SyncPattern, 0, sector, 0, 12);

                long absolute = lba + 150;
                sector[12] = ToBcd((int)(absolute / 4500));
                sector[13] = ToBcd((int)(absolute / 75 % 60));
                sector[14] = ToBcd((int)(absolute % 75));
                sector[15] = 0x02;

                Array.Copy(data, 0, sector, 16, Math.Min(data.Length, 2336));

                return sector;
            }

            private static byte ToBcd(int val) => (byte)(((val / 10) << 4) | (val % 10));
        }

        #endregion

        #region SUB Writer

        private static class SubFileWriter
        {
            private static readonly ushort[] CrcTable;

            static SubFileWriter()
            {
                CrcTable = new ushort[256];
                for (int i = 0; i < 256; i++)
                {
                    ushort crc = (ushort)(i << 8);
                    for (int j = 0; j < 8; j++)
                    {
                        if ((crc & 0x8000) != 0)
                            crc = (ushort)((crc << 1) ^ 0x1021);
                        else
                            crc = (ushort)(crc << 1);
                    }
                    CrcTable[i] = crc;
                }
            }

            public static void Generate(DiscLayout disc, string outputPath)
            {
                var cdgStreams = new Dictionary<string, FileStream>();
                try
                {
                    foreach (var track in disc.Tracks)
                    {
                        if (track.IsCdg && !cdgStreams.ContainsKey(track.SourceFilePath))
                            cdgStreams[track.SourceFilePath] = new FileStream(track.SourceFilePath,
                                FileMode.Open, FileAccess.Read);
                    }

                    using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 81920, FileOptions.SequentialScan);

                    byte[] subBuf = new byte[96];

                    byte[] mcnBytes = null;
                    if (disc.Catalog != null && disc.Catalog.Length == 13)
                        mcnBytes = EncodeMcn(disc.Catalog);

                    for (long sector = 0; sector < disc.TotalSectors; sector++)
                    {
                        var (track, indexNum, relativePos, isGap) = FindTrackForSector(disc, sector);

                        if (track.IsCdg && cdgStreams.TryGetValue(track.SourceFilePath, out var cdgFs))
                        {
                            long sourceStart = track.HasIndex00 && track.AbsoluteIndex00.HasValue
                                && track.GeneratedPregap == 0
                                ? track.AbsoluteIndex00.Value : track.AbsoluteStart;
                            long sourceEnd = sourceStart + track.SectorCount;

                            if (sector >= sourceStart && sector < sourceEnd)
                            {
                                long sectorInSource = sector - sourceStart;
                                long fileOffset = track.SourceOffsetInFile + sectorInSource * 2448 + 2352;
                                cdgFs.Seek(fileOffset, SeekOrigin.Begin);
                                int total = 0;
                                while (total < 96)
                                {
                                    int n = cdgFs.Read(subBuf, total, 96 - total);
                                    if (n == 0) break;
                                    total += n;
                                }
                                if (total < 96)
                                    Array.Clear(subBuf, total, 96 - total);
                                output.Write(subBuf, 0, 96);
                                continue;
                            }
                        }

                        Array.Clear(subBuf, 0, 96);

                        bool pHigh = isGap || (relativePos == 0 && indexNum >= 1);
                        if (pHigh)
                        {
                            for (int i = 0; i < 12; i++)
                                subBuf[i] = 0xFF;
                        }

                        if (mcnBytes != null && sector % 100 == 24)
                        {
                            byte controlAdr = (byte)((track.IsAudio ? 0x00 : 0x40) | 0x02);
                            subBuf[12] = controlAdr;
                            Array.Copy(mcnBytes, 0, subBuf, 13, 7);
                            subBuf[20] = 0x00;
                            long absFrame = (sector + 150) % 75;
                            subBuf[21] = ToBcd((int)absFrame);
                        }
                        else
                        {
                            byte controlAdr = (byte)((track.IsAudio ? 0x00 : 0x40) | 0x01);
                            subBuf[12] = controlAdr;
                            subBuf[13] = ToBcd(track.Number);
                            subBuf[14] = ToBcd(indexNum);

                            long relCount = relativePos < 0 ? -relativePos : relativePos;
                            var (rM, rS, rF) = SectorsToMsf(relCount);
                            subBuf[15] = ToBcd(rM);
                            subBuf[16] = ToBcd(rS);
                            subBuf[17] = ToBcd(rF);

                            subBuf[18] = 0x00;

                            long absSector = sector + 150;
                            var (aM, aS, aF) = SectorsToMsf(absSector);
                            subBuf[19] = ToBcd(aM);
                            subBuf[20] = ToBcd(aS);
                            subBuf[21] = ToBcd(aF);
                        }

                        ushort crc = ComputeCrc(subBuf, 12, 10);
                        subBuf[22] = (byte)(crc >> 8);
                        subBuf[23] = (byte)(crc & 0xFF);

                        output.Write(subBuf, 0, 96);
                    }
                }
                finally
                {
                    foreach (var fs in cdgStreams.Values)
                        fs.Dispose();
                }
            }

            private static byte[] EncodeMcn(string catalog)
            {
                byte[] result = new byte[7];
                for (int i = 0; i < 13; i++)
                {
                    int digit = catalog[i] - '0';
                    int byteIdx = i / 2;
                    if (i % 2 == 0)
                        result[byteIdx] = (byte)(digit << 4);
                    else
                        result[byteIdx] |= (byte)digit;
                }
                return result;
            }

            private static (DiscTrack Track, int IndexNum, long RelativePos, bool IsGap) FindTrackForSector(
                DiscLayout disc, long sector)
            {
                for (int i = disc.Tracks.Count - 1; i >= 0; i--)
                {
                    var t = disc.Tracks[i];

                    long trackEnd = t.AbsoluteStart + t.SectorCount;
                    long postgapEnd = trackEnd + t.PostgapLength;

                    if (sector >= trackEnd && sector < postgapEnd)
                        return (t, 1, sector - t.AbsoluteStart, true);

                    if (t.HasIndex00 && t.AbsoluteIndex00.HasValue && sector >= t.AbsoluteIndex00.Value && sector < t.AbsoluteStart)
                    {
                        long relPos = sector - t.AbsoluteStart;
                        return (t, 0, relPos, true);
                    }

                    if (sector >= t.AbsoluteStart && sector < trackEnd)
                    {
                        long relPos = sector - t.AbsoluteStart;
                        int idxNum = 1;
                        foreach (var (num, pos) in t.AdditionalIndices)
                        {
                            if (sector >= pos)
                                idxNum = num;
                        }
                        return (t, idxNum, relPos, false);
                    }
                }

                return (disc.Tracks[0], 1, sector, false);
            }

            private static (int M, int S, int F) SectorsToMsf(long sectors)
            {
                int m = (int)(sectors / 4500);
                int s = (int)(sectors / 75 % 60);
                int f = (int)(sectors % 75);
                return (m, s, f);
            }

            private static byte ToBcd(int val) => (byte)(((val / 10) << 4) | (val % 10));

            private static ushort ComputeCrc(byte[] data, int offset, int length)
            {
                ushort crc = 0x0000;
                for (int i = 0; i < length; i++)
                    crc = (ushort)(CrcTable[((crc >> 8) ^ data[offset + i]) & 0xFF] ^ (crc << 8));
                return (ushort)(crc ^ 0xFFFF);
            }
        }

        #endregion
    }
}
