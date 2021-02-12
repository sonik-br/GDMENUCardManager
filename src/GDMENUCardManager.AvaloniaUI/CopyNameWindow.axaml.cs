using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core;
using Avalonia.Interactivity;

namespace GDMENUCardManager
{
    public class CopyNameWindow : Window, INotifyPropertyChanged
    {
        public bool OnCard { get; set; }
        public bool NotOnCard { get; set; } = true;
        public bool FolderName { get; set; } = true;
        public bool ParseTosec { get; set; } = true;

        public CopyNameWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
