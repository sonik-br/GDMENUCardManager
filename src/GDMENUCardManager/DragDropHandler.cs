using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GongSolutions.Wpf.DragDrop;
using GongSolutions.Wpf.DragDrop.Utilities;

namespace GDMENUCardManager
{
    internal static class DragDropHandler
    {
        public static void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo == null)
            {
                if (dropInfo.Data is DataObject data && data.ContainsFileDropList())
                    dropInfo.Effects = DragDropEffects.Copy;
            }
            else if (DefaultDropHandler.CanAcceptData(dropInfo))
            {
                dropInfo.Effects = DragDropEffects.Move;
            }

            if (dropInfo.Effects != DragDropEffects.None)
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        }

        public static async Task Drop(IDropInfo dropInfo)
        {
            var invalid = new List<string>();

            var insertIndex = dropInfo.UnfilteredInsertIndex;
            var destinationList = dropInfo.TargetCollection.TryGetList();

            if (dropInfo.DragInfo == null)
            {
                if (!(dropInfo.Data is DataObject data) || !data.ContainsFileDropList())
                    return;

                foreach (var o in data.GetFileDropList())
                {
                    try
                    {
                        var toInsert = await ImageHelper.CreateGdItemAsync(o);
                        destinationList.Insert(insertIndex++, toInsert);
                    }
                    catch
                    {
                        invalid.Add(o);
                    }
                }
            }
            else
            {
                var data = DefaultDropHandler.ExtractData(dropInfo.Data).OfType<object>().ToList();

                var sourceList = dropInfo.DragInfo.SourceCollection.TryGetList();
                if (sourceList != null)
                {
                    foreach (var o in data)
                    {
                        var index = sourceList.IndexOf(o);
                        if (index != -1)
                        {
                            sourceList.RemoveAt(index);
                            if (destinationList != null && Equals(sourceList, destinationList) && index < insertIndex)
                                --insertIndex;
                        }
                    }
                }

                if (destinationList != null)
                    foreach (var o in data)
                        destinationList.Insert(insertIndex++, o);
            }

            if (invalid.Any())
                throw new InvalidDropException(string.Join(Environment.NewLine, invalid));
        }
    }

    internal class InvalidDropException : Exception
    {
        public InvalidDropException(string message) : base(message)
        {
        }
    }
}
