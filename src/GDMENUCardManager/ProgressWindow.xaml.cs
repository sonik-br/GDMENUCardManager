using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window, INotifyPropertyChanged
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBTN = 0x10000;
        private const int WS_MINIMIZEBTN = 0x20000;


        private int _TotalItems;
        public int TotalItems
        {
            get { return _TotalItems; }
            set { _TotalItems = value; RaisePropertyChanged(); }
        }


        private int _ProcessedItems;
        public int ProcessedItems
        {
            get { return _ProcessedItems; }
            set { _ProcessedItems = value; RaisePropertyChanged(); }
        }


        private string _TextContent;
        public string TextContent
        {
            get { return _TextContent; }
            set { _TextContent = value; RaisePropertyChanged(); }
        }


        public ProgressWindow()
        {
            InitializeComponent();
            DataContext = this;

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~(WS_MAXIMIZEBTN | WS_MINIMIZEBTN));
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
