using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    public class SingleChartViewModel : INotifyPropertyChanged
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        private PlotModel _plotModel;
        public PlotModel PlotModel
        {
            get => _plotModel;
            set
            {
                _plotModel = value;
                OnPropertyChanged();
            }
        }

        public PlotController Controller { get; set; }

        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
                if (PlotModel != null)
                    PlotModel.Title = value;
            }
        }

        private string _originalTitle;

        public ObservableCollection<string> PlottedKeys { get; set; } = new ObservableCollection<string>();

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                BorderColor = value ? "#3B82F6" : "#CCCCCC";
                BorderThickness = value ? new Thickness(2) : new Thickness(1);
                OnPropertyChanged();
                OnPropertyChanged(nameof(BorderColor));
                OnPropertyChanged(nameof(BorderThickness));
            }
        }

        public string BorderColor { get; set; } = "#CCCCCC";
        public Thickness BorderThickness { get; set; } = new Thickness(1);

        public ICommand FloatCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public SingleChartViewModel(string title)
        {
            Title = title;
            _originalTitle = title;

            // ? ????? Controller ??? - ??? ???????? ??? ??????
            Controller = new PlotController();

            CreateNewChart();
        }

        public void CreateNewChart()
        {
            PlotModel = new PlotModel { Title = Title };

            // Create X axis (DateTime)
            var xAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                MinorIntervalType = DateTimeIntervalType.Seconds,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            };
            PlotModel.Axes.Add(xAxis);

            // Create Y axis
            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            };
            PlotModel.Axes.Add(yAxis);
        }

        public void UpdateTitle(string newParams)
        {
            _originalTitle = newParams;
            Title = newParams;
        }

        public void SetXAxisLimits(double minX, double maxX)
        {
            var plotModel = this.PlotModel;
            if (plotModel == null) return;

            var xAxis = plotModel.Axes.FirstOrDefault(a => a is DateTimeAxis);
            if (xAxis != null)
            {
                // ????? ????? ??????
                if (double.IsNaN(minX) || double.IsInfinity(minX) || minX == 0 ||
                    double.IsNaN(maxX) || double.IsInfinity(maxX) || maxX == 0 ||
                    minX >= maxX)
                {
                    System.Diagnostics.Debug.WriteLine($"?? SetXAxisLimits: Invalid values! minX={minX}, maxX={maxX}");
                    return;
                }

                xAxis.Minimum = minX;
                xAxis.Maximum = maxX;

                plotModel.InvalidatePlot(true);

                System.Diagnostics.Debug.WriteLine($"? SetXAxisLimits: {minX} to {maxX}");
            }
        }

        public void SetAxisAbsoluteLimits(double minX, double maxX)
        {
            var plotModel = this.PlotModel;
            if (plotModel == null) return;

            var xAxis = plotModel.Axes.FirstOrDefault(a => a is DateTimeAxis);
            if (xAxis != null)
            {
                // ????? ????? ??????
                if (double.IsNaN(minX) || double.IsInfinity(minX) || minX == 0 ||
                    double.IsNaN(maxX) || double.IsInfinity(maxX) || maxX == 0 ||
                    minX >= maxX)
                {
                    System.Diagnostics.Debug.WriteLine($"?? SetAxisAbsoluteLimits: Invalid values! minX={minX}, maxX={maxX}");
                    return;
                }

                xAxis.AbsoluteMinimum = minX;
                xAxis.AbsoluteMaximum = maxX;
                xAxis.Minimum = minX;
                xAxis.Maximum = maxX;

                plotModel.InvalidatePlot(true);

                System.Diagnostics.Debug.WriteLine($"? SetAxisAbsoluteLimits: {minX} to {maxX}");
            }
        }
        public void SetYAxisLimits(double? minY, double? maxY)
        {
            var plotModel = this.PlotModel;
            if (plotModel == null) return;

            var yAxis = plotModel.Axes.FirstOrDefault(a => a is LinearAxis);
            if (yAxis != null)
            {
                if (minY.HasValue && maxY.HasValue)
                {
                    // ????? ????? ??????
                    if (double.IsNaN(minY.Value) || double.IsInfinity(minY.Value) ||
                        double.IsNaN(maxY.Value) || double.IsInfinity(maxY.Value) ||
                        minY.Value >= maxY.Value)
                    {
                        System.Diagnostics.Debug.WriteLine($"?? SetYAxisLimits: Invalid values! minY={minY}, maxY={maxY}");
                        yAxis.Reset();
                        return;
                    }

                    yAxis.Minimum = minY.Value;
                    yAxis.Maximum = maxY.Value;
                }
                else
                {
                    yAxis.Reset();
                }

                plotModel.InvalidatePlot(true);
            }
        }
    }

    public class GraphsViewModel : INotifyPropertyChanged
    {
        private readonly GraphService _graphService;
        private Dictionary<string, List<DateTimePoint>> _allData;
        private List<MachineStateSegment> _allStates;

        public ObservableCollection<SingleChartViewModel> Charts { get; set; } = new ObservableCollection<SingleChartViewModel>();
        public ObservableCollection<GraphNode> ComponentTree { get; set; } = new ObservableCollection<GraphNode>();
        public ObservableCollection<string> ActiveChartSignals { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<MachineStateSegment> StateTimeline { get; set; } = new ObservableCollection<MachineStateSegment>();
        public ObservableCollection<AnalysisEvent> AnalysisResults { get; set; } = new ObservableCollection<AnalysisEvent>();

        private string _selectedSignalToRemove;
        public string SelectedSignalToRemove { get => _selectedSignalToRemove; set { _selectedSignalToRemove = value; OnPropertyChanged(); } }

        private SingleChartViewModel _selectedChart;
        public SingleChartViewModel SelectedChart
        {
            get => _selectedChart;
            set
            {
                if (_selectedChart != null) _selectedChart.IsActive = false;
                _selectedChart = value;
                if (_selectedChart != null) { _selectedChart.IsActive = true; UpdateActiveSignalsList(); }
                OnPropertyChanged();
            }
        }

        private string _status = "Ready";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string _searchText;
        public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(); FilterTree(_searchText); } }

        private DateTime _logStartTime, _logEndTime;

        private DateTime _filterStartTime;
        public DateTime FilterStartTime
        {
            get => _filterStartTime;
            set { _filterStartTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartTimeText)); }
        }

        private DateTime _filterEndTime;
        public DateTime FilterEndTime
        {
            get => _filterEndTime;
            set { _filterEndTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndTimeText)); }
        }

        public string StartTimeText
        {
            get => _filterStartTime.ToString("HH:mm:ss");
            set { if (DateTime.TryParse(value, out DateTime dt)) FilterStartTime = _filterStartTime.Date + dt.TimeOfDay; }
        }

        public string EndTimeText
        {
            get => _filterEndTime.ToString("HH:mm:ss");
            set { if (DateTime.TryParse(value, out DateTime dt)) FilterEndTime = _filterEndTime.Date + dt.TimeOfDay; }
        }

        public ICommand AddChartCommand { get; }
        public ICommand RemoveChartCommand { get; }
        public ICommand ClearChartCommand { get; }
        public ICommand AddSignalToActiveCommand { get; }
        public ICommand RemoveSignalFromActiveCommand { get; }
        public ICommand ApplyTimeFilterCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand ZoomToStateCommand { get; }
        public ICommand ClearAnalysisCommand { get; }
        public ICommand ZoomToAnalysisEventCommand { get; }

        public GraphsViewModel()
        {
            _graphService = new GraphService();

            AddChartCommand = new RelayCommand(o => AddNewChart());
            RemoveChartCommand = new RelayCommand(o => RemoveSelectedChart());
            ClearChartCommand = new RelayCommand(o => ClearSelectedChart());
            AddSignalToActiveCommand = new RelayCommand(param => { if (param is string s) AddSignalToChart(s); });

            RemoveSignalFromActiveCommand = new RelayCommand(param =>
            {
                string toRemove = param as string ?? SelectedSignalToRemove;
                if (!string.IsNullOrEmpty(toRemove)) RemoveSignalFromChart(toRemove);
            });

            ApplyTimeFilterCommand = new RelayCommand(o => ApplyTimeFilter());
            ResetZoomCommand = new RelayCommand(o => ResetZoom());
            ZoomToStateCommand = new RelayCommand(ZoomToState);
            ZoomToAnalysisEventCommand = new RelayCommand(ZoomToAnalysisEvent);
            ClearAnalysisCommand = new RelayCommand(o => AnalysisResults.Clear());

            AddNewChart();
        }

        public async Task ProcessLogsAsync(IEnumerable<LogEntry> logs)
        {
            Status = "Processing Data...";
            System.Diagnostics.Debug.WriteLine("?? ProcessLogsAsync STARTED");

            var result = await _graphService.ParseLogsToGraphDataAsync(logs);

            _allData = new Dictionary<string, List<DateTimePoint>>();
            foreach (var kvp in result.Item1)
            {
                _allData[kvp.Key] = kvp.Value.Select(dp => new DateTimePoint(
                    new DateTime((long)dp.X),
                    dp.Y
                )).ToList();
            }

            ComponentTree = result.Item2;
            _allStates = result.Item3;

            OnPropertyChanged(nameof(ComponentTree));

            StateTimeline.Clear();
            if (_allStates != null)
                foreach (var s in _allStates)
                    StateTimeline.Add(s);

            if (logs.Any())
            {
                var t1 = logs.First().Date;
                var t2 = logs.Last().Date;
                _logStartTime = t1 < t2 ? t1 : t2;
                _logEndTime = t1 > t2 ? t1 : t2;

                if ((_logEndTime - _logStartTime).TotalSeconds < 1)
                    _logEndTime = _logStartTime.AddSeconds(1);

                double minX = DateTimeAxis.ToDouble(_logStartTime);
                double maxX = DateTimeAxis.ToDouble(_logEndTime);

                // ????? ????? ??????
                if (minX == 0 || maxX == 0 || minX >= maxX)
                {
                    System.Diagnostics.Debug.WriteLine($"?? ProcessLogsAsync: Invalid time conversion!");
                    Status = "Error: Invalid log times!";
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"?? Log range: {_logStartTime:HH:mm:ss.fff} - {_logEndTime:HH:mm:ss.fff}");
                System.Diagnostics.Debug.WriteLine($"?? Axis values: minX={minX}, maxX={maxX}");

                foreach (var chart in Charts)
                {
                    chart.SetAxisAbsoluteLimits(minX, maxX);
                    chart.SetXAxisLimits(minX, maxX);
                }

                FilterStartTime = _logStartTime;
                FilterEndTime = _logEndTime;
            }

            System.Diagnostics.Debug.WriteLine($"?? About to call PlotStateBackgrounds. _allStates count: {_allStates?.Count ?? 0}, Charts count: {Charts?.Count ?? 0}");
            PlotStateBackgrounds();

            Status = $"Loaded {_allData.Count} signals.";
        }
        public void SetTimeRange(DateTime start, DateTime end)
        {
            if (end <= start) end = start.AddMilliseconds(500);
            FilterStartTime = start;
            FilterEndTime = end;
            UpdateGraphsView(start, end);
        }

        private void FilterTree(string text)
        {
            if (ComponentTree == null) return;
            foreach (var node in ComponentTree) FilterNodeRecursive(node, text);
        }

        private bool FilterNodeRecursive(GraphNode node, string text)
        {
            bool match = string.IsNullOrEmpty(text) || node.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            bool childMatch = false;
            foreach (var child in node.Children)
            {
                if (FilterNodeRecursive(child, text)) childMatch = true;
            }
            if (childMatch) { node.IsVisible = true; node.IsExpanded = true; return true; }
            node.IsVisible = match;
            if (!match) node.IsExpanded = false;
            return match;
        }

        private void AddNewChart()
        {
            if (Charts.Count >= 5) return;
            var vm = new SingleChartViewModel($"Chart {Charts.Count + 1}");

            if (_logStartTime != DateTime.MinValue)
            {
                double minX = DateTimeAxis.ToDouble(_logStartTime);
                double maxX = DateTimeAxis.ToDouble(_logEndTime);

                vm.SetAxisAbsoluteLimits(minX, maxX);
                vm.SetXAxisLimits(minX, maxX);
            }

            Charts.Add(vm);
            SelectedChart = vm;

            PlotStateBackgrounds();
        }

        private void RemoveSelectedChart()
        {
            if (SelectedChart != null && Charts.Count > 1)
            {
                Charts.Remove(SelectedChart);
                SelectedChart = Charts.FirstOrDefault();
            }
        }

        private void ClearSelectedChart()
        {
            if (SelectedChart != null)
            {
                SelectedChart.PlotModel.Series.Clear();
                SelectedChart.PlottedKeys.Clear();
                UpdateActiveSignalsList();
                PlotStateBackgrounds();
                SelectedChart.PlotModel.InvalidatePlot(true);
            }
        }

        public void AddSignalToChart(string key)
        {
            if (SelectedChart == null) { MessageBox.Show("Please select a chart first."); return; }
            if (_allData == null || !_allData.ContainsKey(key))
            {
                MessageBox.Show($"No data found for signal: {key}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedChart.PlottedKeys.Contains(key)) return;

            // ????? ?????? ???? ???? ????? ????
            var xAxis = SelectedChart.PlotModel.Axes.FirstOrDefault(a => a is DateTimeAxis);
            double savedMinX = xAxis?.Minimum ?? 0;
            double savedMaxX = xAxis?.Maximum ?? 0;

            System.Diagnostics.Debug.WriteLine($"?? BEFORE adding series:");
            System.Diagnostics.Debug.WriteLine($"   Minimum: {xAxis?.Minimum}");
            System.Diagnostics.Debug.WriteLine($"   Maximum: {xAxis?.Maximum}");
            System.Diagnostics.Debug.WriteLine($"   AbsoluteMinimum: {xAxis?.AbsoluteMinimum}");
            System.Diagnostics.Debug.WriteLine($"   AbsoluteMaximum: {xAxis?.AbsoluteMaximum}");

            if (double.IsNaN(savedMinX) || savedMinX == 0 || double.IsNaN(savedMaxX) || savedMaxX == 0)
            {
                savedMinX = DateTimeAxis.ToDouble(_logStartTime);
                savedMaxX = DateTimeAxis.ToDouble(_logEndTime);
            }

            var points = _allData[key];
            List<DateTimePoint> displayPoints;

            if (points.Count > 10000)
            {
                displayPoints = new List<DateTimePoint>(10000);
                int step = Math.Max(1, points.Count / 8000);
                displayPoints.Add(points[0]);
                for (int i = step; i < points.Count - 1; i += step)
                {
                    displayPoints.Add(points[i]);
                }
                if (points.Count > 1)
                    displayPoints.Add(points[points.Count - 1]);
            }
            else
            {
                displayPoints = points;
            }

            var colors = new[] { OxyColors.Blue, OxyColors.Red, OxyColors.Green, OxyColors.OrangeRed, OxyColors.Purple, OxyColors.Teal };
            var lineColor = colors[SelectedChart.PlotModel.Series.Count % colors.Length];

            var series = new StairStepSeries
            {
                Title = key.Split('.').Last(),
                Color = lineColor,
                StrokeThickness = 2,
                MarkerType = MarkerType.None,
                Tag = key
            };

            foreach (var pt in displayPoints)
            {
                series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(pt.DateTime), pt.Value));
            }

            SelectedChart.PlotModel.Series.Add(series);

            System.Diagnostics.Debug.WriteLine($"?? AFTER adding series (before restore):");
            System.Diagnostics.Debug.WriteLine($"   Minimum: {xAxis?.Minimum}");
            System.Diagnostics.Debug.WriteLine($"   Maximum: {xAxis?.Maximum}");

            SelectedChart.PlottedKeys.Add(key);

            string newTitle = string.Join(", ", SelectedChart.PlotModel.Series.Select(s => s.Title));
            SelectedChart.UpdateTitle(newTitle);

            UpdateActiveSignalsList();

            // ????? ?????? ???? InvalidatePlot
            SelectedChart.SetXAxisLimits(savedMinX, savedMaxX);

            System.Diagnostics.Debug.WriteLine($"?? AFTER SetXAxisLimits:");
            System.Diagnostics.Debug.WriteLine($"   Minimum: {xAxis?.Minimum}");
            System.Diagnostics.Debug.WriteLine($"   Maximum: {xAxis?.Maximum}");

            AutoZoomYAxis(SelectedChart, savedMinX, savedMaxX);

            PlotStateBackgrounds();

            System.Diagnostics.Debug.WriteLine($"?? AFTER PlotStateBackgrounds:");
            System.Diagnostics.Debug.WriteLine($"   Minimum: {xAxis?.Minimum}");
            System.Diagnostics.Debug.WriteLine($"   Maximum: {xAxis?.Maximum}");

            SelectedChart.PlotModel.InvalidatePlot(false);

            System.Diagnostics.Debug.WriteLine($"?? AFTER InvalidatePlot:");
            System.Diagnostics.Debug.WriteLine($"   Minimum: {xAxis?.Minimum}");
            System.Diagnostics.Debug.WriteLine($"   Maximum: {xAxis?.Maximum}");

            System.Diagnostics.Debug.WriteLine($"? AddSignalToChart: Restored X axis to {savedMinX} - {savedMaxX}");
        }
        public void RemoveSignalFromChart(string key)
        {
            if (SelectedChart == null) return;

            // ? ????? ?????? ???????? ???? ?? ?????
            var xAxis = SelectedChart.PlotModel.Axes.FirstOrDefault(a => a is DateTimeAxis);
            double savedMinX = xAxis?.Minimum ?? 0;
            double savedMaxX = xAxis?.Maximum ?? 0;

            // ?? ??? ????? ??????, ????? ????? ????
            if (double.IsNaN(savedMinX) || savedMinX == 0 || double.IsNaN(savedMaxX) || savedMaxX == 0)
            {
                savedMinX = DateTimeAxis.ToDouble(_logStartTime);
                savedMaxX = DateTimeAxis.ToDouble(_logEndTime);
            }

            var series = SelectedChart.PlotModel.Series.FirstOrDefault(s => (string)s.Tag == key);
            if (series != null)
            {
                SelectedChart.PlotModel.Series.Remove(series);
                SelectedChart.PlottedKeys.Remove(key);

                string newTitle = string.Join(", ", SelectedChart.PlotModel.Series.Select(s => s.Title));
                SelectedChart.UpdateTitle(newTitle);

                UpdateActiveSignalsList();

                // ? ????? ?????? ???????
                SelectedChart.SetXAxisLimits(savedMinX, savedMaxX);
                AutoZoomYAxis(SelectedChart, savedMinX, savedMaxX);

                PlotStateBackgrounds();
                SelectedChart.PlotModel.InvalidatePlot(false);

                System.Diagnostics.Debug.WriteLine($"? RemoveSignalFromChart: Restored X axis to {savedMinX} - {savedMaxX}");
            }
        }

        private void UpdateActiveSignalsList()
        {
            ActiveChartSignals.Clear();
            if (SelectedChart != null)
                foreach (var k in SelectedChart.PlottedKeys)
                    ActiveChartSignals.Add(k);
        }

        public void UpdateGraphsView(DateTime start, DateTime end)
        {
            System.Diagnostics.Debug.WriteLine($"?? UpdateGraphsView: {start:HH:mm:ss.fff} - {end:HH:mm:ss.fff}");

            // ????? ???? ????
            if (start < _logStartTime) start = _logStartTime;
            if (end > _logEndTime) end = _logEndTime;
            if (start >= end)
            {
                System.Diagnostics.Debug.WriteLine($"?? Invalid range! start >= end");
                return;
            }

            FilterStartTime = start;
            FilterEndTime = end;

            double min = DateTimeAxis.ToDouble(start);
            double max = DateTimeAxis.ToDouble(end);

            System.Diagnostics.Debug.WriteLine($"?? Updating {Charts.Count} charts: minX={min}, maxX={max}");

            foreach (var chart in Charts)
            {
                if (chart == null) continue;

                chart.SetXAxisLimits(min, max);
                AutoZoomYAxis(chart, min, max);
            }

            PlotStateBackgrounds();
        }

        public void AutoZoomYAxis(SingleChartViewModel chart, double minX, double maxX)
        {
            try
            {
                var plotModel = chart.PlotModel;
                if (plotModel == null) return;

                double yMin = double.MaxValue;
                double yMax = double.MinValue;
                bool found = false;

                foreach (var series in plotModel.Series)
                {
                    if (series is StairStepSeries stepSeries)
                    {
                        foreach (var p in stepSeries.Points)
                        {
                            if (p.X >= minX && p.X <= maxX)
                            {
                                found = true;
                                if (!double.IsNaN(p.Y) && !double.IsInfinity(p.Y))
                                {
                                    if (p.Y < yMin) yMin = p.Y;
                                    if (p.Y > yMax) yMax = p.Y;
                                }
                            }
                        }
                    }
                }

                if (found && yMin != double.MaxValue && yMax != double.MinValue && yMin != yMax)
                {
                    double pad = (yMax - yMin) * 0.1;
                    if (pad == 0) pad = 1;
                    chart.SetYAxisLimits(yMin - pad, yMax + pad);
                }
                else if (found && yMin == yMax)
                {
                    chart.SetYAxisLimits(yMin - 10, yMax + 10);
                }
                else
                {
                    chart.SetYAxisLimits(null, null);
                }
            }
            catch { }
        }

        // ? RESET - ????? ?? ?? ??'????? ????? ????
        private void ResetZoom()
        {
            if (_logStartTime == DateTime.MinValue)
            {
                Status = "No data loaded.";
                return;
            }

            // ????? ???? ???? ?????
            FilterStartTime = _logStartTime;
            FilterEndTime = _logEndTime;

            double minX = DateTimeAxis.ToDouble(_logStartTime);
            double maxX = DateTimeAxis.ToDouble(_logEndTime);

            // ????? **??** ??'????? ?-??????
            foreach (var chart in Charts)
            {
                if (chart == null) continue;

                chart.SetXAxisLimits(minX, maxX);
                AutoZoomYAxis(chart, minX, maxX);
            }

            PlotStateBackgrounds();

            Status = $"Reset: showing full log range ({_logStartTime:HH:mm:ss} - {_logEndTime:HH:mm:ss})";

            System.Diagnostics.Debug.WriteLine($"?? RESET ALL CHARTS: {_logStartTime:HH:mm:ss.fff} to {_logEndTime:HH:mm:ss.fff}");
        }
        // ? APPLY - ????? ?? ??'????? ??? ???? Start/End
        private void ApplyTimeFilter()
        {
            System.Diagnostics.Debug.WriteLine($"? APPLY clicked: Start={FilterStartTime:HH:mm:ss.fff}, End={FilterEndTime:HH:mm:ss.fff}");

            var start = FilterStartTime;
            var end = FilterEndTime;

            // ????? ???? ????
            if (start < _logStartTime)
            {
                start = _logStartTime;
                FilterStartTime = start;
            }
            if (end > _logEndTime)
            {
                end = _logEndTime;
                FilterEndTime = end;
            }

            // ????? ??????
            if (start >= end)
            {
                MessageBox.Show($"Invalid time range.\nStart: {start:HH:mm:ss}\nEnd: {end:HH:mm:ss}\n\nStart time must be before end time.",
                    "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ????? **??** ??'????? ?-??????
            double minX = DateTimeAxis.ToDouble(start);
            double maxX = DateTimeAxis.ToDouble(end);

            foreach (var chart in Charts)
            {
                if (chart == null) continue;

                chart.SetXAxisLimits(minX, maxX);
                AutoZoomYAxis(chart, minX, maxX);
            }

            PlotStateBackgrounds();

            Status = $"Applied: {start:HH:mm:ss} - {end:HH:mm:ss}";

            System.Diagnostics.Debug.WriteLine($"? APPLY ALL CHARTS: minX={minX}, maxX={maxX}");
        }
        private void ZoomToState(object param)
        {
            if (param is MachineStateSegment s)
            {
                SetTimeRange(s.StartTimeValue, s.EndTimeValue);
            }
        }

        private void ZoomToAnalysisEvent(object param)
        {
            if (param is AnalysisEvent ev)
                UpdateGraphsView(ev.PeakTime.AddSeconds(-10), ev.PeakTime.AddSeconds(10));
        }

        public void PlotStateBackgrounds()
        {
            if (_allStates == null || !_allStates.Any())
            {
                System.Diagnostics.Debug.WriteLine("?? No states to plot!");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"?? Plotting {_allStates.Count} states on {Charts.Count} charts");

            foreach (var chart in Charts)
            {
                if (chart?.PlotModel == null) continue;

                var plotModel = chart.PlotModel;

                var existingStateAnnotations = plotModel.Annotations
                    .Where(a => a is RectangleAnnotation && a.Tag?.ToString() == "StateBackground")
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"  ??? Removing {existingStateAnnotations.Count} old annotations from chart");

                foreach (var ann in existingStateAnnotations)
                {
                    plotModel.Annotations.Remove(ann);
                }

                foreach (var state in _allStates)
                {
                    var annotation = new RectangleAnnotation
                    {
                        MinimumX = state.Start,
                        MaximumX = state.End,
                        MinimumY = double.NegativeInfinity,
                        MaximumY = double.PositiveInfinity,
                        Fill = OxyColor.FromAColor(80, state.Color),
                        Layer = AnnotationLayer.BelowSeries,
                        Tag = "StateBackground",
                        ClipByXAxis = false,
                        ClipByYAxis = false
                    };

                    plotModel.Annotations.Add(annotation);
                }

                System.Diagnostics.Debug.WriteLine($"  ? Chart now has {plotModel.Annotations.Count} annotations");

                // ? ????? ????? - false ????? true!
                plotModel.InvalidatePlot(false);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}