using System;
using System.Linq;
using System.Text;
using DiscUtils.Iso9660;
using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.Raw;

namespace DiscUtils.Gdrom
{
    public class GDromBuilder
    {
        const int DATA_SECTOR_SIZE = 2048;
        const int RAW_SECTOR_SIZE = 2352;
        const int GD_START_LBA = 45000;
        const int GD_END_LBA = 549150;
        public string VolumeIdentifier { get; set; }
        public string SystemIdentifier { get; set; }
        public string VolumeSetIdentifier { get; set; }
        public string PublisherIdentifier { get; set; }
        public string DataPreparerIdentifier { get; set; }
        public string ApplicationIdentifier { get; set; }
        private int _lastProgress;
        public delegate void OnReportProgress(int percent);
        public OnReportProgress ReportProgress { get; set; }
        public string Track03Path { get; set; }
        public string LastTrackPath { get; set; }
        public bool RawMode { get; set; }
        public bool TruncateData { get; set; }

        public GDromBuilder()
        {
            VolumeIdentifier = "DREAMCAST";
            SystemIdentifier = string.Empty;
            VolumeSetIdentifier = string.Empty;
            PublisherIdentifier = string.Empty;
            DataPreparerIdentifier = string.Empty;
            ApplicationIdentifier = string.Empty;
        }

