using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GDMENUCardManager.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GDMENUCardManager
{
    public partial class GdiShrinkWindow : Window
    {
        public class ItemToShrink : INotifyPropertyChanged
        {
            public GdItem Key { get; set; }

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

        // Null unless Proceed was clicked.
        public GdItem[] Result { get; private set; }

        public GdiShrinkWindow()
        {
            InitializeComponent();
        }

        public GdiShrinkWindow(IEnumerable<GdItem> items, string title = null) : this()
        {
            if (!string.IsNullOrEmpty(title))
                Title = title;

            foreach (var item in items)
                List.Add(new ItemToShrink { Key = item });

            // The grid reads the list as soon as the context is set, and a plain
            // list raises nothing, so it has to be filled first.
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            Result = List.Where(x => x.Value).Select(x => x.Key).ToArray();
            Close();
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
