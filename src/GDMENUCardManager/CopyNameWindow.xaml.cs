using System.Windows;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for CopyNameWindow.xaml
    /// </summary>
    public partial class CopyNameWindow : Window
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
