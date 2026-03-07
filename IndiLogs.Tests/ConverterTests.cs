using IndiLogs_3._0.Converters;
using System;
using System.Globalization;
using System.Windows;
using Xunit;

namespace IndiLogs.Tests
{
    public class ConverterTests
    {
        // ── BoolToVisibilityConverter ──

        [Fact]
        public void BoolToVisibility_True_ReturnsVisible()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void BoolToVisibility_False_ReturnsCollapsed()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void BoolToVisibility_NonBool_ReturnsCollapsed()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.Convert("notabool", typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void BoolToVisibility_Null_ReturnsCollapsed()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_Visible_ReturnsTrue()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_Collapsed_ReturnsFalse()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_NonVisibility_ReturnsFalse()
        {
            var converter = new BoolToVisibilityConverter();
            var result = converter.ConvertBack("notvisibility", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        // ── InverseBoolToVisibilityConverter ──

        [Fact]
        public void InverseBoolToVisibility_True_ReturnsCollapsed()
        {
            var converter = new InverseBoolToVisibilityConverter();
            var result = converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void InverseBoolToVisibility_False_ReturnsVisible()
        {
            var converter = new InverseBoolToVisibilityConverter();
            var result = converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void InverseBoolToVisibility_NonBool_ReturnsVisible()
        {
            var converter = new InverseBoolToVisibilityConverter();
            var result = converter.Convert(42, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void InverseBoolToVisibility_ConvertBack_Visible_ReturnsFalse()
        {
            var converter = new InverseBoolToVisibilityConverter();
            var result = converter.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void InverseBoolToVisibility_ConvertBack_Collapsed_ReturnsTrue()
        {
            var converter = new InverseBoolToVisibilityConverter();
            var result = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        // ── NullToVisibilityConverter ──

        [Fact]
        public void NullToVisibility_NonNull_ReturnsVisible()
        {
            var converter = new NullToVisibilityConverter();
            var result = converter.Convert("hello", typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void NullToVisibility_Null_ReturnsCollapsed()
        {
            var converter = new NullToVisibilityConverter();
            var result = converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void NullToVisibility_EmptyString_ReturnsCollapsed()
        {
            var converter = new NullToVisibilityConverter();
            var result = converter.Convert("", typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void NullToVisibility_ConvertBack_Throws()
        {
            var converter = new NullToVisibilityConverter();
            Assert.Throws<NotImplementedException>(() =>
                converter.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture));
        }

        // ── BoolToArrowConverter ──

        [Fact]
        public void BoolToArrow_True_ReturnsTrueValue()
        {
            var converter = new BoolToArrowConverter();
            var result = converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("◀", result);
        }

        [Fact]
        public void BoolToArrow_False_ReturnsFalseValue()
        {
            var converter = new BoolToArrowConverter();
            var result = converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("▶", result);
        }

        [Fact]
        public void BoolToArrow_NonBool_ReturnsFalseValue()
        {
            var converter = new BoolToArrowConverter();
            var result = converter.Convert(42, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("▶", result);
        }

        [Fact]
        public void BoolToArrow_CustomValues()
        {
            var converter = new BoolToArrowConverter { TrueValue = "UP", FalseValue = "DOWN" };
            Assert.Equal("UP", converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture));
            Assert.Equal("DOWN", converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void BoolToArrow_ConvertBack_Throws()
        {
            var converter = new BoolToArrowConverter();
            Assert.Throws<NotImplementedException>(() =>
                converter.ConvertBack("◀", typeof(bool), null, CultureInfo.InvariantCulture));
        }

        // ── ScreenshotWidthConverter ──

        [Fact]
        public void ScreenshotWidth_SingleImage_ReturnsNaN()
        {
            var converter = new ScreenshotWidthConverter();
            var result = converter.Convert(new object?[] { 400.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True(double.IsNaN((double)result));
        }

        [Fact]
        public void ScreenshotWidth_MultipleImages_ReturnsZoom()
        {
            var converter = new ScreenshotWidthConverter();
            var result = converter.Convert(new object?[] { 600.0, 3 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(600.0, result);
        }

        [Fact]
        public void ScreenshotWidth_InvalidValues_ReturnsNaN()
        {
            var converter = new ScreenshotWidthConverter();
            var result = converter.Convert(new object?[] { "notdouble", 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True(double.IsNaN((double)result));
        }

        [Fact]
        public void ScreenshotWidth_EmptyValues_ReturnsNaN()
        {
            var converter = new ScreenshotWidthConverter();
            var result = converter.Convert(Array.Empty<object?>(), typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True(double.IsNaN((double)result));
        }

        // ── ScreenshotScaleConverter ──

        [Fact]
        public void ScreenshotScale_SingleImage_DefaultZoom_Returns1()
        {
            var converter = new ScreenshotScaleConverter();
            var result = converter.Convert(new object?[] { 400.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, result);
        }

        [Fact]
        public void ScreenshotScale_SingleImage_DoubleZoom_Returns2()
        {
            var converter = new ScreenshotScaleConverter();
            var result = converter.Convert(new object?[] { 800.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(2.0, result);
        }

        [Fact]
        public void ScreenshotScale_SingleImage_MinimumClamp()
        {
            var converter = new ScreenshotScaleConverter();
            var result = converter.Convert(new object?[] { 0.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(0.1, (double)result, 5);
        }

        [Fact]
        public void ScreenshotScale_MultipleImages_Returns1()
        {
            var converter = new ScreenshotScaleConverter();
            var result = converter.Convert(new object?[] { 800.0, 3 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, result);
        }

        [Fact]
        public void ScreenshotScale_InvalidValues_Returns1()
        {
            var converter = new ScreenshotScaleConverter();
            var result = converter.Convert(new object?[] { "bad", "bad" }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, result);
        }
    }
}
