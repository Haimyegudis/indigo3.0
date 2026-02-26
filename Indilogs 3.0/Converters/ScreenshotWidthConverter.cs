using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IndiLogs_3._0.Converters
{
    /// <summary>
    /// For single screenshot: returns NaN (auto-fit to container).
    /// For multiple screenshots: returns the zoom pixel width.
    /// </summary>
    public class ScreenshotWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double zoom &&
                values[1] is int count)
            {
                return count == 1 ? double.NaN : zoom;
            }
            return double.NaN;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// For single screenshot: converts ScreenshotZoom to a scale factor relative to base (400).
    /// Returns 1.0 when zoom is at default (400), scales proportionally.
    /// For multiple screenshots: returns 1.0 (zoom is handled by Width binding instead).
    /// </summary>
    public class ScreenshotScaleConverter : IMultiValueConverter
    {
        private const double BaseZoom = 400.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double zoom &&
                values[1] is int count)
            {
                if (count == 1)
                {
                    // Scale relative to base: zoom=400 → 1.0, zoom=800 → 2.0, zoom=200 → 0.5
                    return Math.Max(0.1, zoom / BaseZoom);
                }
            }
            return 1.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
