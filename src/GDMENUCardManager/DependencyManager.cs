using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using SevenZip;

namespace GDMENUCardManager
{
    public class DependencyManager : IDependencyManager
    {
        private Window getMainWindow() => App.Current.MainWindow;

        public IProgressWindow CreateAndShowProgressWindow()
        {
            var p = new ProgressWindow() { Owner = getMainWindow() };
            p.Show();
            return p;
        }

        public GdItem[] GdiShrinkWindowShowDialog(IEnumerable<GdItem> items)
        {
            var w = new GdiShrinkWindow(items) { Owner = getMainWindow() };
            return w.ShowDialog().GetValueOrDefault() ? w.List.Where(x => x.Value).Select(x => x.Key).ToArray() : null;
        }

        public ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return new ValueTask<bool>(MessageBox.Show(getMainWindow(), text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        }

        public void ExtractArchive(string archivePath, string extractTo)
        {
            using (var extr = new SevenZipExtractor(archivePath))
            {
                extr.PreserveDirectoryStructure = false;
                extr.ExtractArchive(extractTo);
            }
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var compressedfile = new SevenZipExtractor(archivePath))
                foreach (var item in compressedfile.ArchiveFileData.Where(x => !x.IsDirectory))
                    if (!toReturn.ContainsKey(item.FileName))
                        toReturn.Add(item.FileName, (long)item.Size);
            return toReturn;
        }
    }
}
