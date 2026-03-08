using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using IndiLogs_3._0;
using IndiLogs_3._0.Converters;
using SkiaSharp;
using Xunit;

namespace IndiLogs.Tests
{
    public class ConverterAndConstantsTests
    {
        // ── BoolToVisibilityConverter ──

        [Fact]
        public void BoolToVisibility_True_ReturnsVisible()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void BoolToVisibility_False_ReturnsCollapsed()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void BoolToVisibility_NonBool_ReturnsCollapsed()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.Convert("not a bool", typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void BoolToVisibility_Null_ReturnsCollapsed()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_Visible_ReturnsTrue()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_Collapsed_ReturnsFalse()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_Hidden_ReturnsFalse()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.ConvertBack(Visibility.Hidden, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void BoolToVisibility_ConvertBack_NonVisibility_ReturnsFalse()
        {
            var conv = new BoolToVisibilityConverter();
            var result = conv.ConvertBack("not a visibility", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        // ── InverseBoolToVisibilityConverter ──

        [Fact]
        public void InverseBoolToVisibility_True_ReturnsCollapsed()
        {
            var conv = new InverseBoolToVisibilityConverter();
            var result = conv.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void InverseBoolToVisibility_False_ReturnsVisible()
        {
            var conv = new InverseBoolToVisibilityConverter();
            var result = conv.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void InverseBoolToVisibility_NonBool_ReturnsVisible()
        {
            var conv = new InverseBoolToVisibilityConverter();
            var result = conv.Convert(42, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void InverseBoolToVisibility_ConvertBack_Visible_ReturnsFalse()
        {
            var conv = new InverseBoolToVisibilityConverter();
            var result = conv.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void InverseBoolToVisibility_ConvertBack_Collapsed_ReturnsTrue()
        {
            var conv = new InverseBoolToVisibilityConverter();
            var result = conv.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void InverseBoolToVisibility_ConvertBack_NonVisibility_ReturnsTrue()
        {
            var conv = new InverseBoolToVisibilityConverter();
            var result = conv.ConvertBack("nope", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        // ── NullToVisibilityConverter ──

        [Fact]
        public void NullToVisibility_NonNull_ReturnsVisible()
        {
            var conv = new NullToVisibilityConverter();
            var result = conv.Convert("hello", typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void NullToVisibility_Null_ReturnsCollapsed()
        {
            var conv = new NullToVisibilityConverter();
            var result = conv.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void NullToVisibility_EmptyString_ReturnsCollapsed()
        {
            var conv = new NullToVisibilityConverter();
            var result = conv.Convert("", typeof(Visibility), null, CultureInfo.InvariantCulture);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void NullToVisibility_ConvertBack_Throws()
        {
            var conv = new NullToVisibilityConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture));
        }

        // ── BoolToArrowConverter ──

        [Fact]
        public void BoolToArrow_True_ReturnsLeftArrow()
        {
            var conv = new BoolToArrowConverter();
            var result = conv.Convert(true, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("◀", result);
        }

        [Fact]
        public void BoolToArrow_False_ReturnsRightArrow()
        {
            var conv = new BoolToArrowConverter();
            var result = conv.Convert(false, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("▶", result);
        }

        [Fact]
        public void BoolToArrow_NonBool_ReturnsFalseValue()
        {
            var conv = new BoolToArrowConverter();
            var result = conv.Convert(42, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("▶", result);
        }

        [Fact]
        public void BoolToArrow_CustomValues()
        {
            var conv = new BoolToArrowConverter { TrueValue = "UP", FalseValue = "DOWN" };
            Assert.Equal("UP", conv.Convert(true, typeof(string), null, CultureInfo.InvariantCulture));
            Assert.Equal("DOWN", conv.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void BoolToArrow_ConvertBack_Throws()
        {
            var conv = new BoolToArrowConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack("◀", typeof(bool), null, CultureInfo.InvariantCulture));
        }

        // ── InverseBooleanConverter ──

        [Fact]
        public void InverseBoolean_True_ReturnsFalse()
        {
            var conv = new InverseBooleanConverter();
            var result = conv.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void InverseBoolean_False_ReturnsTrue()
        {
            var conv = new InverseBooleanConverter();
            var result = conv.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void InverseBoolean_NonBool_ReturnsFalse()
        {
            var conv = new InverseBooleanConverter();
            var result = conv.Convert("nope", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void InverseBoolean_ConvertBack_True_ReturnsFalse()
        {
            var conv = new InverseBooleanConverter();
            var result = conv.ConvertBack(true, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void InverseBoolean_ConvertBack_False_ReturnsTrue()
        {
            var conv = new InverseBooleanConverter();
            var result = conv.ConvertBack(false, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void InverseBoolean_ConvertBack_NonBool_ReturnsFalse()
        {
            var conv = new InverseBooleanConverter();
            var result = conv.ConvertBack(42, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        // ── NotNullConverter ──

        [Fact]
        public void NotNull_WithValue_ReturnsTrue()
        {
            var result = NotNullConverter.Instance.Convert("hello", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void NotNull_WithNull_ReturnsFalse()
        {
            var result = NotNullConverter.Instance.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void NotNull_WithObject_ReturnsTrue()
        {
            var result = NotNullConverter.Instance.Convert(42, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void NotNull_ConvertBack_Throws()
        {
            Assert.Throws<NotImplementedException>(() =>
                NotNullConverter.Instance.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture));
        }

        // ── EnumBoolConverter ──

        [Fact]
        public void EnumBool_MatchingValue_ReturnsTrue()
        {
            var conv = new EnumBoolConverter();
            var result = conv.Convert(DayOfWeek.Monday, typeof(bool), DayOfWeek.Monday, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void EnumBool_NonMatchingValue_ReturnsFalse()
        {
            var conv = new EnumBoolConverter();
            var result = conv.Convert(DayOfWeek.Monday, typeof(bool), DayOfWeek.Friday, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void EnumBool_NullValue_ReturnsFalse()
        {
            var conv = new EnumBoolConverter();
            var result = conv.Convert(null, typeof(bool), DayOfWeek.Monday, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void EnumBool_NullParameter_ReturnsFalse()
        {
            var conv = new EnumBoolConverter();
            var result = conv.Convert(DayOfWeek.Monday, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void EnumBool_ConvertBack_True_ReturnsParameter()
        {
            var conv = new EnumBoolConverter();
            var result = conv.ConvertBack(true, typeof(DayOfWeek), DayOfWeek.Friday, CultureInfo.InvariantCulture);
            Assert.Equal(DayOfWeek.Friday, result);
        }

        [Fact]
        public void EnumBool_ConvertBack_False_ReturnsDoNothing()
        {
            var conv = new EnumBoolConverter();
            var result = conv.ConvertBack(false, typeof(DayOfWeek), DayOfWeek.Friday, CultureInfo.InvariantCulture);
            Assert.Equal(Binding.DoNothing, result);
        }

        [Fact]
        public void EnumBool_ConvertBack_NonBool_ReturnsDoNothing()
        {
            var conv = new EnumBoolConverter();
            var result = conv.ConvertBack("nope", typeof(DayOfWeek), DayOfWeek.Friday, CultureInfo.InvariantCulture);
            Assert.Equal(Binding.DoNothing, result);
        }

        [Fact]
        public void EnumBool_ConvertBack_TrueNullParam_ReturnsDoNothing()
        {
            var conv = new EnumBoolConverter();
            var result = conv.ConvertBack(true, typeof(DayOfWeek), null, CultureInfo.InvariantCulture);
            Assert.Equal(Binding.DoNothing, result);
        }

        // ── ExtraFieldConverter ──

        [Fact]
        public void ExtraField_ValidDict_ReturnsValue()
        {
            var conv = new ExtraFieldConverter();
            var dict = new Dictionary<string, string> { { "Key1", "Value1" } };
            var result = conv.Convert(dict, typeof(string), "Key1", CultureInfo.InvariantCulture);
            Assert.Equal("Value1", result);
        }

        [Fact]
        public void ExtraField_MissingKey_ReturnsEmpty()
        {
            var conv = new ExtraFieldConverter();
            var dict = new Dictionary<string, string> { { "Key1", "Value1" } };
            var result = conv.Convert(dict, typeof(string), "Missing", CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ExtraField_NullDict_ReturnsEmpty()
        {
            var conv = new ExtraFieldConverter();
            var result = conv.Convert(null, typeof(string), "Key1", CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ExtraField_NullParameter_ReturnsEmpty()
        {
            var conv = new ExtraFieldConverter();
            var dict = new Dictionary<string, string> { { "Key1", "Value1" } };
            var result = conv.Convert(dict, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ExtraField_NonDictValue_ReturnsEmpty()
        {
            var conv = new ExtraFieldConverter();
            var result = conv.Convert("not a dict", typeof(string), "Key1", CultureInfo.InvariantCulture);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ExtraField_ConvertBack_Throws()
        {
            var conv = new ExtraFieldConverter();
            Assert.Throws<NotSupportedException>(() =>
                conv.ConvertBack("val", typeof(Dictionary<string, string>), "Key1", CultureInfo.InvariantCulture));
        }

        // ── ArrayConverter ──

        [Fact]
        public void ArrayConverter_ClonesValues()
        {
            var conv = new ArrayConverter();
            var values = new object?[] { "a", 1, null };
            var result = conv.Convert(values, typeof(object[]), null, CultureInfo.InvariantCulture) as object?[];
            Assert.NotNull(result);
            Assert.Equal(3, result!.Length);
            Assert.Equal("a", result[0]);
            Assert.Equal(1, result[1]);
            Assert.Null(result[2]);
            Assert.NotSame(values, result);
        }

        [Fact]
        public void ArrayConverter_ConvertBack_Throws()
        {
            var conv = new ArrayConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack(null, new[] { typeof(object) }, null, CultureInfo.InvariantCulture));
        }

        // ── AnnotationTextBrushConverter ──

        [Fact]
        public void AnnotationTextBrush_ReturnsBlack()
        {
            var conv = new AnnotationTextBrushConverter();
            var result = conv.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);
            Assert.Equal(Brushes.Black, result);
        }

        [Fact]
        public void AnnotationTextBrush_ConvertBack_Throws()
        {
            var conv = new AnnotationTextBrushConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack(Brushes.Black, typeof(object), null, CultureInfo.InvariantCulture));
        }

        // ── BoolToSelectionBrushConverter ──

        [Fact]
        public void BoolToSelectionBrush_True_ReturnsBlueBrush()
        {
            var conv = new BoolToSelectionBrushConverter();
            var result = conv.Convert(true, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
            Assert.NotNull(result);
            Assert.Equal(Color.FromRgb(59, 130, 246), result!.Color);
        }

        [Fact]
        public void BoolToSelectionBrush_False_ReturnsTransparent()
        {
            var conv = new BoolToSelectionBrushConverter();
            var result = conv.Convert(false, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
            Assert.NotNull(result);
            Assert.Equal(Colors.Transparent, result!.Color);
        }

        [Fact]
        public void BoolToSelectionBrush_NonBool_ReturnsTransparent()
        {
            var conv = new BoolToSelectionBrushConverter();
            var result = conv.Convert("nope", typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
            Assert.NotNull(result);
            Assert.Equal(Colors.Transparent, result!.Color);
        }

        [Fact]
        public void BoolToSelectionBrush_ConvertBack_Throws()
        {
            var conv = new BoolToSelectionBrushConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack(Brushes.Blue, typeof(bool), null, CultureInfo.InvariantCulture));
        }

        // ── SKColorToBrushConverter ──

        [Fact]
        public void SKColorToBrush_ValidColor_ReturnsWpfColor()
        {
            var conv = new SKColorToBrushConverter();
            var skColor = new SKColor(255, 128, 64, 200);
            var result = conv.Convert(skColor, typeof(Color), null, CultureInfo.InvariantCulture);
            Assert.IsType<Color>(result);
            var color = (Color)result;
            Assert.Equal(200, color.A);
            Assert.Equal(255, color.R);
            Assert.Equal(128, color.G);
            Assert.Equal(64, color.B);
        }

        [Fact]
        public void SKColorToBrush_NonSKColor_ReturnsGray()
        {
            var conv = new SKColorToBrushConverter();
            var result = conv.Convert("nope", typeof(Color), null, CultureInfo.InvariantCulture);
            Assert.Equal(Colors.Gray, result);
        }

        [Fact]
        public void SKColorToBrush_Null_ReturnsGray()
        {
            var conv = new SKColorToBrushConverter();
            var result = conv.Convert(null, typeof(Color), null, CultureInfo.InvariantCulture);
            Assert.Equal(Colors.Gray, result);
        }

        [Fact]
        public void SKColorToBrush_ConvertBack_ValidColor_ReturnsSKColor()
        {
            var conv = new SKColorToBrushConverter();
            var wpfColor = Color.FromArgb(200, 255, 128, 64);
            var result = conv.ConvertBack(wpfColor, typeof(SKColor), null, CultureInfo.InvariantCulture);
            Assert.IsType<SKColor>(result);
            var skColor = (SKColor)result;
            Assert.Equal(255, skColor.Red);
            Assert.Equal(128, skColor.Green);
            Assert.Equal(64, skColor.Blue);
            Assert.Equal(200, skColor.Alpha);
        }

        [Fact]
        public void SKColorToBrush_ConvertBack_NonColor_ReturnsSKGray()
        {
            var conv = new SKColorToBrushConverter();
            var result = conv.ConvertBack("nope", typeof(SKColor), null, CultureInfo.InvariantCulture);
            Assert.Equal(SKColors.Gray, result);
        }

        // ── ScreenshotWidthConverter ──

        [Fact]
        public void ScreenshotWidth_SingleImage_ReturnsNaN()
        {
            var conv = new ScreenshotWidthConverter();
            var result = conv.Convert(new object?[] { 400.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True(double.IsNaN((double)result));
        }

        [Fact]
        public void ScreenshotWidth_MultipleImages_ReturnsZoom()
        {
            var conv = new ScreenshotWidthConverter();
            var result = conv.Convert(new object?[] { 600.0, 3 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(600.0, (double)result);
        }

        [Fact]
        public void ScreenshotWidth_InsufficientValues_ReturnsNaN()
        {
            var conv = new ScreenshotWidthConverter();
            var result = conv.Convert(new object?[] { 400.0 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True(double.IsNaN((double)result));
        }

        [Fact]
        public void ScreenshotWidth_WrongTypes_ReturnsNaN()
        {
            var conv = new ScreenshotWidthConverter();
            var result = conv.Convert(new object?[] { "not double", "not int" }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True(double.IsNaN((double)result));
        }

        [Fact]
        public void ScreenshotWidth_ConvertBack_Throws()
        {
            var conv = new ScreenshotWidthConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack(400.0, new[] { typeof(double), typeof(int) }, null, CultureInfo.InvariantCulture));
        }

        // ── ScreenshotScaleConverter ──

        [Fact]
        public void ScreenshotScale_SingleImage_DefaultZoom_Returns1()
        {
            var conv = new ScreenshotScaleConverter();
            var result = conv.Convert(new object?[] { 400.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, (double)result);
        }

        [Fact]
        public void ScreenshotScale_SingleImage_DoubleZoom_Returns2()
        {
            var conv = new ScreenshotScaleConverter();
            var result = conv.Convert(new object?[] { 800.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(2.0, (double)result);
        }

        [Fact]
        public void ScreenshotScale_SingleImage_HalfZoom()
        {
            var conv = new ScreenshotScaleConverter();
            var result = conv.Convert(new object?[] { 200.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(0.5, (double)result);
        }

        [Fact]
        public void ScreenshotScale_SingleImage_VerySmallZoom_ClampsTo01()
        {
            var conv = new ScreenshotScaleConverter();
            var result = conv.Convert(new object?[] { 1.0, 1 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.True((double)result >= 0.1);
        }

        [Fact]
        public void ScreenshotScale_MultipleImages_Returns1()
        {
            var conv = new ScreenshotScaleConverter();
            var result = conv.Convert(new object?[] { 800.0, 3 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, (double)result);
        }

        [Fact]
        public void ScreenshotScale_InsufficientValues_Returns1()
        {
            var conv = new ScreenshotScaleConverter();
            var result = conv.Convert(new object?[] { 400.0 }, typeof(double), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, (double)result);
        }

        [Fact]
        public void ScreenshotScale_ConvertBack_Throws()
        {
            var conv = new ScreenshotScaleConverter();
            Assert.Throws<NotImplementedException>(() =>
                conv.ConvertBack(1.0, new[] { typeof(double), typeof(int) }, null, CultureInfo.InvariantCulture));
        }

        // ── AppConstants ──

        [Fact]
        public void RegexTimeout_Is2Seconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(2), AppConstants.RegexTimeout);
        }

        [Fact]
        public void JsonMaxDepth_Is64()
        {
            Assert.Equal(64, AppConstants.JsonMaxDepth);
        }

        [Fact]
        public void SafeJsonSettings_HasMaxDepth()
        {
            Assert.Equal(64, AppConstants.SafeJsonSettings.MaxDepth);
        }

        [Fact]
        public void SafeJsonDocumentOptions_HasMaxDepth()
        {
            Assert.Equal(64, AppConstants.SafeJsonDocumentOptions.MaxDepth);
        }

        [Fact]
        public void ZipExtension_IsZip()
        {
            Assert.Equal(".zip", AppConstants.ZipExtension);
        }

        [Fact]
        public void CsvExtension_IsCsv()
        {
            Assert.Equal(".csv", AppConstants.CsvExtension);
        }

        [Fact]
        public void UiUpdateBatchSize_Is500()
        {
            Assert.Equal(500, AppConstants.UiUpdateBatchSize);
        }

        [Fact]
        public void TabIndices_AreSequential()
        {
            Assert.Equal(0, AppConstants.TAB_PLC);
            Assert.Equal(1, AppConstants.TAB_APP);
            Assert.Equal(2, AppConstants.TAB_EVENTS);
            Assert.Equal(3, AppConstants.TAB_SCREENSHOTS);
            Assert.Equal(4, AppConstants.TAB_CONFIG);
            Assert.Equal(5, AppConstants.TAB_TERMINALS);
            Assert.Equal(6, AppConstants.TAB_SETUP_INFO);
            Assert.Equal(7, AppConstants.TAB_GLOBALS);
            Assert.Equal(8, AppConstants.TAB_SYSTAB);
            Assert.Equal(9, AppConstants.TAB_CHARTS);
            Assert.Equal(10, AppConstants.TAB_CPR);
            Assert.Equal(11, AppConstants.TAB_STEP_RECORDER);
            Assert.Equal(12, AppConstants.TAB_DIFFERENT_LOGS);
        }

        [Fact]
        public void BuiltInFields_ContainsExpectedFields()
        {
            Assert.True(AppConstants.BuiltInFields.Contains("Date"));
            Assert.True(AppConstants.BuiltInFields.Contains("Level"));
            Assert.True(AppConstants.BuiltInFields.Contains("Message"));
            Assert.True(AppConstants.BuiltInFields.Contains("ThreadName"));
            Assert.True(AppConstants.BuiltInFields.Contains("Logger"));
            Assert.True(AppConstants.BuiltInFields.Contains("ProcessName"));
            Assert.True(AppConstants.BuiltInFields.Contains("Source"));
            Assert.True(AppConstants.BuiltInFields.Contains("LineNumber"));
        }

        [Fact]
        public void BuiltInFields_IsCaseInsensitive()
        {
            Assert.True(AppConstants.BuiltInFields.Contains("date"));
            Assert.True(AppConstants.BuiltInFields.Contains("DATE"));
            Assert.True(AppConstants.BuiltInFields.Contains("message"));
        }

        [Fact]
        public void ErrorLevels_ContainsErrorAndFatal()
        {
            Assert.True(AppConstants.ErrorLevels.Contains("Error"));
            Assert.True(AppConstants.ErrorLevels.Contains("Fatal"));
        }

        [Fact]
        public void ErrorLevels_IsCaseInsensitive()
        {
            Assert.True(AppConstants.ErrorLevels.Contains("error"));
            Assert.True(AppConstants.ErrorLevels.Contains("ERROR"));
            Assert.True(AppConstants.ErrorLevels.Contains("fatal"));
        }

        [Fact]
        public void ErrorLevels_DoesNotContainWarn()
        {
            Assert.False(AppConstants.ErrorLevels.Contains("Warning"));
            Assert.False(AppConstants.ErrorLevels.Contains("Info"));
        }

        // ── EscapeSqlBracketId ──

        [Fact]
        public void EscapeSqlBracketId_NoBrackets_ReturnsUnchanged()
        {
            Assert.Equal("TableName", AppConstants.EscapeSqlBracketId("TableName"));
        }

        [Fact]
        public void EscapeSqlBracketId_WithBracket_Doubles()
        {
            Assert.Equal("Table]]Name", AppConstants.EscapeSqlBracketId("Table]Name"));
        }

        [Fact]
        public void EscapeSqlBracketId_Empty_ReturnsEmpty()
        {
            Assert.Equal("", AppConstants.EscapeSqlBracketId(""));
        }

        [Fact]
        public void EscapeSqlBracketId_Null_ReturnsNull()
        {
            Assert.Null(AppConstants.EscapeSqlBracketId(null!));
        }

        // ── EscapeSqlIdentifier ──

        [Fact]
        public void EscapeSqlIdentifier_NoQuotes_ReturnsUnchanged()
        {
            Assert.Equal("ColumnName", AppConstants.EscapeSqlIdentifier("ColumnName"));
        }

        [Fact]
        public void EscapeSqlIdentifier_WithQuote_Doubles()
        {
            Assert.Equal("Col\"\"Name", AppConstants.EscapeSqlIdentifier("Col\"Name"));
        }

        [Fact]
        public void EscapeSqlIdentifier_Empty_ReturnsEmpty()
        {
            Assert.Equal("", AppConstants.EscapeSqlIdentifier(""));
        }

        [Fact]
        public void EscapeSqlIdentifier_Null_ReturnsNull()
        {
            Assert.Null(AppConstants.EscapeSqlIdentifier(null!));
        }

        // ── S4StateRegex ──

        [Fact]
        public void S4StateRegex_MatchesEnter()
        {
            var match = AppConstants.S4StateRegex().Match("STATE_STANDBY - Enter ======");
            Assert.True(match.Success);
            Assert.Equal("STANDBY", match.Groups[1].Value);
            Assert.Equal("Enter", match.Groups[2].Value);
        }

        [Fact]
        public void S4StateRegex_MatchesExit()
        {
            var match = AppConstants.S4StateRegex().Match("STATE_SERVICE - Exit ======");
            Assert.True(match.Success);
            Assert.Equal("SERVICE", match.Groups[1].Value);
            Assert.Equal("Exit", match.Groups[2].Value);
        }

        [Fact]
        public void S4StateRegex_CaseInsensitive()
        {
            var match = AppConstants.S4StateRegex().Match("state_printing - enter");
            Assert.True(match.Success);
        }

        [Fact]
        public void S4StateRegex_NoMatch()
        {
            var match = AppConstants.S4StateRegex().Match("Just a normal log line");
            Assert.False(match.Success);
        }

        // ── FileTimestampRegex ──

        [Fact]
        public void FileTimestampRegex_MatchesTFormat()
        {
            var match = AppConstants.FileTimestampRegex().Match("log_2024-01-15T14-30-00.zip");
            Assert.True(match.Success);
            Assert.Equal("2024", match.Groups[1].Value);
            Assert.Equal("01", match.Groups[2].Value);
            Assert.Equal("15", match.Groups[3].Value);
            Assert.Equal("14", match.Groups[4].Value);
            Assert.Equal("30", match.Groups[5].Value);
            Assert.Equal("00", match.Groups[6].Value);
        }

        [Fact]
        public void FileTimestampRegex_MatchesUnderscoreFormat()
        {
            var match = AppConstants.FileTimestampRegex().Match("log_2024-01-15_14-30-00.zip");
            Assert.True(match.Success);
        }

        [Fact]
        public void FileTimestampRegex_NoMatch()
        {
            var match = AppConstants.FileTimestampRegex().Match("logfile.zip");
            Assert.False(match.Success);
        }

        // ── AppPaths ──

        [Fact]
        public void AppPaths_Root_ContainsIndiLogs()
        {
            Assert.Contains("IndiLogs3.0", AppPaths.Root);
        }

        [Fact]
        public void AppPaths_SearchLocations_UnderRoot()
        {
            Assert.StartsWith(AppPaths.Root, AppPaths.SearchLocations);
        }

        [Fact]
        public void AppPaths_Prefixes_AreCorrect()
        {
            Assert.Equal("config_", AppPaths.ConfigPrefix);
            Assert.Equal("searchprofile_", AppPaths.SearchProfilePrefix);
            Assert.Equal("dbcol_", AppPaths.DbColumnPrefix);
        }
    }
}
