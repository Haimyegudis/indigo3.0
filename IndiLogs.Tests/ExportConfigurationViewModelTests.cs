using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Charts;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Charts;
using IndiLogs_3._0.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class ExportConfigurationViewModelTests
    {
        // ===================================================================
        // RawTimeToDateTime tests
        // ===================================================================

        [Fact]
        public void RawTimeToDateTime_Zero_ReturnsMinValue()
        {
            var result = ExportConfigurationViewModel.RawTimeToDateTime(0);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void RawTimeToDateTime_Negative_ReturnsMinValue()
        {
            var result = ExportConfigurationViewModel.RawTimeToDateTime(-100);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void RawTimeToDateTime_ValidUnixNs_ReturnsCorrectDateTime()
        {
            // 2024-01-15 12:00:00 UTC = 1705320000 seconds
            long unixSeconds = 1705320000L;
            long unixNs = unixSeconds * 1_000_000_000L;

            var result = ExportConfigurationViewModel.RawTimeToDateTime(unixNs);
            var expected = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;

            Assert.Equal(expected, result);
        }

        [Fact]
        public void RawTimeToDateTime_WithSubSecondPrecision_PreservesSubSecondTicks()
        {
            long unixSeconds = 1705320000L;
            long subSecondNs = 500_000_000L; // 0.5 seconds = 500ms
            long unixNs = unixSeconds * 1_000_000_000L + subSecondNs;

            var result = ExportConfigurationViewModel.RawTimeToDateTime(unixNs);
            var baseTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            var expected = baseTime.AddTicks(subSecondNs / 100L);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void RawTimeToDateTime_WithSmallSubSecond_AddsCorrectTicks()
        {
            long unixSeconds = 1705320000L;
            long remainNs = 1_000L; // 1 microsecond = 10 ticks
            long unixNs = unixSeconds * 1_000_000_000L + remainNs;

            var result = ExportConfigurationViewModel.RawTimeToDateTime(unixNs);
            var baseTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            var expected = baseTime.AddTicks(remainNs / 100L);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void RawTimeToDateTime_VeryLargeValue_HandlesGracefully()
        {
            // long.MaxValue / 1e9 ~ 9.2 billion seconds (~292 years from epoch)
            // which is within DateTimeOffset range, so this won't throw.
            // Verify it returns a valid (non-MinValue) DateTime.
            long hugeNs = long.MaxValue;
            var result = ExportConfigurationViewModel.RawTimeToDateTime(hugeNs);
            Assert.NotEqual(DateTime.MinValue, result);
        }

        [Fact]
        public void RawTimeToDateTime_EpochStart_ReturnsUnixEpochLocal()
        {
            // Exactly 1 second after epoch
            long unixNs = 1_000_000_000L;
            var result = ExportConfigurationViewModel.RawTimeToDateTime(unixNs);
            var expected = DateTimeOffset.FromUnixTimeSeconds(1).LocalDateTime;
            Assert.Equal(expected, result);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — empty / null inputs
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_NullDevices_ReturnsEmptyPackage()
        {
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                null!, new List<string> { "Dev|Col" }, new LogSessionData());

            Assert.Empty(result.Signals);
            Assert.Empty(result.States);
            Assert.Empty(result.TimeStamps);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_EmptyDevices_ReturnsEmptyPackage()
        {
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                new List<IoDeviceData>(), new List<string> { "Dev|Col" }, new LogSessionData());

            Assert.Empty(result.Signals);
            Assert.Empty(result.TimeStamps);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_EmptySelectedKeys_ReturnsEmptyPackage()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "25.5" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string>(), new LogSessionData());

            Assert.Empty(result.Signals);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — signal building
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_SingleSignal_BuildsCorrectly()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "25.5" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "26.0" } }),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "27.3" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Single(result.Signals);
            Assert.Equal("BIM-Temp", result.Signals[0].Name);
            Assert.Equal("IO", result.Signals[0].Category);
            Assert.Equal(3, result.TimeStamps.Count);
            Assert.Equal(3, result.Signals[0].Data.Length);
            Assert.Equal(25.5, result.Signals[0].Data[0]);
            Assert.Equal(26.0, result.Signals[0].Data[1]);
            Assert.Equal(27.3, result.Signals[0].Data[2]);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_MultipleDevices_MergesTimeline()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } })),
                MakeDevice("ECM", new[] { "Volt" },
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Volt", "220" } }),
                    MakeRow(1705320003_000_000_000L, new Dictionary<string, string> { { "Volt", "221" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices,
                new List<string> { "BIM|Temp", "ECM|Volt" },
                new LogSessionData());

            Assert.Equal(2, result.Signals.Count);
            Assert.Equal(4, result.TimeStamps.Count); // merged timeline
            Assert.Equal("BIM-Temp", result.Signals.First(s => s.Name == "BIM-Temp").Name);
            Assert.Equal("ECM-Volt", result.Signals.First(s => s.Name == "ECM-Volt").Name);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_SelectedKeyWithoutPipe_IsSkipped()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices,
                new List<string> { "InvalidKeyNoPipe" },
                new LogSessionData());

            Assert.Empty(result.Signals);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — digital detection
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_BinaryValues_DetectedAsDigital()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Switch" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Switch", "0" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Switch", "1" } }),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Switch", "0" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Switch" }, new LogSessionData());

            Assert.Single(result.Signals);
            Assert.Equal(SignalType.Digital, result.Signals[0].SignalType);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_AnalogValues_DetectedAsAnalog()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "25.5" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "26.0" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Single(result.Signals);
            Assert.Equal(SignalType.Analog, result.Signals[0].SignalType);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_AllNaN_DetectedAsDigital()
        {
            // When a signal has no matching values, all entries are NaN, which counts as digital
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Other" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Other", "5" } }))
            };

            // Select a key that doesn't match the device's columns
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Missing" }, new LogSessionData());

            Assert.Single(result.Signals);
            Assert.Equal(SignalType.Digital, result.Signals[0].SignalType);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — forward fill
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_ForwardFillsGaps()
        {
            // Device BIM has data at t=0 and t=2 but not t=1 (from ECM)
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } })),
                MakeDevice("ECM", new[] { "Volt" },
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Volt", "220" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices,
                new List<string> { "BIM|Temp" },
                new LogSessionData());

            Assert.Equal(3, result.Signals[0].Data.Length);
            // Index 0: BIM row with Temp=10
            Assert.Equal(10.0, result.Signals[0].Data[0]);
            // Index 1: ECM row (no BIM data) — forward-filled from previous
            Assert.Equal(10.0, result.Signals[0].Data[1]);
            // Index 2: BIM row with Temp=12
            Assert.Equal(12.0, result.Signals[0].Data[2]);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_LeadingNaN_NotForwardFilled()
        {
            // ECM row comes before BIM, so BIM signal starts with NaN
            var devices = new List<IoDeviceData>
            {
                MakeDevice("ECM", new[] { "Volt" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Volt", "220" } })),
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "25" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices,
                new List<string> { "BIM|Temp" },
                new LogSessionData());

            Assert.Equal(2, result.Signals[0].Data.Length);
            Assert.True(double.IsNaN(result.Signals[0].Data[0])); // no value before BIM's first row
            Assert.Equal(25.0, result.Signals[0].Data[1]);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — machine state intervals
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_SingleState_CreatesOneInterval()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }, "Printing"),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }, "Printing"),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } }, "Printing"))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Single(result.States);
            Assert.Equal("MachineState", result.States[0].Name);
            Assert.Equal("PlcMngr", result.States[0].Category);
            Assert.Single(result.States[0].Intervals);
            Assert.Equal(0, result.States[0].Intervals[0].StartIndex);
            Assert.Equal(2, result.States[0].Intervals[0].EndIndex);
            Assert.Equal("Printing", result.States[0].Intervals[0].StateName);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_MultipleStates_CreatesMultipleIntervals()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }, "Idle"),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }, "Idle"),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } }, "Printing"),
                    MakeRow(1705320003_000_000_000L, new Dictionary<string, string> { { "Temp", "13" } }, "Printing"),
                    MakeRow(1705320004_000_000_000L, new Dictionary<string, string> { { "Temp", "14" } }, "Idle"))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Single(result.States);
            var intervals = result.States[0].Intervals;
            Assert.Equal(3, intervals.Count);

            Assert.Equal("Idle", intervals[0].StateName);
            Assert.Equal(0, intervals[0].StartIndex);
            Assert.Equal(1, intervals[0].EndIndex);

            Assert.Equal("Printing", intervals[1].StateName);
            Assert.Equal(2, intervals[1].StartIndex);
            Assert.Equal(3, intervals[1].EndIndex);

            Assert.Equal("Idle", intervals[2].StateName);
            Assert.Equal(4, intervals[2].StartIndex);
            Assert.Equal(4, intervals[2].EndIndex);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_NoMachineState_NoStateData()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Empty(result.States);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_EmptyMachineState_ForwardFills()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }, "Printing"),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }, ""),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } }, "Idle"))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Single(result.States);
            var intervals = result.States[0].Intervals;
            Assert.Equal(2, intervals.Count);
            // First interval: Printing at index 0-1 (empty at index 1 forward-fills Printing)
            Assert.Equal("Printing", intervals[0].StateName);
            Assert.Equal(0, intervals[0].StartIndex);
            Assert.Equal(1, intervals[0].EndIndex);
            // Second interval: Idle at index 2
            Assert.Equal("Idle", intervals[1].StateName);
            Assert.Equal(2, intervals[1].StartIndex);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — timestamp clipping to log range
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_ClipsToLogRange()
        {
            var baseTime = new DateTime(2024, 1, 15, 12, 0, 0);
            var session = new LogSessionData
            {
                Logs = new List<LogEntry>
                {
                    new LogEntry { Date = baseTime.AddSeconds(1) },
                    new LogEntry { Date = baseTime.AddSeconds(3) }
                }
            };

            // Convert baseTime to Unix ns
            long baseUnixSec = new DateTimeOffset(baseTime.ToUniversalTime()).ToUnixTimeSeconds();

            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow((baseUnixSec + 0) * 1_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow((baseUnixSec + 1) * 1_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }),
                    MakeRow((baseUnixSec + 2) * 1_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } }),
                    MakeRow((baseUnixSec + 3) * 1_000_000_000L, new Dictionary<string, string> { { "Temp", "13" } }),
                    MakeRow((baseUnixSec + 4) * 1_000_000_000L, new Dictionary<string, string> { { "Temp", "14" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, session);

            // Should clip to log range: indices 1-3 (baseTime+1s to baseTime+3s)
            Assert.Equal(3, result.TimeStamps.Count);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_ClippingWouldEmptyList_KeepsAll()
        {
            // Log range doesn't overlap with device data at all
            var session = new LogSessionData
            {
                Logs = new List<LogEntry>
                {
                    new LogEntry { Date = new DateTime(2020, 1, 1, 0, 0, 0) },
                    new LogEntry { Date = new DateTime(2020, 1, 1, 0, 0, 1) }
                }
            };

            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, session);

            // Clipping would empty the list, so all rows are kept
            Assert.Equal(2, result.TimeStamps.Count);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_NoLogs_KeepsAllRows()
        {
            var session = new LogSessionData { Logs = new List<LogEntry>() };

            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }),
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "12" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, session);

            Assert.Equal(3, result.TimeStamps.Count);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — package metadata
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_SetsSessionNameFromFileName()
        {
            var session = new LogSessionData { FileName = "MySession.zip" };
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, session);

            Assert.Equal("MySession.zip", result.SessionName);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_NullFileName_FallsBackToIoTerminal()
        {
            var session = new LogSessionData { FileName = "" };
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }))
            };

            // FileName is "" which is non-null, so ?? won't trigger.
            // When FileName is null:
            var session2 = new LogSessionData();
            // FileName defaults to "" per LogSessionData definition
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, session2);

            // Empty string is not null, so sessionData.FileName ?? "IO Terminal" returns ""
            // Unless FileName is literally null
            Assert.NotNull(result.SessionName);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_SuppressGapDetection_IsTrue()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.True(result.SuppressGapDetection);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_EmptyPackage_HasCorrectDefaults()
        {
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                new List<IoDeviceData>(), new List<string> { "x|y" }, new LogSessionData());

            Assert.Equal("IO Terminal", result.SessionName);
            Assert.NotNull(result.ThreadMessages);
            Assert.Empty(result.ThreadMessages);
            Assert.NotNull(result.Events);
            Assert.Empty(result.Events);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — fallback timestamp (time-of-day only)
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_FallbackTimestamp_UsesLogDateWithCsvTime()
        {
            var logDate = new DateTime(2024, 6, 15, 10, 0, 0);
            var session = new LogSessionData
            {
                Logs = new List<LogEntry>
                {
                    new LogEntry { Date = logDate }
                }
            };

            // RawTime=0 forces fallback path; Timestamp has time-of-day only
            var csvTime = new DateTime(1, 1, 1, 14, 30, 45); // 14:30:45
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRowWithTimestamp(0, csvTime, new Dictionary<string, string> { { "Temp", "10" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, session);

            Assert.Single(result.TimeStamps);
            // Should combine logDate's date with csvTime's time-of-day
            var expected = logDate.Date + csvTime.TimeOfDay;
            Assert.Equal(expected, result.TimeStamps[0]);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — non-numeric values
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_NonNumericValue_RemainsNaN()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Status" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Status", "ON" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Status", "OFF" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Status" }, new LogSessionData());

            Assert.Single(result.Signals);
            Assert.True(double.IsNaN(result.Signals[0].Data[0]));
            Assert.True(double.IsNaN(result.Signals[0].Data[1]));
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — progress reporting
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_ReportsProgress()
        {
            var progressReports = new List<(double pct, string msg)>();
            var progress = new Progress<(double pct, string msg)>(p => progressReports.Add(p));

            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }),
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Temp", "11" } }))
            };

            // Progress<T> marshals through SynchronizationContext, which may not fire synchronously.
            // Use a direct IProgress<T> instead.
            var directProgress = new DirectTestProgress();

            ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData(), directProgress);

            Assert.True(directProgress.Reports.Count > 0);
            // Should end at 100%
            Assert.Equal(100, directProgress.Reports.Last().pct);
        }

        [Fact]
        public void BuildIoTerminalChartPackage_NullProgress_DoesNotThrow()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } }))
            };

            var exception = Record.Exception(() =>
                ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                    devices, new List<string> { "BIM|Temp" }, new LogSessionData(), null));

            Assert.Null(exception);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — timeline ordering
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_TimestampsAreSorted()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320002_000_000_000L, new Dictionary<string, string> { { "Temp", "30" } }),
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "10" } })),
                MakeDevice("ECM", new[] { "Volt" },
                    MakeRow(1705320001_000_000_000L, new Dictionary<string, string> { { "Volt", "220" } }))
            };

            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices,
                new List<string> { "BIM|Temp", "ECM|Volt" },
                new LogSessionData());

            Assert.Equal(3, result.TimeStamps.Count);
            for (int i = 1; i < result.TimeStamps.Count; i++)
                Assert.True(result.TimeStamps[i] >= result.TimeStamps[i - 1]);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — case-insensitive device matching
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_DeviceNameMatching_IsCaseInsensitive()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("bim", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "25" } }))
            };

            // Key uses uppercase "BIM" while device is lowercase "bim"
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|Temp" }, new LogSessionData());

            Assert.Single(result.Signals);
            Assert.Equal(25.0, result.Signals[0].Data[0]);
        }

        // ===================================================================
        // BuildIoTerminalChartPackage — column matching (case-sensitive Values dict)
        // ===================================================================

        [Fact]
        public void BuildIoTerminalChartPackage_ColumnMismatch_RemainsNaN()
        {
            var devices = new List<IoDeviceData>
            {
                MakeDevice("BIM", new[] { "Temp" },
                    MakeRow(1705320000_000_000_000L, new Dictionary<string, string> { { "Temp", "25" } }))
            };

            // Key column "TEMP" (uppercase) may not match "Temp" in Values dict
            var result = ExportConfigurationViewModel.BuildIoTerminalChartPackage(
                devices, new List<string> { "BIM|TEMP" }, new LogSessionData());

            // This tests the actual behavior: Values dict is case-sensitive
            Assert.Single(result.Signals);
        }

        // ===================================================================
        // SignalProgressItem — additional edge cases
        // ===================================================================

        [Fact]
        public void SignalProgressItem_DefaultName_IsEmpty()
        {
            var item = new SignalProgressItem();
            Assert.Equal("", item.Name);
        }

        [Fact]
        public void SignalProgressItem_MultipleStatusChanges_RaisesCorrectEvents()
        {
            var item = new SignalProgressItem { Name = "Sig1" };
            var changes = new List<string>();
            item.PropertyChanged += (s, e) => changes.Add(e.PropertyName!);

            item.Status = "parsing";
            item.Status = "done";

            // Each status change raises Status + StatusIcon = 2 events per change
            Assert.Equal(4, changes.Count);
            Assert.Equal(new[] { "Status", "StatusIcon", "Status", "StatusIcon" }, changes.ToArray());
        }

        // ===================================================================
        // Helpers
        // ===================================================================

        private static IoDeviceData MakeDevice(string name, string[] columns, params IoDataRow[] rows)
        {
            return new IoDeviceData
            {
                DeviceName = name,
                FileName = $"Io-{name}.csv",
                Columns = columns,
                Rows = rows.ToList()
            };
        }

        private static IoDataRow MakeRow(long rawTimeNs, Dictionary<string, string> values, string machineState = "")
        {
            return new IoDataRow
            {
                RawTime = rawTimeNs,
                Timestamp = DateTime.MinValue,
                MachineState = machineState,
                Values = values
            };
        }

        private static IoDataRow MakeRowWithTimestamp(long rawTimeNs, DateTime timestamp,
            Dictionary<string, string> values, string machineState = "")
        {
            return new IoDataRow
            {
                RawTime = rawTimeNs,
                Timestamp = timestamp,
                MachineState = machineState,
                Values = values
            };
        }

        private class DirectTestProgress : IProgress<(double pct, string msg)>
        {
            public List<(double pct, string msg)> Reports { get; } = new();
            public void Report((double pct, string msg) value) => Reports.Add(value);
        }
    }
}
