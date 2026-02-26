using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace IndiLogs_3._0.Converters
{
    /// <summary>
    /// Safely reads a value from a Dictionary&lt;string, string&gt; by key.
    /// Used for DataGrid columns that bind to LogEntry.ExtraFields[fieldName].
    /// Returns empty string if the key is missing — avoids KeyNotFoundException.
    ///
    /// Usage:  Binding Path=ExtraFields, Converter={StaticResource ExtraFieldConverter}, ConverterParameter=FieldName
    /// </summary>
    public class ExtraFieldConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Dictionary<string, string> dict && parameter is string key)
            {
                return dict.TryGetValue(key, out string val) ? val : string.Empty;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
