using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using Xunit;

namespace IndiLogs.Tests
{
    public class ExportPresetModelTests
    {
        // ===================================================================
        // ExportPreset tests
        // ===================================================================

        [Fact]
        public void ExportPreset_Defaults_AreCorrect()
        {
            var preset = new ExportPreset();
            Assert.True(preset.IncludeUnixTime);
            Assert.True(preset.IncludeEvents);
            Assert.True(preset.IncludeMachineState);
            Assert.False(preset.IncludeLogStats);
            Assert.Empty(preset.SelectedIOComponents);
            Assert.Empty(preset.SelectedAxisComponents);
            Assert.Empty(preset.SelectedCHSteps);
            Assert.Empty(preset.SelectedThreads);
            Assert.Equal("", preset.Name);
        }

        [Fact]
        public void ExportPreset_SetProperties_Persist()
        {
            var preset = new ExportPreset
            {
                Name = "Test",
                IncludeUnixTime = false,
                IncludeEvents = false,
                IncludeMachineState = false,
                IncludeLogStats = true,
                SelectedIOComponents = new List<string> { "Sub|IO1" },
                SelectedAxisComponents = new List<string> { "Sub|Axis1" },
                SelectedCHSteps = new List<string> { "Parent|CH1" },
                SelectedThreads = new List<string> { "Thread1" }
            };

            Assert.Equal("Test", preset.Name);
            Assert.False(preset.IncludeUnixTime);
            Assert.True(preset.IncludeLogStats);
            Assert.Single(preset.SelectedIOComponents);
            Assert.Single(preset.SelectedThreads);
        }

        // ===================================================================
        // SelectableItem tests
        // ===================================================================

        [Fact]
        public void SelectableItem_DefaultState_NotSelected()
        {
            var item = new SelectableItem();
            Assert.False(item.IsSelected);
            Assert.Equal("", item.Name);
            Assert.Equal("", item.Category);
        }

        [Fact]
        public void SelectableItem_SetProperties_RaisesPropertyChanged()
        {
            var item = new SelectableItem();
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            item.Name = "Sensor1";
            item.Category = "IO_Mon";
            item.IsSelected = true;

            Assert.Contains("Name", changed);
            Assert.Contains("Category", changed);
            Assert.Contains("IsSelected", changed);
        }

        [Fact]
        public void SelectableItem_Toggle_UpdatesState()
        {
            var item = new SelectableItem { Name = "Test", IsSelected = false };
            Assert.False(item.IsSelected);

            item.IsSelected = true;
            Assert.True(item.IsSelected);

            item.IsSelected = false;
            Assert.False(item.IsSelected);
        }

        // ===================================================================
        // SignalProgressItem tests
        // ===================================================================

        [Fact]
        public void SignalProgressItem_DefaultStatus_IsPending()
        {
            var item = new SignalProgressItem { Name = "Signal1" };
            Assert.Equal("pending", item.Status);
            Assert.Equal("\u2022", item.StatusIcon); // bullet
        }

        [Fact]
        public void SignalProgressItem_Parsing_ShowsHourglass()
        {
            var item = new SignalProgressItem { Name = "Signal1" };
            item.Status = "parsing";
            Assert.Equal("\u23F3", item.StatusIcon); // hourglass
        }

        [Fact]
        public void SignalProgressItem_Done_ShowsCheckmark()
        {
            var item = new SignalProgressItem { Name = "Signal1" };
            item.Status = "done";
            Assert.Equal("\u2714", item.StatusIcon); // checkmark
        }

        [Fact]
        public void SignalProgressItem_StatusChange_RaisesPropertyChanged()
        {
            var item = new SignalProgressItem { Name = "Signal1" };
            var changed = new List<string>();
            item.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            item.Status = "done";

            Assert.Contains("Status", changed);
            Assert.Contains("StatusIcon", changed);
        }

        [Fact]
        public void SignalProgressItem_UnknownStatus_ShowsBullet()
        {
            var item = new SignalProgressItem { Name = "Signal1" };
            item.Status = "unknown_status";
            Assert.Equal("\u2022", item.StatusIcon); // bullet (default)
        }
    }
}
