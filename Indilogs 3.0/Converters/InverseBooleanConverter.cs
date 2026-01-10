using System;
using System.Globalization;
using System.Windows.Data;

// שים לב: הורדנו את ".Converters" כדי שזה יהיה בראשי
namespace IndiLogs_3._0
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue) return !booleanValue;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue) return !booleanValue;
            return false;
        }
    }
}