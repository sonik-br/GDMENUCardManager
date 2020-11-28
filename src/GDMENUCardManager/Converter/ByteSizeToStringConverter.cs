using System;
using System.Globalization;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    class ByteSizeToStringConverter : IValueConverter
    {
        public static bool UseBinaryString = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var bsize = (ByteSizeLib.ByteSize)value;
            return UseBinaryString ? bsize.ToBinaryString() : bsize.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
