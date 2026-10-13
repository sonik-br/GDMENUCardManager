using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Rewrites a GD-ROM dump without the blank padding between its directory
    /// structures and its first file, and without the low density area.
    /// </summary>
    /// <remarks>
    /// File data keeps its original LBA, so the filesystem stays valid. The
    /// saving comes from declaring the game data track at a higher LBA instead
    /// of storing the empty sectors that precede it.
    /// </remarks>
    public static class GdiShrinker
    {
        const int SectorSize = GdRomImage.SectorSize;
        const long TocLba = GdRomImage.TocLba;

        // Sectors examined at a time when looking for blank space.
        const int ScanBlockSectors = 512;

        /// <summary>
        /// Writes a shrunk copy of the disc into outputDirectory.
        /// </summary>
        /// <param name="gdiPath">Index file of the disc to shrink.</param>
        /// <param name="outputDirectory">
        /// Must be a different folder to the one holding the source.
        /// </param>
        public static Task<(bool Success, string Message)> Shrink(
            string gdiPath,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            // Task.Run so the file I/O does not block the caller's thread.
            return Task.Run(() => ShrinkImpl(gdiPath, outputDirectory, cancellationToken), cancellationToken);
        }

        static (bool Success, string Message) ShrinkImpl(
            string gdiPath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            try
            {
                string sourcePath = Path.GetFullPath(gdiPath);
                string sourceFolder = Path.GetDirectoryName(sourcePath);
                string targetFolder = Path.GetFullPath(outputDirectory);

                if (string.Equals(sourceFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
                    return (false, "The output folder must differ from the source folder.");

                Directory.CreateDirectory(targetFolder);

                List<GdiTrack> sourceTracks = GdiIndex.Parse(sourcePath);
                long firstBlank;
                long firstFile;
                byte[] expected;

                using (var image = new GdRomImage(sourceTracks))
                {
                    var filesystem = new GdRomFileSystem(image);
                    List<DirectoryRecord> files = filesystem.ReadFiles();
                    if (files.Count == 0)
                        return (false, "The disc has no files to preserve.");

                    long gameDataLba = sourceTracks[sourceTracks.Count - 1].Lba;
                    long firstFileLba = long.MaxValue;
                    DirectoryRecord lastFile = files[0];

                    foreach (DirectoryRecord file in files)
                    {
                        if (file.Lba >= gameDataLba && file.Lba < firstFileLba)
                            firstFileLba = file.Lba;
                        if (file.Lba > lastFile.Lba)
                            lastFile = file;
                    }

                    if (firstFileLba == long.MaxValue)
                        return (false, "No file starts inside the game data track.");

                    (firstBlank, firstFile) = FindWidestBlankRun(
                        image, filesystem.EndOfDirectories, firstFileLba, cancellationToken);

                    expected = image.Read(lastFile.Lba * SectorSize, lastFile.Length);
                }

                List<GdiTrack> outputTracks = BuildOutputTracks(
                    sourceTracks, targetFolder, firstBlank, firstFile);

                cancellationToken.ThrowIfCancellationRequested();

                using (var image = new GdRomImage(sourceTracks))
                {
                    long tocStart = TocLba * SectorSize;
                    using (var stream = File.Create(outputTracks[2].FilePath))
                        image.CopyTo(stream, tocStart, firstBlank * SectorSize - tocStart);

                    using (var stream = File.Create(outputTracks[outputTracks.Count - 1].FilePath))
                        image.CopyTo(stream, firstFile * SectorSize, image.Length - firstFile * SectorSize);
                }

                File.WriteAllBytes(outputTracks[0].FilePath, BuildStubDataTrack());
                File.WriteAllBytes(outputTracks[1].FilePath, new byte[300 * 2352]);

                // Audio tracks in the high density area sit between the table of
                // contents and the game data, so they carry over untouched.
                for (int i = 3; i < sourceTracks.Count - 1; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(outputTracks[i].FilePath))
                        File.Copy(sourceTracks[i].FilePath, outputTracks[i].FilePath);
                }

                string outputGdiPath = Path.Combine(targetFolder, Path.GetFileName(sourcePath));
                File.WriteAllBytes(outputGdiPath, Encoding.ASCII.GetBytes(GdiIndex.Compose(outputTracks)));

                if (!VerifyLastFile(outputGdiPath, expected))
                    return (false, "The shrunk disc did not read back correctly and may be incomplete.");

                return (true, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Names the output tracks and moves the game data track up to the first
        // sector that actually holds a file.
        static List<GdiTrack> BuildOutputTracks(
            List<GdiTrack> sourceTracks, string targetFolder, long firstBlank, long firstFile)
        {
            var tracks = new List<GdiTrack>(sourceTracks.Count + 1);
            foreach (GdiTrack track in sourceTracks)
            {
                GdiTrack copy = track.Clone();
                copy.FilePath = Path.Combine(targetFolder, Path.GetFileName(track.FilePath));
                tracks.Add(copy);
            }

            // The low density track, the table of contents and the game data all
            // become plain 2048 byte sector images.
            foreach (int index in new[] { 0, 2, tracks.Count - 1 })
            {
                tracks[index].SectorSize = SectorSize;
                tracks[index].FilePath = Path.Combine(
                    targetFolder,
                    Path.GetFileName(tracks[index].FilePath).Replace(".bin", ".iso"));
            }

            if (sourceTracks.Count == 3)
            {
                // The single high density track splits in two, keeping the
                // filesystem and the files at their original sectors.
                tracks.Add(new GdiTrack
                {
                    FilePath = Path.Combine(targetFolder, "track04.iso"),
                    Number = 4,
                    Lba = firstFile,
                    SectorSize = SectorSize,
                    IsData = true,
                });
            }
            else
            {
                tracks[tracks.Count - 1].Lba = firstFile;
            }

            return tracks;
        }

        // Finds the longest run of blank sectors between the end of the
        // directory structures and the first file, and returns its edges.
        static (long FirstBlank, long FirstFile) FindWidestBlankRun(
            GdRomImage image, long begin, long end, CancellationToken cancellationToken)
        {
            var occupied = new List<long> { begin };

            for (long sector = begin; sector < end; sector += ScanBlockSectors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long count = Math.Min(ScanBlockSectors, end - sector);
                if (image.IsBlank(sector, count))
                    continue;

                for (long i = 0; i < count; i++)
                {
                    if (!image.IsBlank(sector + i, 1))
                        occupied.Add(sector + i);
                }
            }

            occupied.Add(end);

            long widest = -1;
            long firstBlank = begin;
            long firstFile = end;

            for (int i = 0; i < occupied.Count - 1; i++)
            {
                long gap = occupied[i + 1] - occupied[i];
                if (gap > widest)
                {
                    widest = gap;
                    firstBlank = occupied[i];
                    firstFile = occupied[i + 1];
                }
            }

            return (firstBlank, firstFile);
        }

        // Reads the highest placed file back out of the finished disc to confirm
        // the filesystem still resolves.
        static bool VerifyLastFile(string gdiPath, byte[] expected)
        {
            using var image = new GdRomImage(GdiIndex.Parse(gdiPath));
            var filesystem = new GdRomFileSystem(image);

            DirectoryRecord lastFile = null;
            foreach (DirectoryRecord file in filesystem.ReadFiles())
            {
                if (lastFile == null || file.Lba > lastFile.Lba)
                    lastFile = file;
            }

            if (lastFile == null)
                return false;

            byte[] actual = image.Read(lastFile.Lba * SectorSize, lastFile.Length);
            return actual.AsSpan().SequenceEqual(expected);
        }

        // A minimal ISO9660 volume, held deflated and expanded on use. It
        // replaces the low density area, which the console never reads.
        const string PackedStubDataTrack =
            "eNqruPX29um8yw4iDBc8b7qI7maKNUpyFV8iHO41ZelTn+DpFo6FShP5H27/sekf8xFdRkfvyI8BFu7bju8r/jvJTersCT5GBvzg" +
            "RejlKW17DdaLz/7+8fmy2ctu2gXt33/v+Psu93e7o3eZfvn98IdeSc2ntb8qt1eK+r3v83Per/3r4eGLk5efj7sn8/Z8/5/QzXtW" +
            "3E69O2WzTLl1oZHU0s+rT5vFXft8J+92aV7S1lNPq3Z2rV+92WKOj16SXM702Udzos59trOoC15i0nW9uXxnzNbTR39+XbB23auf" +
            "60LP5RXveqWXLrP65avcvX2vY8zq/5R2m8gsV4rl6U76rqN59w73EvmWakmpt//9lh+2f3x5+j95oCcsYvbetix7XA9ivvlY/2Lf" +
            "emOwv9nfXP9/w3bOR6WaswblDKOAWHAg/7tkxo8DNwGBZqUF";

        static byte[] BuildStubDataTrack() =>
            Inflate(Inflate(Convert.FromBase64String(PackedStubDataTrack)));

        static byte[] Inflate(byte[] input)
        {
            using var source = new MemoryStream(input);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
    }
}
