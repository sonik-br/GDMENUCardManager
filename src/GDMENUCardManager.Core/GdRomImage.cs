using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GDMENUCardManager.Core
{
    // One track line from a .gdi index file.
    internal sealed class GdiTrack
    {
        public string FilePath;
        public int Number;
        public long Lba;
        public int SectorSize;
        public bool IsData;

        public GdiTrack Clone() => (GdiTrack)MemberwiseClone();
    }

    // Presents the high density area of a GD-ROM dump as one continuous run of
    // 2048 byte sectors addressed by absolute LBA. The table of contents track
    // sits at LBA 45000 and the game data track at its own LBA. Anything
    // outside a real track, including the space between the two, reads as
    // zeros.
    internal sealed class GdRomImage : IDisposable
    {
        public const int SectorSize = 2048;
        public const long TocLba = 45000;

        // A GD-ROM carries its volume descriptor at sector 16 of the high
        // density area rather than of the disc. Aliasing the first 32 sectors
        // onto the table of contents track puts the descriptor where an ISO9660
        // reader expects to find it.
        const long AliasLength = 32 * SectorSize;

        readonly TrackStream _toc;
        readonly TrackStream _gameData;
        readonly long _tocStart;
        readonly long _gameDataStart;

        byte[] _scratch;

        public long Length { get; }

        byte[] Scratch => _scratch ??= new byte[1 << 20];

        public GdRomImage(IReadOnlyList<GdiTrack> tracks)
        {
            GdiTrack tocTrack = null;
            foreach (GdiTrack track in tracks)
            {
                if (track.Lba == TocLba)
                {
                    tocTrack = track;
                    break;
                }
            }

            if (tocTrack == null)
                throw new InvalidDataException("No track starts at LBA 45000.");

            _toc = new TrackStream(tocTrack);
            _tocStart = TocLba * SectorSize;

            // Discs carrying audio in the high density area keep the game data
            // in a separate final track. Otherwise the table of contents track
            // holds everything.
            if (tracks.Count > tocTrack.Number)
            {
                GdiTrack lastTrack = tracks[tracks.Count - 1];
                _gameData = new TrackStream(lastTrack);
                _gameDataStart = lastTrack.Lba * SectorSize;
                Length = _gameDataStart + _gameData.Length;
            }
            else
            {
                Length = _tocStart + _toc.Length;
            }
        }

        public byte[] Read(long offset, long count)
        {
            long available = Math.Max(0, Math.Min(count, Length - offset));
            var buffer = new byte[available];
            Fill(offset, buffer, 0, (int)available);
            return buffer;
        }

        public void CopyTo(Stream destination, long offset, long count)
        {
            long remaining = Math.Max(0, Math.Min(count, Length - offset));
            byte[] buffer = Scratch;

            while (remaining > 0)
            {
                int take = (int)Math.Min(buffer.Length, remaining);
                Fill(offset, buffer, 0, take);
                destination.Write(buffer, 0, take);
                offset += take;
                remaining -= take;
            }
        }

        // Reports whether every sector in the range holds nothing but zeros.
        public bool IsBlank(long lba, long sectorCount)
        {
            long remaining = sectorCount * SectorSize;
            long offset = lba * SectorSize;
            byte[] buffer = Scratch;

            while (remaining > 0)
            {
                int take = (int)Math.Min(buffer.Length, remaining);
                Fill(offset, buffer, 0, take);
                for (int i = 0; i < take; i++)
                {
                    if (buffer[i] != 0)
                        return false;
                }
                offset += take;
                remaining -= take;
            }

            return true;
        }

        void Fill(long offset, byte[] destination, int destinationOffset, int count)
        {
            while (count > 0)
            {
                int written = FillSegment(offset, destination, destinationOffset, count);
                offset += written;
                destinationOffset += written;
                count -= written;
            }
        }

        // Copies as much of the request as a single source can satisfy and
        // reports how many bytes that was.
        int FillSegment(long offset, byte[] destination, int destinationOffset, int count)
        {
            if (offset < AliasLength)
            {
                int take = (int)Math.Min(count, AliasLength - offset);
                return _toc.Read(offset, destination, destinationOffset, take);
            }

            int fromToc = TryReadTrack(_toc, _tocStart, offset, destination, destinationOffset, count);
            if (fromToc > 0)
                return fromToc;

            int fromGameData = TryReadTrack(_gameData, _gameDataStart, offset, destination, destinationOffset, count);
            if (fromGameData > 0)
                return fromGameData;

            // Blank space only runs as far as the next track.
            long nextTrackStart = long.MaxValue;
            if (offset < _tocStart)
                nextTrackStart = Math.Min(nextTrackStart, _tocStart);
            if (_gameData != null && offset < _gameDataStart)
                nextTrackStart = Math.Min(nextTrackStart, _gameDataStart);

            int blank = nextTrackStart == long.MaxValue
                ? count
                : (int)Math.Min(count, nextTrackStart - offset);

            Array.Clear(destination, destinationOffset, blank);
            return blank;
        }

        int TryReadTrack(TrackStream track, long start, long offset, byte[] destination, int destinationOffset, int count)
        {
            if (track == null || offset < start || offset >= start + track.Length)
                return 0;

            int take = (int)Math.Min(count, start + track.Length - offset);
            return track.Read(offset - start, destination, destinationOffset, take);
        }

        public void Dispose()
        {
            _toc?.Dispose();
            _gameData?.Dispose();
        }

        // Reads a track stored at 2048, 2336 or 2352 bytes per sector as if it
        // held only the 2048 bytes of user data.
        sealed class TrackStream : IDisposable
        {
            readonly FileStream _file;
            readonly int _rawSectorSize;
            readonly int _headerSize;

            // Length in user data bytes.
            public long Length { get; }

            public TrackStream(GdiTrack track)
            {
                _rawSectorSize = track.SectorSize;
                _headerSize = _rawSectorSize switch
                {
                    2048 => 0,
                    2336 => 8,
                    2352 => 16,
                    _ => throw new InvalidDataException(
                        $"Track {track.Number} uses an unsupported sector size of {track.SectorSize}."),
                };

                _file = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

                // A dump can stop part way through its final sector. Whatever
                // user data that sector already holds is still readable.
                long wholeSectors = _file.Length / _rawSectorSize;
                long trailing = _file.Length % _rawSectorSize - _headerSize;
                Length = wholeSectors * SectorSize + Math.Clamp(trailing, 0, SectorSize);
            }

            public int Read(long offset, byte[] destination, int destinationOffset, int count)
            {
                int total = 0;

                while (count > 0)
                {
                    long sector = offset / SectorSize;
                    int withinSector = (int)(offset % SectorSize);
                    int take = Math.Min(count, SectorSize - withinSector);

                    _file.Position = sector * _rawSectorSize + _headerSize + withinSector;
                    ReadFully(destination, destinationOffset, take);

                    offset += take;
                    destinationOffset += take;
                    count -= take;
                    total += take;
                }

                return total;
            }

            // A track file shorter than its sector count implies is damaged, so
            // the shortfall reads as zeros.
            void ReadFully(byte[] destination, int destinationOffset, int count)
            {
                int read = 0;
                while (read < count)
                {
                    int received = _file.Read(destination, destinationOffset + read, count - read);
                    if (received == 0)
                    {
                        Array.Clear(destination, destinationOffset + read, count - read);
                        return;
                    }
                    read += received;
                }
            }

            public void Dispose() => _file.Dispose();
        }
    }

    internal static class GdiIndex
    {
        // Reads a .gdi index file and resolves each track path against the
        // folder holding the index.
        public static List<GdiTrack> Parse(string gdiPath)
        {
            string fullPath = Path.GetFullPath(gdiPath);
            string folder = Path.GetDirectoryName(fullPath);

            var rows = new List<string[]>();
            foreach (string line in File.ReadAllLines(fullPath))
            {
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > 0)
                    rows.Add(fields);
            }

            if (rows.Count < 4)
                throw new InvalidDataException("Not a valid GDI: fewer than three tracks.");

            var tracks = new List<GdiTrack>();
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length < 5)
                    throw new InvalidDataException($"Not a valid GDI: malformed track line {i}.");

                tracks.Add(new GdiTrack
                {
                    FilePath = Helper.ResolveActualPath(Path.Combine(folder, row[4])),
                    Number = int.Parse(row[0], CultureInfo.InvariantCulture),
                    Lba = long.Parse(row[1], CultureInfo.InvariantCulture),
                    IsData = row[2] == "4",
                    SectorSize = int.Parse(row[3], CultureInfo.InvariantCulture),
                });
            }

            if (tracks[2].Lba != GdRomImage.TocLba)
                throw new InvalidDataException("Not a valid GDI: track 3 must start at LBA 45000.");

            return tracks;
        }

        public static string Compose(IReadOnlyList<GdiTrack> tracks)
        {
            var text = new StringBuilder();
            text.Append(tracks.Count).Append('\n');
            foreach (GdiTrack track in tracks)
            {
                text.Append(track.Number).Append(' ')
                    .Append(track.Lba).Append(' ')
                    .Append(track.IsData ? 4 : 0).Append(' ')
                    .Append(track.SectorSize).Append(' ')
                    .Append(Path.GetFileName(track.FilePath)).Append(' ')
                    .Append(0).Append('\n');
            }
            return text.ToString();
        }
    }
}
