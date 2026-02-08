using System;
using System.Globalization;
using System.Windows.Data;

namespace IndiLogs_3._0.Converters
{
    public class EnumBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
                return parameter;
            return Binding.DoNothing;
        }
    }
}
