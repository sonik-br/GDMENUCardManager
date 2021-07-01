using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core.Interface;

namespace GDMENUCardManager.Core
{
    public class Manager
    {
        public static readonly string[] supportedImageFormats = new string[] { ".gdi", ".cdi", ".mds", ".ccd" };

        public static string sdPath = null;

        private readonly string currentAppPath = AppDomain.CurrentDomain.BaseDirectory;

        private readonly string gdishrinkPath;
        private readonly string ipbinPath;

        public readonly bool EnableLazyLoading = true;
        public bool EnableGDIShrink;
        public bool EnableGDIShrinkCompressed = true;
        public bool EnableGDIShrinkBlackList = true;


        public ObservableCollection<GdItem> ItemList { get; } = new ObservableCollection<GdItem>();

        public static Manager CreateInstance(IDependencyManager m, string[] compressedFileExtensions)
        {
            Helper.DependencyManager = m;
            Helper.CompressedFileExpression = new Func<string, bool>(x => compressedFileExtensions.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase)));

            return new Manager();
        }

        private Manager()
        {
            gdishrinkPath = Path.Combine(currentAppPath, "tools", "gdishrink.exe");
            ipbinPath = Path.Combine(currentAppPath, "tools", "IP.BIN");
        }

        public async Task LoadItemsFromCard()
        {
            ItemList.Clear();

            var toAdd = new List<Tuple<int, string>>();
            var rootDirs = await Helper.GetDirectoriesAsync(sdPath);
            foreach (var item in rootDirs)//.OrderBy(x => x))
                if (int.TryParse(Path.GetFileName(item), out int number))
                    toAdd.Add(new Tuple<int, string>(number, item));

            var invalid = new List<string>();

            foreach (var item in toAdd.OrderBy(x => x.Item1))
                try
                {
                    GdItem itemToAdd = null;

                    if (EnableLazyLoading)//load item without reading ip.bin. only read name.txt. will be null if no name.txt or empty
                        try
                        {
                            itemToAdd = await LazyLoadItemFromCard(item.Item1, item.Item2);
                        }
                        catch { }

                    if (itemToAdd == null)
                        itemToAdd = await ImageHelper.CreateGdItemAsync(item.Item2);
                    
                    ItemList.Add(itemToAdd);
                }
                catch (Exception ex) { invalid.Add($"{item.Item2} {ex.Message}"); }

            if (invalid.Any())
                throw new Exception(string.Join(Environment.NewLine, invalid));
        }

        private async ValueTask loadIP(IEnumerable<GdItem> items)
        {
            var query = items.Where(x => x.Ip == null);
            if (!query.Any())
                return;

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = items.Count();
            progress.TextContent = "Loading file info...";

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                foreach (var item in query)
                {
                    await LoadIP(item);
                    progress.ProcessedItems++;
                    if (!progress.IsVisible)//user closed window
                        throw new ProgressWindowClosedException();
                }
                await Task.Delay(100);
            }
            finally
            {
                progress.Close();
            }
        }

        public ValueTask LoadIpAll()
        {
            return loadIP(ItemList);
        }

        public async Task LoadIP(GdItem item)
        {
            //await Task.Delay(2000);
            
            string filePath = string.Empty;
            try
            {
                filePath = Path.Combine(item.FullFolderPath, item.ImageFile);

                var i = await ImageHelper.CreateGdItemAsync(filePath);
                item.Ip = i.Ip;
                item.CanApplyGDIShrink = i.CanApplyGDIShrink;
                item.ImageFiles.Clear();
                item.ImageFiles.AddRange(i.ImageFiles);
            }
            catch (Exception)
            {
                throw new Exception("Error loading file " + filePath);
            }
        }

        public async Task RenameItems(IEnumerable<GdItem> items, RenameBy renameBy)
        {
            if (renameBy == RenameBy.Ip)
                try
                {
                    await loadIP(items);
                }
                catch (ProgressWindowClosedException)
                {
                    return;
                }
                

            string name;

            foreach (var item in items)
            {
                if (renameBy == RenameBy.Ip)
                {
                    name = item.Ip.Name;
                }
                else
                {
                    if (renameBy == RenameBy.Folder)
                        name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
                    else//file
                        name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
                    var m = RegularExpressions.TosecnNameRegexp.Match(name);
                    if (m.Success)
                        name = name.Substring(0, m.Index);
                }
                item.Name = name;
            }
        }

        public async Task<int> BatchRenameItems(bool NotOnCard, bool OnCard, bool FolderName, bool ParseTosec)
        {
            int count = 0;

            foreach (var item in ItemList)
            {
                if (item.SdNumber == 1)
                {
                    if (item.Ip == null)
                        await LoadIP(item);

                    if (item.Ip.Name == "GDMENU")
                        continue;
                }

                if ((item.SdNumber == 0 && NotOnCard) || (item.SdNumber != 0 && OnCard))
                {
                    string name;

                    if (FolderName)
                        name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
                    else//file name
                        name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();

                    if (ParseTosec)
                    {
                        var m = RegularExpressions.TosecnNameRegexp.Match(name);
                        if (m.Success)
                            name = name.Substring(0, m.Index);
                    }

                    item.Name = name;
                    count++;
                }
            }
            return count;
        }


        private async Task<GdItem> LazyLoadItemFromCard(int sdNumber, string folderPath)
        {
            var files = await Helper.GetFilesAsync(folderPath);

            var itemName = string.Empty;
            var nameFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.NameTextFile, StringComparison.OrdinalIgnoreCase));
            if (nameFile != null)
                itemName = await Helper.ReadAllTextAsync(nameFile);

            if (string.IsNullOrWhiteSpace(nameFile))
                return null;

            string itemImageFile = null;

            //is uncompressed?
            foreach (var file in files)
            {
                if (supportedImageFormats.Any(x => x.Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase)))
                {
                    itemImageFile = file;
                    break;
                }
            }

            if (itemImageFile == null)
                throw new Exception("No valid image found on folder");

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed,
                SdNumber = sdNumber,
                Name = itemName,
                Length = ByteSizeLib.ByteSize.FromBytes(new DirectoryInfo(folderPath).GetFiles().Sum(x => x.Length)),
            };

            item.ImageFiles.Add(Path.GetFileName(itemImageFile));
            
            return item;
        }

        public async Task<bool> Save(string tempFolderRoot)
        {
            string tempDirectory = null;
            var containsCompressedFile = false;

            try
            {
                if (ItemList.Count == 0 || await Helper.DependencyManager.ShowYesNoDialog("Save", $"Save changes to {sdPath} drive?") == false)
                    return false;

                //load ipbin from lazy loaded items
                try
                {
                    await LoadIpAll();
                }
                catch (ProgressWindowClosedException)
                {
                    return false;
                }


                containsCompressedFile = ItemList.Any(x => x.FileFormat != FileFormat.Uncompressed);

                StringBuilder sb = new StringBuilder();

                //delete unused folders that are numbers
                List<string> foldersToDelete = new List<string>();
                foreach (var item in await Helper.GetDirectoriesAsync(sdPath))
                    if (int.TryParse(Path.GetFileName(item), out int number))
                        if (number > 0 && !ItemList.Any(x => x.SdNumber == number))
                            foldersToDelete.Add(item);

                if (foldersToDelete.Any())
                {
                    foldersToDelete.Sort();
                    var max = 15;
                    sb.AppendLine(string.Join(Environment.NewLine, foldersToDelete.Take(max)));
                    var more = foldersToDelete.Count - max;
                    if (more > 0)
                        sb.AppendLine($"[and more {more} folders]");

                    if (await Helper.DependencyManager.ShowYesNoDialog("Confirm", $"The following folders need to be deleted.\nConfirm deletion?\n\n{sb.ToString()}") == false)
                        return false;

                    foreach (var item in foldersToDelete)
                        if (Directory.Exists(item))
                            await Helper.DeleteDirectoryAsync(item);
                }
                sb.Clear();


                if (!tempFolderRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    tempFolderRoot += Path.DirectorySeparatorChar.ToString();

                tempDirectory = Path.Combine(tempFolderRoot, Guid.NewGuid().ToString());


                if (!await Helper.DirectoryExistsAsync(tempDirectory))
                    await Helper.CreateDirectoryAsync(tempDirectory);

                bool foundMenuOnSdCard = false;

                sb.AppendLine("[GDMENU]");

                var ammountToIncrement = 2;//foundMenuOnSdCard ? 2 : 1
                var folder01 = Path.Combine(sdPath, "01");
                if (await Helper.DirectoryExistsAsync(folder01))
                {
                    try
                    {
                        var ip01 = await ImageHelper.CreateGdItemAsync(folder01);
                        if (ip01 != null && ip01.Ip.Name == "GDMENU")
                        {
                            foundMenuOnSdCard = true;
                            ammountToIncrement = 1;

                            //delete sdcard menu folder 01
                            await Helper.DeleteDirectoryAsync(folder01);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw;//todo check?

                    }
                }

                if (!foundMenuOnSdCard)//ammountToIncrement == 2
                {
                    var menuIpBin = ImageHelper.GetIpData(File.ReadAllBytes(ipbinPath));

                    //increment sdfolder numbers
                    //foreach (var item in ItemList.Where(x => x.SdNumber > 0).ToArray())
                    //    item.SdNumber++;

                    FillListText(sb, menuIpBin, menuIpBin.Name, 1);//insert menu in text list
                }

                for (int i = 0; i < ItemList.Count; i++)
                    FillListText(sb, ItemList[i].Ip, ItemList[i].Name, i + ammountToIncrement);

                //generate iso and save in temp
                await GenerateMenuImageAsync(tempDirectory, sb.ToString());
                sb.Clear();

                //define what to do with each folder
                for (int i = 0; i < ItemList.Count; i++)
                {
                    int folderNumber = i + 1;// + ammountToIncrement;
                    var item = ItemList[i];
                    //fillListText(sb, item.Ip, folderNumber);

                    if (item.SdNumber == 0)
                        item.Work = WorkMode.New;
                    else if (item.SdNumber != folderNumber)
                        item.Work = WorkMode.Move;
                }

                //set correct folder numbers
                for (int i = 0; i < ItemList.Count; i++)
                    ItemList[i].SdNumber = i + 1;// + ammountToIncrement;

                //rename numbers to guid
                foreach (var item in ItemList.Where(x => x.Work == WorkMode.Move))
                {
                    await Helper.MoveDirectoryAsync(item.FullFolderPath, Path.Combine(sdPath, item.Guid));
                }

                //rename guid to number
                await MoveCardItems();

                //copy new folders
                await CopyNewItems(tempDirectory);


                //finally rename disc images, write name text file
                foreach (var item in ItemList)
                {
                    //rename image file
                    if (Path.GetFileNameWithoutExtension(item.ImageFile) != Constants.DefaultImageFileName)
                    {
                        var originalExt = Path.GetExtension(item.ImageFile).ToLower();

                        if (originalExt == ".gdi")
                        {
                            var newImageFile = Constants.DefaultImageFileName + originalExt;
                            await Helper.MoveFileAsync(Path.Combine(item.FullFolderPath, item.ImageFile), Path.Combine(item.FullFolderPath, newImageFile));
                            item.ImageFiles[0] = newImageFile;
                        }
                        else
                        {
                            for (int i = 0; i < item.ImageFiles.Count; i++)
                            {
                                var oldFileName = item.ImageFiles[i];
                                var newfilename = Constants.DefaultImageFileName + Path.GetExtension(oldFileName);
                                await Helper.MoveFileAsync(Path.Combine(item.FullFolderPath, oldFileName), Path.Combine(item.FullFolderPath, newfilename));
                                item.ImageFiles[i] = newfilename;
                            }
                        }
                    }

                    //write text name into folder
                    var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
                    if (!await Helper.FileExistsAsync(itemNamePath) || (await Helper.ReadAllTextAsync(itemNamePath)).Trim() != item.Name)
                        await Helper.WriteTextFileAsync(itemNamePath, item.Name);

                    //write info text into folder for cdi files
                    //var itemInfoPath = Path.Combine(item.FullFolderPath, infotextfile);
                    //if (item.CdiTarget > 0)
                    //{
                    //    var newTarget = $"target|{item.CdiTarget}";
                    //    if (!await Helper.FileExistsAsync(itemInfoPath) || (await Helper.ReadAllTextAsync(itemInfoPath)).Trim() != newTarget)
                    //        await Helper.WriteTextFileAsync(itemInfoPath, newTarget);
                    //}
                }

                if (containsCompressedFile)
                {
                    //build the menu again

                    var orderedList = ItemList.OrderBy(x => x.SdNumber);

                    sb.AppendLine("[GDMENU]");

                    foreach (var item in orderedList)
                        FillListText(sb, item.Ip, item.Name, item.SdNumber);

                    //generate iso and save in temp
                    await GenerateMenuImageAsync(tempDirectory, sb.ToString(), true);

                    //move to card
                    var menuitem = orderedList.First();

                    if (await Helper.DirectoryExistsAsync(menuitem.FullFolderPath))
                        await Helper.DeleteDirectoryAsync(menuitem.FullFolderPath);

                    //await Helper.MoveDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);
                    await Helper.CopyDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);

                    sb.Clear();
                }

                //write menu config to root of sdcard
                var menuConfigPath = Path.Combine(sdPath, Constants.MenuConfigTextFile);
                if (!await Helper.FileExistsAsync(menuConfigPath))
                {
                    sb.AppendLine("open_time = 150");
                    sb.AppendLine("detect_time = 150");
                    sb.AppendLine("reset_goto = 1");
                    await Helper.WriteTextFileAsync(menuConfigPath, sb.ToString());
                    sb.Clear();
                }
                return true;
            }
            finally
            {
                try
                {
                    if (tempDirectory != null && await Helper.DirectoryExistsAsync(tempDirectory))
                        await Helper.DeleteDirectoryAsync(tempDirectory);
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task GenerateMenuImageAsync(string tempDirectory, string listText, bool isRebuilding = false)
        {
            //create hi density track
            var dataPath = Path.Combine(tempDirectory, "data");
            if (!await Helper.DirectoryExistsAsync(dataPath))
                await Helper.CreateDirectoryAsync(dataPath);

            //var isoPath = Path.Combine(tempDirectory, "iso");
            //if (!await Helper.DirectoryExistsAsync(isoPath))
            //    await Helper.CreateDirectoryAsync(isoPath);

            //var isoFilePath = Path.Combine(isoPath, "menu.iso");
            //var isoFilePath = Path.Combine(isoPath, "menu.iso");

            var cdiPath = Path.Combine(tempDirectory, "menu_gdi");//var destinationFolder = Path.Combine(sdPath, "01");
            if (await Helper.DirectoryExistsAsync(cdiPath))
                await Helper.DeleteDirectoryAsync(cdiPath);

            await Helper.CreateDirectoryAsync(cdiPath);
            var cdiFilePath = Path.Combine(cdiPath, "disc.gdi");

            await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "menu_data"), dataPath);
            await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "menu_gdi"), cdiPath);
            await Helper.WriteTextFileAsync(Path.Combine(tempDirectory, "LIST.INI"), listText);

            //generate menu gdi
            var builder = new DiscUtils.Gdrom.GDromBuilder()
            {
                RawMode = false,
                TruncateData = true,
                VolumeIdentifier = "GDMENU"
            };
            //builder.ReportProgress += ProgressReport;

            //create low density track
            builder.CreateFirstTrack(Path.Combine(cdiPath, "track01.iso"), new FileInfo(Path.Combine(tempDirectory, "LIST.INI")));



            var updatetDiscTracks = builder.BuildGDROM(dataPath, ipbinPath, new List<string> { Path.Combine(cdiPath, "track04.raw") }, cdiPath);//todo await
            builder.UpdateGdiFile(updatetDiscTracks, cdiFilePath);

            if (ItemList.First().Ip.Name == "GDMENU")
            {
                //long start;
                //GetIpData(cdiFilePath, out long fileLength);

                var item = ItemList[0];

                //item.CdiTarget = start;

                if (isRebuilding)
                    return;

                //if user's menu is not in GDI format, update to GDI format.
                if (!Path.GetExtension(item.ImageFile).Equals(".gdi", StringComparison.OrdinalIgnoreCase))
                {
                    item.ImageFiles.Clear();
                    var gdi = await ImageHelper.CreateGdItemAsync(cdiPath);
                    item.ImageFiles.AddRange(gdi.ImageFiles);
                }

                item.FullFolderPath = cdiPath;
                item.ImageFiles[0] = Path.GetFileName(cdiFilePath);
                //item.RenameImageFile(Path.GetFileName(cdiFilePath));

                item.SdNumber = 0;
                item.Work = WorkMode.New;
            }
            else if (!isRebuilding)
            {
                ItemList.Insert(0, await ImageHelper.CreateGdItemAsync(cdiPath));
            }
        }

        private void FillListText(StringBuilder sb, IpBin ip, string name, int number)
        {
            string strnumber = FormatFolderNumber(number);

            sb.AppendLine($"{strnumber}.name={name}");
            if (ip.SpecialDisc == SpecialDisc.CodeBreaker)
                sb.AppendLine($"{strnumber}.disc=");
            else
                sb.AppendLine($"{strnumber}.disc={ip.Disc}");
            sb.AppendLine($"{strnumber}.vga={(ip.Vga ? '1' : '0')}");
            sb.AppendLine($"{strnumber}.region={ip.Region}");
            sb.AppendLine($"{strnumber}.version={ip.Version}");
            sb.AppendLine($"{strnumber}.date={ip.ReleaseDate}");
            sb.AppendLine();
        }

        private string FormatFolderNumber(int number)
        {
            string strnumber;
            if (number < 100)
                strnumber = number.ToString("00");
            else if (number < 1000)
                strnumber = number.ToString("000");
            else if (number < 10000)
                strnumber = number.ToString("0000");
            else
                throw new Exception();
            return strnumber;
        }

        private async Task MoveCardItems()
        {
            for (int i = 0; i < ItemList.Count; i++)
            {
                var item = ItemList[i];
                if (item.Work == WorkMode.Move)
                    await MoveOrCopyFolder(item, false, i + 1);//+ ammountToIncrement
            }
        }

        private async Task MoveOrCopyFolder(GdItem item, bool shrink, int folderNumber)
        {
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
            if (item.Work == WorkMode.Move)
            {
                await Helper.MoveDirectoryAsync(Path.Combine(sdPath, item.Guid), newPath);
            }
            else if (item.Work == WorkMode.New)
            {
                if (shrink)
                {
                    using (var p = CreateProcess(gdishrinkPath))
                        if (!await RunShrinkProcess(p, Path.Combine(item.FullFolderPath, item.ImageFile), newPath))
                            throw new Exception("Error during GDIShrink");
                }
                else
                {
                    //if (item.ImageFile.EndsWith(".gdi", StringComparison.InvariantCultureIgnoreCase))
                    //{
                    //    await Helper.CopyDirectoryAsync(item.FullFolderPath, newPath);
                    //}
                    //else
                    //{
                    //    if (!Directory.Exists(item.FullFolderPath))
                    //        throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + item.FullFolderPath);

                    //    // If the destination directory exist, delete it.
                    //    if (Directory.Exists(newPath))
                    //        await Helper.DeleteDirectoryAsync(newPath);
                    //    //then create a new one
                    //    await Helper.CreateDirectoryAsync(newPath);

                    //    //todo async!
                    //    await Task.Run(() => File.Copy(Path.Combine(item.FullFolderPath, Path.GetFileName(item.ImageFile)), Path.Combine(newPath, Path.GetFileName(item.ImageFile))));
                    //}

                    // If the destination directory exist, delete it.
                    if (Directory.Exists(newPath))
                        await Helper.DeleteDirectoryAsync(newPath);
                    //then create a new one
                    await Helper.CreateDirectoryAsync(newPath);
                    foreach (var f in item.ImageFiles)
                    {
                        //todo async!
                        await Task.Run(() => File.Copy(Path.Combine(item.FullFolderPath, f), Path.Combine(newPath, f)));
                    }


                }
            }

            item.FullFolderPath = newPath;
            item.SdNumber = folderNumber;

            if (item.Work == WorkMode.New && shrink)
            {
                //get the new filenames
                var gdi = await ImageHelper.CreateGdItemAsync(newPath);
                item.ImageFiles.Clear();
                item.ImageFiles.AddRange(gdi.ImageFiles);
                UpdateItemLength(item);
            }
            item.Work = WorkMode.None;
        }

        private Process CreateProcess(string fileName)
        {
            var p = new Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.FileName = fileName;
            return p;
        }

        private async Task CopyNewItems(string tempdir)
        {
            var total = ItemList.Count(x => x.Work == WorkMode.New);
            if (total == 0)
                return;

            //gdishrink
            var itemsToShrink = new List<GdItem>();
            var ignoreShrinkList = new List<string>();
            if (EnableGDIShrink)
            {
                if (EnableGDIShrinkBlackList)
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(Constants.GdiShrinkBlacklistFile))
                        {
                            var split = line.Split(';');
                            if (split.Length > 2 && !string.IsNullOrWhiteSpace(split[1]))
                                ignoreShrinkList.Add(split[1].Trim());
                        }
                    }
                    catch { }
                }

                var shrinkableItems = ItemList.Where(x =>
                    x.Work == WorkMode.New && x.Ip.Name != "GDMENU" && x.CanApplyGDIShrink
                        && (x.FileFormat == FileFormat.Uncompressed || (EnableGDIShrinkCompressed)
                        && !ignoreShrinkList.Contains(x.Ip.ProductNumber, StringComparer.OrdinalIgnoreCase)
                    )).OrderBy(x => x.Name).ThenBy(x => x.Ip.Disc).ToArray();
                if (shrinkableItems.Any())
                {
                    var result = Helper.DependencyManager.GdiShrinkWindowShowDialog(shrinkableItems);
                    if (result != null)
                        itemsToShrink.AddRange(result);
                }
            }

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = total;
            //progress.Show();

            do { await Task.Delay(50); } while (!progress.IsInitialized);

            try
            {
                for (int i = 0; i < ItemList.Count; i++)
                {
                    var item = ItemList[i];
                    if (item.Work == WorkMode.New)
                    {
                        bool shrink;
                        if (item.FileFormat == FileFormat.Uncompressed)
                        {
                            if (EnableGDIShrink && itemsToShrink.Contains(item))
                            {
                                progress.TextContent = $"Copying/Shrinking {item.Name} ...";
                                shrink = true;
                            }
                            else
                            {
                                progress.TextContent = $"Copying {item.Name} ...";
                                shrink = false;
                            }

                            await MoveOrCopyFolder(item, shrink, i + 1);//+ ammountToIncrement
                        }
                        else//compressed file
                        {
                            if (EnableGDIShrink && EnableGDIShrinkCompressed && itemsToShrink.Contains(item))
                            {
                                progress.TextContent = $"Uncompressing {item.Name} ...";

                                shrink = true;

                                //extract game to temp folder
                                var folderNumber = i + 1;
                                var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

                                var tempExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");
                                if (!await Helper.DirectoryExistsAsync(tempExtractDir))
                                    await Helper.CreateDirectoryAsync(tempExtractDir);

                                await Task.Run(() => Helper.DependencyManager.ExtractArchive(Path.Combine(item.FullFolderPath, item.ImageFile), tempExtractDir));

                                var gdi = await ImageHelper.CreateGdItemAsync(tempExtractDir);

                                if (EnableGDIShrinkBlackList)//now with the game uncompressed we can check the blacklist
                                {
                                    if (ignoreShrinkList.Contains(gdi.Ip.ProductNumber, StringComparer.OrdinalIgnoreCase))
                                        shrink = false;
                                }
                                
                                if (shrink)
                                {
                                    progress.TextContent = $"Shrinking {item.Name} ...";

                                    using (var p = CreateProcess(gdishrinkPath))
                                        if (!await RunShrinkProcess(p, Path.Combine(tempExtractDir, gdi.ImageFile), newPath))
                                            throw new Exception("Error during GDIShrink");

                                    //get the new filenames
                                    gdi = await ImageHelper.CreateGdItemAsync(newPath);
                                }
                                else
                                {
                                    progress.TextContent = $"Copying {item.Name} ...";
                                    await Helper.CopyDirectoryAsync(tempExtractDir, newPath);
                                }

                                await Helper.DeleteDirectoryAsync(tempExtractDir);

                                item.FullFolderPath = newPath;
                                item.Work = WorkMode.None;
                                item.SdNumber = folderNumber;


                                item.FileFormat = FileFormat.Uncompressed;

                                //item.ImageFiles[0] = gdi.ImageFile;
                                item.ImageFiles.Clear();
                                item.ImageFiles.AddRange(gdi.ImageFiles);

                                item.Ip = gdi.Ip;

                                UpdateItemLength(item);
                            }
                            else// if not shrinking, can extract directly to card
                            {
                                progress.TextContent = $"Uncompressing {item.Name} ...";
                                await Uncompress(item, i + 1);//+ ammountToIncrement
                            }

                        }


                        progress.ProcessedItems++;

                        //user closed window
                        if (!progress.IsVisible)
                            break;
                    }
                }
                progress.TextContent = "Done!";
                progress.Close();
            }
            catch (Exception ex)
            {
                progress.TextContent = $"{progress.TextContent}\nERROR: {ex.Message}";
                throw;
            }
            finally
            {
                do { await Task.Delay(200); } while (progress.IsVisible);

                progress.Close();

                if (progress.ProcessedItems != total)
                    throw new Exception("Operation canceled.\nThere might be unused folders/files on the SD Card.");
            }
        }

        public async ValueTask SortList()
        {
            if (ItemList.Count == 0)
                return;

            try
            {
                await LoadIpAll();
            }
            catch (ProgressWindowClosedException)
            {
                return;
            }

            var sortedlist = new List<GdItem>(ItemList.Count);
            if (ItemList.First().Ip.Name == "GDMENU")
            {
                sortedlist.Add(ItemList.First());
                ItemList.RemoveAt(0);
            }

            foreach (var item in ItemList.OrderBy(x => x.Name).ThenBy(x => x.Ip.Disc))
                sortedlist.Add(item);

            ItemList.Clear();
            foreach (var item in sortedlist)
                ItemList.Add(item);
        }

        private async Task Uncompress(GdItem item, int folderNumber)
        {
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
            
            if (!await Helper.DirectoryExistsAsync(newPath))
                await Helper.CreateDirectoryAsync(newPath);

            await Task.Run(() => Helper.DependencyManager.ExtractArchive(Path.Combine(item.FullFolderPath, item.ImageFile), newPath));

            item.FullFolderPath = newPath;
            item.Work = WorkMode.None;
            item.SdNumber = folderNumber;

            item.FileFormat = FileFormat.Uncompressed;

            var gdi = await ImageHelper.CreateGdItemAsync(newPath);
            //item.ImageFiles[0] = gdi.ImageFile;
            item.ImageFiles.Clear();
            item.ImageFiles.AddRange(gdi.ImageFiles);
            item.Length = gdi.Length;
            item.Ip = gdi.Ip;
        }

        private async Task<bool> RunShrinkProcess(Process p, string inputFilePath, string outputFolderPath)
        {
            if (!Directory.Exists(outputFolderPath))
                Directory.CreateDirectory(outputFolderPath);

            p.StartInfo.ArgumentList.Clear();
            
            p.StartInfo.ArgumentList.Add(inputFilePath);
            p.StartInfo.ArgumentList.Add(outputFolderPath);

            await RunProcess(p);
            return p.ExitCode == 0;
        }

        private Task RunProcess(Process p)
        {
            //p.StartInfo.RedirectStandardOutput = true;
            //p.StartInfo.RedirectStandardError = true;

            //p.OutputDataReceived += (ss, ee) => { Debug.WriteLine("[OUTPUT] " + ee.Data); };
            //p.ErrorDataReceived += (ss, ee) => { Debug.WriteLine("[ERROR] " + ee.Data); };

            p.Start();

            //p.BeginOutputReadLine();
            //p.BeginErrorReadLine();

            return Task.Run(() => p.WaitForExit());
        }
        //todo implement
        internal static void UpdateItemLength(GdItem item)
        {
            item.Length = ByteSizeLib.ByteSize.FromBytes(item.ImageFiles.Sum(x => new FileInfo(Path.Combine(item.FullFolderPath, x)).Length));
        }

        public async Task<List<string>> AddGames(string[] files)
        {
            var invalid = new List<string>();
            if (files != null)
            {
                foreach (var item in files)
                {
                    try
                    {
                        ItemList.Add(await ImageHelper.CreateGdItemAsync(item));
                    }
                    catch (Exception)
                    {
                        invalid.Add(item);
                    }
                }
            }
            return invalid;
        }

    }

    public class ProgressWindowClosedException : Exception
    {
    }


}
