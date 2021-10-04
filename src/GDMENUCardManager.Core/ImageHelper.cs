using Aaru.CommonTypes;
using Aaru.CommonTypes.Interfaces;
using Aaru.Filesystems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    public static class ImageHelper
    {
        private static readonly char[] katanachar = "SEGA SEGAKATANA SEGA ENTERPRISES".ToCharArray();

        public static async Task<GdItem> CreateGdItemAsync(string fileOrFolderPath)
        {
            string folderPath;
            string[] files;

            FileAttributes attr = await Helper.GetAttributesAsync(fileOrFolderPath);//path is a file or folder?
            if (attr.HasFlag(FileAttributes.Directory))
            {
                folderPath = fileOrFolderPath;
                files = await Helper.GetFilesAsync(folderPath);
            }
            else
            {
                folderPath = Path.GetDirectoryName(fileOrFolderPath);
                files = new string[] { fileOrFolderPath };
            }

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed
            };

            IpBin ip = null;
            string itemImageFile = null;

            //is uncompressed?
            foreach (var file in files)
            {
                var fileExt = Path.GetExtension(file).ToLower();
                if (Manager.supportedImageFormats.Any(x => x == fileExt))
                {
                    itemImageFile = file;
                    break;
                }
            }

            //is compressed?
            if (itemImageFile == null && files.Any(Helper.CompressedFileExpression))
            {
                string compressedFile = files.First(Helper.CompressedFileExpression);
                
                var filesInsideArchive = await Task.Run(() => Helper.DependencyManager.GetArchiveFiles(compressedFile));

                foreach (var file in filesInsideArchive.Keys)
                {
                    var fileExt = Path.GetExtension(file).ToLower();
                    if (Manager.supportedImageFormats.Any(x => x == fileExt))
                    {
                        itemImageFile = file;
                        break;
                    }
                }

                item.CanApplyGDIShrink = filesInsideArchive.Keys.Any(x => Path.GetExtension(x).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase));

                if (!string.IsNullOrEmpty(itemImageFile))
                {
                    item.ImageFiles.Add(Path.GetFileName(compressedFile));

                    var itemName = Path.GetFileNameWithoutExtension(compressedFile);
                    var m = RegularExpressions.TosecnNameRegexp.Match(itemName);
                    if (m.Success)
                        itemName = itemName.Substring(0, m.Index);

                    ip = new IpBin
                    {
                        Name = itemName,
                        Disc = "?/?"
                    };

                    item.Length = ByteSizeLib.ByteSize.FromBytes(filesInsideArchive.Sum(x => x.Value));
                    item.FileFormat = FileFormat.SevenZip;
                }
            }

            if (itemImageFile == null)
                throw new Exception("Cant't read data from file");

            if (item.FileFormat == FileFormat.Uncompressed)
            {
                var filtersList = new FiltersList();
                IFilter inputFilter = null;
                try
                {
                    inputFilter = await Task.Run(() => filtersList.GetFilter(itemImageFile));

                    //todo check inputFilter null Cannot open specified file.

                    IOpticalMediaImage opticalImage;

                    switch (Path.GetExtension(itemImageFile).ToLower())
                    {
                        case ".gdi":
                            opticalImage = new Aaru.DiscImages.Gdi();
                            break;
                        case ".cdi":
                            opticalImage = new Aaru.DiscImages.DiscJuggler();
                            break;
                        case ".mds":
                            opticalImage = new Aaru.DiscImages.Alcohol120();
                            break;
                        case ".ccd":
                            opticalImage = new Aaru.DiscImages.CloneCd();
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    //if(!opticalImage.Identify(inputFilter))
                    //    throw new NotSupportedException();

                    //todo check imageFormat null Image format not identified.

                    try
                    {
                        bool useAaru;
                        try
                        {
                            useAaru = await Task.Run(() => opticalImage.Open(inputFilter));
                        }
                        catch (Exception)
                        {
                            useAaru = false;
                            opticalImage?.Close();
                        }


                        if (useAaru) //try to load file using Aaru
                        {
                            try
                            {
                                Partition partition;

                                if (Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase))//first track not audio and skip one
                                {
                                    partition = opticalImage.Partitions.Where(x => x.Type != "Audio").Skip(1).First();
                                    ip = await GetIpData(opticalImage, partition);
                                }
                                else//try to find from last
                                {
                                    for (int i = opticalImage.Partitions.Count - 1; i >= 0; i--)
                                    {
                                        partition = opticalImage.Partitions[i];
                                        ip = await GetIpData(opticalImage, partition);
                                        if (ip != null)
                                            break;
                                    }
                                }

                                //Aaru fails to read the ip.bin from some cdis in CdMode2Formless.
                                if (ip == null)
                                    throw new Exception();

                                //var imageFiles = new List<string> { Path.GetFileName(item.ImageFile) };
                                item.ImageFiles.Add(Path.GetFileName(itemImageFile));
                                foreach (var track in opticalImage.Tracks)
                                {
                                    if (!string.IsNullOrEmpty(track.TrackFile) && !item.ImageFiles.Any(x => x.Equals(track.TrackFile, StringComparison.InvariantCultureIgnoreCase)))
                                        item.ImageFiles.Add(track.TrackFile);
                                    if (!string.IsNullOrEmpty(track.TrackSubchannelFile) && !item.ImageFiles.Any(x => x.Equals(track.TrackSubchannelFile, StringComparison.InvariantCultureIgnoreCase)))
                                        item.ImageFiles.Add(track.TrackSubchannelFile);
                                }

                                item.CanApplyGDIShrink = Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase);

                                Manager.UpdateItemLength(item);
                            }
                            catch
                            {
                                useAaru = false;
                            }
                            finally
                            {
                                opticalImage?.Close();
                            }
                        }


                        if (!useAaru) //if cant open using Aaru, try to parse file manually
                        {
                            if (inputFilter != null && inputFilter.IsOpened())
                                inputFilter.Close();

                            var temp = await CreateGdItem2Async(itemImageFile);

                            if (temp == null || temp.Ip == null)
                                throw new Exception("Unable to open image format");

                            ip = temp.Ip;
                            item = temp;
                        }

                    }
                    finally
                    {
                        opticalImage?.Close();
                    }

                }
                //catch (Exception ex)
                //{

                //    throw;
                //}
                finally
                {
                    if (inputFilter != null && inputFilter.IsOpened())
                        inputFilter.Close();
                }
            }

            if (ip == null)
                throw new Exception("Cant't read data from file");


            item.Ip = ip;
            item.Name = ip.Name;

            var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
            if (await Helper.FileExistsAsync(itemNamePath))
                item.Name = await Helper.ReadAllTextAsync(itemNamePath);

            if (item.FullFolderPath.StartsWith(Manager.sdPath, StringComparison.InvariantCultureIgnoreCase) && int.TryParse(Path.GetFileName(Path.GetDirectoryName(itemImageFile)), out int number))
                item.SdNumber = number;

            //item.ImageFile = Path.GetFileName(item.ImageFile);

            return item;
        }

        private static Task<IpBin> GetIpData(IOpticalMediaImage opticalImage, Partition partition)
        {
            return Task.Run(() => GetIpData(opticalImage.ReadSector(partition.Start)));
        }

        internal static IpBin GetIpData(byte[] ipData)
        {

            var dreamcastip = Aaru.Decoders.Sega.Dreamcast.DecodeIPBin(ipData);
            if (dreamcastip == null)
                return null;

            var ipbin = dreamcastip.Value;

            var special = SpecialDisc.None;
            var releaseDate = GetString(ipbin.release_date);
            var version = GetString(ipbin.product_version);

            string disc;
            if (ipbin.disc_no == 32 || ipbin.disc_total_nos == 32)
            {
                disc = "1/1";
                if (GetString(ipbin.dreamcast_media) == "FCD" && releaseDate == "20000627" && version == "V1.000" && GetString(ipbin.boot_filename) == "PELICAN.BIN")
                    special = SpecialDisc.CodeBreaker;
            }
            else
            {
                disc = $"{(char)ipbin.disc_no}/{(char)ipbin.disc_total_nos}";
            }

            //int iPeripherals = int.Parse(Encoding.ASCII.GetString(ipbin.peripherals), System.Globalization.NumberStyles.HexNumber);

            var ip = new IpBin
            {
                CRC = GetString(ipbin.dreamcast_crc),
                Disc = disc,
                Region = GetString(ipbin.region_codes),
                Vga = ipbin.peripherals[5] == 49,
                ProductNumber = GetString(ipbin.product_no),
                Version = version,
                ReleaseDate = releaseDate,
                Name = GetString(ipbin.product_name),
                SpecialDisc = special
            };

            return ip;
        }

        private static string GetString(byte[] bytearray)
        {
            var str = Encoding.ASCII.GetString(bytearray).Trim();

            //handle null terminated string
            int index = str.IndexOf('\0');
            if (index > -1)
                str = str.Substring(0, index).Trim();
            return str;
        }


        //returns null if file not exists on image. throw on any error
        public static async Task<byte[]> GetGdText(string itemImageFile)
        {
            var filtersList = new FiltersList();
            IFilter inputFilter = null;
            try
            {
                inputFilter = filtersList.GetFilter(itemImageFile);

                //todo check inputFilter null Cannot open specified file.

                IOpticalMediaImage opticalImage;

                switch (Path.GetExtension(itemImageFile).ToLower())
                {
                    case ".gdi":
                        opticalImage = new Aaru.DiscImages.Gdi();
                        break;
                    case ".cdi":
                        opticalImage = new Aaru.DiscImages.DiscJuggler();
                        break;
                    case ".mds":
                        opticalImage = new Aaru.DiscImages.Alcohol120();
                        break;
                    case ".ccd":
                        opticalImage = new Aaru.DiscImages.CloneCd();
                        break;
                    default:
                        throw new NotSupportedException();
                }

                //if(!opticalImage.Identify(inputFilter))
                //    throw new NotSupportedException();

                //todo check imageFormat null Image format not identified.

                try
                {
                    if (! await Task.Run(() => opticalImage.Open(inputFilter)))
                        throw new Exception("Can't load game file");

                    Partition partition;
                    string filename = "0GDTEX.PVR";
                    if (Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase))//first track not audio and skip one
                    {
                        partition = opticalImage.Partitions.Where(x => x.Type != "Audio").Skip(1).First();
                        return await Task.Run(() => extractFileFromPartition(opticalImage, partition, filename));
                    }
                    else//try to find from last
                    {
                        for (int i = opticalImage.Partitions.Count - 1; i >= 0; i--)
                        {
                            partition = opticalImage.Partitions[i];
                            if ((await GetIpData(opticalImage, partition)) != null)
                                return await Task.Run(() => extractFileFromPartition(opticalImage, partition, filename));
                        }
                    }
                    return null;
                }
                finally
                {
                    opticalImage?.Close();
                }
            }
            finally
            {
                if (inputFilter != null && inputFilter.IsOpened())
                    inputFilter.Close();
            }
        }

        private static byte[] extractFileFromPartition(IOpticalMediaImage opticalImage, Partition partition, string fileName)
        {
            var iso = new ISO9660();
            try
            {
                //string information;
                //iso.GetInformation(opticalImage, partition, out information, Encoding.ASCII);

                var dict = new Dictionary<string, string>();
                iso.Mount(opticalImage, partition, Encoding.ASCII, dict, "normal");
                //System.Collections.Generic.List<string> strlist = null;
                //iso.ReadDir("/", out strlist);

                if (iso.Stat(fileName, out var stat) == Aaru.CommonTypes.Structs.Errno.NoError && stat.Length > 0)
                {
                    //file exists
                    var buff = new byte[stat.Length];
                    iso.Read(fileName, 0, stat.Length, ref buff);
                    return buff;
                }
            }
            finally
            {
                iso.Unmount();
            }
            return null;
        }


        #region fallback methods if cant parse using Aaru
        internal static async Task<GdItem> CreateGdItem2Async(string filePath)
        {
            string folderPath = Path.GetDirectoryName(filePath);

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed
            };

            IpBin ip = null;

            var ext = Path.GetExtension(filePath).ToLower();
            string itemImageFile = null;

            item.ImageFiles.Add(Path.GetFileName(filePath));

            if (ext == ".gdi")
            {
                itemImageFile = filePath;

                var gdi = await GetGdiFileListAsync(filePath);

                foreach (var datafile in gdi.Where(x => !x.EndsWith(".raw", StringComparison.InvariantCultureIgnoreCase)).Skip(1))
                {
                    ip = await Task.Run(() => GetIpData(Path.Combine(item.FullFolderPath, datafile)));
                    if (ip != null)
                        break;
                }

                var gdifiles = gdi.Distinct().ToArray();
                item.ImageFiles.AddRange(gdifiles);
            }
            else
            {
                var imageNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                string dataFile;
                if (ext == ".ccd")
                {
                    
                    var img = Path.ChangeExtension(filePath, ".img");
                    if (!File.Exists(img))
                        throw new Exception("Missing file: " + img);
                    item.ImageFiles.Add(Path.GetFileName(img));

                    var sub = Path.ChangeExtension(filePath, ".sub");
                    if (File.Exists(sub))
                        item.ImageFiles.Add(Path.GetFileName(sub));

                    dataFile = img;
                }
                else if (ext == ".mds")
                {
                    var mdf = Path.ChangeExtension(filePath, ".mdf");
                    if (!File.Exists(mdf))
                        throw new Exception("Missing file: " + mdf);
                    item.ImageFiles.Add(Path.GetFileName(mdf));

                    dataFile = mdf;
                }
                else //cdi
                {
                    dataFile = filePath;
                }

                ip = await Task.Run(() => GetIpData(dataFile));
            }


            if (ip == null)
                throw new Exception("Cant't read data from file");


            item.Ip = ip;
            item.Name = ip.Name;

            var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
            if (await Helper.FileExistsAsync(itemNamePath))
                item.Name = await Helper.ReadAllTextAsync(itemNamePath);

            if (item.FullFolderPath.StartsWith(Manager.sdPath, StringComparison.InvariantCultureIgnoreCase) && int.TryParse(new DirectoryInfo(item.FullFolderPath).Name, out int number))
                item.SdNumber = number;

            Manager.UpdateItemLength(item);

            return item;
        }

        private static async Task<string[]> GetGdiFileListAsync(string gdiFilePath)
        {
            var tracks = new List<string>();
            
            var files = await File.ReadAllLinesAsync(gdiFilePath);
            foreach (var item in files.Skip(1))
            {
                var m = RegularExpressions.GdiRegexp.Match(item);
                if (m.Success)
                    tracks.Add(m.Groups[1].Value);
            }
            return tracks.ToArray();
        }

        private static IpBin GetIpData(string filepath)
        {
            using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                long headerOffset = GetHeaderOffset(fs);

                fs.Seek(headerOffset, SeekOrigin.Begin);

                byte[] buffer = new byte[512];
                fs.Read(buffer, 0, buffer.Length);
                return GetIpData(buffer);
            }
        }

        private static long GetHeaderOffset(Stream stream)
        {
            /// based on https://keestalkstech.com/2010/11/seek-position-of-a-string-in-a-file-or-filestream/

            char[] search = katanachar;
            long result = -1, position = 0, stored = -1,
            begin = stream.Position;
            int c;

            //read byte by byte
            while ((c = stream.ReadByte()) != -1)
            {
                //check if data in array matches
                if ((char)c == search[position])
                {
                    //if charater matches first character of 
                    //seek string, store it for later
                    if (stored == -1 && position > 0 && (char)c == search[0])
                    {
                        stored = stream.Position;
                    }

                    //check if we're done
                    if (position + 1 == search.Length)
                    {
                        //correct position for array lenth
                        result = stream.Position - search.Length;
                        //set position in stream
                        stream.Position = result;
                        break;
                    }

                    //advance position in the array
                    position++;
                }
                //no match, check if we have a stored position
                else if (stored > -1)
                {
                    //go to stored position + 1
                    stream.Position = stored + 1;
                    position = 1;
                    stored = -1; //reset stored position!
                }
                //no match, no stored position, reset array
                //position and continue reading
                else
                {
                    position = 0;
                }
            }

            //reset stream position if no match has been found
            if (result == -1)
            {
                stream.Position = begin;
            }

            return result;
        }
        #endregion
    }
}
