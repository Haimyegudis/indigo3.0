using System;
using System.Collections.Generic;
using System.ComponentModel;
using IndiLogs_3._0.Models.Charts;
using Xunit;

namespace IndiLogs.Tests;

public class ChartModelTests
{
    #region SignalSeries Tests

    [Fact]
    public void SignalSeries_Defaults_AreCorrect()
    {
        var series = new SignalSeries();

        Assert.Equal("", series.Name);
        Assert.Empty(series.Data);
        Assert.True(series.IsVisible);
        Assert.Equal(AxisType.Left, series.YAxisType);
        Assert.Equal("-", series.CurrentValueDisplay);
        Assert.False(series.IsSmoothed);
        Assert.Null(series.SmoothedData);
    }

    [Fact]
    public void SignalSeries_AxisDisplay_ReturnsL_ForLeft()
    {
        var series = new SignalSeries { YAxisType = AxisType.Left };
        Assert.Equal("L", series.AxisDisplay);
    }

    [Fact]
    public void SignalSeries_AxisDisplay_ReturnsR_ForRight()
    {
        var series = new SignalSeries { YAxisType = AxisType.Right };
        Assert.Equal("R", series.AxisDisplay);
    }

    [Fact]
    public void SignalSeries_IsVisible_FiresPropertyChanged()
    {
        var series = new SignalSeries();
        var changed = new List<string>();
        series.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        series.IsVisible = false;

        Assert.Contains("IsVisible", changed);
    }

    [Fact]
    public void SignalSeries_YAxisType_FiresPropertyChanged_ForYAxisTypeAndAxisDisplay()
    {
        var series = new SignalSeries();
        var changed = new List<string>();
        series.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        series.YAxisType = AxisType.Right;

        Assert.Contains("YAxisType", changed);
        Assert.Contains("AxisDisplay", changed);
    }

    [Fact]
    public void SignalSeries_CurrentValueDisplay_FiresPropertyChanged()
    {
        var series = new SignalSeries();
        var changed = new List<string>();
        series.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        series.CurrentValueDisplay = "42.5";

        Assert.Contains("CurrentValueDisplay", changed);
    }

    [Fact]
    public void SignalSeries_CurrentValueDisplay_DoesNotFire_WhenSameValue()
    {
        var series = new SignalSeries();
        var changed = new List<string>();
        series.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        series.CurrentValueDisplay = "-";

        Assert.DoesNotContain("CurrentValueDisplay", changed);
    }

    [Fact]
    public void SignalSeries_IsSmoothed_FiresPropertyChanged()
    {
        var series = new SignalSeries { Data = new double[] { 1, 2, 3 } };
        var changed = new List<string>();
        series.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        series.IsSmoothed = true;

        Assert.Contains("IsSmoothed", changed);
    }

    [Fact]
    public void SignalSeries_IsSmoothed_AutoCalculatesSmoothing_WhenSmoothedDataIsNull()
    {
        var series = new SignalSeries { Data = new double[] { 1, 2, 3, 4, 5 } };

        series.IsSmoothed = true;

        Assert.NotNull(series.SmoothedData);
        Assert.Equal(series.Data.Length, series.SmoothedData!.Length);
    }

    [Fact]
    public void SignalSeries_IsSmoothed_DoesNotRecalculate_WhenSmoothedDataExists()
    {
        var series = new SignalSeries { Data = new double[] { 1, 2, 3, 4, 5 } };
        var precomputed = new double[] { 10, 20, 30, 40, 50 };
        series.SmoothedData = precomputed;

        series.IsSmoothed = true;

        Assert.Same(precomputed, series.SmoothedData);
    }

    [Fact]
    public void SignalSeries_CalculateSmoothing_EmptyData_DoesNothing()
    {
        var series = new SignalSeries { Data = Array.Empty<double>() };

        series.CalculateSmoothing();

        Assert.Null(series.SmoothedData);
    }

