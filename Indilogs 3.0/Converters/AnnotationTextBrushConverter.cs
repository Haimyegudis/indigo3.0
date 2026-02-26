using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IndiLogs_3._0.Converters
{
    /// <summary>
    /// Converter that returns Black for annotation text in Dark Mode (yellow background)
    /// </summary>
    public class AnnotationTextBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Always return black text for annotations (yellow background)
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
