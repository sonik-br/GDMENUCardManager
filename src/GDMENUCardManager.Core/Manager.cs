using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core.Interface;
using GDMENUCardManager.Core.Resources;

namespace GDMENUCardManager.Core
{
    public sealed class Manager
    {
        public static readonly string[] supportedImageFormats = new string[] { ".gdi", ".cdi", ".mds", ".ccd", ".cue", ".chd" };

        public static string sdPath = null;
        public static bool debugEnabled = false;
        public static MenuKind MenuKindSelected { get; set; } = MenuKind.None;

        private readonly string currentAppPath = AppDomain.CurrentDomain.BaseDirectory;

        private string ipbinPath
        {
            get
            {
                if (MenuKindSelected == MenuKind.None)
                    throw new Exception(AppStrings.MenuNotSelectedSettings);
                return Path.Combine(currentAppPath, "tools", MenuKindSelected.ToString(), "IP.BIN");
            }
        }

        public readonly bool EnableLazyLoading = true;
        public bool EnableGDIShrink;
        public bool EnableGDIShrinkCompressed = true;
        public bool EnableGDIShrinkBlackList = true;
        public bool TruncateMenuGDI = true;


        public ObservableCollection<GdItem> ItemList { get; } = new ObservableCollection<GdItem>();

        public static Manager CreateInstance(IDependencyManager m, string[] compressedFileExtensions)
        {
            Helper.DependencyManager = m;
            Helper.CompressedFileExpression = new Func<string, bool>(x => compressedFileExtensions.Any(y => x.EndsWith(y, StringComparison.InvariantCultureIgnoreCase)));

            return new Manager();
        }

        private Manager()
        {
            //ipbinPath = Path.Combine(currentAppPath, "tools", "IP.BIN");
            PlayStationDB.LoadFrom(Constants.PS1GameDBFile);
        }

        public async Task LoadItemsFromCard()
        {
            ItemList.Clear();
            MenuKindSelected = MenuKind.None;

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

                    if (EnableLazyLoading)//load item without reading ip.bin. only read name.txt+serial.txt. will be null if no name.txt or empty
                        try
                        {
                            itemToAdd = await LazyLoadItemFromCard(item.Item1, item.Item2);
                        }
                        catch { }

                    //not lazyloaded. force full reading
                    if (itemToAdd == null)
                        itemToAdd = await ImageHelper.CreateGdItemAsync(item.Item2);
                    
                    ItemList.Add(itemToAdd);
                }
                catch (Exception ex) { invalid.Add($"{item.Item2} {ex.Message}"); }

            if (invalid.Any())
                throw new Exception(string.Join(Environment.NewLine, invalid));

            var firstItem = ItemList.FirstOrDefault();
            if (firstItem != null)
            {

                ////try to detect using name.txt info
                //MenuKindSelected = getMenuKindFromName(firstItem.Name);

                ////not detected using name.txt. Try to load from ip.bin
                //if (MenuKindSelected == MenuKind.None)
                //{
                //    await LoadIP(firstItem);
                //    MenuKindSelected = getMenuKindFromName(firstItem.Ip.Name);
                //}



                //For menu, try to skip lazy loading and force read
                try
                {
                    await LoadIP(firstItem);
                }
                catch
                {
                }

                if (firstItem.Ip != null) // IP was loaded, use it
                    MenuKindSelected = getMenuKindFromName(firstItem.Ip.Name);
                else //try to detect using name.txt info
                    MenuKindSelected = getMenuKindFromName(firstItem.Name);

            }

            //todo implement menu fallback? to default or forced mode (in config)
            //if (MenuKindSelected == MenuKind.None) { }
        }

