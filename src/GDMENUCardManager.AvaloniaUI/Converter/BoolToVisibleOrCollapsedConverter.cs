//using Avalonia.Data;
//using Avalonia.Data.Converters;
//using System;
//using System.Globalization;


//namespace GDMENUCardManager.Converter
//{
//    class BoolToVisibleOrCollapsedConverter : IValueConverter
//    {
//        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
//        {
//            if (value == null)
//                return Binding.DoNothing;

//            return (bool)value ? Visibility.Visible : Visibility.Collapsed;
//        }

//        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
//        {
//            throw new NotImplementedException();
//        }
//    }
//}
