#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartTabControl
    {
        /// <summary>
        /// Handles In-Memory data transfer from ExportConfigurationWindow
        /// </summary>
        private void OnInMemoryDataReady(ChartDataPackage dataPackage)
        {
            if (dataPackage == null) return;

            AppLogger.Info($"[Chart] OnInMemoryDataReady: {dataPackage.Signals?.Count ?? 0} signals, {dataPackage.TimeStamps?.Count ?? 0} timestamps, SuppressGap={dataPackage.SuppressGapDetection}");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _ = LoadInMemoryData(dataPackage);
            }));
        }

        /// <summary>
        /// Loads data directly from memory without file I/O.
        /// Heavy work runs on background threads; UI thread only does final assignments.
        /// </summary>
        public async Task LoadInMemoryData(ChartDataPackage dataPackage)
        {
            if (dataPackage == null) return;

            // Show loading overlay immediately
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Loading chart data...";
            LoadingDetail.Text = $"Processing {dataPackage.Signals?.Count ?? 0} signals";

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _currentDataPackage = dataPackage;
                _inMemoryDataLoaded = true;

                // Set total data length
                _totalDataLength = dataPackage.TimeStamps.Count;
                if (_totalDataLength == 0 && dataPackage.Signals.Any())
                {
                    _totalDataLength = dataPackage.Signals.Max(s => s.DataLength);
                }

                LoadingDetail.Text = $"Building time index for {_totalDataLength:N0} points...";

                // ── Run ALL heavy work on background threads in parallel ──
                var stamps = dataPackage.TimeStamps;
                string[] timeArr = null;
                List<GapRegion> gapRegions = null;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    // 1) Build time mapping directly from DateTimes — no string parsing needed!
                    //    Old code: format→string→TryParse→DateTime (wasteful round-trip for 40K+ items)
                    _syncService.BuildTimeMappingFromDateTimes(stamps);

                    // 2) Format timestamps to strings (for display only) — parallel
                    if (stamps.Count > 0)
                    {
                        timeArr = new string[stamps.Count];
                        System.Threading.Tasks.Parallel.For(0, stamps.Count,
                            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                            i => { timeArr[i] = stamps[i].ToString("yyyy-MM-dd HH:mm:ss.ffffff"); });
                    }

                    // 3) Compute time gap regions (suppressed for IO terminal data)
                    gapRegions = dataPackage.SuppressGapDetection
                        ? new List<GapRegion>()
                        : ComputeTimeGapRegions(stamps);
                });

                _timeData = timeArr;
                _timeGapRegions = gapRegions ?? new List<GapRegion>();

                var bgMs = sw.ElapsedMilliseconds;
                AppLogger.Info($"[ChartLoad] Background work done in {bgMs}ms (time mapping + format + gaps)");

                LoadingDetail.Text = "Populating signal list...";

                // ── UI-thread work — kept minimal ──

                // Populate signal list (with virtualization, ~1900 items is fast)
                SignalList.SetDataPackage(dataPackage);

                // Extract global states from state data (MachineState for timeline)
                _globalStates.Clear();
                var machineState = dataPackage.States.FirstOrDefault(s =>
                    s.Name.Equals("MachineState", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase));

                if (machineState != null)
                {
                    _globalStates.AddRange(machineState.Intervals);
                }

                // Reset view
                _viewStartIndex = 0;
                _viewEndIndex = _totalDataLength - 1;

                // Update timeline with machine states only
                StateTimeline.SetStates(_globalStates, _totalDataLength);

                // Store thread messages (NOT displayed automatically - user selects from list)
                _threadMessages = dataPackage.ThreadMessages ?? new List<ThreadMessageData>();

                // Store CHSTEP states (NOT displayed automatically - user selects from list)
                _chStepStates = dataPackage.States
                    .Where(s => !s.Name.Equals("MachineState", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Store event markers for display on charts
                _eventMarkers = dataPackage.Events ?? new List<EventMarkerData>();

                // Clear existing charts
                _charts.Clear();

                // Ensure theme is current before creating charts
                SyncThemeFromSettings();

                // Don't auto-add a chart — user double-clicks a signal to create one
                EmptyStateMessage.Visibility = Visibility.Visible;

                // Update slider
                NavSlider.Maximum = _totalDataLength > 0 ? _totalDataLength - 1 : 100;

                RefreshChartViews();

                // Parse and store EM Statistics data — add to signal list for on-demand display
                _emStatisticsStates = null;
                _emTimestamps = null;
                _emTotalLength = 0;
                if (!string.IsNullOrEmpty(dataPackage.EmStatisticsCsvContent))
                {
                    try
                    {
                        var (states, timestamps, totalLength) = EmStatisticsService.ParseEmStatistics(dataPackage.EmStatisticsCsvContent);
                        if (states.Count > 0)
                        {
                            _emStatisticsStates = states;
                            _emTimestamps = timestamps;
                            _emTotalLength = totalLength;
                            SignalList.AddEmStatisticsItems(states.Count);
                        }
                    }
                    catch (Exception emEx)
                    {
                        AppLogger.Error("Failed to parse EM Statistics", emEx);
                    }
                }

                sw.Stop();
                AppLogger.Info($"[ChartLoad] Total LoadInMemoryData: {sw.ElapsedMilliseconds}ms (bg={bgMs}ms, UI={sw.ElapsedMilliseconds - bgMs}ms)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading In-Memory data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Computes time gap regions from timestamps (gaps >= 2 seconds)
        /// </summary>
        private List<GapRegion> ComputeTimeGapRegions(List<DateTime> timestamps)
        {
            var regions = new List<GapRegion>();
            if (timestamps == null || timestamps.Count < 2) return regions;

            const double threshold = 2.0; // seconds

            for (int i = 1; i < timestamps.Count; i++)
            {
                var diff = timestamps[i] - timestamps[i - 1];
                if (diff.TotalSeconds >= threshold)
                {
                    regions.Add(new GapRegion
                    {
                        StartIndex = i - 1,
                        EndIndex = i,
                        Duration = FormatGapDuration(diff),
                        StartTime = timestamps[i - 1].ToString("HH:mm:ss.ffffff"),
                        EndTime = timestamps[i].ToString("HH:mm:ss.ffffff")
                    });
                }
            }
            return regions;
        }

        private string FormatGapDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1)
                return $"{ts.TotalMinutes:F1}m";
            return $"{ts.TotalSeconds:F1}s";
        }

        /// <summary>
        /// Computes time gap regions from string timestamps (for CSV loaded data)
        /// </summary>
        private static readonly string[] _gapTimeFormats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.ffffff", "yyyy-MM-dd HH:mm:ss.fffffff",
            "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss",
            "HH:mm:ss.ffffff", "HH:mm:ss.fff", "HH:mm:ss"
        };

        private List<GapRegion> ComputeTimeGapRegionsFromStrings(string[] timeData)
        {
            var regions = new List<GapRegion>();
            if (timeData == null || timeData.Length < 2) return regions;

            const double threshold = 2.0;

            DateTime prevTime = DateTime.MinValue;
            for (int i = 0; i < timeData.Length; i++)
            {
                // Use TryParseExact with known formats - much faster than TryParse
                if (DateTime.TryParseExact(timeData[i], _gapTimeFormats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime currentTime))
                {
                    if (prevTime != DateTime.MinValue)
                    {
                        var diff = currentTime - prevTime;
                        if (diff.TotalSeconds >= threshold)
                        {
                            regions.Add(new GapRegion
                            {
                                StartIndex = i - 1,
                                EndIndex = i,
                                Duration = FormatGapDuration(diff),
                                StartTime = prevTime.ToString("HH:mm:ss.ffffff"),
                                EndTime = currentTime.ToString("HH:mm:ss.ffffff")
                            });
                        }
                    }
                    prevTime = currentTime;
                }
            }
            return regions;
        }
    }
}
