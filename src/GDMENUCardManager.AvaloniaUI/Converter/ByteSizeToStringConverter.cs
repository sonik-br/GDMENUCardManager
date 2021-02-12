using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GDMENUCardManager.Converter
{
    //todo not public
    public class ByteSizeToStringConverter : IValueConverter
    {
        public static bool UseBinaryString = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var bsize = (ByteSizeLib.ByteSize)value;
            return UseBinaryString ? bsize.ToBinaryString() : bsize.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //todo verify
            return Avalonia.Data.BindingNotification.UnsetValue;
            //return new Avalonia.Data.BindingNotification();
        }
    }
}
