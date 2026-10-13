using Aaru.Decoders.Sega;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        private bool _HaveGDIShrinkBlacklist;
        public bool HaveGDIShrinkBlacklist
        {
            get { return _HaveGDIShrinkBlacklist; }
            set { _HaveGDIShrinkBlacklist = value; RaisePropertyChanged(); }
        }

        public bool EnableGDIShrink
        {
            get { return Manager.EnableGDIShrink; }
            set { Manager.EnableGDIShrink = value; RaisePropertyChanged(); }
        }

        public bool EnableGDIShrinkCompressed
        {
            get { return Manager.EnableGDIShrinkCompressed; }
            set { Manager.EnableGDIShrinkCompressed = value; RaisePropertyChanged(); }
        }

        public bool EnableGDIShrinkBlackList
        {
            get { return Manager.EnableGDIShrinkBlackList; }
            set { Manager.EnableGDIShrinkBlackList = value; RaisePropertyChanged(); }
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

        // Where a drop will land, worked out while the drag hovers and used when it lands.
        // -1 means we have not settled on a spot yet.
        private int _pendingDropIndex = -1;
        private readonly DispatcherTimer _dragScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        private Point _dragScrollPosition;

        // Row reorder drag state. The dragged items ride in _rowDragItems since source
        // and target are the same window, the marker format is there because macOS
        // refuses a drag that declares no types at all.
        private static readonly DataFormat<byte[]> RowDragFormat =
            DataFormat.CreateBytesApplicationFormat("gdmcm-games-row-drag");
        private Avalonia.Input.PointerPressedEventArgs _rowDragTrigger;
        private Point _rowDragStartPoint;
        private GdItem _rowDragPressedItem;
        private List<GdItem> _rowDragItems;
        private bool _rowDragKeepSelection;
        #endregion

        // Row whose cell is being edited. A reorder drag must not start from a cell editor.
        private GdItem _editingItem;

        public MainWindow()
        {
            InitializeComponent();
            this.AddHandler(DragDrop.DropEvent, WindowDrop);
            this.AddHandler(DragDrop.DragEnterEvent, WindowDragOver);
            this.AddHandler(DragDrop.DragOverEvent, WindowDragOver);
            this.AddHandler(DragDrop.DragLeaveEvent, WindowDragLeave);
            _dragScrollTimer.Tick += DragScrollTimer_Tick;
            this.Closed += (_, _) => _dragScrollTimer.Stop();

            dg1.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, DataGrid_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            dg1.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, DataGrid_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            dg1.PointerMoved += DataGrid_PointerMoved;
            dg1.BeginningEdit += (_, e) => _editingItem = e.Row?.DataContext as GdItem;
            dg1.CellEditEnded += (_, _) => _editingItem = null;
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

            this.Opened += (ss, ee) =>
            {
                HaveGDIShrinkBlacklist = System.IO.File.Exists(Constants.GdiShrinkBlacklistFile);
                FillDriveList();
            };

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
            if (bool.TryParse(ConfigurationManager.AppSettings["CompressedFileInfo"], out bool compressedFileInfo))
                Manager.EnableCompressedFileInfo = compressedFileInfo;
            if (bool.TryParse(ConfigurationManager.AppSettings["EnableCardCache"], out bool enableCardCache))
                Manager.EnableCardCache = enableCardCache;

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

        private void DataGrid_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            // A left press on a row may turn into a reorder drag, remember it until the
            // pointer has moved far enough to count as one.
            if (e.GetCurrentPoint(dg1).Properties.IsLeftButtonPressed)
            {
                var pressSource = e.Source as Avalonia.Controls.Control;
                while (pressSource != null)
                {
                    if (pressSource is Avalonia.Controls.DataGridRow pressRow)
                    {
                        _rowDragPressedItem = pressRow.DataContext as GdItem;
                        _rowDragTrigger = _rowDragPressedItem != null ? e : null;
                        _rowDragStartPoint = e.GetPosition(this);

                        // The grid would collapse a multi selection to this row on press, so keep it until release.
                        _rowDragKeepSelection = _rowDragPressedItem != null && e.KeyModifiers == KeyModifiers.None &&
                            dg1.SelectedItems.Count > 1 && dg1.SelectedItems.Contains(_rowDragPressedItem);
                        if (_rowDragKeepSelection)
                            e.Handled = true;
                        break;
                    }
                    pressSource = pressSource.Parent as Avalonia.Controls.Control;
                }
            }
        }

        private void DataGrid_PointerReleased(object sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            // Released without crossing the drag threshold, so no reorder drag.
            // A plain click on a multi selection still selects just that row.
            if (_rowDragKeepSelection && _rowDragTrigger != null)
                dg1.SelectedItem = _rowDragPressedItem;
            _rowDragKeepSelection = false;
            _rowDragTrigger = null;
            _rowDragPressedItem = null;
        }

        private async void DataGrid_PointerMoved(object sender, Avalonia.Input.PointerEventArgs e)
        {
            if (_rowDragTrigger == null || _rowDragItems != null)
                return;

            if (!e.GetCurrentPoint(dg1).Properties.IsLeftButtonPressed)
            {
                _rowDragTrigger = null;
                _rowDragPressedItem = null;
                return;
            }

            if (IsBusy || Manager.sdPath == null || _editingItem != null)
                return;

            var current = e.GetPosition(this);
            if (Math.Abs(current.X - _rowDragStartPoint.X) < 4 &&
                Math.Abs(current.Y - _rowDragStartPoint.Y) < 4)
                return;

            // Dragging a row that is part of the selection moves the whole selection.
            var items = new List<GdItem>();
            if (dg1.SelectedItems != null && dg1.SelectedItems.Contains(_rowDragPressedItem) && dg1.SelectedItems.Count > 1)
                items.AddRange(dg1.SelectedItems.OfType<GdItem>().OrderBy(x => Manager.ItemList.IndexOf(x)));
            else
                items.Add(_rowDragPressedItem);

            // The menu entry stays in slot 0, it does not get dragged.
            if (items.Count == 0 || items.Any(IsMenuEntry))
            {
                _rowDragTrigger = null;
                _rowDragPressedItem = null;
                return;
            }

            var trigger = _rowDragTrigger;
            _rowDragTrigger = null;
            _rowDragPressedItem = null;
            _rowDragItems = items;


            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(RowDragFormat, new byte[] { 1 }));

            try
            {
                await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Move);
            }
            catch (Exception)
            {
                // A failed platform drag just cancels the move.
            }
            finally
            {
                _dragScrollTimer.Stop();
                _rowDragItems = null;
                _pendingDropIndex = -1;
                HideDropLine();
            }
        }

        private async void WindowDrop(object sender, DragEventArgs e)
        {
            _dragScrollTimer.Stop();
            HideDropLine();

            int pending = _pendingDropIndex;
            _pendingDropIndex = -1;

            if (Manager.sdPath == null)
                return;

            if (_rowDragItems != null && e.DataTransfer.Contains(RowDragFormat))
            {
                try
                {
                    // Reorder drop. Remove the dragged rows, walking the target index back.
                    // for each one that sat above it, then put them back at the target spot.
                    int moveIndex = pending >= 0 ? pending : DefaultDropIndex();
                    moveIndex = Math.Min(moveIndex, Manager.ItemList.Count);

                    foreach (var item in _rowDragItems)
                    {
                        var idx = Manager.ItemList.IndexOf(item);
                        if (idx < 0)
                            continue;
                        Manager.ItemList.RemoveAt(idx);
                        if (idx < moveIndex)
                            moveIndex--;
                    }

                    if (moveIndex == 0 && Manager.ItemList.Count > 0 && IsMenuEntry(Manager.ItemList[0]))
                        moveIndex = 1;
                    moveIndex = Math.Min(moveIndex, Manager.ItemList.Count);

                    foreach (var item in _rowDragItems)
                        Manager.ItemList.Insert(moveIndex++, item);
                }
                catch (Exception ex)
                {
                    await MessageBoxManager.GetMessageBoxStandard(AppStrings.Error, ex.Message, icon: MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
                }
                return;
            }

            if (e.DataTransfer.Contains(DataFormat.File))
            {
                // Land the drop where the guide line settled during the drag. The drop
                // event position is not trustworthy on some setups, the hover position is.
                int insertIndex = pending >= 0 ? pending : DefaultDropIndex();
                insertIndex = Math.Min(insertIndex, Manager.ItemList.Count);

                IsBusy = true;
                var invalid = new List<string>();

                try
                {
                    // Dropped files arrive as storage items, take the real path off each.
                    var droppedItems = e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>();
                    // Files referenced by an index file in this same drop ride along with it.
                    var companions = await ImageHelper.GetCompanionFilePathsAsync(
                        droppedItems.Select(x => x.TryGetLocalPath()).Where(x => x != null));

                    foreach (var o in droppedItems)
                    {
                        var path = o.TryGetLocalPath();
                        if (path != null && !companions.Contains(path))
                        {
                            try
                            {
                                Manager.ItemList.Insert(insertIndex, await ImageHelper.CreateGdItemAsync(path));
                                insertIndex++;
                            }
                            catch (UnsupportedDiscFormatException ex)
                            {
                                invalid.Add($"{path} - {ex.Message}");
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

        // The menu disc must stay in slot 0, so everything that protects that slot goes
        // through this. Ip stays null for lazy loaded items until it is read, so check
        // the cached name and the folder number too.
        private static bool IsMenuEntry(GdItem item)
        {
            if (item == null)
                return false;
            if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                return true;
            if (item.Name == "GDMENU" || item.Name == "openMenu")
                return true;
            return item.SdNumber == 1;
        }

        // Fallback spot when the pointer is not over a row. Anything we can't place
        // goes to the end of the list.
        private int DefaultDropIndex()
        {
            return Manager.ItemList.Count;
        }

        private void WindowDragOver(object sender, DragEventArgs e)
        {
            bool isFileDrag = e.DataTransfer.Contains(DataFormat.File);
            bool isRowDrag = _rowDragItems != null && e.DataTransfer.Contains(RowDragFormat);

            if (IsBusy || Manager.sdPath == null || (!isFileDrag && !isRowDrag))
            {
                _dragScrollTimer.Stop();
                _pendingDropIndex = -1;
                HideDropLine();
                return;
            }

            if (isRowDrag)
                e.DragEffects = DragDropEffects.Move;

            _dragScrollPosition = e.GetPosition(this);
            UpdateDropTarget(e.GetPosition(dg1));

            if (GetDragScrollDirection() != 0)
                _dragScrollTimer.Start();
            else
                _dragScrollTimer.Stop();
        }

        private void UpdateDropTarget(Point point)
        {
            var target = HitTestDropRow(point);
            if (target == null)
            {
                _pendingDropIndex = DefaultDropIndex();
                HideDropLine();
            }
            else
            {
                _pendingDropIndex = target.Value.InsertIndex;
                ShowDropLine(target.Value.Row, target.Value.Below);
            }
        }

        private void WindowDragLeave(object sender, RoutedEventArgs e)
        {
            _dragScrollTimer.Stop();
            // DragLeave can precede Drop, so keep the last insertion index.
            HideDropLine();
        }

        private int GetDragScrollDirection()
        {
            if (IsBusy || Manager.sdPath == null || !IsVisible ||
                dg1 == null || !dg1.IsVisible)
                return 0;

            var presenter = dg1.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
            if (presenter == null)
                return 0;

            var point = this.TranslatePoint(_dragScrollPosition, presenter);
            if (point == null || !new Rect(presenter.Bounds.Size).Contains(point.Value))
                return 0;

            double edgeHeight = Math.Min(32, presenter.Bounds.Height / 2);
            if (point.Value.Y < edgeHeight)
                return -1;
            if (point.Value.Y >= presenter.Bounds.Height - edgeHeight)
                return 1;
            return 0;
        }

        private void DragScrollTimer_Tick(object sender, EventArgs e)
        {
            int direction = GetDragScrollDirection();
            var scrollBar = dg1?.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(x => x.Name == "PART_VerticalScrollbar");
            if (direction == 0 || scrollBar == null || !scrollBar.IsVisible ||
                (direction < 0 && scrollBar.Value <= scrollBar.Minimum) ||
                (direction > 0 && scrollBar.Value >= scrollBar.Maximum))
            {
                _dragScrollTimer.Stop();
                return;
            }

            if (direction < 0)
                scrollBar.LineUp();
            else
                scrollBar.LineDown();

            dg1.UpdateLayout();
            var point = this.TranslatePoint(_dragScrollPosition, dg1);
            if (point != null)
                UpdateDropTarget(point.Value);
        }

        // Finds the row under the pointer and where an item would go. Upper half of a row
        // means above it, lower half below. The menu entry keeps slot 0, so a drop meant.
        // for the very top lands just under it instead. pointing at the open space under
        // The last row means after that row. Returns null when off the rows.
        private (DataGridRow Row, bool Below, int InsertIndex)? HitTestDropRow(Point pos)
        {
            try
            {
                var list = Manager.ItemList;

                if (dg1 == null || !dg1.IsVisible)
                    return null;

                double y = pos.Y;

                DataGridRow bottomRow = null;
                GdItem bottomItem = null;
                double bottomEdge = double.MinValue;

                foreach (var row in dg1.GetVisualDescendants().OfType<DataGridRow>())
                {
                    // Recycled rows from a previous card load stay parked in the visual
                    // tree, invisible, holding items that are no longer in the list. They
                    // tile the empty area below the live rows, so they must not count as
                    // drop targets or as the bottom row.
                    if (!row.IsVisible)
                        continue;

                    if (!(row.DataContext is GdItem hoveredItem))
                        continue;

                    int index = list.IndexOf(hoveredItem);
                    if (index < 0)
                        continue;

                    var rowTop = row.TranslatePoint(new Point(0, 0), dg1);
                    if (rowTop == null)
                        continue;

                    double top = rowTop.Value.Y;
                    double height = row.Bounds.Height;

                    if (top + height > bottomEdge)
                    {
                        bottomEdge = top + height;
                        bottomRow = row;
                        bottomItem = hoveredItem;
                    }

                    if (y < top || y >= top + height)
                        continue;

                    bool below = y > top + height / 2;
                    int insertIndex = below ? index + 1 : index;

                    if (insertIndex == 0 && list.Count > 0 && IsMenuEntry(list[0]))
                    {
                        insertIndex = 1;
                        below = true;
                    }

                    return (row, below, Math.Min(insertIndex, list.Count));
                }

                // Pointer is somewhere under the rows, in the grid's empty space or past
                // the bottom of the grid itself, both read as plain white space to the
                // user. Land the drop after the lowest row, which can only be the last
                // item since rows fill the view when scrolled mid list. No lower bound
                // on y, anything below the last row within the grid's width means append.
                if (bottomRow != null && y >= bottomEdge &&
                    pos.X >= 0 && pos.X < dg1.Bounds.Width)
                {
                    int index = list.IndexOf(bottomItem);
                    if (index >= 0)
                        return (bottomRow, true, Math.Min(index + 1, list.Count));
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ShowDropLine(DataGridRow row, bool below)
        {
            if (DropLine == null || dg1 == null)
                return;

            var top = row.TranslatePoint(new Point(0, 0), dg1);
            if (top == null)
            {
                HideDropLine();
                return;
            }

            double y = top.Value.Y;
            if (below)
                y += row.Bounds.Height;

            DropLine.Margin = new Thickness(0, y - 1, 0, 0);
            DropLine.IsVisible = true;
        }

        private void HideDropLine()
        {
            if (DropLine != null)
                DropLine.IsVisible = false;
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
                await Manager.Preload();
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
}