    [Fact]
    public void SignalSeries_CalculateSmoothing_CreatesArraySameLength()
    {
        var series = new SignalSeries { Data = new double[] { 10, 20, 30, 40, 50 } };

        series.CalculateSmoothing(3);

        Assert.NotNull(series.SmoothedData);
        Assert.Equal(5, series.SmoothedData!.Length);
    }

    [Fact]
    public void SignalSeries_CalculateSmoothing_SingleElement_ReturnsSameValue()
    {
        var series = new SignalSeries { Data = new double[] { 42.0 } };

        series.CalculateSmoothing(10);

        Assert.Equal(42.0, series.SmoothedData![0]);
    }

    [Fact]
    public void SignalSeries_CalculateSmoothing_NaN_ProducesNaN()
    {
        var series = new SignalSeries { Data = new double[] { 1, double.NaN, 3 } };

        series.CalculateSmoothing(3);

        Assert.True(double.IsNaN(series.SmoothedData![1]));
    }

    [Fact]
    public void SignalSeries_CalculateSmoothing_NaN_SkippedInAveraging()
    {
        var series = new SignalSeries { Data = new double[] { 10, double.NaN, 30 } };

        series.CalculateSmoothing(10);

        // halfWindow=5, but array length=3, so window is clamped
        // Index 0: window [0, min(2, 5)] = [0,2], non-NaN = {10, 30} => avg=20
        Assert.Equal(20.0, series.SmoothedData![0]);
        // Index 1: NaN => stays NaN
        Assert.True(double.IsNaN(series.SmoothedData![1]));
        // Index 2: window [0,2], non-NaN = {10, 30} => avg=20
        Assert.Equal(20.0, series.SmoothedData![2]);
    }

    [Fact]
    public void SignalSeries_CalculateSmoothing_WindowSize2_CorrectAveraging()
    {
        // halfWindow = 1, so window is [i-1, i+1]
        var series = new SignalSeries { Data = new double[] { 10, 20, 30, 40, 50 } };

        series.CalculateSmoothing(2);

        // Index 0: window [0,1] => (10+20)/2 = 15
        Assert.Equal(15.0, series.SmoothedData![0]);
        // Index 1: window [0,2] => (10+20+30)/3 = 20
        Assert.Equal(20.0, series.SmoothedData![1]);
        // Index 4: window [3,4] => (40+50)/2 = 45
        Assert.Equal(45.0, series.SmoothedData![4]);
    }

    [Theory]
    [InlineData(AxisType.Left, "L")]
    [InlineData(AxisType.Right, "R")]
    public void SignalSeries_AxisDisplay_MatchesYAxisType(AxisType axisType, string expected)
    {
        var series = new SignalSeries { YAxisType = axisType };
        Assert.Equal(expected, series.AxisDisplay);
    }

    #endregion

    #region ChartViewModel Tests

    [Fact]
    public void ChartViewModel_Defaults_AreCorrect()
    {
        var vm = new ChartViewModel();

        Assert.Equal("", vm.Title);
        Assert.Equal(ChartViewType.Signal, vm.ViewType);
        Assert.False(vm.IsSelected);
        Assert.False(vm.IsDetached);
        Assert.Equal(180.0, vm.ChartHeight);
        Assert.True(vm.IsSignalView);
        Assert.False(vm.IsGanttView);
        Assert.False(vm.IsThreadView);
        Assert.True(vm.IsNotDetached);
    }

    [Fact]
    public void ChartViewModel_ViewType_Signal_ComputedProperties()
    {
        var vm = new ChartViewModel { ViewType = ChartViewType.Signal };

        Assert.True(vm.IsSignalView);
        Assert.False(vm.IsGanttView);
        Assert.False(vm.IsThreadView);
    }

    [Fact]
    public void ChartViewModel_ViewType_Gantt_ComputedProperties()
    {
        var vm = new ChartViewModel { ViewType = ChartViewType.Gantt };

        Assert.False(vm.IsSignalView);
        Assert.True(vm.IsGanttView);
        Assert.False(vm.IsThreadView);
    }

