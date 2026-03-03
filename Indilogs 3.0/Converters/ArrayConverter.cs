#nullable disable
﻿using System;
using System.Globalization;
using System.Windows.Data;

namespace IndiLogs_3._0.Converters
{
    public class ArrayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.Clone(); // Returns the values array as-is for the command
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}