using System;
using System.Globalization;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    public class FolderNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
                return (int)value == 0 ? null : value.ToString();
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