    [Fact]
    public void ChartViewModel_ViewType_Thread_ComputedProperties()
    {
        var vm = new ChartViewModel { ViewType = ChartViewType.Thread };

        Assert.False(vm.IsSignalView);
        Assert.False(vm.IsGanttView);
        Assert.True(vm.IsThreadView);
    }

    [Fact]
    public void ChartViewModel_ViewType_FiresAllComputedProperties()
    {
        var vm = new ChartViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.ViewType = ChartViewType.Gantt;

        Assert.Contains("ViewType", changed);
        Assert.Contains("IsSignalView", changed);
        Assert.Contains("IsGanttView", changed);
        Assert.Contains("IsThreadView", changed);
    }

    [Fact]
    public void ChartViewModel_IsDetached_FiresIsNotDetached()
    {
        var vm = new ChartViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.IsDetached = true;

        Assert.Contains("IsDetached", changed);
        Assert.Contains("IsNotDetached", changed);
        Assert.False(vm.IsNotDetached);
    }

    [Fact]
    public void ChartViewModel_Title_FiresPropertyChanged()
    {
        var vm = new ChartViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.Title = "Test Chart";

        Assert.Contains("Title", changed);
    }

    [Fact]
    public void ChartViewModel_IsSelected_FiresPropertyChanged()
    {
        var vm = new ChartViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.IsSelected = true;

        Assert.Contains("IsSelected", changed);
    }

    [Fact]
    public void ChartViewModel_ChartHeight_FiresPropertyChanged()
    {
        var vm = new ChartViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.ChartHeight = 300;

        Assert.Contains("ChartHeight", changed);
    }

    [Fact]
    public void ChartViewModel_Series_InitializedEmpty()
    {
        var vm = new ChartViewModel();
        Assert.NotNull(vm.Series);
        Assert.Empty(vm.Series);
    }

    [Fact]
    public void ChartViewModel_ReferenceLines_InitializedEmpty()
    {
        var vm = new ChartViewModel();
        Assert.NotNull(vm.ReferenceLines);
        Assert.Empty(vm.ReferenceLines);
    }

    #endregion

    #region ReferenceLine Tests

    [Fact]
    public void ReferenceLine_Defaults_AreCorrect()
    {
        var line = new ReferenceLine();

        Assert.Equal("Line", line.Name);
        Assert.Equal(0.0, line.Value);
        Assert.Equal(2.0f, line.Thickness);
        Assert.True(line.IsDashed);
        Assert.Equal(AxisType.Left, line.YAxis);
    }

    [Fact]
    public void ReferenceLine_Name_FiresPropertyChanged()
    {
        var line = new ReferenceLine();
        var changed = new List<string>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        line.Name = "Threshold";

        Assert.Contains("Name", changed);
    }

    [Fact]
    public void ReferenceLine_Value_FiresPropertyChanged()
    {
        var line = new ReferenceLine();
        var changed = new List<string>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        line.Value = 99.5;

        Assert.Contains("Value", changed);
    }

    [Fact]
    public void ReferenceLine_Thickness_FiresPropertyChanged()
    {
        var line = new ReferenceLine();
        var changed = new List<string>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        line.Thickness = 5.0f;

        Assert.Contains("Thickness", changed);
    }

    [Fact]
    public void ReferenceLine_IsDashed_FiresPropertyChanged()
    {
        var line = new ReferenceLine();
        var changed = new List<string>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        line.IsDashed = false;

        Assert.Contains("IsDashed", changed);
    }

    [Fact]
    public void ReferenceLine_YAxis_FiresPropertyChanged()
    {
        var line = new ReferenceLine();
        var changed = new List<string>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        line.YAxis = AxisType.Right;

        Assert.Contains("YAxis", changed);
    }

    #endregion

    #region Model Defaults Tests

    [Fact]
    public void WorkspaceModel_Defaults()
    {
        var model = new WorkspaceModel();

        Assert.Equal("", model.SourceCsvPath);
        Assert.NotNull(model.Charts);
        Assert.Empty(model.Charts);
    }

