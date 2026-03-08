using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace IndiLogs.Tests
{
    public class IoTerminalDataServiceTests
    {
        private IoTerminalDataService CreateService() => new IoTerminalDataService();

        // ── ExtractDeviceName ─────────────────────────────────────────────

        [Theory]
        [InlineData("Io-BIM[0]_e[1]-Mon_P000_foo.csv", "BIM[0]")]
        [InlineData("Io-ECM_e[1]-Something.csv", "ECM")]
        [InlineData("Io-PDC_xxx.csv", "PDC")]
        [InlineData("Io-SomeDevice_bar.csv", "SomeDevice")]
        public void ExtractDeviceName_StandardNames_ExtractsCorrectly(string fileName, string expected)
        {
            var result = IoTerminalDataService.ExtractDeviceName(fileName);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ExtractDeviceName_NoUnderscore_ReturnsFullNameAfterPrefix()
        {
            var result = IoTerminalDataService.ExtractDeviceName("Io-DeviceOnly.csv");
            Assert.Equal("DeviceOnly", result);
        }

        [Fact]
        public void ExtractDeviceName_CaseInsensitivePrefix()
        {
            var result = IoTerminalDataService.ExtractDeviceName("IO-BIM[0]_e[1].csv");
            Assert.Equal("BIM[0]", result);
        }

        [Fact]
        public void ExtractDeviceName_NoPrefix_ReturnsUpToUnderscore()
        {
            var result = IoTerminalDataService.ExtractDeviceName("SomeFile_extra.csv");
            Assert.Equal("SomeFile", result);
        }

        [Fact]
        public void ExtractDeviceName_UnderscoreAtStart_ReturnsEmpty()
        {
            // "Io-_rest.csv" → name after removing prefix is "_rest", indexOf('_') = 0 → condition us > 0 fails
            var result = IoTerminalDataService.ExtractDeviceName("Io-_rest.csv");
            Assert.Equal("_rest", result);
        }

        // ── ParseIoFiles — basic scenarios ────────────────────────────────

        [Fact]
        public void ParseIoFiles_EmptyDictionaries_ReturnsEmpty()
        {
            var svc = CreateService();
            var result = svc.ParseIoFiles(new Dictionary<string, string>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_NoIoFiles_ReturnsEmpty()
        {
            var svc = CreateService();
            var files = new Dictionary<string, string>
            {
                { "SomeOtherFile.csv", "data" },
                { "notio.txt", "text" }
            };
            var result = svc.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_ValidCsv_ParsesDeviceAndColumns()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal1,Signal2\n" +
                      "100,1700000000000000000,12:00:00.000,Running,50,1.5,2.5\n" +
                      "200,1700000001000000000,12:00:01.000,Running,51,3.0,4.0\n";

            var files = new Dictionary<string, string>
            {
                { "Io-BIM[0]_e[1]-Mon.csv", csv }
            };

            var result = svc.ParseIoFiles(files);

            Assert.Single(result);
            var device = result[0];
            Assert.Equal("BIM[0]", device.DeviceName);
            Assert.Equal("Io-BIM[0]_e[1]-Mon.csv", device.FileName);
            Assert.Equal(new[] { "Signal1", "Signal2" }, device.Columns);
            Assert.Equal(2, device.Rows.Count);
        }

        [Fact]
        public void ParseIoFiles_ValidCsv_ParsesRowValues()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Voltage\n" +
                      "100,1700000000000000000,12:00:00.000,Running,50,3.14\n";

            var files = new Dictionary<string, string>
            {
                { "Io-ECM_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            var row = result[0].Rows[0];

            Assert.Equal(1700000000000000000L, row.RawTime);
            Assert.Equal("Running", row.MachineState);
            Assert.Equal("3.14", row.Values["Voltage"]);
        }

        [Fact]
        public void ParseIoFiles_EmptyCsvContent_ReturnsEmpty()
        {
            var svc = CreateService();
            var files = new Dictionary<string, string>
            {
                { "Io-BIM[0]_e[1].csv", "" }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_WhitespaceOnlyCsv_ReturnsEmpty()
        {
            var svc = CreateService();
            var files = new Dictionary<string, string>
            {
                { "Io-BIM[0]_e[1].csv", "   \n  \n  " }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_HeaderOnlyNoCols_ReturnsDeviceWithNoIoColumns()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions\n";

            var files = new Dictionary<string, string>
            {
                { "Io-PDC_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Single(result);
            Assert.Empty(result[0].Columns);
            Assert.Empty(result[0].Rows);
        }

        [Fact]
        public void ParseIoFiles_MultipleDevices_SortedByName()
        {
            var svc = CreateService();
            var csv1 = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,V1\n100,0,12:00:00.000,Idle,0,1\n";
            var csv2 = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,V2\n100,0,12:00:00.000,Idle,0,2\n";

            var files = new Dictionary<string, string>
            {
                { "Io-ZZZ_e[1].csv", csv1 },
                { "Io-AAA_e[1].csv", csv2 }
            };

            var result = svc.ParseIoFiles(files);

            Assert.Equal(2, result.Count);
            Assert.Equal("AAA", result[0].DeviceName);
            Assert.Equal("ZZZ", result[1].DeviceName);
        }

        // ── ParseIoFiles — byte[] source (terminalCsvBytes) ──────────────

        [Fact]
        public void ParseIoFiles_ByteSource_PreferredOverStringSource()
        {
            var svc = CreateService();
            var csvBytes = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,FromBytes\n100,0,12:00:00.000,Idle,0,yes\n";
            var csvString = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,FromString\n100,0,12:00:00.000,Idle,0,no\n";

            var stringFiles = new Dictionary<string, string>
            {
                { "Io-BIM[0]_e[1].csv", csvString }
            };
            var byteFiles = new Dictionary<string, byte[]>
            {
                { "Io-BIM[0]_e[1].csv", Encoding.UTF8.GetBytes(csvBytes) }
            };

            var result = svc.ParseIoFiles(stringFiles, byteFiles);

            Assert.Single(result);
            Assert.Contains("FromBytes", result[0].Columns);
        }

        [Fact]
        public void ParseIoFiles_ByteSource_NestedPathExtractsFilename()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Sig\n100,0,12:00:00.000,Idle,0,1\n";

            var byteFiles = new Dictionary<string, byte[]>
            {
                { "InnerZip/TerminalLogs/Io-ECM_e[1].csv", Encoding.UTF8.GetBytes(csv) }
            };

            var result = svc.ParseIoFiles(new Dictionary<string, string>(), byteFiles);

            Assert.Single(result);
            Assert.Equal("ECM", result[0].DeviceName);
        }

        [Fact]
        public void ParseIoFiles_ByteSource_NonIoFilesSkipped()
        {
            var svc = CreateService();
            var byteFiles = new Dictionary<string, byte[]>
            {
                { "NotAnIoFile.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n") },
                { "Io-but-not-csv.txt", Encoding.UTF8.GetBytes("a,b\n1,2\n") }
            };

            var result = svc.ParseIoFiles(new Dictionary<string, string>(), byteFiles);
            Assert.Empty(result);
        }

        // ── Timestamp parsing ──────────────────────────────────────────────

        [Fact]
        public void ParseIoFiles_TimestampFormats_ParsedCorrectly()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Val\n" +
                      "1,0,14:30:45.123,Running,0,1\n" +
                      "2,0,9:05:30.456,Idle,0,2\n" +
                      "3,0,23:59:59,Off,0,3\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            var rows = result[0].Rows;

            // ParseTimestamp uses DateTime.TryParseExact with time-only formats;
            // the date part defaults to today's date.
            var today = DateTime.Today;
            Assert.Equal(today.Add(new TimeSpan(0, 14, 30, 45, 123)), rows[0].Timestamp);
            Assert.Equal(today.Add(new TimeSpan(0, 9, 5, 30, 456)), rows[1].Timestamp);
            Assert.Equal(today.Add(new TimeSpan(0, 23, 59, 59, 0)), rows[2].Timestamp);
        }

        [Fact]
        public void ParseIoFiles_EmptyTimestamp_ReturnsMinValue()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Val\n" +
                      "1,0,,Running,0,1\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Equal(DateTime.MinValue, result[0].Rows[0].Timestamp);
        }

        // ── RawTime parsing ───────────────────────────────────────────────

        [Fact]
        public void ParseIoFiles_InvalidRawTime_DefaultsToZero()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Val\n" +
                      "1,not_a_number,12:00:00.000,Running,0,1\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Equal(0L, result[0].Rows[0].RawTime);
        }

        [Fact]
        public void ParseIoFiles_MissingRawTimeColumn_DefaultsToZero()
        {
            var svc = CreateService();
            var csv = "Ticks,Timestamp,MachineState,TotalImpressions,Val\n" +
                      "1,12:00:00.000,Running,0,1\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Equal(0L, result[0].Rows[0].RawTime);
        }

        // ── Standard columns excluded ─────────────────────────────────────

        [Fact]
        public void ParseIoFiles_StandardColumnsExcludedFromIoColumns()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,CustomSignal\n" +
                      "1,0,12:00:00.000,Running,0,42\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);

            Assert.Single(result[0].Columns);
            Assert.Equal("CustomSignal", result[0].Columns[0]);
        }

        [Fact]
        public void ParseIoFiles_CaseInsensitiveStandardColumns()
        {
            var svc = CreateService();
            // All standard columns in different casing — should still be excluded
            var csv = "ticks,RAWTIME,timestamp,machinestate,totalimpressions,MySignal\n" +
                      "1,0,12:00:00.000,Running,0,99\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Single(result[0].Columns);
            Assert.Equal("MySignal", result[0].Columns[0]);
        }

        // ── Empty/blank rows skipped ──────────────────────────────────────

        [Fact]
        public void ParseIoFiles_BlankRowsSkipped()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Val\n" +
                      "1,0,12:00:00.000,Running,0,1\n" +
                      "\n" +
                      "   \n" +
                      "2,0,12:00:01.000,Running,0,2\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Equal(2, result[0].Rows.Count);
        }

        // ── Row with fewer columns than header ────────────────────────────

        [Fact]
        public void ParseIoFiles_ShortRow_MissingValuesAreEmpty()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Sig1,Sig2\n" +
                      "1,0,12:00:00.000,Running,0,val1\n"; // missing Sig2

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            var row = result[0].Rows[0];

            Assert.Equal("val1", row.Values["Sig1"]);
            Assert.Equal("", row.Values["Sig2"]); // SafeGet returns null → stored as ""
        }

        // ── CSV with quoted values ────────────────────────────────────────

        [Fact]
        public void ParseIoFiles_QuotedValues_ParsedCorrectly()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal\n" +
                      "1,0,12:00:00.000,\"Running\",0,\"3.14\"\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Equal("Running", result[0].Rows[0].MachineState);
            Assert.Equal("3.14", result[0].Rows[0].Values["Signal"]);
        }

        [Fact]
        public void ParseIoFiles_QuotedValueWithComma_NotSplit()
        {
            var svc = CreateService();
            // A quoted value containing a comma should not be split
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal\n" +
                      "1,0,12:00:00.000,Running,0,\"value,with,commas\"\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e[1].csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Equal("value,with,commas", result[0].Rows[0].Values["Signal"]);
        }

        // ── GetAllComponents ──────────────────────────────────────────────

        [Fact]
        public void GetAllComponents_EmptyDevices_ReturnsEmpty()
        {
            var svc = CreateService();
            var result = svc.GetAllComponents(new List<IoDeviceData>());
            Assert.Empty(result);
        }

        [Fact]
        public void GetAllComponents_SingleDevice_ReturnsCorrectItems()
        {
            var svc = CreateService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData
                {
                    DeviceName = "BIM[0]",
                    Columns = new[] { "Voltage", "Current" }
                }
            };

            var result = svc.GetAllComponents(devices);

            Assert.Equal(2, result.Count);
            Assert.Equal("BIM[0]", result[0].Category);
            Assert.Equal("Voltage", result[0].Name);
            Assert.False(result[0].IsSelected);
            Assert.Equal("BIM[0]", result[1].Category);
            Assert.Equal("Current", result[1].Name);
        }

        [Fact]
        public void GetAllComponents_MultipleDevices_ReturnsAllColumns()
        {
            var svc = CreateService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData { DeviceName = "DEV1", Columns = new[] { "A", "B" } },
                new IoDeviceData { DeviceName = "DEV2", Columns = new[] { "C" } }
            };

            var result = svc.GetAllComponents(devices);

            Assert.Equal(3, result.Count);
            Assert.Equal("DEV1", result[0].Category);
            Assert.Equal("A", result[0].Name);
            Assert.Equal("DEV2", result[2].Category);
            Assert.Equal("C", result[2].Name);
        }

        [Fact]
        public void GetAllComponents_DeviceWithNoColumns_ReturnsNoItems()
        {
            var svc = CreateService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData { DeviceName = "Empty", Columns = Array.Empty<string>() }
            };

            var result = svc.GetAllComponents(devices);
            Assert.Empty(result);
        }

        // ── ExportMergedCsvAsync ──────────────────────────────────────────

        [Fact]
        public async Task ExportMergedCsvAsync_EmptyDevices_WritesHeaderOnly()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>();
                var keys = new List<string>();
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Single(lines);
                Assert.Equal("Timestamp,MachineState", lines[0]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_SingleDevice_WritesCorrectCsv()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "DEV",
                        Columns = new[] { "Sig1", "Sig2" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow
                            {
                                RawTime = 1700000000000000000L,
                                MachineState = "Running",
                                Values = new Dictionary<string, string> { { "Sig1", "1.0" }, { "Sig2", "2.0" } }
                            }
                        }
                    }
                };
                var keys = new List<string> { "DEV|Sig1" };
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Equal(2, lines.Length);
                Assert.Equal("Timestamp,MachineState,DEV|Sig1", lines[0]);
                // Data row should contain timestamp, MachineState, and value
                Assert.Contains("Running", lines[1]);
                Assert.Contains("1.0", lines[1]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_ForwardFill_PreviousValuesCarried()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "A",
                        Columns = new[] { "V" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow
                            {
                                RawTime = 1000,
                                MachineState = "Run",
                                Values = new Dictionary<string, string> { { "V", "10" } }
                            }
                        }
                    },
                    new IoDeviceData
                    {
                        DeviceName = "B",
                        Columns = new[] { "W" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow
                            {
                                RawTime = 2000,
                                MachineState = "",
                                Values = new Dictionary<string, string> { { "W", "20" } }
                            }
                        }
                    }
                };
                var keys = new List<string> { "A|V", "B|W" };
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Equal(3, lines.Length); // header + 2 data rows

                // Second row should forward-fill A|V from previous row
                var lastRowParts = lines[2].Split(',');
                // Columns: Timestamp, MachineState, A|V, B|W
                Assert.Equal("10", lastRowParts[2]); // forward-filled from first row
                Assert.Equal("20", lastRowParts[3]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_ForwardFillMachineState()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "D",
                        Columns = new[] { "X" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow
                            {
                                RawTime = 100,
                                MachineState = "Printing",
                                Values = new Dictionary<string, string> { { "X", "1" } }
                            },
                            new IoDataRow
                            {
                                RawTime = 200,
                                MachineState = "",
                                Values = new Dictionary<string, string> { { "X", "2" } }
                            }
                        }
                    }
                };
                var keys = new List<string> { "D|X" };
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Equal(3, lines.Length);
                // Second data row should carry forward "Printing" machine state
                Assert.Contains("Printing", lines[2]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_RowsSortedByRawTime()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "D",
                        Columns = new[] { "V" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow { RawTime = 3000, MachineState = "C", Values = new Dictionary<string, string> { { "V", "3" } } },
                            new IoDataRow { RawTime = 1000, MachineState = "A", Values = new Dictionary<string, string> { { "V", "1" } } },
                            new IoDataRow { RawTime = 2000, MachineState = "B", Values = new Dictionary<string, string> { { "V", "2" } } }
                        }
                    }
                };
                var keys = new List<string> { "D|V" };
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Equal(4, lines.Length);
                // Rows should be sorted by rawTime: 1000, 2000, 3000
                Assert.Contains(",A,1", lines[1]);
                Assert.Contains(",B,2", lines[2]);
                Assert.Contains(",C,3", lines[3]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_Cancellation_ThrowsOperationCanceled()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "D",
                        Columns = new[] { "V" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow { RawTime = 1, MachineState = "X", Values = new Dictionary<string, string> { { "V", "1" } } }
                        }
                    }
                };
                var keys = new List<string> { "D|V" };
                var cts = new CancellationTokenSource();
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    svc.ExportMergedCsvAsync(devices, keys, path, new Progress<double>(), cts.Token));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_NoSelectedKeys_WritesTimelineWithoutValueColumns()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "D",
                        Columns = new[] { "V" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow { RawTime = 100, MachineState = "Run", Values = new Dictionary<string, string> { { "V", "1" } } }
                        }
                    }
                };
                var keys = new List<string>(); // nothing selected
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Equal("Timestamp,MachineState", lines[0]);
                // Still outputs rows (timeline), just no value columns
                Assert.Equal(2, lines.Length);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportMergedCsvAsync_HeaderWithComma_IsQuoted()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                // Create a device+column combination where the key contains a comma
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "D",
                        Columns = new[] { "A,B" }, // column name with comma
                        Rows = new List<IoDataRow>()
                    }
                };
                var keys = new List<string> { "D|A,B" };
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var headerLine = File.ReadAllLines(path)[0];
                // The header "D|A,B" should be quoted since it contains a comma
                Assert.Contains("\"D|A,B\"", headerLine);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ── Multiple IO columns per row ───────────────────────────────────

        [Fact]
        public void ParseIoFiles_MultipleIoColumns_AllStoredInValues()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,A,B,C,D\n" +
                      "1,0,12:00:00.000,Run,0,v1,v2,v3,v4\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_x.csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            var row = result[0].Rows[0];

            Assert.Equal(4, result[0].Columns.Length);
            Assert.Equal("v1", row.Values["A"]);
            Assert.Equal("v2", row.Values["B"]);
            Assert.Equal("v3", row.Values["C"]);
            Assert.Equal("v4", row.Values["D"]);
        }

        // ── Fallback string source when bytes source has same file ────────

        [Fact]
        public void ParseIoFiles_StringSourceSkippedWhenByteSourceHasSameFile()
        {
            var svc = CreateService();
            var byteContent = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,ByteCol\n1,0,12:00:00.000,X,0,byte\n";
            var stringContent = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,StringCol\n1,0,12:00:00.000,X,0,str\n";

            var stringFiles = new Dictionary<string, string>
            {
                { "Io-DEV_e.csv", stringContent }
            };
            var byteFiles = new Dictionary<string, byte[]>
            {
                { "Io-DEV_e.csv", Encoding.UTF8.GetBytes(byteContent) }
            };

            var result = svc.ParseIoFiles(stringFiles, byteFiles);

            Assert.Single(result);
            Assert.Contains("ByteCol", result[0].Columns);
            Assert.DoesNotContain("StringCol", result[0].Columns);
        }

        // ── String source adds files not in byte source ───────────────────

        [Fact]
        public void ParseIoFiles_StringSourceAddsExtraFiles()
        {
            var svc = CreateService();
            var byteCsv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,B1\n1,0,12:00:00.000,X,0,1\n";
            var stringCsv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,S1\n1,0,12:00:00.000,X,0,2\n";

            var stringFiles = new Dictionary<string, string>
            {
                { "Io-STR_e.csv", stringCsv }
            };
            var byteFiles = new Dictionary<string, byte[]>
            {
                { "Io-BYTE_e.csv", Encoding.UTF8.GetBytes(byteCsv) }
            };

            var result = svc.ParseIoFiles(stringFiles, byteFiles);

            Assert.Equal(2, result.Count);
            Assert.True(result.Any(d => d.DeviceName == "BYTE"));
            Assert.True(result.Any(d => d.DeviceName == "STR"));
        }

        // ── IoDeviceData / IoDataRow model defaults ───────────────────────

        [Fact]
        public void IoDeviceData_Defaults()
        {
            var d = new IoDeviceData();
            Assert.Equal("", d.DeviceName);
            Assert.Equal("", d.FileName);
            Assert.Empty(d.Columns);
            Assert.NotNull(d.Rows);
            Assert.Empty(d.Rows);
        }

        [Fact]
        public void IoDataRow_Defaults()
        {
            var r = new IoDataRow();
            Assert.Equal(0L, r.RawTime);
            Assert.Equal(default, r.Timestamp);
            Assert.Equal("", r.MachineState);
            Assert.NotNull(r.Values);
            Assert.Empty(r.Values);
        }

        // ── Edge: header-only CSV (no data rows) ──────────────────────────

        [Fact]
        public void ParseIoFiles_HeaderOnly_ReturnsDeviceWithZeroRows()
        {
            var svc = CreateService();
            var csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal\n";

            var files = new Dictionary<string, string>
            {
                { "Io-DEV_e.csv", csv }
            };

            var result = svc.ParseIoFiles(files);
            Assert.Single(result);
            Assert.Single(result[0].Columns);
            Assert.Empty(result[0].Rows);
        }

        // ── Edge: many rows for progress reporting ────────────────────────

        [Fact]
        public async Task ExportMergedCsvAsync_ManyRows_ProgressReported()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var rows = new List<IoDataRow>();
                for (int i = 0; i < 1500; i++)
                {
                    rows.Add(new IoDataRow
                    {
                        RawTime = i,
                        MachineState = "Run",
                        Values = new Dictionary<string, string> { { "V", i.ToString() } }
                    });
                }

                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData { DeviceName = "D", Columns = new[] { "V" }, Rows = rows }
                };
                var keys = new List<string> { "D|V" };

                var progressValues = new List<double>();
                var progress = new Progress<double>(v => progressValues.Add(v));

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                // Allow time for Progress<T> callbacks (they run via SynchronizationContext)
                await Task.Delay(100);

                var lines = File.ReadAllLines(path);
                Assert.Equal(1501, lines.Length); // header + 1500 rows
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ── ExportMergedCsvAsync with multiple devices ────────────────────

        [Fact]
        public async Task ExportMergedCsvAsync_MultipleDevices_MergesTimeline()
        {
            var svc = CreateService();
            var path = Path.GetTempFileName();
            try
            {
                var devices = new List<IoDeviceData>
                {
                    new IoDeviceData
                    {
                        DeviceName = "A",
                        Columns = new[] { "X" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow { RawTime = 100, MachineState = "Run", Values = new Dictionary<string, string> { { "X", "a1" } } },
                            new IoDataRow { RawTime = 300, MachineState = "Run", Values = new Dictionary<string, string> { { "X", "a2" } } }
                        }
                    },
                    new IoDeviceData
                    {
                        DeviceName = "B",
                        Columns = new[] { "Y" },
                        Rows = new List<IoDataRow>
                        {
                            new IoDataRow { RawTime = 200, MachineState = "Idle", Values = new Dictionary<string, string> { { "Y", "b1" } } }
                        }
                    }
                };
                var keys = new List<string> { "A|X", "B|Y" };
                var progress = new Progress<double>();

                await svc.ExportMergedCsvAsync(devices, keys, path, progress, CancellationToken.None);

                var lines = File.ReadAllLines(path);
                Assert.Equal(4, lines.Length); // header + 3 rows

                // Row order by rawTime: 100 (A), 200 (B), 300 (A)
                // Row at rawTime=200: A|X should be forward-filled from "a1"
                var row2Parts = lines[2].Split(',');
                // Columns: Timestamp, MachineState, A|X, B|Y
                Assert.Equal("a1", row2Parts[2]); // forward-filled
                Assert.Equal("b1", row2Parts[3]);

                // Row at rawTime=300: B|Y should be forward-filled from "b1"
                var row3Parts = lines[3].Split(',');
                Assert.Equal("a2", row3Parts[2]);
                Assert.Equal("b1", row3Parts[3]); // forward-filled
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
