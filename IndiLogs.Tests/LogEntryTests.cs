using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Windows.Media;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogEntryTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var entry = new LogEntry();

            Assert.Equal("", entry.Level);
            Assert.Equal("", entry.Message);
            Assert.Equal("", entry.ThreadName);
            Assert.Equal("", entry.Logger);
            Assert.Equal("", entry.Method);
            Assert.Equal("", entry.Pattern);
            Assert.Equal("", entry.Data);
            Assert.Equal("", entry.Exception);
            Assert.False(entry.IsMarked);
            Assert.False(entry.IsCurrentMarked);
            Assert.False(entry.HasAnnotation);
            Assert.False(entry.IsErrorOrEvents);
            Assert.Null(entry.CustomColor);
            Assert.Null(entry.SystemDefaultColor);
            Assert.Null(entry.ExtraFields);
            Assert.False(entry.HasSystemDefaultColor);
        }

        [Fact]
        public void IsMarked_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var raised = new List<string>();
            entry.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            entry.IsMarked = true;

            Assert.Contains("IsMarked", raised);
            Assert.Contains("RowBackground", raised);
        }

        [Fact]
        public void IsCurrentMarked_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var raised = new List<string>();
            entry.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            entry.IsCurrentMarked = true;

            Assert.Contains("IsCurrentMarked", raised);
            Assert.Contains("RowBackground", raised);
        }

        [Fact]
        public void CustomColor_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var raised = new List<string>();
            entry.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            entry.CustomColor = Color.FromRgb(255, 0, 0);

            Assert.Contains("CustomColor", raised);
            Assert.Contains("RowBackground", raised);
        }

        [Fact]
        public void SystemDefaultColor_RaisesPropertyChanged()
        {
            var entry = new LogEntry();
            var raised = new List<string>();
            entry.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            entry.SystemDefaultColor = Color.FromRgb(173, 216, 230);

            Assert.Contains("SystemDefaultColor", raised);
            Assert.Contains("FilteredRowBackground", raised);
            Assert.Contains("HasSystemDefaultColor", raised);
        }

        [Fact]
        public void HasSystemDefaultColor_ReflectsSystemDefaultColor()
        {
            var entry = new LogEntry();
            Assert.False(entry.HasSystemDefaultColor);

            entry.SystemDefaultColor = Color.FromRgb(0, 0, 0);
            Assert.True(entry.HasSystemDefaultColor);

            entry.SystemDefaultColor = null;
            Assert.False(entry.HasSystemDefaultColor);
        }

        [Fact]
        public void RowBackground_Priority_CurrentMarkedFirst()
        {
            var entry = new LogEntry
            {
                IsMarked = true,
                IsCurrentMarked = true,
                HasAnnotation = true,
                CustomColor = Color.FromRgb(255, 0, 0)
            };

            var bg = entry.RowBackground as SolidColorBrush;
            Assert.NotNull(bg);
            // CurrentMarked = Light Purple (200, 170, 255)
            Assert.Equal(200, bg.Color.R);
            Assert.Equal(170, bg.Color.G);
            Assert.Equal(255, bg.Color.B);
        }

        [Fact]
        public void RowBackground_Priority_MarkedSecond()
        {
            var entry = new LogEntry
            {
                IsMarked = true,
                HasAnnotation = true,
                CustomColor = Color.FromRgb(255, 0, 0)
            };

            var bg = entry.RowBackground as SolidColorBrush;
            Assert.NotNull(bg);
            // Marked = Light Green (144, 238, 144)
            Assert.Equal(144, bg.Color.R);
            Assert.Equal(238, bg.Color.G);
            Assert.Equal(144, bg.Color.B);
        }

        [Fact]
        public void RowBackground_Priority_AnnotationThird()
        {
            var entry = new LogEntry
            {
                HasAnnotation = true,
                CustomColor = Color.FromRgb(255, 0, 0)
            };

            var bg = entry.RowBackground as SolidColorBrush;
            Assert.NotNull(bg);
            // Annotation = Yellow (255, 255, 200)
            Assert.Equal(255, bg.Color.R);
            Assert.Equal(255, bg.Color.G);
            Assert.Equal(200, bg.Color.B);
        }

        [Fact]
        public void RowBackground_Priority_CustomColorFourth()
        {
            var entry = new LogEntry
            {
                CustomColor = Color.FromRgb(100, 150, 200)
            };

            var bg = entry.RowBackground as SolidColorBrush;
            Assert.NotNull(bg);
            Assert.Equal(100, bg.Color.R);
            Assert.Equal(150, bg.Color.G);
            Assert.Equal(200, bg.Color.B);
        }

        [Fact]
        public void RowBackground_WarningLevel_SubtleTint()
        {
            var entry = new LogEntry { Level = "WARNING" };
            var bg = entry.RowBackground;
            Assert.NotEqual(Brushes.Transparent, bg);
        }

        [Fact]
        public void RowBackground_WarnLevel_SubtleTint()
        {
            var entry = new LogEntry { Level = "WARN" };
            var bg = entry.RowBackground;
            Assert.NotEqual(Brushes.Transparent, bg);
        }

        [Fact]
        public void RowBackground_NoSpecialState_Transparent()
        {
            var entry = new LogEntry { Level = "Info" };
            Assert.Equal(Brushes.Transparent, entry.RowBackground);
        }

        [Fact]
        public void FilteredRowBackground_NoSystemDefault_Transparent()
        {
            var entry = new LogEntry();
            Assert.Equal(Brushes.Transparent, entry.FilteredRowBackground);
        }

        [Fact]
        public void FilteredRowBackground_WithSystemDefault_ReturnsCachedBrush()
        {
            var entry = new LogEntry { SystemDefaultColor = Color.FromRgb(173, 216, 230) };

            var bg1 = entry.FilteredRowBackground;
            var bg2 = entry.FilteredRowBackground;

            Assert.Same(bg1, bg2); // Should be cached
            var solid = bg1 as SolidColorBrush;
            Assert.NotNull(solid);
            Assert.Equal(173, solid.Color.R);
        }

        [Fact]
        public void AnnotationIcon_NoAnnotation_Empty()
        {
            var entry = new LogEntry();
            Assert.Equal("", entry.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_HasAnnotation_NotExpanded()
        {
            var entry = new LogEntry { HasAnnotation = true };
            Assert.NotEmpty(entry.AnnotationIcon);
        }

        [Fact]
        public void AnnotationIcon_HasAnnotation_Expanded_DifferentIcon()
        {
            var entry = new LogEntry { HasAnnotation = true };
            string collapsed = entry.AnnotationIcon;

            entry.IsAnnotationExpanded = true;
            string expanded = entry.AnnotationIcon;

            Assert.NotEqual(collapsed, expanded);
        }

        [Fact]
        public void HasAnnotation_RaisesRowBackground()
        {
            var entry = new LogEntry();
            var raised = new List<string>();
            entry.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            entry.HasAnnotation = true;

            Assert.Contains("HasAnnotation", raised);
            Assert.Contains("RowBackground", raised);
            Assert.Contains("AnnotationIcon", raised);
        }

        [Fact]
        public void CustomColor_CachesBrush()
        {
            var entry = new LogEntry { CustomColor = Color.FromRgb(50, 100, 150) };

            var bg1 = entry.RowBackground;
            var bg2 = entry.RowBackground;

            Assert.Same(bg1, bg2);
        }

        [Fact]
        public void SyncedTime_IsNullable()
        {
            var entry = new LogEntry();
            Assert.Null(entry.SyncedTime);

            entry.SyncedTime = new DateTime(2025, 1, 1);
            Assert.Equal(new DateTime(2025, 1, 1), entry.SyncedTime);
        }
    }
}