    [Fact]
    public void ChartSaveData_Defaults()
    {
        var data = new ChartSaveData();

        Assert.Equal("", data.Title);
        Assert.NotNull(data.Series);
        Assert.Empty(data.Series);
        Assert.NotNull(data.ReferenceLines);
        Assert.Empty(data.ReferenceLines);
    }

    [Fact]
    public void SeriesSaveData_Defaults()
    {
        var data = new SeriesSaveData();

        Assert.Equal("", data.Name);
        Assert.Equal("", data.ColorHex);
        Assert.False(data.IsVisible);
        Assert.Equal(AxisType.Left, data.Axis);
    }

    [Fact]
    public void EventMarker_Defaults()
    {
        var marker = new EventMarker();

        Assert.Equal(0, marker.Index);
        Assert.Equal("", marker.Name);
        Assert.Equal("", marker.Message);
        Assert.Equal("", marker.Time);
        Assert.Equal("", marker.Severity);
        Assert.Equal("", marker.Description);
    }

    [Fact]
    public void GapRegion_Defaults()
    {
        var gap = new GapRegion();

        Assert.Equal(0, gap.StartIndex);
        Assert.Equal(0, gap.EndIndex);
        Assert.Equal("", gap.Duration);
        Assert.Equal("", gap.StartTime);
        Assert.Equal("", gap.EndTime);
    }

    [Fact]
    public void StateEventItem_Defaults()
    {
        var item = new StateEventItem();

        Assert.Equal("", item.Time);
        Assert.Equal("", item.StateName);
        Assert.Equal(0, item.LineIndex);
    }

    [Fact]
    public void StateInterval_Defaults()
    {
        var interval = new StateInterval();

        Assert.Equal(0, interval.StartIndex);
        Assert.Equal(0, interval.EndIndex);
        Assert.Equal(0, interval.StateId);
        Assert.Null(interval.StateName);
        Assert.Null(interval.TooltipText);
    }

    #endregion

    #region SignalCategory Tests

    [Fact]
    public void SignalCategory_Defaults()
    {
        var cat = new SignalCategory();

        Assert.Equal("", cat.Name);
        Assert.Empty(cat.Keywords);
    }

    [Fact]
    public void SignalCategory_DefaultCategories_Has7Items()
    {
        Assert.Equal(7, SignalCategory.DefaultCategories.Length);
    }

    [Fact]
    public void SignalCategory_DefaultCategories_FirstIsAllSignals()
    {
        var first = SignalCategory.DefaultCategories[0];

        Assert.Equal("All Signals", first.Name);
        Assert.Empty(first.Keywords);
    }

    [Theory]
    [InlineData(1, "Axis / Motion")]
    [InlineData(2, "IO / Sensors")]
    [InlineData(3, "States / Logic")]
    [InlineData(4, "Temperature")]
    [InlineData(5, "Pressure")]
    [InlineData(6, "Events")]
    public void SignalCategory_DefaultCategories_HasExpectedNames(int index, string expectedName)
    {
        Assert.Equal(expectedName, SignalCategory.DefaultCategories[index].Name);
    }

    [Fact]
    public void SignalCategory_DefaultCategories_NonFirstHaveKeywords()
    {
        for (int i = 1; i < SignalCategory.DefaultCategories.Length; i++)
        {
            Assert.NotEmpty(SignalCategory.DefaultCategories[i].Keywords);
        }
    }

    #endregion

    #region Enum Tests

    [Theory]
    [InlineData(AxisType.Left, 0)]
    [InlineData(AxisType.Right, 1)]
    public void AxisType_HasExpectedValues(AxisType value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(ChartViewType.Signal, 0)]
    [InlineData(ChartViewType.Gantt, 1)]
    [InlineData(ChartViewType.Thread, 2)]
    public void ChartViewType_HasExpectedValues(ChartViewType value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    #endregion
}
