using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SkiaSharp;

namespace IndiLogs_3._0.Converters
{
    public class SKColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SKColor skColor)
            {
                return Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color wpfColor)
            {
                return new SKColor(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A);
            }
            return SKColors.Gray;
        }
    }
}