        public List<DiscTrack> BuildGDROM(string data, string ipbin, List<string> cdda)
        {
            string bootBin;
            byte[] ipbinData = new byte[0x8000];
            CDBuilder builder = new CDBuilder();
            builder.VolumeIdentifier = VolumeIdentifier;
            builder.SystemIdentifier = SystemIdentifier;
            builder.VolumeSetIdentifier = VolumeSetIdentifier;
            builder.PublisherIdentifier = PublisherIdentifier;
            builder.DataPreparerIdentifier = DataPreparerIdentifier;
            builder.ApplicationIdentifier = ApplicationIdentifier;
            builder.UseJoliet = false; //A stupid default, mkisofs won't do this by default.
            builder.LBAoffset = GD_START_LBA;
            builder.EndSector = GD_END_LBA;

            using (FileStream ipfs = new FileStream(ipbin, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (ipfs.Length != ipbinData.Length)
                {
                    throw new Exception("IP.BIN is the wrong size. Possibly the wrong file? Cannot continue.");
                }
                bootBin = GetBootBin(ipfs);
                ipfs.Seek(0, SeekOrigin.Begin);
                ipfs.Read(ipbinData, 0, ipbinData.Length);
            }
            List<DiscTrack> retval = new List<DiscTrack>();
            if (cdda != null && cdda.Count > 0)
            {
                retval = ReadCDDA(cdda);
            }
            DirectoryInfo di = new DirectoryInfo(data);
            PopulateFromFolder(builder, di, di.FullName, bootBin);

            using (BuiltStream isoStream = (BuiltStream)builder.Build())
            {
                _lastProgress = 0;
                if (retval.Count > 0 || (TruncateData && !string.IsNullOrEmpty(LastTrackPath)))
                {
                    if (RawMode)
                    {
                        ExportMultiTrackRaw(isoStream, ipbinData, retval);
                    }
                    else
                    {
                        ExportMultiTrack(isoStream, ipbinData, retval);
                    }
                }
                else
                {
                    if (RawMode)
                    {
                        ExportSingleTrackRaw(isoStream, ipbinData, retval);
                    }
                    else
                    {
                        ExportSingleTrack(isoStream, ipbinData, retval);
                    }
                }
            }
            return retval;
        }

        public List<DiscTrack> BuildGDROM(string data, string ipbin, List<string> cdda, string outDir)
        {
            Track03Path = Path.Combine(outDir, "track03.iso");
            LastTrackPath = Path.Combine(outDir, GetLastTrackName(cdda != null ? cdda.Count : 0));
            return BuildGDROM(data, ipbin, cdda);
        }

        private void ExportSingleTrack(BuiltStream isoStream, byte[] ipbinData, List<DiscTrack> tracks)
        {
            long currentBytes = 0;
            long totalBytes = isoStream.Length;
            int skip = 0;

            DiscTrack track3 = new DiscTrack();
            track3.FileName = Path.GetFileName(Track03Path);
            track3.LBA = GD_START_LBA;
            track3.Type = 4;
            track3.FileSize = (GD_END_LBA - GD_START_LBA) * DATA_SECTOR_SIZE;
            tracks.Add(track3);
            UpdateIPBIN(ipbinData, tracks);
            using (FileStream destStream = new FileStream(Track03Path, FileMode.Create, FileAccess.Write))
            {
                destStream.Write(ipbinData, 0, ipbinData.Length);
                isoStream.Seek(ipbinData.Length, SeekOrigin.Begin);
                currentBytes += ipbinData.Length;

                byte[] buffer = new byte[64 * 1024];
                int numRead = isoStream.Read(buffer, 0, buffer.Length);
                while (numRead != 0)
                {
                    destStream.Write(buffer, 0, numRead);
                    numRead = isoStream.Read(buffer, 0, buffer.Length);
                    currentBytes += numRead;
                    skip++;
                    if (skip >= 10)
                    {
                        skip = 0;
                        int percent = (int)((currentBytes*100) / totalBytes);
                        if (percent > _lastProgress)
                        {
                            _lastProgress = percent;
                            if(ReportProgress != null){
                                ReportProgress(_lastProgress);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Separate raw logic to maintain performance of the 2048 version
        /// </summary>
        private void ExportSingleTrackRaw(BuiltStream isoStream, byte[] ipbinData, List<DiscTrack> tracks)
        {
            long currentBytes = 0;
            long totalBytes = isoStream.Length;
            int skip = 0;

            DiscTrack track3 = new DiscTrack();
            track3.FileName = Path.GetFileName(Track03Path);
            track3.LBA = GD_START_LBA;
            track3.Type = 4;
            track3.FileSize = (GD_END_LBA - GD_START_LBA) * DATA_SECTOR_SIZE;
            tracks.Add(track3);
            UpdateIPBIN(ipbinData, tracks);
            using (FileStream destStream = new FileStream(Track03Path, FileMode.Create, FileAccess.Write))
            {
                int currentLBA = GD_START_LBA;
                byte[] buffer = new byte[DATA_SECTOR_SIZE];
                byte[] resultSector;
                for (int i = 0; i < ipbinData.Length; i += buffer.Length)
                {
                    Array.Copy(ipbinData, i, buffer, 0, buffer.Length);
                    resultSector = SectorConversion.ConvertSectorToRawMode1(buffer, currentLBA++);
                    destStream.Write(resultSector, 0, resultSector.Length);
                    currentBytes += 2048;
                }
                isoStream.Seek(ipbinData.Length, SeekOrigin.Begin);

                int numRead = isoStream.Read(buffer, 0, buffer.Length);
                while (numRead != 0)
                {
                    while (numRead != 0 && numRead < buffer.Length)
                    {
                        //We need all 2048 bytes for a complete sector!
                        int localRead = isoStream.Read(buffer, numRead, buffer.Length - numRead);
                        numRead += localRead;
                        if (localRead == 0)
                        {
                            for (int i = numRead; i < buffer.Length; i++)
                            {
                                buffer[i] = 0;
                            }
                            break; //Prevent infinite loop
                        }
                    }
                    resultSector = SectorConversion.ConvertSectorToRawMode1(buffer, currentLBA++);
                    destStream.Write(resultSector, 0, resultSector.Length);
                    numRead = isoStream.Read(buffer, 0, buffer.Length);
                    currentBytes += numRead;
                    skip++;
                    if (skip >= 10)
                    {
                        skip = 0;
                        int percent = (int)((currentBytes * 100) / totalBytes);
                        if (percent > _lastProgress)
                        {
                            _lastProgress = percent;
                            if (ReportProgress != null)
                            {
                                ReportProgress(_lastProgress);
                            }
                        }
                    }
                }
            }
        }
        
        private void ExportMultiTrack(BuiltStream isoStream, byte[] ipbinData, List<DiscTrack> tracks)
        {
            //There is a 150 sector gap before and after the CDDA
            long lastHeaderEnd = 0;
            long firstFileStart = 0;
            foreach (BuilderExtent extent in isoStream.BuilderExtents)
            {
                if (extent is FileExtent)
                {
                    firstFileStart = extent.Start;
                    break;
                }
                else
                {
                    lastHeaderEnd = extent.Start + RoundUp(extent.Length, DATA_SECTOR_SIZE);
                }
            }
            lastHeaderEnd = lastHeaderEnd / DATA_SECTOR_SIZE;
            firstFileStart = firstFileStart / DATA_SECTOR_SIZE;
            int trackEnd = (int)(firstFileStart - 150);
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                trackEnd = trackEnd - (int)(RoundUp(tracks[i].FileSize, RAW_SECTOR_SIZE) / RAW_SECTOR_SIZE);
                //Track end is now the beginning of this track and the end of the previous
                tracks[i].LBA = (uint)(trackEnd + GD_START_LBA);
            }
            trackEnd = trackEnd - 150;
            if (trackEnd < lastHeaderEnd)
            {
                throw new Exception("Not enough room to fit all of the CDDA after we added the data.");
            }
            if (TruncateData)
            {
                trackEnd = (int)lastHeaderEnd;
            }
            DiscTrack track3 = new DiscTrack();
            track3.FileName = Path.GetFileName(Track03Path);
            track3.LBA = GD_START_LBA;
            track3.Type = 4;
            track3.FileSize = trackEnd * DATA_SECTOR_SIZE;
            tracks.Insert(0, track3);
            DiscTrack lastTrack = new DiscTrack();
            lastTrack.FileName = GetLastTrackName(tracks.Count - 1);
            lastTrack.FileSize = (GD_END_LBA - GD_START_LBA - firstFileStart) * DATA_SECTOR_SIZE;
            lastTrack.LBA = (uint)(GD_START_LBA + firstFileStart);
            lastTrack.Type = 4;
            tracks.Add(lastTrack);
            UpdateIPBIN(ipbinData, tracks);

            long currentBytes = 0;
            long totalBytes = isoStream.Length;
            int skip = 0;

            using (FileStream destStream = new FileStream(Track03Path, FileMode.Create, FileAccess.Write))
            {
                destStream.Write(ipbinData, 0, ipbinData.Length);
                isoStream.Seek(ipbinData.Length, SeekOrigin.Begin);
                long bytesWritten = (long)ipbinData.Length;

                byte[] buffer = new byte[DATA_SECTOR_SIZE];
                int numRead = isoStream.Read(buffer, 0, buffer.Length);
                while (numRead != 0 && bytesWritten < track3.FileSize)
                {
                    destStream.Write(buffer, 0, numRead);
                    numRead = isoStream.Read(buffer, 0, buffer.Length);
                    bytesWritten += numRead;

                    currentBytes += numRead;
                    skip++;
                    if (skip >= 50)
                    {
                        skip = 0;
                        int percent = (int)((currentBytes * 100) / totalBytes);
                        if (percent > _lastProgress)
                        {
                            _lastProgress = percent;
                            if (ReportProgress != null)
                            {
                                ReportProgress(_lastProgress);
                            }
                        }
                    }
                }
            }
            using (FileStream destStream = new FileStream(LastTrackPath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[64 * 1024];
                currentBytes = firstFileStart * DATA_SECTOR_SIZE;
                isoStream.Seek(currentBytes, SeekOrigin.Begin);
                int numRead = isoStream.Read(buffer, 0, buffer.Length);
                while (numRead != 0)
                {
                    destStream.Write(buffer, 0, numRead);
                    numRead = isoStream.Read(buffer, 0, buffer.Length);

                    currentBytes += numRead;
                    skip++;
                    if (skip >= 10)
                    {
                        skip = 0;
                        int percent = (int)((currentBytes * 100) / totalBytes);
                        if (percent > _lastProgress)
                        {
                            _lastProgress = percent;
                            if (ReportProgress != null)
                            {
                                ReportProgress(_lastProgress);
                            }
                        }
                    }
                }
            }
        }

        private void ExportMultiTrackRaw(BuiltStream isoStream, byte[] ipbinData, List<DiscTrack> tracks)
        {
            //There is a 150 sector gap before and after the CDDA
            long lastHeaderEnd = 0;
            long firstFileStart = 0;
            foreach (BuilderExtent extent in isoStream.BuilderExtents)
            {
                if (extent is FileExtent)
                {
                    firstFileStart = extent.Start;
                    break;
                }
                else
                {
                    lastHeaderEnd = extent.Start + RoundUp(extent.Length, DATA_SECTOR_SIZE);
                }
            }
            lastHeaderEnd = lastHeaderEnd / DATA_SECTOR_SIZE;
            firstFileStart = firstFileStart / DATA_SECTOR_SIZE;
            int trackEnd = (int)(firstFileStart - 150);
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                trackEnd = trackEnd - (int)(RoundUp(tracks[i].FileSize, RAW_SECTOR_SIZE) / RAW_SECTOR_SIZE);
                //Track end is now the beginning of this track and the end of the previous
                tracks[i].LBA = (uint)(trackEnd + GD_START_LBA);
            }
            trackEnd = trackEnd - 150;
            if (trackEnd < lastHeaderEnd)
            {
                throw new Exception("Not enough room to fit all of the CDDA after we added the data.");
            }
            if (TruncateData)
            {
                trackEnd = (int)lastHeaderEnd;
            }
            DiscTrack track3 = new DiscTrack();
            track3.FileName = Path.GetFileName(Track03Path);
            track3.LBA = GD_START_LBA;
            track3.Type = 4;
            track3.FileSize = trackEnd * DATA_SECTOR_SIZE;
            tracks.Insert(0, track3);
            DiscTrack lastTrack = new DiscTrack();
            lastTrack.FileName = GetLastTrackName(tracks.Count - 1);
            lastTrack.FileSize = (GD_END_LBA - GD_START_LBA - firstFileStart) * DATA_SECTOR_SIZE;
            lastTrack.LBA = (uint)(GD_START_LBA + firstFileStart);
            lastTrack.Type = 4;
            tracks.Add(lastTrack);
            UpdateIPBIN(ipbinData, tracks);

            long currentBytes = 0;
            long totalBytes = isoStream.Length;
            int skip = 0;
            int currentLBA = GD_START_LBA;

            using (FileStream destStream = new FileStream(Track03Path, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[DATA_SECTOR_SIZE];
                byte[] resultSector;
                for (int i = 0; i < ipbinData.Length; i += buffer.Length)
                {
                    Array.Copy(ipbinData, i, buffer, 0, buffer.Length);
                    resultSector = SectorConversion.ConvertSectorToRawMode1(buffer, currentLBA++);
                    destStream.Write(resultSector, 0, resultSector.Length);
                    currentBytes += 2048;
                }
                isoStream.Seek(ipbinData.Length, SeekOrigin.Begin);
                long bytesWritten = (long)ipbinData.Length;

                int numRead = isoStream.Read(buffer, 0, buffer.Length);
                while (numRead != 0 && bytesWritten < track3.FileSize)
                {
                    while (numRead != 0 && numRead < buffer.Length)
                    {
                        //We need all 2048 bytes for a complete sector!
                        int localRead = isoStream.Read(buffer, numRead, buffer.Length - numRead);
                        numRead += localRead;
                        if (localRead == 0)
                        {
                            for (int i = numRead; i < buffer.Length; i++)
                            {
                                buffer[i] = 0;
                            }
                            break; //Prevent infinite loop
                        }
                    }
                    resultSector = SectorConversion.ConvertSectorToRawMode1(buffer, currentLBA++);
                    destStream.Write(resultSector, 0, resultSector.Length);
                    numRead = isoStream.Read(buffer, 0, buffer.Length);
                    bytesWritten += numRead;
                    currentBytes += numRead;
                    skip++;
                    if (skip >= 50)
                    {
                        skip = 0;
                        int percent = (int)((currentBytes * 100) / totalBytes);
                        if (percent > _lastProgress)
                        {
                            _lastProgress = percent;
                            if (ReportProgress != null)
                            {
                                ReportProgress(_lastProgress);
                            }
                        }
                    }
                }
            }
            currentLBA = (int)lastTrack.LBA;
            using (FileStream destStream = new FileStream(LastTrackPath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[DATA_SECTOR_SIZE];
                byte[] resultSector;
                currentBytes = firstFileStart * DATA_SECTOR_SIZE;
                isoStream.Seek(currentBytes, SeekOrigin.Begin);
                int numRead = isoStream.Read(buffer, 0, buffer.Length);
                while (numRead != 0)
                {
                    while (numRead != 0 && numRead < buffer.Length)
                    {
                        //We need all 2048 bytes for a complete sector!
                        int localRead = isoStream.Read(buffer, numRead, buffer.Length - numRead);
                        numRead += localRead;
                        if (localRead == 0)
                        {
                            for (int i = numRead; i < buffer.Length; i++)
                            {
                                buffer[i] = 0;
                            }
                            break; //Prevent infinite loop
                        }
                    }
                    resultSector = SectorConversion.ConvertSectorToRawMode1(buffer, currentLBA++);
                    destStream.Write(resultSector, 0, resultSector.Length);
                    numRead = isoStream.Read(buffer, 0, buffer.Length);

                    currentBytes += numRead;
                    skip++;
                    if (skip >= 10)
                    {
                        skip = 0;
                        int percent = (int)((currentBytes * 100) / totalBytes);
                        if (percent > _lastProgress)
                        {
                            _lastProgress = percent;
                            if (ReportProgress != null)
                            {
                                ReportProgress(_lastProgress);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateIPBIN(byte[] ipbinData, List<DiscTrack> tracks)
        {
            //Tracks 03 to 99, 1 and 2 were in the low density area
            for (int t = 0; t < 97; t++)
            {
                uint dcLBA = 0xFFFFFF;
                byte dcType = 0xFF;
                if (t < tracks.Count)
                {
                    DiscTrack track = tracks[t];
                    dcLBA = track.LBA + 150;
                    dcType = (byte)((track.Type << 4) | 0x1);
                }
                int offset = 0x104 + (t * 4);
                ipbinData[offset++] = (byte)(dcLBA & 0xFF);
                ipbinData[offset++] = (byte)((dcLBA >> 8) & 0xFF);
                ipbinData[offset++] = (byte)((dcLBA >> 16) & 0xFF);
                ipbinData[offset] = dcType;
            }
        }

        public string CheckOutputExists(List<string> cdda, string output)
        {
            List<string> filesToCheck = new List<string>();
            filesToCheck.Add("track03.iso");
            if (cdda != null && cdda.Count > 0)
            {
                filesToCheck.Add(GetLastTrackName(cdda.Count));
            }
            StringBuilder sb = new StringBuilder();
            int fc = 0;
            foreach (string file in filesToCheck)
            {
                if (File.Exists(Path.Combine(output, file)))
                {
                    sb.Append(file + ", ");
                    fc++;
                }
            }
            if (fc >= 2)
            {
                return "The files " + sb.ToString(0, sb.Length - 2) + " already exist. They will be overwritten. Are you sure?";
            }
            else if (fc == 1)
            {
                return "The file " + sb.ToString(0, sb.Length - 2) + " already exists. It will be overwritten. Are you sure?";
            }
            return null;
        }

        public string GetGDIText(List<DiscTrack> tracks)
        {
            StringBuilder sb = new StringBuilder();
            int tn = 3;
            foreach (DiscTrack track in tracks)
            {
                sb.Append(tn + " " + track.LBA + " " + track.Type + " ");
                if (track.Type == 0 || RawMode)
                {
                    sb.Append("2352 ");
                }
                else
                {
                    sb.Append("2048 ");
                }
                sb.AppendLine(track.FileName + " 0");
                tn++;
            }
            return sb.ToString();
        }

        public void UpdateGdiFile(List<DiscTrack> tracks, string gdiPath)
        {
            StringBuilder sb = new StringBuilder();
            if (File.Exists(gdiPath))
            {
                string[] file = File.ReadAllLines(gdiPath);
                int i = 0;
                sb.AppendLine((tracks.Count + 2).ToString());
                if (file.Length > 0 && file[0].Length <= 3)
                {
                    i++;
                }
                for (; i < file.Length; i++)
                {
                    if (file[i].StartsWith("3"))
                    {
                        break;
                    }
                    else
                    {
                        sb.AppendLine(file[i]);
                    }
                }
            }
            sb.Append(GetGDIText(tracks));
            File.WriteAllText(gdiPath, sb.ToString());
        }

        private string GetLastTrackName(int cddaTracks)
        {
            return "track" + (cddaTracks + 4).ToString("00") + ".iso";
        }

        private List<DiscTrack> ReadCDDA(List<string> paths)
        {
            List<DiscTrack> retval = new List<DiscTrack>();
            foreach (string path in paths)
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    throw new FileNotFoundException("CDDA track " + fi.Name + " could not be accessed.");
                }
                DiscTrack track = new DiscTrack();
                track.FileName = fi.Name;
                track.Type = 0;
                track.FileSize = fi.Length;
                retval.Add(track);
            }
            return retval;
        }

        private string GetBootBin(FileStream ipfs)
        {            
            byte[] name = new byte[16];
            ipfs.Seek(0x60, SeekOrigin.Begin);
            ipfs.Read(name, 0, name.Length);
            return System.Text.Encoding.ASCII.GetString(name).Trim();
        }

        private void PopulateFromFolder(CDBuilder builder, DirectoryInfo di, string basePath, string bootBin)
        {
            FileInfo bootBinFile = null;
            FileInfo[] folderFiles = di.GetFiles();
            //Add directory first, so we can set the creation time correctly.
            string localDirPath = di.FullName.Substring(basePath.Length);
            if (localDirPath.Length > 1)
            {
                //Add directory first, so it has the correct creation time.
                BuildDirectoryInfo dir = builder.AddDirectory(localDirPath);
                dir.CreationTime = di.CreationTimeUtc;
            }
            foreach (FileInfo file in folderFiles)
            {
                string filePath = file.FullName.Substring(basePath.Length);
                if (bootBin != null && file.Name.Equals(bootBin, StringComparison.OrdinalIgnoreCase))
                {
                    bootBinFile = file; //Ignore this for now, we want it last
                }
                else
                {
                    builder.AddFile(filePath, file.FullName);
                }
            }

            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                PopulateFromFolder(builder, dir, basePath, null);
            }

            if (bootBinFile != null && bootBin != null)
            {
                builder.AddFile(bootBin, bootBinFile.FullName);
                long sectorSize = RoundUp(bootBinFile.Length, DATA_SECTOR_SIZE);
                builder.LastFileStartSector = (uint)(GD_END_LBA - 150 - (sectorSize / DATA_SECTOR_SIZE));
            }
            else if (bootBin != null)
            {
                //User doesn't know what they're doing and gave us bad data.
                throw new FileNotFoundException("IP.BIN requires the boot file " + bootBin + 
                    " which was not found in the data directory.");
            }
        }
        
        private long RoundUp(long value, long unit)
        {
            return ((value + (unit - 1)) / unit) * unit;
        }


        public void CreateFirstTrack(string destinationIsoPath, FileInfo listFile)
        {
            CDBuilder builder = new CDBuilder()
            {
                VolumeIdentifier = VolumeIdentifier,
                SystemIdentifier = SystemIdentifier,
                VolumeSetIdentifier = VolumeSetIdentifier,
                PublisherIdentifier = PublisherIdentifier,
                DataPreparerIdentifier = DataPreparerIdentifier,
                ApplicationIdentifier = ApplicationIdentifier,
                UseJoliet = false,
            };
            //builder.EndSector = 512;
            builder.AddFile(listFile.Name, listFile.FullName);
            builder.Build(destinationIsoPath);
        }
    }

    public class DiscTrack
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public uint LBA { get; set; }
        public byte Type { get; set; } //4 is Data, 0 is audio
    }
}
