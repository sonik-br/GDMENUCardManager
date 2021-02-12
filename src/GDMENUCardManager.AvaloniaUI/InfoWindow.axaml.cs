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

namespace GDMENUCardManager
{
    public class InfoWindow : Window, INotifyPropertyChanged
    {
        public string FileInfo { get; }
        public string IpInfo { get; }

        private string _LabelText = "Loading...";
        public string LabelText
        {
            get { return _LabelText; }
            private set { _LabelText = value; RaisePropertyChanged(); }
        }

        private Avalonia.Media.Imaging.Bitmap _GdTexture = null;
        public Avalonia.Media.Imaging.Bitmap GdTexture
        {
            get { return _GdTexture; }
            private set { _GdTexture = value; RaisePropertyChanged(); }
        }

        private GdItem item;
        public event PropertyChangedEventHandler PropertyChanged;

        public InfoWindow() { }

        public InfoWindow(GdItem item)
        {
            InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
            //this.OpenDevTools();
#endif

            this.Opened += InfoWindow_Opened;

            this.item = item;

            string vga = item.Ip.Vga ? "   VGA" : null;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Folder:");
            sb.AppendLine(Path.GetFileName(item.FullFolderPath));
            sb.AppendLine();
            sb.AppendLine("File:");
            sb.AppendLine(Path.GetFileName(item.ImageFile));

            FileInfo = sb.ToString();

            if (item.FileFormat == FileFormat.Uncompressed)
            {
                sb.Clear();
                sb.AppendLine(item.Ip.Name);
                sb.AppendLine();
                sb.AppendLine($"{item.Ip.Version}   DISC {item.Ip.Disc}{vga}");
                sb.AppendLine($"CRC: {item.Ip.CRC}   Product: {item.Ip.ProductNumber}");

                if (item.Ip.SpecialDisc != SpecialDisc.None)
                {
                    sb.AppendLine();
                    sb.AppendLine("Detected as: " + item.Ip.SpecialDisc);
                }
                IpInfo = sb.ToString();
            }
            else
            {
                IpInfo = "Compressed file";
            }

            this.KeyUp += (ss, ee) => { if (ee.Key == Avalonia.Input.Key.Escape) Close(); };
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

        private async void InfoWindow_Opened(object sender, EventArgs e)
        {
            await Task.Delay(100);
            try
            {
                if (item.FileFormat == FileFormat.SevenZip)
                    throw new Exception("Can't load from compressed files.");

                var filePath = Path.Combine(item.FullFolderPath, item.ImageFile);

                var gdtexture = await Task.Run(() => ImageHelper.GetGdText(filePath));

                if (gdtexture == null)
                {
                    throw new Exception("File not found");
                }
                else
                {
                    var decoded = await Task.Run(() => new PuyoTools.PvrTexture().GetDecoded(gdtexture));

                    using (var memory = new MemoryStream())
                    {
                        byte[] data = decoded.Item1;
                        using (var writeableBitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(decoded.Item2, decoded.Item3), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul))
                        {
                            using (var l = writeableBitmap.Lock())
                                System.Runtime.InteropServices.Marshal.Copy(data, 0, l.Address, data.Length);

                            writeableBitmap.Save(memory);
                            memory.Position = 0;
                            GdTexture = new Avalonia.Media.Imaging.Bitmap(memory);
                            LabelText = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LabelText = ex.Message;
            }
        }
    }
}
