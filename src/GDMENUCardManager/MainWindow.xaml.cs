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

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDropTarget, INotifyPropertyChanged
    {
        private const string katana = "SEGA SEGAKATANA";
        private const string gdiregstr = @"\d+ \d+ \d+ \d+ (track\d+.\w+) \d+$";
        private const string tosecnameregstr = @" (V\d\.\d{3}) (\(\d{4}\))";

        private const string nametextfile = "name.txt";
        private const string infotextfile = "info.txt";
        private const string menuconfigtextfile = "GDEMU.ini";

        private const string disc = "disc";

        private static string sdPath = null;

        private static char[] katanachar;
        private static Regex gdiregexp;
        private static Regex tosecnameregexp;

        private readonly string currentAppPath;
        private readonly string cdi4dcPath;
        private readonly string mkisoPath;
        private readonly string ipbinPath;

        private readonly bool showAllDrives = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<GdItem> ItemList { get; private set; } = new ObservableCollection<GdItem>();

        public ObservableCollection<DriveInfo> DriveList { get; private set; } = new ObservableCollection<DriveInfo>();

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

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            this.Loaded += (ss, ee) => FillDriveList();
            this.Closing += MainWindow_Closing;
            this.PropertyChanged += MainWindow_PropertyChanged;
            ItemList.CollectionChanged += ItemList_CollectionChanged;

            //showAllDrives = ;
            bool.TryParse(ConfigurationManager.AppSettings["ShowAllDrives"], out showAllDrives);

            TempFolder = Path.GetTempPath();

            currentAppPath = AppDomain.CurrentDomain.BaseDirectory;

            cdi4dcPath = Path.Combine(currentAppPath, "tools", "cdi4dc.exe");
            mkisoPath = Path.Combine(currentAppPath, "tools", "mkisofs.exe");
            ipbinPath = Path.Combine(currentAppPath, "tools", "IP.BIN");

            katanachar = katana.ToCharArray();//Encoding.UTF8.GetBytes(katana);
            gdiregexp = new Regex(gdiregstr, RegexOptions.Compiled);
            tosecnameregexp = new Regex(tosecnameregstr, RegexOptions.Compiled);
        }

        private async void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedDrive) && SelectedDrive != null)
                await LoadItemsFromCard();
        }

        private void ItemList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            TotalFilesLength = ByteSizeLib.ByteSize.FromBytes(ItemList.Sum(x => x.Length.Bytes)).ToString();
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
                foreach (var item in Directory.GetDirectories(sdPath))//.OrderBy(x => x))
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

            try
            {
                if (ItemList.Count == 0)
                    return;

                StringBuilder sb = new StringBuilder();


                //delete unused folders that are numbers
                List<string> foldersToDelete = new List<string>();
                foreach (var item in Directory.GetDirectories(sdPath))
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


                if (!Directory.Exists(tempDirectory))
                    await Helper.CreateDirectoryAsync(tempDirectory);

                bool foundMenuOnSdCard = false;

                sb.AppendLine("[GDMENU]");

                var ammountToIncrement = 2;//foundMenuOnSdCard ? 2 : 1
                var folder01 = Path.Combine(sdPath, "01");
                if (Directory.Exists(folder01))
                {
                    var ip01 = await CreateGdItemAsync(folder01);
                    if (ip01 != null && ip01.Name == "GDMENU")
                    {
                        foundMenuOnSdCard = true;
                        ammountToIncrement = 1;

                        //delete sdcard menu folder 01
                        await Helper.DeleteDirectoryAsync(folder01);
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
                await CopyNewItems();


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
                    if (!File.Exists(itemNamePath) || File.ReadAllText(itemNamePath).Trim() != item.Name)
                        await Helper.WriteTextFileAsync(itemNamePath, item.Name);

                    //write info text into folder for cdi files
                    var itemInfoPath = Path.Combine(item.FullFolderPath, infotextfile);
                    if (item.CdiTarget > 0)
                    {
                        var newTarget = $"target|{item.CdiTarget}";
                        if (!File.Exists(itemInfoPath) || File.ReadAllText(itemInfoPath).Trim() != newTarget)
                            await Helper.WriteTextFileAsync(itemInfoPath, newTarget);
                    }
                }

                //write menu config to root of sdcard
                var menuConfigPath = Path.Combine(sdPath, menuconfigtextfile);
                if (!File.Exists(menuConfigPath))
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
                    if (tempDirectory != null && Directory.Exists(tempDirectory))
                        await Helper.DeleteDirectoryAsync(tempDirectory);
                }
                catch (Exception)
                {
                }

                IsBusy = false;
            }
        }

        private async Task GenerateMenuImageAsync(string tempDirectory, string listText)
        {
            //create iso
            var dataPath = Path.Combine(tempDirectory, "data");
            if (!Directory.Exists(dataPath))
                await Helper.CreateDirectoryAsync(dataPath);

            var isoPath = Path.Combine(tempDirectory, "iso");
            if (!Directory.Exists(isoPath))
                await Helper.CreateDirectoryAsync(isoPath);

            var isoFilePath = Path.Combine(isoPath, "menu.iso");
            //var isoFilePath = Path.Combine(isoPath, "menu.iso");

            var cdiPath = Path.Combine(tempDirectory, "cdi");//var destinationFolder = Path.Combine(sdPath, "01");
            if (Directory.Exists(cdiPath))
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
                item.FullFolderPath = cdiPath;
                item.CdiTarget = start;
                item.ImageFile = cdiFilePath;
                item.SdNumber = 0;
                item.Work = WorkMode.New;
            }
            else
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

        private Task RunProcess(Process p)
        {
            //p.StartInfo.RedirectStandardOutput = true;
            //p.StartInfo.RedirectStandardError = true;

            //p.OutputDataReceived += (ss, ee) => { Debug.WriteLine(ee.Data); };
            //p.ErrorDataReceived += (ss, ee) => { Debug.WriteLine(ee.Data); };

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
                    await MoveOrCopyFolder(item, i + 1);//+ ammountToIncrement
            }
        }


        private async Task CopyNewItems()
        {
            var total = ItemList.Count(x => x.Work == WorkMode.New);
            if (total == 0)
                return;

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
                        progress.TextContent = $"Copying {item.Name} ...";
                        await MoveOrCopyFolder(item, i + 1);//+ ammountToIncrement
                        progress.ProcessedItems++;

                        //user closed window
                        if (!progress.IsLoaded)
                            break;
                    }
                }
                progress.TextContent = "Done!";
                progress.Close();
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                while (progress.IsLoaded)
                    await Task.Delay(200);

                progress.Close();

                if (progress.ProcessedItems != total)
                    throw new Exception("Operation canceled.\nThere might be unused folders/files on the SD Card.");
            }
        }

        private async Task MoveOrCopyFolder(GdItem item, int folderNumber)
        {
            var newPath = Path.Combine(sdPath, FormatFolderNumber(folderNumber));
            if (item.Work == WorkMode.Move)
                await Helper.MoveDirectoryAsync(Path.Combine(sdPath, item.Guid), newPath);
            else if (item.Work == WorkMode.New)
                await Helper.CopyDirectoryAsync(item.FullFolderPath, newPath);

            item.FullFolderPath = newPath;
            item.Work = WorkMode.None;
            item.SdNumber = folderNumber;
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
            sb.AppendLine($"{strnumber}.disc={ip.Disc}");
            sb.AppendLine($"{strnumber}.vga={ip.Vga}");
            sb.AppendLine($"{strnumber}.region={ip.Region}");
            sb.AppendLine($"{strnumber}.version={ip.Version}");
            sb.AppendLine($"{strnumber}.date={ip.ReleaseDate}");
            sb.AppendLine();
        }

        internal static async Task<GdItem> CreateGdItemAsync(string fileOrFolderPath)
        {
            //todo handle compressed files (zip, 7z, rar)

            string folderPath;
            string[] files;

            FileAttributes attr = File.GetAttributes(fileOrFolderPath);
            if (attr.HasFlag(FileAttributes.Directory))
            {
                folderPath = fileOrFolderPath;
                files = Directory.GetFiles(folderPath);
            }
            else
            {
                folderPath = Path.GetDirectoryName(fileOrFolderPath);
                files = new string[] { fileOrFolderPath };
            }

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath
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

            if (ip == null)
                throw new Exception("Cant't read data from file");
            

            item.Ip = ip;
            item.Name = ip.Name;

            var itemNamePath = Path.Combine(item.FullFolderPath, nametextfile);
            if (File.Exists(itemNamePath))
                item.Name = File.ReadAllText(itemNamePath);

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

                byte[] buffer = new byte[16];

                if (GetString(buffer, fs, headerOffset, 16) != katana)
                    return null;

                var ip = new IpBin
                {
                    //hardwareid = gettemp(buffer, fs, headerOffset, 16),
                    //makerid = gettemp(buffer, fs, headerOffset + 16, 16),
                    //checksum = gettemp(buffer, fs, headerOffset + 32, 4),
                    Disc = GetString(buffer, fs, headerOffset + 37, 11).Substring(6),
                    Region = GetString(buffer, fs, headerOffset + 47, 8),
                    //peripherals = gettemp(buffer, fs, headerOffset + 55, 7),
                    Vga = GetString(buffer, fs, headerOffset + 61, 1),
                    Version = GetString(buffer, fs, headerOffset + 74, 6),
                    ReleaseDate = GetString(buffer, fs, headerOffset + 80, 16),
                    //Producer = gettemp(buffer, fs, headerOffset + 112, 16),
                    Name = GetString(buffer, fs, headerOffset + 128, 16),
                };

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
                var w = new TextWindow("Ignored folders", ex.Message);
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
            IsBusy = true;

            var helptext = @"Tool to manage games on SD card
For use with GDEMU/GDMENU

Uses mkisofs by E.YOUNGDALE/J.PEARSON/J.SCHILLING,
CDI4DC by [big_fury]SiZiOUS

Works only with GDI and CDI files.

*** To add items to list ***
  Drag/drop gdi or cdi files into ""Games List""  
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
            
            var helptext = sb.ToString();

            MessageBox.Show(helptext, "File Info", MessageBoxButton.OK, MessageBoxImage.Information);
            IsBusy = false;
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
                    if (item.SdNumber == 1 && item.Name == "GDMENU")
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

            foreach (DriveInfo drive in list)
            {
                DriveList.Add(drive);
                if (SelectedDrive == null && drive.RootDirectory.GetDirectories("01").Any())
                    SelectedDrive = drive;
            }

            if (!DriveList.Any())
                return;

            if (SelectedDrive == null)
                SelectedDrive = DriveList.LastOrDefault();
        }

        private void MenuItemRenameIP_Click(object sender, RoutedEventArgs e)
        {
            rename(sender, 0);
        }
        private void MenuItemRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            rename(sender, 1);
        }
        private void MenuItemRenameFile_Click(object sender, RoutedEventArgs e)
        {
            rename(sender, 2);
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
            if (e.Key == Key.Delete && !(e.OriginalSource is TextBox))
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
    }
}
