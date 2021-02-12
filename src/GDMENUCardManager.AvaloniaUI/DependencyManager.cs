using Avalonia.Controls;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;

namespace GDMENUCardManager
{
    public class DependencyManager : IDependencyManager
    {
        private Window getMainWindow() => ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).MainWindow;

        public IProgressWindow CreateAndShowProgressWindow()
        {
            var p = new ProgressWindow();
            p.Show(getMainWindow());
            return p;
        }

        public GdItem[] GdiShrinkWindowShowDialog(System.Collections.Generic.IEnumerable<GdItem> items) => null;

        public async ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return await MessageBoxManager.GetMessageBoxStandardWindow(caption, text, ButtonEnum.YesNo).ShowDialog(getMainWindow()) == ButtonResult.Yes;
        }


        public void ExtractArchive(string archivePath, string extractTo)
        {
            using (var compressedfile = ZipFile.OpenRead(archivePath))
            {
                compressedfile.ExtractToDirectory(extractTo, true);
                //move files from subfolders to root folder
                foreach (var file in compressedfile.Entries.Where(x => !string.IsNullOrEmpty(x.Name) && x.FullName.Split('/').Length > 1))
                    File.Move(Path.Combine(extractTo, file.FullName), Path.Combine(extractTo, file.Name), true);

                //todo delete subfolders?
                //foreach (var dir in compressedfile.Entries.Where(x => string.IsNullOrEmpty(x.Name) && x.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 1))
                //    Directory.Delete(Path.Combine(extractTo, dir.FullName), true);
            }
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var compressedfile = ZipFile.OpenRead(archivePath))
                foreach (var item in compressedfile.Entries.Where(x => !string.IsNullOrEmpty(x.Name)))
                    if (!toReturn.ContainsKey(item.FullName))
                        toReturn.Add(item.FullName, item.Length);
            return toReturn;
        }
    }
}
