using IndiLogs_3._0.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogEntryExtendedTests
    {
        // ── RowBackground priority tests ──

        [Fact]
        public void RowBackground_Default_IsTransparent()
        {
            var entry = new LogEntry();
            Assert.Equal(Brushes.Transparent, entry.RowBackground);
        }

        [Fact]
        public void RowBackground_CurrentMarked_HasHighestPriority()
        {
            var entry = new LogEntry
            {
                IsMarked = true,
                HasAnnotation = true,
                CustomColor = Colors.Red,
                IsCurrentMarked = true
            };
            var bg = entry.RowBackground;
            Assert.NotEqual(Brushes.Transparent, bg);
        }

        [Fact]
        public void RowBackground_Marked_OverridesCustomColor()
        {
            var entry = new LogEntry
            {
                IsMarked = true,
                CustomColor = Colors.Red
            };
            var bg = entry.RowBackground;
            Assert.NotEqual(Brushes.Transparent, bg);
        }

        [Fact]
        public void RowBackground_Annotation_OverridesCustomColor()
        {
            var entry = new LogEntry
            {
                HasAnnotation = true,
                CustomColor = Colors.Red
            };
            var bg = entry.RowBackground;
            Assert.NotEqual(Brushes.Transparent, bg);
        }

        [Fact]
        public void RowBackground_CustomColor_Applied()
        {
            var entry = new LogEntry { CustomColor = Colors.Blue };
            var bg = entry.RowBackground as SolidColorBrush;
            Assert.NotNull(bg);
            Assert.Equal(Colors.Blue, bg!.Color);
        }

        [Fact]
        public void RowBackground_CustomColor_CachesInstance()
        {
            var entry = new LogEntry { CustomColor = Colors.Blue };
            var bg1 = entry.RowBackground;
            var bg2 = entry.RowBackground;
            Assert.Same(bg1, bg2);
        }

        [Fact]
        public void RowBackground_WarningLevel_ShowsWarningTint()
        {
            var entry = new LogEntry { Level = "WARNING" };
            Assert.NotEqual(Brushes.Transparent, entry.RowBackground);
        }

        [Fact]
        public void RowBackground_WarnLevel_ShowsWarningTint()
        {
            var entry = new LogEntry { Level = "WARN" };
            Assert.NotEqual(Brushes.Transparent, entry.RowBackground);
        }

        [Fact]
        public void RowBackground_InfoLevel_IsTransparent()
        {
            var entry = new LogEntry { Level = "INFO" };
            Assert.Equal(Brushes.Transparent, entry.RowBackground);
        }

        // ── FilteredRowBackground tests ──

        [Fact]
        public void FilteredRowBackground_NoSystemDefault_IsTransparent()
        {
            var entry = new LogEntry();
            Assert.Equal(Brushes.Transparent, entry.FilteredRowBackground);
        }

        [Fact]
        public void FilteredRowBackground_WithSystemDefault_ReturnsColor()
        {
            var entry = new LogEntry { SystemDefaultColor = Colors.LightBlue };
            var bg = entry.FilteredRowBackground as SolidColorBrush;
            Assert.NotNull(bg);
            Assert.Equal(Colors.LightBlue, bg!.Color);
        }

        [Fact]
        public void FilteredRowBackground_CachesInstance()
        {
            var entry = new LogEntry { SystemDefaultColor = Colors.LightBlue };
            var bg1 = entry.FilteredRowBackground;
            var bg2 = entry.FilteredRowBackground;
            Assert.Same(bg1, bg2);
        }

        [Fact]
        public void FilteredRowBackground_IgnoresCustomColor()
        {
            var entry = new LogEntry
            {
                CustomColor = Colors.Red,
                SystemDefaultColor = null
            };
            Assert.Equal(Brushes.Transparent, entry.FilteredRowBackground);
        }

        // ── HasSystemDefaultColor ──

        [Fact]
        public void HasSystemDefaultColor_True_WhenSet()
        {
            var entry = new LogEntry { SystemDefaultColor = Colors.Blue };
            Assert.True(entry.HasSystemDefaultColor);
        }

        [Fact]
        public void HasSystemDefaultColor_False_WhenNull()
        {
            var entry = new LogEntry();
            Assert.False(entry.HasSystemDefaultColor);
        }

        // ── Annotation tests ──

        [Fact]
        public void AnnotationIcon_NoAnnotation_Empty()
        {
            var entry = new LogEntry { HasAnnotation = false };
            Assert.Equal("", entry.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_HasAnnotation_NotExpanded_ShowsClip()
        {
            var entry = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = false };
            Assert.Equal("📎", entry.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_HasAnnotation_Expanded_ShowsPin()
        {
            var entry = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = true };
            Assert.Equal("📌", entry.AnnotationIcon);
        }

        // ── PropertyChanged notifications ──

        [Fact]
        public void IsMarked_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.IsMarked = true;

            Assert.Contains("IsMarked", changedProps);
            Assert.Contains("RowBackground", changedProps);
        }

        [Fact]
        public void IsCurrentMarked_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.IsCurrentMarked = true;

            Assert.Contains("IsCurrentMarked", changedProps);
            Assert.Contains("RowBackground", changedProps);
        }

        [Fact]
        public void CustomColor_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.CustomColor = Colors.Red;

            Assert.Contains("CustomColor", changedProps);
            Assert.Contains("RowBackground", changedProps);
        }

        [Fact]
        public void HasAnnotation_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.HasAnnotation = true;

            Assert.Contains("HasAnnotation", changedProps);
            Assert.Contains("RowBackground", changedProps);
            Assert.Contains("AnnotationIcon", changedProps);
        }

        [Fact]
        public void IsAnnotationExpanded_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.IsAnnotationExpanded = true;

            Assert.Contains("IsAnnotationExpanded", changedProps);
            Assert.Contains("AnnotationIcon", changedProps);
        }

        [Fact]
        public void SystemDefaultColor_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.SystemDefaultColor = Colors.Blue;

            Assert.Contains("SystemDefaultColor", changedProps);
            Assert.Contains("FilteredRowBackground", changedProps);
            Assert.Contains("HasSystemDefaultColor", changedProps);
        }

        [Fact]
        public void AnnotationContent_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.AnnotationContent = "test note";

            Assert.Contains("AnnotationContent", changedProps);
        }

        [Fact]
        public void IsErrorOrEvents_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.IsErrorOrEvents = true;

            Assert.Contains("IsErrorOrEvents", changedProps);
        }

        [Fact]
        public void RowForeground_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.RowForeground = Brushes.Red;

            Assert.Contains("RowForeground", changedProps);
        }

        // ── Same value doesn't raise ──

        [Fact]
        public void IsMarked_SameValue_NoPropertyChanged()
        {
            var entry = new LogEntry { IsMarked = false };
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.IsMarked = false;

            Assert.Empty(changedProps);
        }

        [Fact]
        public void CustomColor_SameValue_NoPropertyChanged()
        {
            var entry = new LogEntry { CustomColor = Colors.Red };
            var changedProps = new List<string>();
            entry.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            entry.CustomColor = Colors.Red;

            Assert.Empty(changedProps);
        }

        // ── ExtraFields ──

        [Fact]
        public void ExtraFields_DefaultNull()
        {
            var entry = new LogEntry();
            Assert.Null(entry.ExtraFields);
        }

        [Fact]
        public void ExtraFields_CanBeSet()
        {
            var entry = new LogEntry
            {
                ExtraFields = new Dictionary<string, string> { { "Key1", "Val1" } }
            };
            Assert.Single(entry.ExtraFields);
            Assert.Equal("Val1", entry.ExtraFields["Key1"]);
        }

        // ── Default property values ──

        [Fact]
        public void DefaultValues_AreEmpty()
        {
            var entry = new LogEntry();
            Assert.Equal("", entry.Level);
            Assert.Equal("", entry.ThreadName);
            Assert.Equal("", entry.Message);
            Assert.Equal("", entry.Logger);
            Assert.Equal("", entry.ProcessName);
            Assert.Equal("", entry.Method);
            Assert.Equal("", entry.Pattern);
            Assert.Equal("", entry.Data);
            Assert.Equal("", entry.Exception);
            Assert.Equal("", entry.AnnotationContent);
            Assert.False(entry.IsMarked);
            Assert.False(entry.IsCurrentMarked);
            Assert.False(entry.HasAnnotation);
            Assert.False(entry.IsAnnotationExpanded);
            Assert.False(entry.IsErrorOrEvents);
            Assert.Null(entry.CustomColor);
            Assert.Null(entry.SystemDefaultColor);
            Assert.Null(entry.SyncedTime);
        }
    }
}
