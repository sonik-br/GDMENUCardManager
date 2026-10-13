using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Written by Derek Pascarella (ateam)

namespace GDMENUCardManager.Core
{
    public sealed record TosecTrackEntry(int TrackNumber, bool IsData, long Size, uint Crc32, byte[] Md5);

    public sealed class TosecDiscEntry
    {
        public uint T1Crc32 { get; init; }
        public string GameName { get; init; } = string.Empty;
        public IReadOnlyList<TosecTrackEntry> Tracks { get; init; } = Array.Empty<TosecTrackEntry>();
    }

    // Loads the compact TOSEC DC blob into memory on first use, then serves
    // O(1) lookups by Track 1 CRC32. Aggregates all 31 DC DATs (JP/US/PAL
    // Games, demos, applications, dev builds, homebrew, multimedia, samplers).
    // A missing or unreadable blob disables the TOSEC path. Conversion falls
    // back to the standard strip-path behavior.
    /// <summary>
    /// Per-track CRC32, MD5 and size from the TOSEC DAT, keyed by the track 1 CRC32.
    /// </summary>
    public static class TosecDatLookup
    {
        private static readonly object _initLock = new object();
        private static Dictionary<uint, TosecDiscEntry> _byT1Crc;
        private static bool _initialized;

        /// <summary>
        /// Null is normal and simply disables byte-exact reconstruction.
        /// </summary>
        public static TosecDiscEntry LookupByT1Crc32(uint t1Crc32)
        {
            EnsureLoaded();
            if (_byT1Crc == null) return null;
            return _byT1Crc.TryGetValue(t1Crc32, out var entry) ? entry : null;
        }

        public static int EntryCount
        {
            get { EnsureLoaded(); return _byT1Crc?.Count ?? 0; }
        }

        private static void EnsureLoaded()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;
                try
                {
                    // Path relative to the assembly so CWD doesn't matter.
                    string path = Path.Combine(AppContext.BaseDirectory, Constants.TosecDatFile);
                    _byT1Crc = Load(path);
                }
                catch
                {
                    _byT1Crc = null;
                }
                _initialized = true;
            }
        }

        private static Dictionary<uint, TosecDiscEntry> Load(string path)
        {
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            using var br = new BinaryReader(stream);

            var magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'U' || magic[1] != 'D' || magic[2] != 'P' || magic[3] != 'T')
                throw new InvalidDataException("TOSEC DAT blob magic mismatch.");
            uint version = br.ReadUInt32();
            if (version != 1)
                throw new InvalidDataException($"Unsupported TOSEC DAT blob version: {version}");
            uint gameCount = br.ReadUInt32();

            var result = new Dictionary<uint, TosecDiscEntry>((int)gameCount);
            for (uint g = 0; g < gameCount; g++)
            {
                uint t1Crc = br.ReadUInt32();
                ushort nameLen = br.ReadUInt16();
                string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
                byte trackCount = br.ReadByte();
                var tracks = new TosecTrackEntry[trackCount];
                for (int t = 0; t < trackCount; t++)
                {
                    byte trackNum = br.ReadByte();
                    byte isData = br.ReadByte();
                    long size = br.ReadInt64();
                    uint crc = br.ReadUInt32();
                    byte[] md5 = br.ReadBytes(16);
                    tracks[t] = new TosecTrackEntry(trackNum, isData != 0, size, crc, md5);
                }
                result[t1Crc] = new TosecDiscEntry
                {
                    T1Crc32 = t1Crc,
                    GameName = name,
                    Tracks = tracks,
                };
            }
            return result;
        }
    }
}
