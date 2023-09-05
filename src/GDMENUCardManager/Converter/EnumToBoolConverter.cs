﻿using System;
using System.Globalization;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null && value is Enum && parameter is Enum)
                return value.Equals(parameter);
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && (bool)value)
                return parameter;
            return Core.MenuKind.None;
        }
    }
}
