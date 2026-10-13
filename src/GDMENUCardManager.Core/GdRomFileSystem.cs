using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace GDMENUCardManager.Core
{
    // A single ISO9660 directory record. Only the fields the shrink needs are
    // kept. The rest are stepped over.
    internal sealed class DirectoryRecord
    {
        public const int DirectoryFlag = 0x02;

        public long Lba;
        public long Length;
        public int Flags;

        public bool IsDirectory => (Flags & DirectoryFlag) != 0;
    }

    // Walks the ISO9660 volume in the high density area.
    internal sealed class GdRomFileSystem
    {
        const int SectorSize = GdRomImage.SectorSize;
        const int RootRecordOffset = 156;
        const int MaxVolumeDescriptors = 64;

        readonly GdRomImage _image;
        readonly DirectoryRecord _root;
        readonly byte[] _sector = new byte[SectorSize];

        int _position;
        long _sectorAfterBuffer;

        // The highest sector any directory record was read from, plus one. The
        // directory structures end here, so everything past it is a candidate
        // for removal.
        public long EndOfDirectories { get; private set; }

        public GdRomFileSystem(GdRomImage image)
        {
            _image = image;
            _root = ReadVolumeDescriptors();
        }

        DirectoryRecord ReadVolumeDescriptors()
        {
            long sector = 16;

            for (int i = 0; i < MaxVolumeDescriptors; i++)
            {
                LoadSector(sector);
                sector++;

                int type = ReadByte();
                if (type == 255)
                    break;

                if (type != 1)
                    continue;

                Skip(RootRecordOffset - 1);
                long consumed = 0;
                DirectoryRecord root = ReadRecord(ref consumed);
                if (root == null)
                    throw new InvalidDataException("The volume descriptor has no root directory record.");
                return root;
            }

            throw new InvalidDataException("No primary volume descriptor was found.");
        }

        // Collects every file record on the volume.
        public List<DirectoryRecord> ReadFiles()
        {
            var files = new List<DirectoryRecord>();
            var visited = new HashSet<long> { _root.Lba };
            Walk(_root, files, visited);
            return files;
        }

        void Walk(DirectoryRecord directory, List<DirectoryRecord> files, HashSet<long> visited)
        {
            var children = new List<DirectoryRecord>(ReadChildren(directory));

            foreach (DirectoryRecord child in children)
            {
                if (!child.IsDirectory)
                    files.Add(child);
                else if (visited.Add(child.Lba))
                    Walk(child, files, visited);
            }
        }

        IEnumerable<DirectoryRecord> ReadChildren(DirectoryRecord directory)
        {
            long sector = directory.Lba;
            long consumed = 0;

            LoadSector(sector);

            // The first two entries are the directory's own record and its
            // parent's. The former carries the length of the whole extent.
            DirectoryRecord self = ReadRecord(ref consumed);
            if (self == null)
                yield break;
            ReadRecord(ref consumed);

            while (consumed < self.Length)
            {
                if (consumed % SectorSize == 0)
                {
                    sector++;
                    LoadSector(sector);
                }

                DirectoryRecord record = ReadRecord(ref consumed);
                if (record == null)
                {
                    // A zero length record means the rest of the sector is
                    // padding.
                    long padding = SectorSize - consumed % SectorSize;
                    Skip((int)padding);
                    consumed += padding;
                }
                else
                {
                    yield return record;
                }
            }
        }

        DirectoryRecord ReadRecord(ref long consumed)
        {
            int length = ReadByte();
            NoteDirectorySector();

            if (length == 0)
            {
                consumed += 1;
                return null;
            }

            ReadByte();
            var record = new DirectoryRecord
            {
                Lba = ReadBothEndianUInt32(),
                Length = ReadBothEndianUInt32(),
            };

            Skip(7);
            record.Flags = ReadByte();
            Skip(2);
            Skip(4);

            int nameLength = ReadByte();
            Skip(nameLength);
            if (nameLength % 2 == 0)
                Skip(1);

            int fixedSize = 34 + nameLength - nameLength % 2;
            if (length > fixedSize)
                Skip(length - fixedSize);

            consumed += length;
            return record;
        }

        void NoteDirectorySector()
        {
            if (_sectorAfterBuffer > EndOfDirectories)
                EndOfDirectories = _sectorAfterBuffer;
        }

        void LoadSector(long sector)
        {
            byte[] data = _image.Read(sector * SectorSize, SectorSize);
            Array.Clear(_sector);
            Array.Copy(data, _sector, data.Length);
            _position = 0;
            _sectorAfterBuffer = sector + 1;
        }

        int ReadByte() => _position < _sector.Length ? _sector[_position++] : 0;

        void Skip(int count) => _position += count;

        long ReadBothEndianUInt32()
        {
            uint value = _position + 4 <= _sector.Length
                ? BinaryPrimitives.ReadUInt32LittleEndian(_sector.AsSpan(_position))
                : 0;
            Skip(8);
            return value;
        }
    }
}
