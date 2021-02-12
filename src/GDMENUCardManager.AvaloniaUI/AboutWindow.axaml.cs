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
using GDMENUCardManager.Core;
using System.Threading;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Media;

namespace GDMENUCardManager
{
    public class AboutWindow : Window, INotifyPropertyChanged
    {
        public string CurrentVersion => Constants.Version;

        private string _LatestVersion = "?";
        public string LatestVersion
        {
            get { return _LatestVersion; }
            set { _LatestVersion = value; RaisePropertyChanged(); }
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
#if DEBUG
            //this.AttachDevTools();
            //this.OpenDevTools();
#endif
            this.KeyDown += AboutWindow_KeyDown;
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

        private void AboutWindow_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
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
                    if (((Grid)Content).Children[0] is Image == false && Application.Current.TryFindResource("dreamcastLogoDrawingImage", out object img))
                        ((Grid)Content).Children.Insert(0, new Image { Source = (DrawingImage)img });
                    Title = "Dreamcast Lives!";
                    lastKeys.Clear();
                }
            }
        }

        private void ButtonLink_Click(object sender, RoutedEventArgs e)
        {
            var url = @"https://github.com/sonik-br/GDMENUCardManager/";
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
            catch
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
