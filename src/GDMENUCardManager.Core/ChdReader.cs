using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace GDMENUCardManager.Core
{
    public sealed class ChdReader : IDisposable
    {
        private const string LibraryName = "libchdr";
        private const int CHD_OPEN_READ = 1;
        private const int ChdFrameSize = 2448; // 2352 data + 96 subcode
        private const int ChdSectorDataSize = 2352;
        private const int TrackPadding = 4; // chdman aligns each track to 4-frame boundaries

        // Metadata tags (big-endian 4CC as uint32)
        private const uint GDROM_TRACK_METADATA_TAG = 0x43484744; // 'CHGD'
        private const uint GDROM_OLD_METADATA_TAG = 0x43484754; // 'CHGT'
        private const uint CDROM_TRACK_METADATA2_TAG = 0x43485432; // 'CHT2'
        private const uint CDROM_TRACK_METADATA_TAG = 0x43485452; // 'CHTR'
        private const uint CDROM_OLD_METADATA_TAG = 0x43484344; // 'CHCD'

        // chd_header field offsets (verified via offsetof on x64 Linux/Windows)
        private const int HeaderOffset_Version = 4;
        private const int HeaderOffset_HunkBytes = 28;
        private const int HeaderOffset_TotalHunks = 32;
        private const int HeaderOffset_LogicalBytes = 40;
        private const int HeaderOffset_UnitBytes = 156;

        private IntPtr _chdHandle;
        private bool _disposed;

        public ChdHeader Header { get; private set; }
        public IReadOnlyList<ChdTrack> Tracks { get; private set; }
        public bool IsGdRom { get; private set; }

        public ChdReader(string filePath)
        {
            var error = chd_open(filePath, CHD_OPEN_READ, IntPtr.Zero, out _chdHandle);
            if (error == ChdError.RequiresParent)
                throw new Exception("This CHD file requires a parent CHD. Delta/diff CHDs are not supported. Please use a standalone (merged) CHD file.");
            if (error != ChdError.None)
                throw new Exception($"Failed to open CHD file: {chd_error_string_safe(error)} ({error})");

            ReadHeader();
            ReadTracks();
        }

        private void ReadHeader()
        {
            IntPtr headerPtr = chd_get_header(_chdHandle);
            if (headerPtr == IntPtr.Zero)
                throw new Exception("Failed to read CHD header");

            Header = new ChdHeader
            {
                Version = (uint)Marshal.ReadInt32(headerPtr, HeaderOffset_Version),
                HunkBytes = (uint)Marshal.ReadInt32(headerPtr, HeaderOffset_HunkBytes),
                TotalHunks = (uint)Marshal.ReadInt32(headerPtr, HeaderOffset_TotalHunks),
                LogicalBytes = (ulong)Marshal.ReadInt64(headerPtr, HeaderOffset_LogicalBytes),
                UnitBytes = (uint)Marshal.ReadInt32(headerPtr, HeaderOffset_UnitBytes),
            };

            if (Header.Version < 1 || Header.Version > 5)
                throw new Exception($"Unsupported CHD version: {Header.Version}");
            if (Header.HunkBytes == 0)
                throw new Exception("Invalid CHD: hunkbytes is 0");
        }

        private void ReadTracks()
        {
            var tracks = new List<ChdTrack>();
            bool isGdRom = false;

            // Try GD-ROM metadata first
            if (TryReadTracksWithTag(GDROM_TRACK_METADATA_TAG, tracks, isGdRomTag: true))
            {
                isGdRom = true;
            }
            else if (TryReadTracksWithTag(GDROM_OLD_METADATA_TAG, tracks, isGdRomTag: true))
            {
                isGdRom = true;
            }
            // Then try CD-ROM metadata
            else if (!TryReadTracksWithTag(CDROM_TRACK_METADATA2_TAG, tracks, isGdRomTag: false))
            {
                if (!TryReadTracksWithTag(CDROM_TRACK_METADATA_TAG, tracks, isGdRomTag: false))
                {
                    TryReadTracksWithTag(CDROM_OLD_METADATA_TAG, tracks, isGdRomTag: false);
                }
            }

            if (tracks.Count == 0)
                throw new Exception("No track metadata found in CHD file. This may not be a disc image.");

            tracks.Sort((a, b) => a.TrackNumber.CompareTo(b.TrackNumber));
            Tracks = tracks.AsReadOnly();
            IsGdRom = isGdRom;
        }

        private bool TryReadTracksWithTag(uint tag, List<ChdTrack> tracks, bool isGdRomTag)
        {
            tracks.Clear();
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                for (uint index = 0; ; index++)
                {
                    var error = chd_get_metadata(_chdHandle, tag, index, buffer, 512,
                        out uint resultLen, out _, out _);

                    if (error == ChdError.MetadataNotFound)
                        break;
                    if (error != ChdError.None)
                        break;

                    string metadata = Marshal.PtrToStringAnsi(buffer, (int)resultLen);
                    if (string.IsNullOrEmpty(metadata))
                        continue;

                    var track = ParseTrackMetadata(metadata, isGdRomTag);
                    if (track != null)
                        tracks.Add(track);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return tracks.Count > 0;
        }

        private static ChdTrack ParseTrackMetadata(string metadata, bool isGdRom)
        {
            var track = new ChdTrack();
            // Format: "TRACK:3 TYPE:MODE1_RAW SUBTYPE:NONE FRAMES:300 PAD:0 PREGAP:0 PGTYPE:MODE1_RAW PGSUB:NONE POSTGAP:0"
            var parts = metadata.TrimEnd('\0').Split(' ');
            foreach (var part in parts)
            {
                int colonIdx = part.IndexOf(':');
                if (colonIdx < 0) continue;
                string key = part.Substring(0, colonIdx);
                string value = part.Substring(colonIdx + 1);

                switch (key)
                {
                    case "TRACK": int.TryParse(value, out int tn); track.TrackNumber = tn; break;
                    case "TYPE": track.Type = value; break;
                    case "SUBTYPE": track.SubType = value; break;
                    case "FRAMES": int.TryParse(value, out int fr); track.Frames = fr; break;
                    case "PAD": int.TryParse(value, out int pd); track.Pad = pd; break;
                    case "PREGAP": int.TryParse(value, out int pg); track.Pregap = pg; break;
                    case "PGTYPE": track.PregapType = value; break;
                    case "PGSUB": track.PregapSubType = value; break;
                    case "POSTGAP": int.TryParse(value, out int psg); track.Postgap = psg; break;
                }
            }

            if (track.TrackNumber <= 0 || string.IsNullOrEmpty(track.Type))
                return null;

            return track;
        }

        public byte[] ReadSector(long sectorIndex)
        {
            int framesPerHunk = (int)(Header.HunkBytes / ChdFrameSize);
            uint hunkNum = (uint)(sectorIndex / framesPerHunk);
            int frameInHunk = (int)(sectorIndex % framesPerHunk);

            IntPtr buffer = Marshal.AllocHGlobal((int)Header.HunkBytes);
            try
            {
                var error = chd_read(_chdHandle, hunkNum, buffer);
                if (error != ChdError.None)
                    throw new Exception($"Failed to read CHD hunk {hunkNum}: {chd_error_string_safe(error)}");

                byte[] sectorData = new byte[ChdSectorDataSize];
                Marshal.Copy(buffer + (frameInHunk * ChdFrameSize), sectorData, 0, ChdSectorDataSize);
                return sectorData;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Sectors are returned concatenated, 2352 bytes each, subcode stripped.
        /// </summary>
        public byte[] ReadSectors(long startSector, int count)
        {
            byte[] result = new byte[count * ChdSectorDataSize];
            int framesPerHunk = (int)(Header.HunkBytes / ChdFrameSize);

            IntPtr buffer = Marshal.AllocHGlobal((int)Header.HunkBytes);
            try
            {
                uint lastHunk = uint.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    long sector = startSector + i;
                    uint hunkNum = (uint)(sector / framesPerHunk);
                    int frameInHunk = (int)(sector % framesPerHunk);

                    if (hunkNum != lastHunk)
                    {
                        var error = chd_read(_chdHandle, hunkNum, buffer);
                        if (error != ChdError.None)
                            throw new Exception($"Failed to read CHD hunk {hunkNum}: {chd_error_string_safe(error)}");
                        lastHunk = hunkNum;
                    }

                    Marshal.Copy(buffer + (frameInHunk * ChdFrameSize), result, i * ChdSectorDataSize, ChdSectorDataSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        /// <summary>
        /// Reads the raw 2352-byte sector holding IP.BIN. Caller has to skip the 16-byte MODE1
        /// header itself.
        /// </summary>
        public byte[] GetIpBin()
        {
            long ipSector = GetIpBinSectorOffset();
            return ReadSector(ipSector);
        }

        private long GetIpBinSectorOffset()
        {
            long currentSector = 0;

            if (IsGdRom)
            {
                // IP.BIN sits at the start of the high-density area, track 3 or later. The
                // 4-frame alignment padding is stored in the stream, so it has to be counted.
                foreach (var track in Tracks)
                {
                    if (track.IsData && track.TrackNumber >= 3)
                        return currentSector + track.Pregap;

                    // Total stored per track: pregap + frames + alignment padding
                    currentSector += track.Pregap + track.Frames
                        + GetExtraFrames(track.Frames);
                }
            }
            else
            {
                foreach (var track in Tracks)
                {
                    if (track.IsData)
                        return currentSector + track.Pregap;

                    currentSector += track.Pregap + track.Frames
                        + GetExtraFrames(track.Frames);
                }
            }

            throw new Exception("No data track found in CHD file");
        }

        // Extra frames chdman appends to round a track up to a 4-frame boundary. They are
        // stored in the stream, so every offset calculation has to include them.
        private static int GetExtraFrames(int frames)
        {
            return ((frames + TrackPadding - 1) / TrackPadding) * TrackPadding - frames;
        }

        public long GetTrackDataStartSector(int trackIndex)
        {
            long currentSector = 0;
            for (int i = 0; i < trackIndex; i++)
            {
                var t = Tracks[i];
                currentSector += t.Pregap + t.Frames + GetExtraFrames(t.Frames);
            }
            return currentSector;
        }

        private static string chd_error_string_safe(ChdError error)
        {
            try
            {
                IntPtr ptr = chd_error_string(error);
                if (ptr != IntPtr.Zero)
                    return Marshal.PtrToStringAnsi(ptr);
            }
            catch { }
            return error.ToString();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_chdHandle != IntPtr.Zero)
                {
                    chd_close(_chdHandle);
                    _chdHandle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        #region P/Invoke Declarations

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ChdError chd_open(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
            int mode,
            IntPtr parent,
            out IntPtr chd);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr chd_get_header(IntPtr chd);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ChdError chd_read(IntPtr chd, uint hunknum, IntPtr buffer);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ChdError chd_get_metadata(
            IntPtr chd,
            uint searchtag,
            uint searchindex,
            IntPtr output,
            uint outputlen,
            out uint resultlen,
            out uint resulttag,
            out byte resultflags);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void chd_close(IntPtr chd);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr chd_error_string(ChdError err);

        #endregion
    }

    public enum ChdError : int
    {
        None = 0,
        NoInterface = 1,
        OutOfMemory = 2,
        InvalidFile = 3,
        InvalidParameter = 4,
        InvalidData = 5,
        FileNotFound = 6,
        RequiresParent = 7,
        FileNotWriteable = 8,
        ReadError = 9,
        WriteError = 10,
        CodecError = 11,
        InvalidParent = 12,
        HunkOutOfRange = 13,
        DecompressionError = 14,
        CompressionError = 15,
        CantCreateFile = 16,
        CantVerify = 17,
        NotSupported = 18,
        MetadataNotFound = 19,
        InvalidMetadataSize = 20,
        UnsupportedVersion = 21,
        VerifyIncomplete = 22,
        InvalidMetadata = 23,
        InvalidState = 24,
        OperationPending = 25,
        NoAsyncOperation = 26,
        UnsupportedFormat = 27
    }

    public class ChdHeader
    {
        public uint Version { get; set; }
        public uint HunkBytes { get; set; }
        public uint TotalHunks { get; set; }
        public ulong LogicalBytes { get; set; }
        public uint UnitBytes { get; set; }
    }

    public class ChdTrack
    {
        public int TrackNumber { get; set; }
        public string Type { get; set; }
        public string SubType { get; set; }
        public int Frames { get; set; }
        public int Pad { get; set; }
        public int Pregap { get; set; }
        public string PregapType { get; set; }
        public string PregapSubType { get; set; }
        public int Postgap { get; set; }

        public bool IsAudio => string.Equals(Type, "AUDIO", StringComparison.OrdinalIgnoreCase);
        public bool IsData => !IsAudio;

        public int SectorDataSize => Type?.ToUpperInvariant() switch
        {
            "AUDIO" => 2352,
            "MODE1" => 2048,
            "MODE1_RAW" => 2352,
            "MODE1/2048" => 2048,
            "MODE1/2352" => 2352,
            "MODE2" => 2336,
            "MODE2_RAW" => 2352,
            "MODE2/2336" => 2336,
            "MODE2/2352" => 2352,
            "MODE2_FORM1" => 2048,
            "MODE2/2048" => 2048,
            "MODE2_FORM2" => 2328,
            "MODE2/2324" => 2324,
            _ => 2352
        };
    }
}
