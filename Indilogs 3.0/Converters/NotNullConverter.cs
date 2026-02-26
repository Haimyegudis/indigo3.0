using System;
using System.Globalization;
using System.Windows.Data;

namespace IndiLogs_3._0.Converters
{
    /// <summary>
    /// Converter that returns true if the value is not null, false otherwise.
    /// Used to check if CustomColor has a value.
    /// </summary>
    public class NotNullConverter : IValueConverter
    {
        public static readonly NotNullConverter Instance = new NotNullConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
