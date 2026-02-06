using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IndiLogs_3._0.Converters
{
    /// <summary>
    /// Converts a boolean IsSelected value to a border brush color.
    /// Returns blue when selected, transparent when not selected.
    /// </summary>
    public class BoolToSelectionBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush SelectedBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // #3B82F6 blue
        private static readonly SolidColorBrush UnselectedBrush = new SolidColorBrush(Colors.Transparent);

        static BoolToSelectionBrushConverter()
        {
            SelectedBrush.Freeze();
            UnselectedBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return SelectedBrush;
            }
            return UnselectedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
