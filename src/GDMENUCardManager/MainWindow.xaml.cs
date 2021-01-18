using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GongSolutions.Wpf.DragDrop;
using SevenZip;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDropTarget, INotifyPropertyChanged
    {
        private const string katana = "SEGA SEGAKATANA";
        private const string segaenterprises = "SEGA ENTERPRISES";
        private const string gdiregstr = @"\d+ \d+ \d+ \d+ (track\d+.\w+) \d+$";
        private const string tosecnameregstr = @" (V\d\.\d{3}) (\(\d{4}\))";

        private const string nametextfile = "name.txt";
        private const string infotextfile = "info.txt";
        private const string menuconfigtextfile = "GDEMU.ini";
        private const string gdishrinkblacklistfile = "gdishrink_blacklist.txt";

        private const string disc = "disc";

        private static string sdPath = null;

        private static char[] katanachar;
        private static Regex gdiregexp;
        private static Regex tosecnameregexp;

        private readonly string currentAppPath;
        private readonly string cdi4dcPath;
        private readonly string mkisoPath;
        private readonly string gdishrinkPath;
        private readonly string ipbinPath;

        private readonly bool showAllDrives = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<GdItem> ItemList { get; private set; } = new ObservableCollection<GdItem>();

        public ObservableCollection<DriveInfo> DriveList { get; private set; } = new ObservableCollection<DriveInfo>();

        public string Version { get; } = "v1.2.2";


        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            set { _IsBusy = value; RaisePropertyChanged(); }
        }

        private DriveInfo _DriveInfo;
        public DriveInfo SelectedDrive
        {
            get { return _DriveInfo; }
            set
            {
                _DriveInfo = value;
                ItemList.Clear();
                sdPath = value?.RootDirectory.ToString();
                RaisePropertyChanged();
            }
        }

        private string _TempFolder;
        public string TempFolder
        {
            get { return _TempFolder; }
            set { _TempFolder = value; RaisePropertyChanged(); }
        }

        private string _TotalFilesLength;
        public string TotalFilesLength
        {
            get { return _TotalFilesLength; }
            private set { _TotalFilesLength = value; RaisePropertyChanged(); }
        }

        private bool _HaveGDIShrinkBlacklist;
        public bool HaveGDIShrinkBlacklist
        {
            get { return _HaveGDIShrinkBlacklist; }
            set { _HaveGDIShrinkBlacklist = value; RaisePropertyChanged(); }
        }

        private bool _EnableGDIShrink;
        public bool EnableGDIShrink
        {
            get { return _EnableGDIShrink; }
            set { _EnableGDIShrink = value; RaisePropertyChanged(); }
        }

        private bool _EnableGDIShrinkCompressed;
        public bool EnableGDIShrinkCompressed
        {
            get { return _EnableGDIShrinkCompressed; }
            set { _EnableGDIShrinkCompressed = value; RaisePropertyChanged(); }
        }

        private bool _EnableGDIShrinkBlackList = true;
        public bool EnableGDIShrinkBlackList
        {
            get { return _EnableGDIShrinkBlackList; }
            set { _EnableGDIShrinkBlackList = value; RaisePropertyChanged(); }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            this.Loaded += (ss, ee) =>
            {
                HaveGDIShrinkBlacklist = File.Exists(gdishrinkblacklistfile);
                FillDriveList();
            };
            this.Closing += MainWindow_Closing;
            this.PropertyChanged += MainWindow_PropertyChanged;
            ItemList.CollectionChanged += ItemList_CollectionChanged;

            SevenZipExtractor.SetLibraryPath(@"7z.dll");


            //showAllDrives = ;
            bool.TryParse(ConfigurationManager.AppSettings["ShowAllDrives"], out showAllDrives);
            if (bool.TryParse(ConfigurationManager.AppSettings["UseBinaryString"], out bool useBinaryString))
                Converter.ByteSizeToStringConverter.UseBinaryString = useBinaryString;


            TempFolder = Path.GetTempPath();

            currentAppPath = AppDomain.CurrentDomain.BaseDirectory;

            cdi4dcPath = Path.Combine(currentAppPath, "tools", "cdi4dc.exe");
            mkisoPath = Path.Combine(currentAppPath, "tools", "mkisofs.exe");
            gdishrinkPath = Path.Combine(currentAppPath, "tools", "gdishrink.exe");
            ipbinPath = Path.Combine(currentAppPath, "tools", "IP.BIN");

            katanachar = $"{katana} {segaenterprises}".ToCharArray();//Encoding.UTF8.GetBytes(katana);
            gdiregexp = new Regex(gdiregstr, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            tosecnameregexp = new Regex(tosecnameregstr, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        private async void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedDrive) && SelectedDrive != null)
                await LoadItemsFromCard();
        }

        private void ItemList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            updateTotalSize();
        }

        private void updateTotalSize()
        {
            var bsize = ByteSizeLib.ByteSize.FromBytes(ItemList.Sum(x => x.Length.Bytes));
            TotalFilesLength = Converter.ByteSizeToStringConverter.UseBinaryString ? bsize.ToBinaryString() : bsize.ToString();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (IsBusy)
                e.Cancel = true;
            else
                ItemList.CollectionChanged -= ItemList_CollectionChanged;//release events
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task LoadItemsFromCard()
        {
            ItemList.Clear();

            IsBusy = true;

            try
            {
                var toAdd = new List<Tuple<int, string>>();
                var rootDirs = await Helper.GetDirectoriesAsync(sdPath);
                foreach (var item in rootDirs)//.OrderBy(x => x))
                    if (int.TryParse(Path.GetFileName(item), out int number))
                        toAdd.Add(new Tuple<int, string>(number, item));

                foreach (var item in toAdd.OrderBy(x => x.Item1))
                    try
                    {
                        ItemList.Add(await CreateGdItemAsync(item.Item2));
                    }
                    catch (Exception) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task Save()
        {
            IsBusy = true;
            string tempDirectory = null;
            var containsCompressedFile = false;

            try
            {
                if (ItemList.Count == 0 || MessageBox.Show($"Save changes to {SelectedDrive} drive?", "Save", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

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

                    if (MessageBox.Show($"The following folders need to be deleted.\nConfirm deletion?\n\n{sb.ToString()}", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
                        return;

                    foreach (var item in foldersToDelete)
                        await Helper.DeleteDirectoryAsync(item);
                }
                sb.Clear();


                if (!TempFolder.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    TempFolder += Path.DirectorySeparatorChar.ToString();

                tempDirectory = Path.Combine(TempFolder, Guid.NewGuid().ToString());


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
                        var ip01 = await CreateGdItemAsync(folder01);
                        if (ip01 != null && ip01.Ip.Name == "GDMENU")
                        {
                            foundMenuOnSdCard = true;
                            ammountToIncrement = 1;

                            //delete sdcard menu folder 01
                            await Helper.DeleteDirectoryAsync(folder01);
                        }
                    }
                    catch (Exception)
                    {


                    }
                }

                if (!foundMenuOnSdCard)//ammountToIncrement == 2
                {
                    var menuIpBin = GetIpData(ipbinPath, false, out long start, out long fileLength);

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
                    if (Path.GetFileNameWithoutExtension(item.ImageFile) != disc)
                    {
                        var originalExt = Path.GetExtension(item.ImageFile).ToLower();
                        var newImageFile = disc + originalExt;
                        await Helper.MoveFileAsync(Path.Combine(item.FullFolderPath, item.ImageFile), Path.Combine(item.FullFolderPath, disc + originalExt));
                        item.ImageFile = newImageFile;
                    }

                    //write text name into folder
                    var itemNamePath = Path.Combine(item.FullFolderPath, nametextfile);
                    if (!await Helper.FileExistsAsync(itemNamePath) || (await Helper.ReadAllTextAsync(itemNamePath)).Trim() != item.Name)
                        await Helper.WriteTextFileAsync(itemNamePath, item.Name);

                    //write info text into folder for cdi files
                    var itemInfoPath = Path.Combine(item.FullFolderPath, infotextfile);
                    if (item.CdiTarget > 0)
                    {
                        var newTarget = $"target|{item.CdiTarget}";
                        if (!await Helper.FileExistsAsync(itemInfoPath) || (await Helper.ReadAllTextAsync(itemInfoPath)).Trim() != newTarget)
                            await Helper.WriteTextFileAsync(itemInfoPath, newTarget);
                    }
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

                    var destinationPath = Path.Combine(menuitem.FullFolderPath, "disc.cdi");

                    if (await Helper.FileExistsAsync(destinationPath))
                        await Helper.DeleteFileAsync(destinationPath);

                    await Helper.MoveFileAsync(Path.Combine(tempDirectory, "cdi", "disc.cdi"), destinationPath);

                    //todo check if need to change info.txt

                    sb.Clear();
                }

                //write menu config to root of sdcard
                var menuConfigPath = Path.Combine(sdPath, menuconfigtextfile);
                if (!await Helper.FileExistsAsync(menuConfigPath))
                {
                    sb.AppendLine("open_time = 150");
                    sb.AppendLine("detect_time = 150");
                    sb.AppendLine("reset_goto = 1");
                    await Helper.WriteTextFileAsync(menuConfigPath, sb.ToString());
                    sb.Clear();
                }

                MessageBox.Show("Done!", "Message", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                IsBusy = false;
            }
        }

        private async Task GenerateMenuImageAsync(string tempDirectory, string listText, bool isRebuilding = false)
        {
            //create iso
            var dataPath = Path.Combine(tempDirectory, "data");
            if (!await Helper.DirectoryExistsAsync(dataPath))
                await Helper.CreateDirectoryAsync(dataPath);

            var isoPath = Path.Combine(tempDirectory, "iso");
            if (!await Helper.DirectoryExistsAsync(isoPath))
                await Helper.CreateDirectoryAsync(isoPath);

            var isoFilePath = Path.Combine(isoPath, "menu.iso");
            //var isoFilePath = Path.Combine(isoPath, "menu.iso");

            var cdiPath = Path.Combine(tempDirectory, "cdi");//var destinationFolder = Path.Combine(sdPath, "01");
            if (await Helper.DirectoryExistsAsync(cdiPath))
                await Helper.DeleteDirectoryAsync(cdiPath);

            await Helper.CreateDirectoryAsync(cdiPath);
            var cdiFilePath = Path.Combine(cdiPath, "disc.cdi");

            await Helper.CopyDirectoryAsync(Path.Combine(currentAppPath, "tools", "menu"), dataPath);
            await Helper.WriteTextFileAsync(Path.Combine(dataPath, "LIST.INI"), listText);

            using (var p = CreateProcess(mkisoPath))
            {
                await RunMkisoProcess(p, dataPath, isoFilePath);
            }

            //convert iso to cdi
            using (var p = CreateProcess(cdi4dcPath))
            {
                await RunCdiProcess(p, isoFilePath, Path.Combine(cdiPath, cdiFilePath));
            }

            if (ItemList.First().Ip.Name == "GDMENU")
            {
                long start;
                GetIpData(cdiFilePath, true, out start, out long fileLength);

                var item = ItemList[0];

                item.CdiTarget = start;

                if (isRebuilding)
                    return;

                item.FullFolderPath = cdiPath;
                item.ImageFile = cdiFilePath;
                item.SdNumber = 0;
                item.Work = WorkMode.New;
            }
            else if (!isRebuilding)
            {
                ItemList.Insert(0, await CreateGdItemAsync(cdiPath));
            }
        }

        private Process CreateProcess(string fileName)
        {
            var p = new Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.FileName = fileName;
            return p;
        }

        private Task RunMkisoProcess(Process p, string originFolder, string destinyIsoPath)
        {
            p.StartInfo.Arguments = $"-V GDMENU -G \"{ipbinPath}\" -joliet -rock -l -o \"{destinyIsoPath}\" \"{originFolder}\"";
            return RunProcess(p);
        }

        private Task RunCdiProcess(Process p, string inputIsoPath, string outputCdiPath)
        {
            p.StartInfo.Arguments = $"\"{inputIsoPath}\" \"{outputCdiPath}\" -d";
            return RunProcess(p);
        }

        private async Task<bool> RunShrinkProcess(Process p, string inputFilePath, string outputFolderPath)
        {
            if (!Directory.Exists(outputFolderPath))
                Directory.CreateDirectory(outputFolderPath);

            p.StartInfo.Arguments = $"\"{inputFilePath}\" \"{outputFolderPath}\"";

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

        private async Task MoveCardItems()
        {
            for (int i = 0; i < ItemList.Count; i++)
            {
                var item = ItemList[i];
                if (item.Work == WorkMode.Move)
                    await MoveOrCopyFolder(item, false, i + 1);//+ ammountToIncrement
            }
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
                        foreach (var line in File.ReadAllLines(gdishrinkblacklistfile))
                        {
                            var split = line.Split(';');
                            if (split.Length > 2 && !string.IsNullOrWhiteSpace(split[1]))
                                ignoreShrinkList.Add(split[1].Trim());
                        }
                    }
                    catch { }
                }

                var shrinkableItems = ItemList.Where(x =>
                    x.Work == WorkMode.New
                    && (
                        x.FileFormat == FileFormat.SevenZip
                        || (x.ImageFile.EndsWith(".gdi", StringComparison.InvariantCultureIgnoreCase) && !ignoreShrinkList.Contains(x.Ip.ProductNumber, StringComparer.OrdinalIgnoreCase))
                    )).OrderBy(x => x.Name).ThenBy(x => x.Ip.Disc).ToArray();
                if (shrinkableItems.Any())
                {
                    var w = new GdiShrinkWindow(shrinkableItems);
                    w.Owner = this;
                    if (w.ShowDialog().GetValueOrDefault())
                        foreach (var item in w.List.Where(x => x.Value))
                            itemsToShrink.Add(item.Key);
                }
            }


            var progress = new ProgressWindow();
            progress.Owner = this;
            progress.TotalItems = total;
            progress.Show();
            while (!progress.IsLoaded)
                await Task.Delay(50);

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

                                await Task.Run(() =>
                                {
                                    using (var extr = new SevenZipExtractor(Path.Combine(item.FullFolderPath, item.ImageFile)))
                                    {
                                        extr.PreserveDirectoryStructure = false;
                                        extr.ExtractArchive(tempExtractDir);
                                    }
                                });

                                var gdi = await CreateGdItemAsync(tempExtractDir);

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

                                item.ImageFile = gdi.ImageFile;
                                item.Ip = gdi.Ip;

                                await updateItemLength(item);
                            }
                            else
                            {
                                progress.TextContent = $"Uncompressing {item.Name} ...";
                                await Uncompress(item, i + 1);//+ ammountToIncrement
                            }

                        }


                        progress.ProcessedItems++;

                        //user closed window
                        if (!progress.IsLoaded)
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
                while (progress.IsLoaded)
                    await Task.Delay(200);

                progress.Close();

                updateTotalSize();

                if (progress.ProcessedItems != total)
                    throw new Exception("Operation canceled.\nThere might be unused folders/files on the SD Card.");
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
                    if (item.ImageFile.EndsWith(".gdi", StringComparison.InvariantCultureIgnoreCase))
                    {
                        await Helper.CopyDirectoryAsync(item.FullFolderPath, newPath);
                    }
                    else
                    {
                        if (!Directory.Exists(item.FullFolderPath))
                            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + item.FullFolderPath);

                        // If the destination directory exist, delete it.
                        if (Directory.Exists(newPath))
                            await Helper.DeleteDirectoryAsync(newPath);
                        //then create a new one
                        await Helper.CreateDirectoryAsync(newPath);

                        //todo async!
                        await Task.Run(() => File.Copy(Path.Combine(item.FullFolderPath, Path.GetFileName(item.ImageFile)), Path.Combine(newPath, Path.GetFileName(item.ImageFile))));
                    }


                }
            }

            item.FullFolderPath = newPath;
            item.Work = WorkMode.None;
            item.SdNumber = folderNumber;

            if (item.Work == WorkMode.New && shrink)
                await updateItemLength(item);
        }

        private async Task Uncompress(GdItem item, int folderNumber)
        {
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));

            await Task.Run(() =>
            {
                using (var extr = new SevenZipExtractor(Path.Combine(item.FullFolderPath, item.ImageFile)))
                {
                    extr.PreserveDirectoryStructure = false;
                    extr.ExtractArchive(newPath);
                }
            });

            item.FullFolderPath = newPath;
            item.Work = WorkMode.None;
            item.SdNumber = folderNumber;

            item.FileFormat = FileFormat.Uncompressed;

            var gdi = await CreateGdItemAsync(newPath);
            item.ImageFile = gdi.ImageFile;
            item.Ip = gdi.Ip;
        }

        private async Task updateItemLength(GdItem item)
        {
            var gdi = await GetGdiFileListAsync(Path.Combine(item.FullFolderPath, item.ImageFile));

            Dictionary<string, long> fileSizes = new Dictionary<string, long>();

            foreach (var file in gdi)
                if (!fileSizes.ContainsKey(file))
                    fileSizes.Add(file, new FileInfo(Path.Combine(item.FullFolderPath, file)).Length);

            item.Length = ByteSizeLib.ByteSize.FromBytes(fileSizes.Values.Sum());
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

        private void FillListText(StringBuilder sb, IpBin ip, string name, int number)
        {
            string strnumber = FormatFolderNumber(number);

            sb.AppendLine($"{strnumber}.name={name}");
            //fix for codebreaker
            if (ip.ProductNumber == "FCD" && ip.Name == "CodeBreaker for DreamCast" && ip.ReleaseDate == "20000627")
                sb.AppendLine($"{strnumber}.disc=");
            else
                sb.AppendLine($"{strnumber}.disc={ip.Disc}");
            sb.AppendLine($"{strnumber}.vga={ip.Vga}");
            sb.AppendLine($"{strnumber}.region={ip.Region}");
            sb.AppendLine($"{strnumber}.version={ip.Version}");
            sb.AppendLine($"{strnumber}.date={ip.ReleaseDate}");
            sb.AppendLine();
        }

        internal static async Task<GdItem> CreateGdItemAsync(string fileOrFolderPath)
        {
            //todo handle compressed files (zip, 7z, rar) done! needs testing

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

            if (files.Any(x => x.EndsWith(".cdi", StringComparison.InvariantCultureIgnoreCase)))
            {
                item.ImageFile = files.First(x => x.EndsWith(".cdi", StringComparison.InvariantCultureIgnoreCase));
                long start = 0;
                long fileLength = 0;
                ip = await Task.Run(() => GetIpData(item.ImageFile, true, out start, out fileLength));
                item.CdiTarget = start;
                item.Length = ByteSizeLib.ByteSize.FromBytes(fileLength);
            }
            else if (files.Any(x => x.EndsWith(".gdi", StringComparison.InvariantCultureIgnoreCase)))
            {

                item.ImageFile = files.First(x => x.EndsWith(".gdi", StringComparison.InvariantCultureIgnoreCase));

                var gdi = await GetGdiFileListAsync(item.ImageFile);

                Dictionary<string, long> fileSizes = new Dictionary<string, long>();

                foreach (var datafile in gdi.Where(x => !x.EndsWith(".raw", StringComparison.InvariantCultureIgnoreCase)).Skip(1))
                {
                    long fileLength = 0;
                    ip = await Task.Run(() => GetIpData(Path.Combine(item.FullFolderPath, datafile), false, out long start, out fileLength));

                    fileSizes.Add(datafile, fileLength);

                    if (ip != null)
                        break;
                }

                foreach (var file in gdi)
                    if (!fileSizes.ContainsKey(file))
                        fileSizes.Add(file, new FileInfo(Path.Combine(item.FullFolderPath, file)).Length);

                item.Length = ByteSizeLib.ByteSize.FromBytes(fileSizes.Values.Sum());
            }
            else if (files.Any(Helper.CompressedFileExpression))
            {
                using (var compressedfile = new SevenZipExtractor(files.First(Helper.CompressedFileExpression)))
                {
                    var gdiFile = compressedfile.ArchiveFileNames.FirstOrDefault(x => x.EndsWith(".gdi", StringComparison.InvariantCultureIgnoreCase));

                    if (!string.IsNullOrEmpty(gdiFile))
                    {
                        item.ImageFile = Path.GetFileName(compressedfile.FileName);

                        var itemName = Path.GetFileNameWithoutExtension(compressedfile.FileName);
                        var m = tosecnameregexp.Match(itemName);
                        if (m.Success)
                            itemName = itemName.Substring(0, m.Index);

                        ip = new IpBin
                        {
                            Name = itemName,
                            Disc = "?/?"
                        };

                        item.Length = ByteSizeLib.ByteSize.FromBytes(compressedfile.ArchiveFileData.Sum(x => (long)x.Size));
                        item.FileFormat = FileFormat.SevenZip;
                    }
                }
            }

            if (ip == null)
                throw new Exception("Cant't read data from file");


            item.Ip = ip;
            item.Name = ip.Name;

            var itemNamePath = Path.Combine(item.FullFolderPath, nametextfile);
            if (await Helper.FileExistsAsync(itemNamePath))
                item.Name = await Helper.ReadAllTextAsync(itemNamePath);

            if (item.FullFolderPath.StartsWith(sdPath, StringComparison.InvariantCultureIgnoreCase) && int.TryParse(Path.GetFileName(Path.GetDirectoryName(item.ImageFile)), out int number))
                item.SdNumber = number;

            item.ImageFile = Path.GetFileName(item.ImageFile);

            return item;
        }

        private static async Task<string[]> GetGdiFileListAsync(string gdiFilePath)
        {
            var tracks = new List<string>();
            var files = await Task.Run(() => File.ReadAllLines(gdiFilePath));
            foreach (var item in files.Skip(1))
            {
                var m = gdiregexp.Match(item);
                if (m.Success)
                    tracks.Add(m.Groups[1].Value);
            }
            return tracks.ToArray();
        }


        private static IpBin GetIpData(string filepath, bool isCdi, out long start, out long fileLength)
        {
            //http://mc.pp.se/dc/ip0000.bin.html

            using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                fileLength = fs.Length;
                long headerOffset = -1;

                if (isCdi)
                {
                    var infopath = Path.Combine(Path.GetDirectoryName(filepath), infotextfile);
                    if (File.Exists(infopath))
                        long.TryParse(File.ReadAllText(infopath).Split('|').Last(), out headerOffset);
                }

                if (headerOffset == -1)
                    headerOffset = GetHeaderOffset(fs);

                start = headerOffset;

                fs.Seek(headerOffset, SeekOrigin.Begin);

                byte[] buffer = new byte[128];

                if (GetString(buffer, fs, headerOffset, 16) != katana || GetString(buffer, fs, headerOffset + 1, 16) != segaenterprises)
                    return null;

                string crc = GetString(buffer, fs, headerOffset + 32, 4);
                string disc;
                try
                {
                    disc = GetString(buffer, fs, headerOffset + 37, 11).Substring(6);
                }
                catch (Exception)
                {
                    disc = "1/1";//fallback in case of error
                }

                var ip = new IpBin
                {
                    //hardwareid = gettemp(buffer, fs, headerOffset, 16),
                    //makerid = gettemp(buffer, fs, headerOffset + 16, 16),
                    CRC = crc,
                    Disc = disc,
                    Region = GetString(buffer, fs, headerOffset + 47, 8),
                    //peripherals = gettemp(buffer, fs, headerOffset + 55, 7),
                    Vga = GetString(buffer, fs, headerOffset + 61, 1),
                    ProductNumber = GetString(buffer, fs, headerOffset + 64, 10),
                    Version = GetString(buffer, fs, headerOffset + 74, 6),
                    ReleaseDate = GetString(buffer, fs, headerOffset + 80, 16),
                    //Producer = gettemp(buffer, fs, headerOffset + 112, 16),
                    Name = GetString(buffer, fs, headerOffset + 128, 128),
                };
                //remove bad chars
                int index = ip.Name.IndexOf("\0");
                if (index > -1)
                    ip.Name = ip.Name.Substring(0, index);

                index = ip.ProductNumber.IndexOf("\0");
                if (index > -1)
                    ip.ProductNumber = ip.ProductNumber.Substring(0, index);

                return ip;
            }
        }

        private static string GetString(byte[] buffer, Stream fs, long start, int len)
        {
            if (start > fs.Position)
                fs.Seek(start - fs.Position, SeekOrigin.Current);

            fs.Read(buffer, 0, len);
            return Encoding.UTF8.GetString(buffer, 0, len).Trim();
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo == null)
                return;

            DragDropHandler.DragOver(dropInfo);
        }

        async void IDropTarget.Drop(IDropInfo dropInfo)
        {
            if (dropInfo == null)
                return;

            IsBusy = true;
            try
            {
                await DragDropHandler.Drop(dropInfo);
            }
            catch (InvalidDropException ex)
            {
                var w = new TextWindow("Ignored folders/files", ex.Message);
                w.Owner = this;
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                IsBusy = false;
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

        private async void ButtonSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            await Save();
        }

        private void ButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            //new AboutWindow() { Title = $"{this.Title} - by Sonik", Owner = this }.ShowDialog();
            new AboutWindow() { CurrentVersion = Version, Owner = this }.ShowDialog();
            return;
            IsBusy = true;

            var helptext = @"Tool to manage games on SD card
For use with GDEMU/GDMENU

https://github.com/sonik-br/GDMENUCardManager/

Uses mkisofs by E.YOUNGDALE/J.PEARSON/J.SCHILLING,
CDI4DC by [big_fury]SiZiOUS, gdishrink by FamilyGuy,
7z by Igor Pavlov

Works only with GDI and CDI files.
Also Compressed GDI files inside 7Z, RAR or ZIP.

*** To add items to list ***
  Drag/drop gdi/cdi (or compressed gdi) files into ""Games List""
    Only one game per compressed file. Must be on root.
  Drag/drop folders with games into ""Games List""
    Only one game per folder will be added

*** To remove items from list ***
  Select items and press Delete key

*** To change list order ***
  Select items and drag to reorder

*** To sort list alphabetically ***
  Click on ""Sort List""

*** To rename a single item ***
  Double click item name

*** To automatically rename a single item ***
  Right mouse button click on item

*** To commit changes ***
  Click on ""Save Changes""";

            MessageBox.Show(helptext, $"{this.Title} - by Sonik", MessageBoxButton.OK, MessageBoxImage.Information);
            IsBusy = false;
        }

        private void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if ((string)btn.CommandParameter == nameof(TempFolder) && !string.IsNullOrEmpty(TempFolder))
                    dialog.SelectedPath = TempFolder;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TempFolder = dialog.SelectedPath;
            }
        }

        //private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        //{
        //    var grid = sender as DataGridRow;
        //    GdItem model;
        //    if (grid != null && grid.DataContext != null && (model = grid.DataContext as GdItem) != null)
        //    {
        //        IsBusy = true;

        //        var helptext = $"{model.Ip.Name}\n{model.Ip.Version}\n{model.Ip.Disc}";

        //        MessageBox.Show(helptext, "IP.BIN Info", MessageBoxButton.OK, MessageBoxImage.Information);
        //        IsBusy = false;
        //    }
        //}

        private void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var model = (GdItem)btn.CommandParameter;

            IsBusy = true;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Folder:");
            sb.AppendLine(Path.GetFileName(model.FullFolderPath));
            sb.AppendLine();
            sb.AppendLine("File:");
            sb.AppendLine(Path.GetFileName(model.ImageFile));
            sb.AppendLine();
            sb.AppendLine("IP.BIN Info");
            sb.AppendLine(model.Ip.Name);
            sb.AppendLine(model.Ip.Version);
            sb.AppendLine($"DISC {model.Ip.Disc}");
            if (model.Ip.Vga == "1")
                sb.AppendLine("VGA");
            sb.AppendLine();
            sb.AppendLine("CRC: " + model.Ip.CRC);
            sb.AppendLine("Product: " + model.Ip.ProductNumber);

            var helptext = sb.ToString();

            MessageBox.Show(helptext, "File Info", MessageBoxButton.OK, MessageBoxImage.Information);
            IsBusy = false;

#if DEBUG
            Clipboard.SetText($"{model.Ip.CRC};{model.Ip.ProductNumber};{model.Name}");
#endif
        }

        private void ButtonSort_Click(object sender, RoutedEventArgs e)
        {
            if (ItemList.Count == 0)
                return;

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

        private void ButtonNameFromFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                var w = new CopyNameWindow();
                w.Owner = this;

                if (!w.ShowDialog().GetValueOrDefault())
                    return;

                int count = 0;

                foreach (var item in ItemList)
                {
                    if (item.SdNumber == 1 && item.Ip.Name == "GDMENU")
                        continue;

                    if ((item.SdNumber == 0 && w.NotOnCard) || (item.SdNumber != 0 && w.OnCard))
                    {
                        string name;

                        if (w.FolderName)
                            name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
                        else//file name
                            name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();

                        if (w.ParseTosec)
                        {
                            var m = tosecnameregexp.Match(name);
                            if (m.Success)
                                name = name.Substring(0, m.Index);
                        }

                        item.Name = name;
                        count++;
                    }
                }

                MessageBox.Show($"{count} items renamed", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }

        }

        private void ButtonRefreshDrive_Click(object sender, RoutedEventArgs e)
        {
            FillDriveList(true);
        }

        private void FillDriveList(bool isRefreshing = false)
        {
            var list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || x.DriveType == DriveType.Removable)).ToArray();

            if (isRefreshing)
            {
                if (DriveList.Select(x => x.Name).SequenceEqual(list.Select(x => x.Name)))
                    return;

                DriveList.Clear();
            }
            //fill drive list and try to find drive with gdemu contents
            foreach (DriveInfo drive in list)
            {
                DriveList.Add(drive);
                //look for GDEMU.ini file
                if (SelectedDrive == null && File.Exists(Path.Combine(drive.RootDirectory.FullName, menuconfigtextfile)))
                    SelectedDrive = drive;
            }

            //look for 01 folder
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                    if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "01")))
                    {
                        SelectedDrive = drive;
                        break;
                    }
            }


            if (!DriveList.Any())
                return;

            if (SelectedDrive == null)
                SelectedDrive = DriveList.LastOrDefault();
        }

        private void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            dg1.BeginEdit();
        }

        private void MenuItemRenameIP_Click(object sender, RoutedEventArgs e)
        {
            foreach (GdItem item in dg1.SelectedItems)
                rename(item, 0);
        }
        private void MenuItemRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            foreach (GdItem item in dg1.SelectedItems)
                rename(item, 1);
        }
        private void MenuItemRenameFile_Click(object sender, RoutedEventArgs e)
        {
            foreach (GdItem item in dg1.SelectedItems)
                rename(item, 2);
        }

        private void rename(GdItem item, short index)
        {
            string name;

            if (index == 0)//ip.bin
            {
                name = item.Ip.Name;
            }
            else
            {
                if (index == 1)//folder
                    name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
                else//file
                    name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
                var m = tosecnameregexp.Match(name);
                if (m.Success)
                    name = name.Substring(0, m.Index);
            }
            item.Name = name;
        }

        private void rename(object sender, short index)
        {
            var menuItem = (MenuItem)sender;
            var item = (GdItem)menuItem.CommandParameter;

            string name;

            if (index == 0)//ip.bin
            {
                name = item.Ip.Name;
            }
            else
            {
                if (index == 1)//folder
                    name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
                else//file
                    name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
                var m = tosecnameregexp.Match(name);
                if (m.Success)
                    name = name.Substring(0, m.Index);
            }
            item.Name = name;
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && !(e.OriginalSource is TextBox))
            {
                dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
                dg1.BeginEdit();
            }
            else if (e.Key == Key.Delete && !(e.OriginalSource is TextBox))
            {
                var grid = (DataGrid)sender;
                List<GdItem> toRemove = new List<GdItem>();
                foreach (GdItem item in grid.SelectedItems)
                    if (!(item.SdNumber == 1 && item.Ip.Name == "GDMENU"))//dont let the user exclude GDMENU
                        toRemove.Add(item);

                foreach (var item in toRemove)
                    ItemList.Remove(item);

                e.Handled = true;
            }
        }

        private async void ButtonAddGames_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Filter = "Dreamcast Game (*.gdi; *.cdi; *.7z; *.rar; *.zip)|*.gdi;*.cdi;*.7z;*.rar;*.zip";
                //dialog.DefaultExt = ".gdi";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    IsBusy = true;
                    var invalid = new List<string>();

                    foreach (var item in dialog.FileNames)
                    {
                        try
                        {
                            var toInsert = await MainWindow.CreateGdItemAsync(item);
                            ItemList.Add(toInsert);
                        }
                        catch (Exception)
                        {
                            invalid.Add(item);
                        }
                    }

                    if (invalid.Any())
                    {
                        var w = new TextWindow("Ignored folders/files", string.Join(Environment.NewLine, invalid));
                        w.Owner = this;
                        w.ShowDialog();
                    }
                    IsBusy = false;
                }
            }
        }

        private void ButtonRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            while (dg1.SelectedItems.Count > 0)
                ItemList.Remove((GdItem)dg1.SelectedItems[0]);
        }
    }
}
