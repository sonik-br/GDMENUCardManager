using Avalonia.Controls;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using static Microsoft.IO.RecyclableMemoryStreamManager;

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
            return await MessageBoxManager.GetMessageBoxStandard(caption, text, ButtonEnum.YesNo).ShowWindowDialogAsync(getMainWindow()) == ButtonResult.Yes;
        }


        public void ExtractArchive(string archivePath, string extractTo)
        {
            var extOptions = new ExtractionOptions()
            {
                ExtractFullPath = false,
                Overwrite = true
            };

            using (var stream = File.OpenRead(archivePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            archive.WriteToDirectory(extractTo, extOptions);
            //using (var reader = archive.ExtractAllEntries())
            //    reader.WriteAllToDirectory(extractTo, extOptions);
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var stream = File.OpenRead(archivePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
                foreach (var item in archive.Entries)
                    if (!item.IsDirectory && !toReturn.ContainsKey(item.Key))
                        toReturn.Add(item.Key, item.Size);
            return toReturn;
        }
    }
}
