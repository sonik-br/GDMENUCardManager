using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for TextWindow.xaml
    /// </summary>
    public partial class TextWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBTN = 0x10000;
        private const int WS_MINIMIZEBTN = 0x20000;

        public string Text { get; set; }

        public TextWindow(string title, string text)
        {
            InitializeComponent();
            this.Title = title;
            this.Text = text;
            DataContext = this;

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~(WS_MAXIMIZEBTN | WS_MINIMIZEBTN));
            };
        }
    }
}
