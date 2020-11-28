using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for GdiShrinkWindow.xaml
    /// </summary>
    public partial class GdiShrinkWindow : Window
    {
        public class ItemToShrink : INotifyPropertyChanged
        {
            public GdItem Key { get; set; }
            private bool myVar;

            //public bool Value
            //{
            //    get { return myVar; }
            //    set { myVar = value; }
            //}
            private bool _Value;
            public bool Value
            {
                get { return _Value; }
                set { _Value = value; RaisePropertyChanged(); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public List<ItemToShrink> List { get; private set; } = new List<ItemToShrink>();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBTN = 0x10000;
        private const int WS_MINIMIZEBTN = 0x20000;

        public GdiShrinkWindow(GdItem[] items)
        {
            InitializeComponent();
            DataContext = this;

            foreach (var item in items)
                List.Add(new ItemToShrink { Key = item });

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~(WS_MAXIMIZEBTN | WS_MINIMIZEBTN));
            };
            this.Closing += (s, e) =>
            {
                if (!DialogResult.GetValueOrDefault())
                    e.Cancel = true;
            };
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void ButtonAll_Click(object sender, RoutedEventArgs e)
        {
            List.ForEach(x => x.Value = true);
        }

        private void ButtonNone_Click(object sender, RoutedEventArgs e)
        {
            List.ForEach(x => x.Value = false);
        }
    }
}
