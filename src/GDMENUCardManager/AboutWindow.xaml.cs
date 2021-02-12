using GDMENUCardManager.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window, INotifyPropertyChanged
    {
        public string CurrentVersion => Constants.Version;

        private string _LatestVersion = "?";
        public string LatestVersion
        {
            get { return _LatestVersion; }
            set { _LatestVersion = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatestVersion))); }
        }

        private static HttpClient _Client = null;
        private HttpClient Client
        {
            get
            {
                if (_Client == null)
                {
                    _Client = new HttpClient();
                    _Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
                    _Client.DefaultRequestHeaders.UserAgent.ParseAdd(@"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:84.0) Gecko/20100101 Firefox/84.0");
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                }
                return _Client;
            }
        }

        private readonly Queue<Key> lastKeys = new Queue<Key>(10);
        private readonly Key[] konamiCodeKeys = new Key[] { Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A };

        public event PropertyChangedEventHandler PropertyChanged;


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

        private async void ButtonVersion_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var oldContent = btn.Content;
            btn.IsEnabled = false;
            btn.Content = "Checking...";
            try
            {
                var token = new CancellationTokenSource(10000).Token;//for time out
                using (var response = await Client.GetAsync("https://api.github.com/repos/sonik-br/GDMENUCardManager/releases/latest", token))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        var obj = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                        LatestVersion = obj.RootElement.GetProperty("tag_name").GetString();
                    }
                }
            }
            catch(System.Exception ex)
            {
                LatestVersion = "Error";
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = oldContent;
            }
        }
    }
}
