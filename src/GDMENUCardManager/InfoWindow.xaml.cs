using GDMENUCardManager.Core;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for infoWindow.xaml
    /// </summary>
    public partial class InfoWindow : Window, INotifyPropertyChanged
    {
        public string FileInfo { get; }
        public string IpInfo { get; }

        private string _LabelText = "Loading...";
        public string LabelText
        {
            get { return _LabelText; }
            private set { _LabelText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelText))); }
        }

        private BitmapImage _GdTexture = null;
        public BitmapImage GdTexture
        {
            get { return _GdTexture; }
            private set { _GdTexture = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GdTexture))); }
        }

        private GdItem item;

        public event PropertyChangedEventHandler PropertyChanged;


        public InfoWindow(GdItem item)
        {
            InitializeComponent();
            Loaded += InfoWindow_Loaded;
            
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

            this.KeyUp += (ss, ee) => { if (ee.Key == Key.Escape) Close(); };
            DataContext = this;

        }

        private async void InfoWindow_Loaded(object sender, RoutedEventArgs e)
        {
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

                    using (MemoryStream memory = new MemoryStream())
                    {
                        byte[] data = decoded.Item1;

                        using (Bitmap img = new Bitmap(decoded.Item2, decoded.Item3, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        {
                            BitmapData bitmapData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, img.PixelFormat);
                            System.Runtime.InteropServices.Marshal.Copy(data, 0, bitmapData.Scan0, data.Length);
                            img.UnlockBits(bitmapData);

                            img.Save(memory, ImageFormat.Png);
                            memory.Position = 0;
                            BitmapImage bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = memory;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            GdTexture = bitmapImage;
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
