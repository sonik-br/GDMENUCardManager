using Aaru.Decoders.Sega;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.DragAndDrop;
using Avalonia.Xaml.Interactivity;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Resources;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static GDMENUCardManager.Core.Manager;
using static System.Net.WebRequestMethods;

namespace GDMENUCardManager
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly GDMENUCardManager.Core.Manager _ManagerInstance;
        public GDMENUCardManager.Core.Manager Manager { get { return _ManagerInstance; } }

        private readonly bool showAllDrives = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<DriveInfo> DriveList { get; } = new ObservableCollection<DriveInfo>();

        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            private set { _IsBusy = value; RaisePropertyChanged(); }
        }

        private DriveInfo _DriveInfo;
        public DriveInfo SelectedDrive
        {
            get { return _DriveInfo; }
            set
            {
                _DriveInfo = value;
                Manager.ItemList.Clear();
                Manager.sdPath = value?.RootDirectory.ToString();
                Filter = null;
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

        public MenuKind MenuKindSelected
        {
            get { return Manager.MenuKindSelected; }
            set { Manager.MenuKindSelected = value; RaisePropertyChanged(); }
        }

        private string _Filter;
        public string Filter
        {
            get { return _Filter; }
            set { _Filter = value; RaisePropertyChanged(); }
        }

        private readonly List<FilePickerFileType> fileFilterList;


        #region window controls
        //DataGrid dg1; // todo check
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            this.AddHandler(DragDrop.DropEvent, WindowDrop);
#if DEBUG
            //this.AttachDevTools();
            //this.OpenDevTools();
#endif
            //some languages requires wider window
            if (string.Equals(CultureInfo.CurrentUICulture.Name, "ru-RU", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CultureInfo.CurrentUICulture.Name, "fr-FR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CultureInfo.CurrentUICulture.Name, "de-DE", StringComparison.OrdinalIgnoreCase))
            {
                this.Width = 1070;
                this.MinWidth = 920;
            }

            var compressedFileFormats = new string[] { ".7z", ".rar", ".zip" };
            _ManagerInstance = GDMENUCardManager.Core.Manager.CreateInstance(new DependencyManager(), compressedFileFormats);
            var fullList = Manager.supportedImageFormats.Concat(compressedFileFormats).ToArray();

            fileFilterList = new List<FilePickerFileType>
            {
                //FilePickerFileTypes.All,// todo use this on MAC?)

                //Name = $"Dreamcast Game ({string.Join("; ", fullList.Select(x => $"*{x}"))})",
                //Extensions = fullList.Select(x => x.Substring(1)).ToList()
                new FilePickerFileType("Dreamcast Game")
                {
                    //Patterns = fullList.Select(x => x.Substring(1)).ToList()
                    Patterns = fullList.Select(x => $"*{x}").ToList()
                }
            };

            this.Opened += (ss, ee) => { FillDriveList(); };

            this.Closing += MainWindow_Closing;
            this.PropertyChanged += MainWindow_PropertyChanged;
            Manager.ItemList.CollectionChanged += ItemList_CollectionChanged;

            //config parsing. all settings are optional and must reverse to default values if missing
            bool.TryParse(ConfigurationManager.AppSettings["ShowAllDrives"], out showAllDrives);
            bool.TryParse(ConfigurationManager.AppSettings["Debug"], out Manager.debugEnabled);
            if (bool.TryParse(ConfigurationManager.AppSettings["UseBinaryString"], out bool useBinaryString))
                Converter.ByteSizeToStringConverter.UseBinaryString = useBinaryString;
            if (int.TryParse(ConfigurationManager.AppSettings["CharLimit"], out int charLimit))
                GdItem.namemaxlen = Math.Min(255, Math.Max(charLimit, 1));
            if (bool.TryParse(ConfigurationManager.AppSettings["TruncateMenuGDI"], out bool truncateMenuGDI))
                Manager.TruncateMenuGDI = truncateMenuGDI;

            TempFolder = Path.GetTempPath();
            Title = "GD MENU Card Manager " + Constants.Version;
            
            //showAllDrives = true;

            DataContext = this;
        }

        //private void InitializeComponent()
        //{
        //    AvaloniaXamlLoader.Load(this);
        //    this.AddHandler(DragDrop.DropEvent, WindowDrop);
        //    dg1 = this.FindControl<DataGrid>("dg1");
        //}


        private async void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedDrive) && SelectedDrive != null)
                await LoadItemsFromCard();
        }

        private void ItemList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            updateTotalSize();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (IsBusy)
                e.Cancel = true;
            else
                Manager.ItemList.CollectionChanged -= ItemList_CollectionChanged;//release events
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void updateTotalSize()
        {
            var bsize = ByteSizeLib.ByteSize.FromBytes(Manager.ItemList.Sum(x => x.Length.Bytes));
            TotalFilesLength = Converter.ByteSizeToStringConverter.UseBinaryString ? bsize.ToBinaryString() : bsize.ToString();
        }


        private async Task LoadItemsFromCard()
        {
            IsBusy = true;

            try
            {
                await Manager.LoadItemsFromCard();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.InvalidFolders, $"{AppStrings.ProblemLoadingFollowingFolders}:\n\n{ex.Message}", icon: MsBox.Avalonia.Enums.Icon.Warning).ShowWindowDialogAsync(this);
            }
            finally
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                IsBusy = false;
            }
        }

        private async Task Save()
        {
            IsBusy = true;
            bool unhandled_error = false;
            try
            {
                if (await Manager.Save(TempFolder))
                    await MessageBoxManager.GetMessageBoxStandard(AppStrings.Message, AppStrings.Done).ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
                if (ex is MenuNotSelectedException == false)
                    unhandled_error = true;
            }
            finally
            {
                IsBusy = false;
                updateTotalSize();
            }

            if (unhandled_error)
            {
                Close();
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            }
        }

        private async void WindowDrop(object sender, DragEventArgs e)
        {
            if (Manager.sdPath == null)
                return;

            if (e.DataTransfer.Contains(DataFormat.File))
            {
                IsBusy = true;
                var invalid = new List<string>();

                try
                {
                    foreach (var o in e.DataTransfer.TryGetFiles() ?? [])
                    {
                        var path = o.TryGetLocalPath();
                        if (path != null)
                        {
                            try
                            {
                                Manager.ItemList.Add(await ImageHelper.CreateGdItemAsync(path));
                            }
                            catch
                            {
                                invalid.Add(path);
                            }
                        }
                    }

                    if (invalid.Any())
                        await MessageBoxManager.GetMessageBoxStandard(AppStrings.IgnoredFoldersFiles, string.Join(Environment.NewLine, invalid), icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
                }
                catch (Exception)
                {
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async void ButtonSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            await Save();
        }

        private async void ButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            if (Manager.debugEnabled)
            {
                var list = DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => $"{x.DriveType}; {x.DriveFormat}; {x.Name}").ToArray();
                await MessageBoxManager.GetMessageBoxStandard("Debug", string.Join(Environment.NewLine, list), icon: MsBox.Avalonia.Enums.Icon.None).ShowWindowDialogAsync(this);
            }
            await new AboutWindow().ShowDialog(this);
            IsBusy = false;
        }

        private async void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDialogOptions = new FolderPickerOpenOptions
            {
                Title = AppStrings.TemporaryFolder,
                AllowMultiple = false,
            };

            if (!string.IsNullOrEmpty(TempFolder))
                folderDialogOptions.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(TempFolder);

            var selectedFolder = (await StorageProvider.OpenFolderPickerAsync(folderDialogOptions)).FirstOrDefault();
            if (selectedFolder != null)
                TempFolder = selectedFolder.TryGetLocalPath();
        }

        private async void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            try
            {
                var btn = (Button)sender;
                var item = (GdItem)btn.CommandParameter;

                if (item.Ip == null)
                    await Manager.LoadIP(item);

                await new InfoWindow(item).ShowDialog(this);
            }
            catch(Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
            }
            IsBusy = false;
        }

        private async void ButtonSort_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            try
            {
                await Manager.SortList();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
            }
            IsBusy = false;
        }

        private async void ButtonBatchRename_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                var w = new CopyNameWindow();
                if (!await w.ShowDialog<bool>(this))
                    return;

                var count = await Manager.BatchRenameItems(w.NotOnCard, w.OnCard, w.FolderName, w.ParseTosec);

                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Done, string.Format(AppStrings.ItemsRenamed, count)).ShowAsPopupAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowAsPopupAsync(this);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ButtonPreload_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                await Manager.LoadIpAll();
            }
            catch (ProgressWindowClosedException) { }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowAsPopupAsync(this);
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
            DriveInfo[] list;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || (x.DriveType == DriveType.Removable && x.DriveFormat.StartsWith("FAT")))).ToArray();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                //list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Fixed)).ToArray();//todo need to test
                list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Fixed || (x.DriveType == DriveType.Unknown && x.DriveFormat.Equals("lifs", StringComparison.InvariantCultureIgnoreCase)))).ToArray();//todo need to test
            else//linux
                list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || ((x.DriveType == DriveType.Removable || x.DriveType == DriveType.Fixed) && x.DriveFormat.Equals("msdos", StringComparison.InvariantCultureIgnoreCase) && (x.Name.StartsWith("/media/", StringComparison.InvariantCultureIgnoreCase) || x.Name.StartsWith("/run/media/", StringComparison.InvariantCultureIgnoreCase)) ))).ToArray();
            

            if (isRefreshing)
            {
                if (DriveList.Select(x => x.Name).SequenceEqual(list.Select(x => x.Name)))
                    return;

                DriveList.Clear();
            }
            //fill drive list and try to find drive with gdemu contents
            //look for GDEMU.ini file
            foreach (DriveInfo drive in list)
            {
                try
                {
                    DriveList.Add(drive);
                    if (SelectedDrive == null && System.IO.File.Exists(Path.Combine(drive.RootDirectory.FullName, Constants.MenuConfigTextFile)))
                        SelectedDrive = drive;
                }
                catch { }
            }

            //look for 01 folder
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                {
                    try
                    {
                        if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "01")))
                        {
                            SelectedDrive = drive;
                            break;
                        }
                    }
                    catch { }
                }
            }

            //look for /media mount
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                {
                    try
                    {
                        if (drive.Name.StartsWith("/media/", StringComparison.InvariantCultureIgnoreCase))
                        {
                            SelectedDrive = drive;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (!DriveList.Any())
                return;

            if (SelectedDrive == null)
                SelectedDrive = DriveList.LastOrDefault();
        }

        private async void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            var menuitem = (MenuItem)sender;
            var item = (GdItem)menuitem.CommandParameter;

            var w = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
            {
                ContentTitle = AppStrings.Rename,
                ContentHeader = "Inform new name",//todo localize
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CloseOnClickAway = true,
                InputParams = new InputParams
                {
                    DefaultValue = item.Name,
                    Multiline = false,
                },
                ButtonDefinitions = new ButtonDefinition[] { new ButtonDefinition { Name = "Ok", IsDefault = true }, new ButtonDefinition { Name = "Cancel", IsCancel = true } }
            });
            var result = await w.ShowAsPopupAsync(this);

            if (!string.IsNullOrEmpty(result) && result == "Ok" && !string.IsNullOrWhiteSpace(w.InputValue))
                item.Name = w.InputValue.Trim();
        }

        private async void MenuItemRenameSentence_Click(object sender, RoutedEventArgs e)
        {
            await casingSelection(LetterCasing.Title);
        }
        private async void MenuItemRenameLowercase_Click(object sender, RoutedEventArgs e)
        {
            await casingSelection(LetterCasing.Lower);
        }
        private async void MenuItemRenameUppercase_Click(object sender, RoutedEventArgs e)
        {
            await casingSelection(LetterCasing.Upper);
        }

        private async Task casingSelection(LetterCasing casing)
        {
            IsBusy = true;
            try
            {
                await Manager.LetterCasingItems(dg1.SelectedItems.Cast<GdItem>(), casing);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowAsPopupAsync(this);
            }
            IsBusy = false;
        }

        private async void MenuItemRenameIP_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.Ip);
        }
        private async void MenuItemRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.Folder);

        }
        private async void MenuItemRenameFile_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.File);
        }

        private async Task renameSelection(RenameBy renameBy)
        {
            IsBusy = true;
            try
            {
                await Manager.RenameItems(dg1.SelectedItems.Cast<GdItem>(), renameBy);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowAsPopupAsync(this);
            }
            IsBusy = false;
        }

        //private void rename(GdItem item, short index)
        //{
        //    string name;

        //    if (index == 0)//ip.bin
        //    {
        //        name = item.Ip.Name;
        //    }
        //    else
        //    {
        //        if (index == 1)//folder
        //            name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
        //        else//file
        //            name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
        //        var m = RegularExpressions.TosecnNameRegexp.Match(name);
        //        if (m.Success)
        //            name = name.Substring(0, m.Index);
        //    }
        //    item.Name = name;
        //}

        //private void rename(object sender, short index)
        //{
        //    var menuItem = (MenuItem)sender;
        //    var item = (GdItem)menuItem.CommandParameter;

        //    string name;

        //    if (index == 0)//ip.bin
        //    {
        //        name = item.Ip.Name;
        //    }
        //    else
        //    {
        //        if (index == 1)//folder
        //            name = Path.GetFileName(item.FullFolderPath).ToUpperInvariant();
        //        else//file
        //            name = Path.GetFileNameWithoutExtension(item.ImageFile).ToUpperInvariant();
        //        var m = RegularExpressions.TosecnNameRegexp.Match(name);
        //        if (m.Success)
        //            name = name.Substring(0, m.Index);
        //    }
        //    item.Name = name;
        //}

        private async void GridOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && !(e.Source is TextBox))
            {
                List<GdItem> toRemove = new List<GdItem>();
                foreach (GdItem item in dg1.SelectedItems)
                {
                    if (item.SdNumber == 1)
                    {
                        if (item.Ip == null)
                        {
                            IsBusy = true;
                            try
                            {
                                await Manager.LoadIP(item);
                            }
                            catch
                            {
                                continue;
                            }
                            finally
                            {
                                IsBusy = false;
                            }
                        }
                        if (item.Ip.Name != "GDMENU" && item.Ip.Name != "openMenu")//dont let the user exclude GDMENU, openMenu
                            toRemove.Add(item);
                    }
                    else
                    {
                        toRemove.Add(item);
                    }
                }

                foreach (var item in toRemove)
                    Manager.ItemList.Remove(item);

                e.Handled = true;
            }
        }

        private async void ButtonAddGames_Click(object sender, RoutedEventArgs e)
        {

            var fileDialogOptions = new FilePickerOpenOptions
            {
                Title = "Select File(s)", //todo localize
                AllowMultiple = true,
                FileTypeFilter = fileFilterList
            };

            var files = await StorageProvider.OpenFilePickerAsync(fileDialogOptions);
            if (files != null && files.Any())
            {
                IsBusy = true;

                var invalid = await Manager.AddGames(files.Select(x => x.TryGetLocalPath()).ToArray());

                if (invalid.Any())
                    await MessageBoxManager.GetMessageBoxStandard(AppStrings.IgnoredFoldersFiles, string.Join(Environment.NewLine, invalid), icon: MsBox.Avalonia.Enums.Icon.Error).ShowAsPopupAsync(this);

                IsBusy = false;
            }
        }

        private async void ButtonRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            //todo prevent not remove gdmenu!
            foreach (var item in dg1.SelectedItems.Cast<GdItem>().ToArray())
            {
                if (item.SdNumber == 1)
                {
                    if (item.Ip == null)
                    {
                        IsBusy = true;
                        try
                        {
                            await Manager.LoadIP(item);
                        }
                        catch
                        {
                            continue;
                        }
                        finally
                        {
                            IsBusy = false;
                        }
                    }
                    if (item.Ip.Name == "GDMENU" || item.Ip.Name == "openMenu") //dont let the user exclude GDMENU
                    {
                        continue;
                    }
                }

                Manager.ItemList.Remove(item);
            }
        }

        private async void ButtonMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToArray();

            if (!selectedItems.Any())
                return;

            int moveTo = Manager.ItemList.IndexOf(selectedItems.First()) -1;

            if (moveTo < 0)
                return;

            if (moveTo == 0)
            {
                var firstItem = Manager.ItemList.First();
                if (firstItem.Ip == null)
                {
                    IsBusy = true;
                    try
                    {
                        await Manager.LoadIP(firstItem);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }

                if (firstItem.Ip?.Name == "GDMENU" || firstItem.Ip?.Name == "openMenu")
                    return;
            }
            
            foreach (var item in selectedItems)
                Manager.ItemList.Remove(item);

            foreach (var item in selectedItems)
                Manager.ItemList.Insert(moveTo++, item);

            dg1.SelectedItems.Clear();
            foreach (var item in selectedItems)
                dg1.SelectedItems.Add(item);
        }

        private async void ButtonMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToArray();

            if (!selectedItems.Any())
                return;

            var firstItem = Manager.ItemList.First();
            var firstSelectedItem = selectedItems.First();

            if (firstItem == firstSelectedItem)
            {
                if (firstItem.Ip == null)
                {
                    IsBusy = true;
                    try
                    {
                        await Manager.LoadIP(firstItem);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }
                if (firstItem.Ip?.Name == "GDMENU" || firstItem.Ip?.Name == "openMenu")
                    return;
            }


            int moveTo = Manager.ItemList.IndexOf(selectedItems.Last()) - selectedItems.Length + 2;

            if (moveTo > Manager.ItemList.Count - selectedItems.Length)
                return;

            foreach (var item in selectedItems)
                Manager.ItemList.Remove(item);

            foreach (var item in selectedItems)
                Manager.ItemList.Insert(moveTo++, item);

            dg1.SelectedItems.Clear();
            foreach (var item in selectedItems)
                dg1.SelectedItems.Add(item);
        }

        private async void ButtonSearch_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0 || string.IsNullOrWhiteSpace(Filter))
                return;

            try
            {
                IsBusy = true;
                await Manager.LoadIpAll();
                IsBusy = false;
            }
            catch (ProgressWindowClosedException)
            {

            }

            if (dg1.SelectedIndex == -1 || !searchInGrid(dg1.SelectedIndex))
                searchInGrid(0);
        }

        private bool searchInGrid(int start)
        {
            for (int i = start; i < Manager.ItemList.Count; i++)
            {
                var item = Manager.ItemList[i];
                if (dg1.SelectedItem != item && Manager.SearchInItem(item, Filter))
                {
                    dg1.SelectedItem = item;
                    dg1.ScrollIntoView(item, null);
                    return true;
                }
            }
            return false;
        }

        private void ButtonLink_Click(object sender, RoutedEventArgs e)
        {
            var url = @"https://ko-fi.com/sonik_br/";
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Process.Start("xdg-open", url);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
            }
            catch { }
        }

    }





    //todo move to mainwindow class, remove generic.
    //drag and drop based on https://github.com/AvaloniaUI/Avalonia/discussions/10877#discussion-5036074
    //also to ckeck... https://github.com/AvaloniaUI/Avalonia.Xaml.Behaviors/pull/174/files
    public class DataGridDnd : DropHandlerBase
    {
        private enum DragDirection
        {
            Up,
            Down
        }

        private struct DndData
        {
            public DndData() { }
            public DataGrid? SrcDataGrid = null;
            public DataGrid DestDataGrid = null!;
            public IList<GdItem> SrcList = null!;
            public IList<GdItem> DestList = null!;
            public int SrcIndex = -1;

            public int DestIndex = -1;
            public DragDirection Direction;
        }

        private const string DraggingUpClassName = "dragging-up";
        private const string DraggingDownClassName = "dragging-down";

        private DndData _dnd = new();

        private bool Validate(object? sender, DragEventArgs e, object? sourceContext)
        {

            if (sourceContext == null)
            {
                if (_dnd.SrcDataGrid is not { } srcDg ||
                    sender is not DataGrid destDg ||
                    srcDg.ItemsSource is not IList<GdItem> srcList ||
                    destDg.ItemsSource is not IList<GdItem> destList
                    //destDg.GetVisualAt(e.GetPosition(destDg),
                    //  v => v.FindDescendantOfType<DataGridCell>() is not null) is not Control
                    //  {
                    //      DataContext: T dest
                    //  } visual
                      )
                {
                    return false;
                }


                if (destDg.GetVisualAt(e.GetPosition(destDg),
                      v => v.FindDescendantOfType<DataGridCell>() is not null) is not Control
                      {
                          DataContext: GdItem dest
                      } visual)
                {

                    if (destList.Any())
                    {
                        _dnd.SrcDataGrid = srcDg;
                        _dnd.DestDataGrid = destDg;
                        _dnd.SrcList = srcList;
                        _dnd.DestList = destList;
                        _dnd.Direction = DragDirection.Down;//cell.DesiredSize.Height / 2 > pos.Y ? DragDirection.Up : DragDirection.Down;
                        _dnd.SrcIndex = destList.Count;// destList.IndexOf(dest);// srcList.IndexOf(src);
                        _dnd.DestIndex = destList.Count;// destList.IndexOf(dest);
                        return true;
                    }
                    else
                    {
                        _dnd.SrcDataGrid = srcDg;
                        _dnd.DestDataGrid = destDg;
                        _dnd.SrcList = srcList;
                        _dnd.DestList = destList;
                        _dnd.Direction = DragDirection.Down;//cell.DesiredSize.Height / 2 > pos.Y ? DragDirection.Up : DragDirection.Down;
                        _dnd.SrcIndex = 0;// destList.IndexOf(dest);// srcList.IndexOf(src);
                        _dnd.DestIndex = 0;// destList.IndexOf(dest);
                        return true;
                    }
                    var cell = destDg.FindDescendantOfType<DataGridCell>();
                    //Debug.WriteLine(cell.DataContext);
                    //foreach (var item in c)
                    //{
                    //    Debug.WriteLine(item);
                    //}

                    _dnd.SrcDataGrid = srcDg;
                    _dnd.DestDataGrid = destDg;
                    _dnd.SrcList = srcList;
                    _dnd.DestList = destList;
                    _dnd.Direction = DragDirection.Down;//cell.DesiredSize.Height / 2 > pos.Y ? DragDirection.Up : DragDirection.Down;
                    _dnd.SrcIndex = destList.Count;// destList.IndexOf(dest);// srcList.IndexOf(src);
                    _dnd.DestIndex = destList.Count;// destList.IndexOf(dest);

                    return true;
                }


                if (false)
                {
                    return false;
                }
                else
                {
                    //nao existe (nulo) quando em cima de um objeto existente.
                    //existe quando em espaco branco

                    //visual no branco é border


                    DataGridCell cell = visual.FindDescendantOfType<DataGridCell>()!;
                    if (cell == null)
                    {
                        Debug.WriteLine("FALSE");
                        return false;
                    }
                    
                    var pos = e.GetPosition(cell);

                    //Debug.WriteLine(visual.DataContext);
                    //return false;


                    _dnd.SrcDataGrid = srcDg;
                    _dnd.DestDataGrid = destDg;
                    _dnd.SrcList = srcList;
                    _dnd.DestList = destList;
                    _dnd.Direction = cell.DesiredSize.Height / 2 > pos.Y ? DragDirection.Up : DragDirection.Down;
                    _dnd.SrcIndex = destList.IndexOf(dest);// srcList.IndexOf(src);
                    _dnd.DestIndex = destList.IndexOf(dest);
                    return true;

                }

                //DataGridCell cell = visual.FindDescendantOfType<DataGridCell>()!;
                //var pos = e.GetPosition(cell);

                //_dnd.SrcDataGrid = srcDg;
                //_dnd.DestDataGrid = destDg;
                //_dnd.SrcList = srcList;
                //_dnd.DestList = destList;
                //_dnd.Direction = cell.DesiredSize.Height / 2 > pos.Y ? DragDirection.Up : DragDirection.Down;
                //_dnd.SrcIndex = srcList.IndexOf(src);
                //_dnd.DestIndex = destList.IndexOf(dest);

                return false;
            }
            else
            {
                if (_dnd.SrcDataGrid is not { } srcDg ||
                    sender is not DataGrid destDg ||
                    sourceContext is not GdItem src ||
                    srcDg.ItemsSource is not IList<GdItem> srcList ||
                    destDg.ItemsSource is not IList<GdItem> destList ||
                    destDg.GetVisualAt(e.GetPosition(destDg),
                      v => v.FindDescendantOfType<DataGridCell>() is not null) is not Control
                      {
                          DataContext: GdItem dest
                      } visual)
                    return false;

                DataGridCell cell = visual.FindDescendantOfType<DataGridCell>()!;
                var pos = e.GetPosition(cell);

                _dnd.SrcDataGrid = srcDg;
                _dnd.DestDataGrid = destDg;
                _dnd.SrcList = srcList;
                _dnd.DestList = destList;
                _dnd.Direction = cell.DesiredSize.Height / 2 > pos.Y ? DragDirection.Up : DragDirection.Down;
                _dnd.SrcIndex = srcList.IndexOf(src);
                _dnd.DestIndex = destList.IndexOf(dest);
            }

            return true;
        }

        public override bool Validate(object? sender, DragEventArgs e, object? sourceContext,
                                      object? targetContext, object? state)
        {
            return Validate(sender, e, sourceContext);
        }

        public override bool Execute(object? sender, DragEventArgs e, object? sourceContext,
                                     object? targetContext, object? state)
        {
            if (!Validate(sender, e, sourceContext))
                return false;

            if (e.DataTransfer.Contains(DataFormat.File))
            {
                //MoveItem(_dnd.SrcList, _dnd.DestList, _dnd.SrcIndex, _dnd.DestIndex);

                if (_dnd.Direction == DragDirection.Up && _dnd.DestIndex > 0)
                    _dnd.DestIndex--;
                else if (_dnd.Direction == DragDirection.Down && _dnd.DestIndex < _dnd.DestList.Count)
                    _dnd.DestIndex++;

                var insertIndex = _dnd.DestIndex;// dropInfo.UnfilteredInsertIndex;
                Debug.WriteLine(insertIndex);

                foreach (var item in e.DataTransfer.Items)
                {
                    var aaa = item.TryGetFile();
                    if (aaa != null)
                    {
                        var o = aaa.TryGetLocalPath();
                        if (o != null)
                        {
                            //var destinationList = dropInfo.TargetCollection.TryGetList();

                            Dispatcher.UIThread.Invoke(async () =>
                            {
                                //todo try
                                
                                var toInsert = await ImageHelper.CreateGdItemAsync(o);
                                InsertItem((IList<GdItem>)_dnd.DestList, toInsert, insertIndex++);
                            });
                        }
                    }
                }
                return true;
            }

            if (_dnd.SrcDataGrid != _dnd.DestDataGrid && _dnd.Direction == DragDirection.Down)
                _dnd.DestIndex++;
            else if (_dnd.SrcIndex > _dnd.DestIndex && _dnd.Direction == DragDirection.Down)
                _dnd.DestIndex++;
            else if (_dnd.SrcIndex < _dnd.DestIndex && _dnd.Direction == DragDirection.Up)
                _dnd.DestIndex--;

            MoveItem(_dnd.SrcList, _dnd.DestList, _dnd.SrcIndex, _dnd.DestIndex);
            _dnd.DestDataGrid.SelectedIndex = _dnd.DestIndex;
            _dnd.DestDataGrid.ScrollIntoView(_dnd.DestList[_dnd.DestIndex], null);
            _dnd.SrcDataGrid = null;
            return true;
        }

        public override void Enter(object? sender, DragEventArgs e, object? sourceContext,
                                   object? targetContext)
        {
            _dnd.SrcDataGrid ??= sender as DataGrid;
            if (!Validate(sender, e, sourceContext))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            string className = _dnd.Direction switch
            {
                DragDirection.Down => DraggingDownClassName,
                DragDirection.Up => DraggingUpClassName,
                _ => throw new UnreachableException($"Invalid drag direction: {_dnd.Direction}")
            };
            _dnd.DestDataGrid.Classes.Add(className);

            e.DragEffects |= DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
            e.Handled = true;
        }

        public override void Over(object? sender, DragEventArgs e, object? sourceContext,
                                  object? targetContext)
        {
            if (!Validate(sender, e, sourceContext))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.DragEffects |= DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
            e.Handled = true;

            (string toAdd, string toRemove) classUpdate = _dnd.Direction switch
            {
                DragDirection.Down => (DraggingDownClassName, DraggingUpClassName),
                DragDirection.Up => (DraggingUpClassName, DraggingDownClassName),
                _ => throw new UnreachableException($"Invalid drag direction: {_dnd.Direction}")
            };
            if (_dnd.DestDataGrid.Classes.Contains(classUpdate.toAdd))
                return;

            _dnd.DestDataGrid.Classes.Remove(classUpdate.toRemove);
            _dnd.DestDataGrid.Classes.Add(classUpdate.toAdd);
        }

        public override void Leave(object? sender, RoutedEventArgs e)
        {
            base.Leave(sender, e);
            RemoveDraggingClass(sender as DataGrid);
        }

        public override void Drop(object? sender, DragEventArgs e, object? sourceContext,
                                  object? targetContext)
        {
            RemoveDraggingClass(sender as DataGrid);
            base.Drop(sender, e, sourceContext, targetContext);
            _dnd.SrcDataGrid = null;
        }

        private static void RemoveDraggingClass(DataGrid? dg)
        {
            if (dg is not null && !dg.Classes.Remove(DraggingUpClassName))
                dg.Classes.Remove(DraggingDownClassName);
        }
    }



}
