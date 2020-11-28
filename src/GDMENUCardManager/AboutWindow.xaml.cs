using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public string TitleAndVersion { get; set; }

        private readonly Queue<Key> lastKeys = new Queue<Key>(10);
        private readonly Key[] konamiCodeKeys = new Key[] { Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A };

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
            this.PreviewKeyDown += AboutWindow_PreviewKeyDown;
        }

        private void AboutWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else
            {
                if (lastKeys.Count == 10)
                    lastKeys.Dequeue();
                lastKeys.Enqueue(e.Key);
                if (lastKeys.Count == 10 && lastKeys.SequenceEqual(konamiCodeKeys))
                {
                    if (((Grid)Content).Children[0] is Image == false)
                        ((Grid)Content).Children.Insert(0, new Image { Source = (DrawingImage)Application.Current.TryFindResource("dreamcastLogoDrawingImage") });
                    Title = "Dreamcast Lives!";
                    lastKeys.Clear();
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