        private async ValueTask loadIP(IEnumerable<GdItem> items)
        {
            var query = items.Where(x => x.Ip == null);
            if (!query.Any())
                return;

            var progress = Helper.DependencyManager.CreateAndShowProgressWindow();
            progress.TotalItems = items.Count();
            progress.TextContent = $"{AppStrings.Loading}...";

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

                //if current productnumber is empty, copy over from the now loaded ip.bin
                //should not happen
                //if (string.IsNullOrWhiteSpace(item.ProductNumber) && !string.IsNullOrWhiteSpace(i.ProductNumber))
                //    item.ProductNumber = i.ProductNumber;
            }
            catch (Exception)
            {
                throw new Exception(string.Format(AppStrings.ErrorLoadingFile, filePath));
            }
        }

        public async Task LetterCasingItems(IEnumerable<GdItem> items, LetterCasing casing)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

            foreach (var item in items)
            {
                if (item.SdNumber == 1)
                {
                    if (item.Ip == null)
                        await LoadIP(item);

                    if (item.Ip.Name == "GDMENU" || (item.Ip?.Name == "openMenu"))
                    continue;
                }


                switch (casing)
                {
                    case LetterCasing.Lower:
                        item.Name = textInfo.ToLower(item.Name);
                        break;
                    case LetterCasing.Upper:
                        item.Name = textInfo.ToUpper(item.Name);
                        break;
                    case LetterCasing.Title:
                        item.Name = textInfo.ToTitleCase( textInfo.ToLower( item.Name) );
                        break;
                    default:
                        break;
                }
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
                if (item.SdNumber == 1)
                {
                    if (item.Ip == null)
                        await LoadIP(item);

                    if (item.Ip.Name == "GDMENU" || (item.Ip?.Name == "openMenu"))
                        continue;
                }

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

                    if (item.Ip.Name == "GDMENU" || item.Ip.Name == "openMenu")
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

            //cached "name.txt" file is required.
            if (string.IsNullOrWhiteSpace(itemName))
                return null;

            var itemSerial = string.Empty;
            var serialFile = files.FirstOrDefault(x => Path.GetFileName(x).Equals(Constants.SerialTextFile, StringComparison.OrdinalIgnoreCase));
            if (serialFile != null)
                itemSerial = await Helper.ReadAllTextAsync(serialFile);

            //cached "serial.txt" file is required.
            if (string.IsNullOrWhiteSpace(itemSerial))
                return null;

            itemName = itemName.Trim();
            itemSerial = itemSerial.Trim();

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
                ProductNumber = itemSerial,
                Length = ByteSizeLib.ByteSize.FromBytes(new DirectoryInfo(folderPath).GetFiles().Sum(x => x.Length)),
            };

            item.ImageFiles.Add(Path.GetFileName(itemImageFile));
            
            return item;
        }

        public sealed class MenuNotSelectedException : Exception
        {
            public MenuNotSelectedException() : base(AppStrings.MenuNotSelectedSettings)
            {
            }
        }


        public async Task<bool> Save(string tempFolderRoot)
        {
            string tempDirectory = null;
            var containsCompressedFile = false;

            try
            {
                if (MenuKindSelected == MenuKind.None)
                {
                    throw new MenuNotSelectedException();
                }
                else
                {
                    //todo validate menu files? check if folder exists?
                }

                if (ItemList.Count == 0 || await Helper.DependencyManager.ShowYesNoDialog(AppStrings.Save, string.Format(AppStrings.SaveChangesToDrive, sdPath)) == false)
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
                StringBuilder sb_open = new StringBuilder();

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
                        sb.AppendLine(string.Format(AppStrings.AndMoreFolders, more));

                    if (await Helper.DependencyManager.ShowYesNoDialog(AppStrings.Confirm, $"{AppStrings.TheFollowingFoldersNeedDeleted}\n{AppStrings.ConfirmDeletion}\n\n{sb.ToString()}") == false)
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
                sb_open.AppendLine("[OPENMENU]");
                sb_open.AppendLine($"num_items={ItemList.Count}");
                sb_open.AppendLine();
                sb_open.AppendLine("[ITEMS]");

                var ammountToIncrement = 2;//foundMenuOnSdCard ? 2 : 1
                var folder01 = Path.Combine(sdPath, "01");
                if (await Helper.DirectoryExistsAsync(folder01))
                {
                    try
                    {
                        var ip01 = await ImageHelper.CreateGdItemAsync(folder01);
                        if (ip01 != null && (ip01.Ip.Name == "GDMENU" || ip01.Ip.Name == "openMenu"))
                        {
                            foundMenuOnSdCard = true;
                            ammountToIncrement = 1;

                            //delete sdcard menu folder 01
                            await Helper.DeleteDirectoryAsync(folder01);

                            //if user changed between GDMENU <> openMenu
                            //reload name and serial from ip.bin
                            if ((ip01.Ip.Name == "GDMENU" && MenuKindSelected != MenuKind.gdMenu) || ip01.Ip.Name == "openMenu" && MenuKindSelected != MenuKind.openMenu)
                            {
                                var menu = ItemList.First();//.OrderBy(x => x.SdNumber).First();
                                var menuIpBin = ImageHelper.GetIpData(File.ReadAllBytes(ipbinPath));
                                menu.Name = menuIpBin.Name;
                                menu.ProductNumber = menuIpBin.ProductNumber;
                                menu.Ip = menuIpBin;
                            }
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

                    FillListText(sb, menuIpBin, menuIpBin.ProductNumber, menuIpBin.Name, 1);//insert menu in text list
                    FillListText(sb_open, menuIpBin, menuIpBin.Name, menuIpBin.ProductNumber, 1, true);//insert menu in text list
                }

                for (int i = 0; i < ItemList.Count; i++)
                {
                    FillListText(sb, ItemList[i].Ip, ItemList[i].Name, ItemList[i].ProductNumber, i + ammountToIncrement);
                    FillListText(sb_open, ItemList[i].Ip, ItemList[i].Name, ItemList[i].ProductNumber, i + ammountToIncrement, true);
                }

                //generate iso and save in temp
                await GenerateMenuImageAsync(tempDirectory, sb.ToString(), sb_open.ToString());
                sb.Clear();
                sb_open.Clear();

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

                    //write serial number into folder
                    var itemSerialPath = Path.Combine(item.FullFolderPath, Constants.SerialTextFile);
                    if (!await Helper.FileExistsAsync(itemSerialPath) || (await Helper.ReadAllTextAsync(itemSerialPath)).Trim() != item.ProductNumber)
                        await Helper.WriteTextFileAsync(itemSerialPath, item.ProductNumber.Trim());

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
                    sb_open.AppendLine("[OPENMENU]");
                    sb_open.AppendLine($"num_items={ItemList.Count}");
                    sb_open.AppendLine();
                    sb_open.AppendLine("[ITEMS]");

                    foreach (var item in orderedList)
                    {
                        FillListText(sb, item.Ip, item.Name, item.ProductNumber, item.SdNumber);
                        FillListText(sb_open, item.Ip, item.Name, item.ProductNumber, item.SdNumber, true);
                    }

                    //generate iso and save in temp
                    await GenerateMenuImageAsync(tempDirectory, sb.ToString(), sb_open.ToString(), true);

                    //move to card
                    var menuitem = orderedList.First();

                    if (await Helper.DirectoryExistsAsync(menuitem.FullFolderPath))
                        await Helper.DeleteDirectoryAsync(menuitem.FullFolderPath);

                    //await Helper.MoveDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);
                    await Helper.CopyDirectoryAsync(Path.Combine(tempDirectory, "menu_gdi"), menuitem.FullFolderPath);

                    sb.Clear();
                    sb_open.Clear();
                }

                //update menu item length
                UpdateItemLength(ItemList.OrderBy(x => x.SdNumber).First());

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

                if (debugEnabled)
                {
                    var originFile = Path.Combine(tempDirectory, "MENU_DEBUG.TXT");
                    if (File.Exists(originFile))
                        File.Copy(originFile, Path.Combine(sdPath, "MENU_DEBUG.TXT"), true);
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

        private async Task GenerateMenuImageAsync(string tempDirectory, string listText, string openmenuListText, bool isRebuilding = false)
        {
            //create low density track
            var lowdataPath = Path.Combine(tempDirectory, "lowdensity_data");
            if (!await Helper.DirectoryExistsAsync(lowdataPath))
                await Helper.CreateDirectoryAsync(lowdataPath);

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

            if (MenuKindSelected == MenuKind.gdMenu)
            {
                await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "gdMenu", "menu_data"), dataPath);
                await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "gdMenu", "menu_gdi"), cdiPath);
                /* Copy to low density */
                if (await Helper.DirectoryExistsAsync(Path.Combine(currentAppPath, "tools", "gdMenu", "menu_low_data")))
                    await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "gdMenu", "menu_low_data"), lowdataPath);
                /* Write to low density */
                await Helper.WriteTextFileAsync(Path.Combine(lowdataPath, "LIST.INI"), listText);
                /* Write to high density */
                await Helper.WriteTextFileAsync(Path.Combine(dataPath, "LIST.INI"), listText);
                /*@Debug*/
                if(debugEnabled)
                    await Helper.WriteTextFileAsync(Path.Combine(tempDirectory, "MENU_DEBUG.TXT"), listText);
                //await Helper.WriteTextFileAsync(Path.Combine(currentAppPath, "LIST.INI"), listText);
            }
            else if (MenuKindSelected == MenuKind.openMenu)
            {
                await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "openMenu", "menu_data"), dataPath);
                await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "openMenu", "menu_gdi"), cdiPath);
                /* Copy to low density */
                if (await Helper.DirectoryExistsAsync(Path.Combine(currentAppPath, "tools", "openMenu", "menu_low_data")))
                    await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "openMenu", "menu_low_data"), lowdataPath);
                /* Write to low density */
                await Helper.WriteTextFileAsync(Path.Combine(lowdataPath, "OPENMENU.INI"), openmenuListText);
                /* Write to high density */
                await Helper.WriteTextFileAsync(Path.Combine(dataPath, "OPENMENU.INI"), openmenuListText);
                /*@Debug*/
                if (debugEnabled)
                    await Helper.WriteTextFileAsync(Path.Combine(tempDirectory, "MENU_DEBUG.TXT"), openmenuListText);
                //await Helper.WriteTextFileAsync(Path.Combine(currentAppPath, "OPENMENU.INI"), openmenuListText);
            }
            else
            {
                throw new Exception(AppStrings.MenuNotSelectedSettings);
            }


            //generate menu gdi
            var builder = new DiscUtils.Gdrom.GDromBuilder()
            {
                RawMode = false,
                TruncateData = TruncateMenuGDI,
                VolumeIdentifier = MenuKindSelected == MenuKind.gdMenu ? "GDMENU" : "OPENMENU"
            };
            //builder.ReportProgress += ProgressReport;

            //create low density track
            List<FileInfo> fileList = new List<FileInfo>();
            //add additional files, like themes
            fileList.AddRange(new DirectoryInfo(lowdataPath).GetFiles());

            builder.CreateFirstTrack(Path.Combine(cdiPath, "track01.iso"), fileList);

            var updatetDiscTracks = builder.BuildGDROM(dataPath, ipbinPath, new List<string> { Path.Combine(cdiPath, "track04.raw") }, cdiPath);//todo await
            builder.UpdateGdiFile(updatetDiscTracks, cdiFilePath);

            if (ItemList.First().Ip.Name == "GDMENU" || ItemList.First().Ip.Name == "openMenu")
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

        private void FillListText(StringBuilder sb, IpBin ip, string name, string serial, int number, bool is_openmenu=false)
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
            if(is_openmenu)
            {
                string productid = serial?.Replace("-", "").Split(' ')[0];
                sb.AppendLine($"{strnumber}.product={productid}");
            }
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
                    var (success, message) = await GdiShrinker.Shrink(
                        Path.Combine(item.FullFolderPath, item.ImageFile), newPath);
                    if (!success)
                        throw new Exception($"{AppStrings.ErrorDuringGdiShrink} {item.ImageFile}: {message}");
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
                    x.Work == WorkMode.New && x.Ip.Name != "GDMENU" && x.Ip.Name != "openMenu" && x.CanApplyGDIShrink
                        && (x.FileFormat == FileFormat.Uncompressed || x.FileFormat == FileFormat.Chd || x.FileFormat == FileFormat.RedumpCueBin || EnableGDIShrinkCompressed)
                        && !ignoreShrinkList.Contains(x.Ip.ProductNumber, StringComparer.OrdinalIgnoreCase)
                    ).OrderBy(x => x.Name).ThenBy(x => x.Ip.Disc).ToArray();
                if (shrinkableItems.Any())
                {
                    var result = await Helper.DependencyManager.GdiShrinkWindowShowDialog(shrinkableItems);
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
                                progress.TextContent = $"{AppStrings.Copying}/{AppStrings.Shrinking} {item.Name} ...";
                                shrink = true;
                            }
                            else
                            {
                                progress.TextContent = $"{AppStrings.Copying} {item.Name} ...";
                                shrink = false;
                            }

                            await MoveOrCopyFolder(item, shrink, i + 1);//+ ammountToIncrement
                        }
                        else if (item.FileFormat == FileFormat.RedumpCueBin)
                        {
                            var folderNumber = i + 1;
                            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
                            var originalFolderPath = item.FullFolderPath;

                            // Get the CUE file path
                            if (item.ImageFiles == null || !item.ImageFiles.Any())
                                throw new Exception("Image files list is empty for CUE/BIN item");

                            var cueFile = item.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrEmpty(cueFile))
                                throw new Exception("CUE file not found in image files list");

                            var cuePath = Path.Combine(originalFolderPath, cueFile);

                            // Create target directory
                            if (!await Helper.DirectoryExistsAsync(newPath))
                                await Helper.CreateDirectoryAsync(newPath);

                            // Check if this is GD-ROM or CD-ROM CUE/BIN
                            if (GdiConverter.IsGdRomCue(cuePath))
                            {
                                if (EnableGDIShrink && itemsToShrink.Contains(item))
                                {
                                    // Convert to GDI in temp dir, then shrink to SD card
                                    progress.TextContent = $"Converting/Shrinking {item.Name}...";

                                    var tempCueDir = Path.Combine(tempdir, $"cue_{folderNumber}");
                                    if (!await Helper.DirectoryExistsAsync(tempCueDir))
                                        await Helper.CreateDirectoryAsync(tempCueDir);

                                    var (success, message) = await GdiConverter.ConvertToGdi(cuePath, tempCueDir);
                                    if (!success)
                                        throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                                    var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempCueDir);

                                    (success, message) = await GdiShrinker.Shrink(
                                        Path.Combine(tempCueDir, tempGdiItem.ImageFile), newPath);
                                    if (!success)
                                        throw new Exception($"{AppStrings.ErrorDuringGdiShrink} {tempGdiItem.ImageFile}: {message}");

                                    await Helper.DeleteDirectoryAsync(tempCueDir);

                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = false;
                                }
                                else
                                {
                                    // GD-ROM: Convert to GDI format
                                    progress.TextContent = $"Converting {item.Name} to GDI...";

                                    var (success, message) = await GdiConverter.ConvertToGdi(cuePath, newPath);
                                    if (!success)
                                        throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                                    // Get the converted GDI item info
                                    var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);

                                    item.FullFolderPath = newPath;
                                    item.Work = WorkMode.None;
                                    item.SdNumber = folderNumber;
                                    item.FileFormat = FileFormat.Uncompressed;
                                    item.ImageFiles.Clear();
                                    item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                    item.CanApplyGDIShrink = true;
                                }
                            }
                            else
                            {
                                // CD-ROM: Convert to CDI format
                                progress.TextContent = $"Converting {item.Name} to CDI...";

                                var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
                                var cdiOutputPath = Path.Combine(newPath, cdiOutputName);

                                var (success, message) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, cdiOutputPath));
                                if (!success)
                                    throw new Exception($"Failed to convert {cueFile} to CDI: {message}");

                                item.FullFolderPath = newPath;
                                item.Work = WorkMode.None;
                                item.SdNumber = folderNumber;
                                item.FileFormat = FileFormat.Uncompressed;
                                item.ImageFiles.Clear();
                                item.ImageFiles.Add(cdiOutputName);
                                item.CanApplyGDIShrink = false;
                            }

                            UpdateItemLength(item);
                        }
                        else if (item.FileFormat == FileFormat.CueBinNonGame)
                        {
                            var folderNumber = i + 1;
                            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
                            var originalFolderPath = item.FullFolderPath;

                            // Get the CUE file path
                            if (item.ImageFiles == null || !item.ImageFiles.Any())
                                throw new Exception("Image files list is empty for CUE/BIN item");

                            var cueFile = item.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrEmpty(cueFile))
                                throw new Exception("CUE file not found in image files list");

                            var cuePath = Path.Combine(originalFolderPath, cueFile);

                            // Create target directory
                            if (!await Helper.DirectoryExistsAsync(newPath))
                                await Helper.CreateDirectoryAsync(newPath);

                            // Convert CUE to CCD/IMG/SUB
                            progress.TextContent = $"Converting {item.Name} to CCD...";

                            await Cue2CcdConverter.ConvertAsync(cuePath, newPath);

                            var baseName = Path.GetFileNameWithoutExtension(cueFile);
                            item.FullFolderPath = newPath;
                            item.Work = WorkMode.None;
                            item.SdNumber = folderNumber;
                            item.FileFormat = FileFormat.Uncompressed;
                            item.ImageFiles.Clear();
                            item.ImageFiles.Add(baseName + ".ccd");
                            item.ImageFiles.Add(baseName + ".img");
                            item.ImageFiles.Add(baseName + ".sub");
                            item.CanApplyGDIShrink = false;

                            UpdateItemLength(item);
                        }
                        else if (item.FileFormat == FileFormat.Chd)
                        {
                            var folderNumber = i + 1;
                            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
                            var originalFolderPath = item.FullFolderPath;

                            // Get the CHD file path
                            if (item.ImageFiles == null || !item.ImageFiles.Any())
                                throw new Exception("Image files list is empty for CHD item");

                            var chdFile = item.ImageFiles.FirstOrDefault(f => f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrEmpty(chdFile))
                                throw new Exception("CHD file not found in image files list");

                            var chdPath = Path.Combine(originalFolderPath, chdFile);

                            // Create target directory
                            if (!await Helper.DirectoryExistsAsync(newPath))
                                await Helper.CreateDirectoryAsync(newPath);

                            if (!ChdConverter.IsGdRomChd(chdPath))
                                throw new Exception("This CHD contains a CD-ROM image, which is not supported. Only GD-ROM CHD files are supported. Please use the original CDI or CUE/BIN files instead.");

                            // GD-ROM CHD: Convert to GDI format
                            if (EnableGDIShrink && itemsToShrink.Contains(item))
                            {
                                // Convert to GDI in temp dir, then shrink to SD card
                                progress.TextContent = $"{AppStrings.Uncompressing}/{AppStrings.Shrinking} {item.Name} ...";

                                var tempChdDir = Path.Combine(tempdir, $"chd_{folderNumber}");
                                if (!await Helper.DirectoryExistsAsync(tempChdDir))
                                    await Helper.CreateDirectoryAsync(tempChdDir);

                                var (success, message) = await ChdConverter.ConvertToGdi(chdPath, tempChdDir);
                                if (!success)
                                    throw new Exception($"Failed to convert CHD to GDI: {message}");

                                var tempGdiItem = await ImageHelper.CreateGdItemAsync(tempChdDir);

                                (success, message) = await GdiShrinker.Shrink(
                                    Path.Combine(tempChdDir, tempGdiItem.ImageFile), newPath);
                                if (!success)
                                    throw new Exception($"{AppStrings.ErrorDuringGdiShrink} {tempGdiItem.ImageFile}: {message}");

                                await Helper.DeleteDirectoryAsync(tempChdDir);

                                var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                item.FullFolderPath = newPath;
                                item.Work = WorkMode.None;
                                item.SdNumber = folderNumber;
                                item.FileFormat = FileFormat.Uncompressed;
                                item.ImageFiles.Clear();
                                item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                item.CanApplyGDIShrink = false;
                            }
                            else
                            {
                                // Convert CHD directly to GDI on SD card
                                progress.TextContent = $"{AppStrings.Uncompressing} {item.Name} ...";

                                var (success, message) = await ChdConverter.ConvertToGdi(chdPath, newPath);
                                if (!success)
                                    throw new Exception($"Failed to convert CHD to GDI: {message}");

                                var gdiItem = await ImageHelper.CreateGdItemAsync(newPath);
                                item.FullFolderPath = newPath;
                                item.Work = WorkMode.None;
                                item.SdNumber = folderNumber;
                                item.FileFormat = FileFormat.Uncompressed;
                                item.ImageFiles.Clear();
                                item.ImageFiles.AddRange(gdiItem.ImageFiles);
                                item.CanApplyGDIShrink = true;
                            }

                            UpdateItemLength(item);
                        }
                        else//compressed file
                        {
                            if (EnableGDIShrink && EnableGDIShrinkCompressed && itemsToShrink.Contains(item))
                            {
                                progress.TextContent = $"{AppStrings.Uncompressing} {item.Name} ...";

                                shrink = true;

                                //extract game to temp folder
                                var folderNumber = i + 1;
                                var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

                                var tempExtractDir = Path.Combine(tempdir, $"ext_{folderNumber}");
                                if (!await Helper.DirectoryExistsAsync(tempExtractDir))
                                    await Helper.CreateDirectoryAsync(tempExtractDir);

                                await Task.Run(() => Helper.DependencyManager.ExtractArchive(Path.Combine(item.FullFolderPath, item.ImageFile), tempExtractDir));

                                var gdi = await ImageHelper.CreateGdItemAsync(tempExtractDir);

                                if (gdi.FileFormat == FileFormat.Chd)
                                {
                                    // CHD inside the archive: convert it to GDI in temp, then carry on as a GDI
                                    var chdFile = gdi.ImageFiles.FirstOrDefault(f => f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase));
                                    if (string.IsNullOrEmpty(chdFile))
                                        throw new Exception("CHD file not found after extraction");

                                    var extractedChdPath = Path.Combine(tempExtractDir, chdFile);

                                    var tempChdGdiDir = Path.Combine(tempdir, $"chdgdi_{folderNumber}");
                                    if (!await Helper.DirectoryExistsAsync(tempChdGdiDir))
                                        await Helper.CreateDirectoryAsync(tempChdGdiDir);

                                    var (success, message) = await ChdConverter.ConvertToGdi(extractedChdPath, tempChdGdiDir);
                                    if (!success)
                                        throw new Exception($"Failed to convert CHD to GDI: {message}");

                                    await Helper.DeleteDirectoryAsync(tempExtractDir);
                                    tempExtractDir = tempChdGdiDir;
                                    gdi = await ImageHelper.CreateGdItemAsync(tempExtractDir);
                                }
                                else if (gdi.FileFormat == FileFormat.RedumpCueBin || gdi.FileFormat == FileFormat.CueBinNonGame)
                                {
                                    // CUE/BIN inside the archive: convert it in temp, then carry on with the result
                                    progress.TextContent = $"Converting {item.Name}...";

                                    var tempCueDir = Path.Combine(tempdir, $"cue_{folderNumber}");
                                    var convertedFiles = await ConvertExtractedCueAsync(gdi, tempExtractDir, tempCueDir);

                                    await Helper.DeleteDirectoryAsync(tempExtractDir);
                                    tempExtractDir = tempCueDir;

                                    // Only a GD-ROM, now a GDI, can be shrunk
                                    if (!convertedFiles.First().EndsWith(".gdi", StringComparison.OrdinalIgnoreCase))
                                        shrink = false;

                                    gdi.ImageFiles.Clear();
                                    gdi.ImageFiles.AddRange(convertedFiles);
                                }

                                if (EnableGDIShrinkBlackList)//now with the game uncompressed we can check the blacklist
                                {
                                    if (ignoreShrinkList.Contains(gdi.Ip.ProductNumber, StringComparer.OrdinalIgnoreCase))
                                        shrink = false;
                                }
                                
                                if (shrink)
                                {
                                    progress.TextContent = $"{AppStrings.Shrinking} {item.Name} ...";

                                    var (success, message) = await GdiShrinker.Shrink(
                                        Path.Combine(tempExtractDir, gdi.ImageFile), newPath);
                                    if (!success)
                                        throw new Exception($"{AppStrings.ErrorDuringGdiShrink} {gdi.ImageFile}: {message}");

                                    //get the new filenames
                                    gdi = await ImageHelper.CreateGdItemAsync(newPath);
                                }
                                else
                                {
                                    progress.TextContent = $"{AppStrings.Copying} {item.Name} ...";
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
                                progress.TextContent = $"{AppStrings.Uncompressing} {item.Name} ...";
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
                progress.TextContent = $"{progress.TextContent}\n{AppStrings.Error.ToUpper()}: {ex.Message}";
                throw;
            }
            finally
            {
                do { await Task.Delay(200); } while (progress.IsVisible);

                progress.Close();

                if (progress.ProcessedItems != total)
                    throw new Exception($"{AppStrings.OperationCanceled}\n{AppStrings.ThereMightBeUnusedFoldersFilesSdCard}");
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
            if (ItemList.First().Ip.Name == "GDMENU" || ItemList.First().Ip.Name == "openMenu")
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

            if (gdi.FileFormat == FileFormat.Chd)
            {
                // CHD inside the archive: convert it to GDI on the card and drop the CHD
                var chdFile = gdi.ImageFiles.FirstOrDefault(f => f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(chdFile))
                    throw new Exception("CHD file not found after extraction");

                var extractedChdPath = Path.Combine(newPath, chdFile);

                var (success, message) = await ChdConverter.ConvertToGdi(extractedChdPath, newPath);
                if (!success)
                    throw new Exception($"Failed to convert CHD to GDI: {message}");

                await Helper.DeleteFileAsync(extractedChdPath);
                gdi = await ImageHelper.CreateGdItemAsync(newPath);
            }

            var isCue = gdi.FileFormat == FileFormat.RedumpCueBin || gdi.FileFormat == FileFormat.CueBinNonGame;
            if (isCue)
            {
                // CUE/BIN inside the archive: move the extracted files aside and convert them into the card folder
                var extractedCueDir = newPath + "_cue";
                await Helper.MoveDirectoryAsync(newPath, extractedCueDir);

                var convertedFiles = await ConvertExtractedCueAsync(gdi, extractedCueDir, newPath);
                await Helper.DeleteDirectoryAsync(extractedCueDir);

                gdi.ImageFiles.Clear();
                gdi.ImageFiles.AddRange(convertedFiles);
            }

            //item.ImageFiles[0] = gdi.ImageFile;
            item.ImageFiles.Clear();
            item.ImageFiles.AddRange(gdi.ImageFiles);
            item.Length = gdi.Length;
            item.Ip = gdi.Ip;

            if (isCue)
                UpdateItemLength(item);

            //compressed file by default will have its serial blank.
            //if still blank, read from the now extracted ip info
            if (string.IsNullOrWhiteSpace(item.ProductNumber))
                item.ProductNumber = gdi.ProductNumber;
        }

        // Converts an extracted CUE/BIN: GD-ROM>GDI, CD-ROM>CDI, OTHER>CCD.
        // Returns the image files written to outputDir.
        private static async Task<List<string>> ConvertExtractedCueAsync(GdItem cueItem, string sourceDir, string outputDir)
        {
            var cueFile = cueItem.ImageFiles.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(cueFile))
                throw new Exception("CUE file not found after extraction");

            var cuePath = Path.Combine(sourceDir, cueFile);

            if (!await Helper.DirectoryExistsAsync(outputDir))
                await Helper.CreateDirectoryAsync(outputDir);

            if (cueItem.FileFormat == FileFormat.CueBinNonGame)
            {
                await Cue2CcdConverter.ConvertAsync(cuePath, outputDir);

                var baseName = Path.GetFileNameWithoutExtension(cueFile);
                return new List<string> { baseName + ".ccd", baseName + ".img", baseName + ".sub" };
            }

            if (GdiConverter.IsGdRomCue(cuePath))
            {
                var (success, message) = await GdiConverter.ConvertToGdi(cuePath, outputDir);
                if (!success)
                    throw new Exception($"Failed to convert {cueFile} to GDI: {message}");

                return (await ImageHelper.CreateGdItemAsync(outputDir)).ImageFiles.ToList();
            }

            var cdiOutputName = Redump2CdiConverter.GetCdiOutputName(cuePath);
            var (cdiSuccess, cdiMessage) = await Task.Run(() => Redump2CdiConverter.ConvertToCdi(cuePath, Path.Combine(outputDir, cdiOutputName)));
            if (!cdiSuccess)
                throw new Exception($"Failed to convert {cueFile} to CDI: {cdiMessage}");

            return new List<string> { cdiOutputName };
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
                // Files referenced by an index file in this same selection ride along with it.
                var companions = await ImageHelper.GetCompanionFilePathsAsync(files);

                foreach (var item in files)
                {
                    if (companions.Contains(item))
                        continue;

                    try
                    {
                        ItemList.Add(await ImageHelper.CreateGdItemAsync(item));
                    }
                    catch (UnsupportedDiscFormatException ex)
                    {
                        invalid.Add($"{item} - {ex.Message}");
                    }
                    catch (Exception)
                    {
                        invalid.Add(item);
                    }
                }
            }
            return invalid;
        }

        public bool SearchInItem(GdItem item, string text)
        {
            if (item.Name?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) != -1)
            {
                return true;
            }
            else if (item.Ip != null)
            {
                if (item.Ip.Name?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) != -1)
                    return true;
                //if (item.Ip.ProductNumber?.IndexOf(text, 0, StringComparison.InvariantCultureIgnoreCase) != -1)
                //    return true;
            }

            return false;
        }

        private MenuKind getMenuKindFromName(string name)
        {
            switch (name)
            {
                case "GDMENU": return MenuKind.gdMenu;
                case "openMenu": return MenuKind.openMenu;
                default: return MenuKind.None;
            }
        }

    }

    public sealed class ProgressWindowClosedException : Exception
    {
    }


}
