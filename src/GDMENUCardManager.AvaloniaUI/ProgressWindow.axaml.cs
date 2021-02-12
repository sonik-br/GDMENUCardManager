using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;
using MessageBox.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GDMENUCardManager.Core.Interface;

namespace GDMENUCardManager
{
    public class ProgressWindow : Window, INotifyPropertyChanged, IProgressWindow
    {
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

        public event PropertyChangedEventHandler PropertyChanged;

        public ProgressWindow()
        {
            InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
            //this.OpenDevTools();
#endif
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
