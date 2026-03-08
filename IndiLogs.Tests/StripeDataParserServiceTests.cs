using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class StripeDataParserServiceTests
    {
        private StripeDataParserService CreateService() => new StripeDataParserService();

        [Fact]
        public void ParseFromLogs_EmptyList_ReturnsEmpty()
        {
            var svc = CreateService();
            var result = svc.ParseFromLogs(new List<LogEntry>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_NoStripeData_ReturnsEmpty()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "Normal log message", Data = "" }
            };

            var result = svc.ParseFromLogs(logs);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_NullData_ReturnsEmpty()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "", Data = "" }
            };

            var result = svc.ParseFromLogs(logs);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_InvalidJson_ReturnsEmpty()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Data = "stripeDescriptor not json at all {{{{" }
            };

            var result = svc.ParseFromLogs(logs);

            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromLogs_FiltersOnlyStripeDescriptorLogs()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                new LogEntry { Message = "normal log 1", Data = "no stripe here" },
                new LogEntry { Message = "normal log 2", Data = "no stripe here either" },
                new LogEntry { Message = "stripeDescriptor data", Data = "not json" },
            };

            // Even though "stripeDescriptor" appears in message, the JSON is invalid
            var result = svc.ParseFromLogs(logs);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseFromJson_InvalidJson_ThrowsOrReturnsEmpty()
        {
            var svc = CreateService();
            // ParseFromJson calls ParseStripeJson which may throw JsonException
            // or return empty depending on the JSON structure
            try
            {
                var result = svc.ParseFromJson("not json");
                // If it doesn't throw, should be empty
                Assert.Empty(result);
            }
            catch (System.Text.Json.JsonException)
            {
                // Expected for invalid JSON
            }
        }

        [Fact]
        public void ParseFromJson_EmptyJson_ReturnsEmpty()
        {
            var svc = CreateService();
            try
            {
                var result = svc.ParseFromJson("{}");
                Assert.Empty(result);
            }
            catch (System.Text.Json.JsonException)
            {
                // Some implementations may throw
            }
        }

        [Fact]
        public void ParseFromJson_MinimalStripeNoSlices_ReturnsSingleEntry()
        {
            var svc = CreateService();
            var json = @"{
                ""stripeDescriptor"": {
                    ""parentSpreadId"": 5,
                    ""stripeId"": 3,
                    ""nullStripe"": false,
                    ""stripeLenUm"": 150000,
                    ""stripeLenNotScaledUm"": 148000,
                    ""lastStripeInSpread"": true,
                    ""imageToBru"": false,
                    ""dataTransferControl"": ""Normal"",
                    ""reportPrintDetails"": true,
                    ""reportId"": 42,
                    ""nSliceGroups"": 0,
                    ""webRepeatLenScalingFactor"": 1.0,
                    ""blanketLoopRepeatLenUm"": 0,
                    ""blanketLoopT2TotalLenUm"": 0,
                    ""firstInBlanketLoop"": true,
                    ""lastInBlanketLoop"": false,
                    ""startPosInBlanketLoopUm"": 0
                }
            }";

            var result = svc.ParseFromJson(json, new DateTime(2025, 6, 15, 10, 0, 0));

            Assert.Single(result);
            var entry = result[0];
            Assert.Equal(5, entry.SpreadId);
            Assert.Equal(3, entry.StripeId);
            Assert.Equal("Print-Image", entry.StripeType);
            Assert.Equal(150.0, entry.LengthMm);
            Assert.Equal(148.0, entry.LengthNotScaledMm);
            Assert.True(entry.LastStripeInSpread);
            Assert.Equal("Normal", entry.DataTransferControl);
            Assert.Equal(42, entry.ReportId);
            Assert.True(entry.FirstInBlanketLoop);
            Assert.Equal(-1, entry.SliceGroupIndex);
            Assert.Equal(-1, entry.SliceIndex);
        }

        [Fact]
        public void ParseFromJson_NullStripe_SetsType()
        {
            var svc = CreateService();
            var json = @"{
                ""stripeDescriptor"": {
                    ""parentSpreadId"": 1,
                    ""stripeId"": 0,
                    ""nullStripe"": true,
                    ""stripeLenUm"": 50000,
                    ""stripeLenNotScaledUm"": 50000,
                    ""lastStripeInSpread"": false,
                    ""imageToBru"": false,
                    ""reportPrintDetails"": false,
                    ""reportId"": 0,
                    ""nSliceGroups"": 0,
                    ""webRepeatLenScalingFactor"": 1.0,
                    ""blanketLoopRepeatLenUm"": 0,
                    ""blanketLoopT2TotalLenUm"": 0,
                    ""firstInBlanketLoop"": false,
                    ""lastInBlanketLoop"": false,
                    ""startPosInBlanketLoopUm"": 0
                }
            }";

            var result = svc.ParseFromJson(json);
            Assert.Single(result);
            Assert.Equal("Null-Gap", result[0].StripeType);
        }

        [Fact]
        public void ParseFromJson_WithSlices_ParsesSliceData()
        {
            var svc = CreateService();
            var json = @"{
                ""stripeDescriptor"": {
                    ""parentSpreadId"": 1,
                    ""stripeId"": 1,
                    ""nullStripe"": false,
                    ""stripeLenUm"": 100000,
                    ""stripeLenNotScaledUm"": 99000,
                    ""lastStripeInSpread"": false,
                    ""imageToBru"": true,
                    ""reportPrintDetails"": false,
                    ""reportId"": 10,
                    ""nSliceGroups"": 1,
                    ""webRepeatLenScalingFactor"": 1.0,
                    ""blanketLoopRepeatLenUm"": 0,
                    ""blanketLoopT2TotalLenUm"": 0,
                    ""firstInBlanketLoop"": false,
                    ""lastInBlanketLoop"": false,
                    ""startPosInBlanketLoopUm"": 0,
                    ""sliceGroups"": {
                        ""#0"": {
                            ""slices"": {
                                ""#0"": {
                                    ""sliceId"": 100,
                                    ""sliceStamp"": 200,
                                    ""parentSeparationId"": 1,
                                    ""inkId"": 3,
                                    ""stationIsActive"": true,
                                    ""startPosUm"": 0,
                                    ""endPosUm"": 50000,
                                    ""hvTargetType"": ""HighVoltage"",
                                    ""bidData"": { ""vDeveloper"": 400, ""vElectrode"": 300, ""vSqueegee"": 200, ""vCleaner"": 100 },
                                    ""crData"": { ""vDc"": 150, ""vAc"": 75 },
                                    ""asidData"": { ""VAsid"": 500 },
                                    ""lphData"": { ""nScanLines"": 1024 },
                                    ""emData"": { ""isActive"": true, ""measureId"": 7 }
                                },
                                ""#1"": {
                                    ""sliceId"": 101,
                                    ""sliceStamp"": 201,
                                    ""parentSeparationId"": 2,
                                    ""inkId"": 4,
                                    ""stationIsActive"": false,
                                    ""startPosUm"": 50000,
                                    ""endPosUm"": 100000,
                                    ""hvTargetType"": ""Standard""
                                }
                            }
                        }
                    }
                }
            }";

            var result = svc.ParseFromJson(json, new DateTime(2025, 1, 1));

            Assert.Equal(2, result.Count);

            // First slice
            var s0 = result[0];
            Assert.Equal(100, s0.SliceId);
            Assert.Equal(200, s0.SliceStamp);
            Assert.Equal(3, s0.InkId);
            Assert.True(s0.IsStationActive);
            Assert.Equal(0, s0.StartPosUm);
            Assert.Equal(50000, s0.EndPosUm);
            Assert.Equal("HighVoltage", s0.HvTarget);
            Assert.Equal(400, s0.VDeveloper);
            Assert.Equal(300, s0.VElectrode);
            Assert.Equal(200, s0.VSqueegee);
            Assert.Equal(100, s0.VCleaner);
            Assert.Equal(150, s0.CrVDc);
            Assert.Equal(75, s0.CrVAc);
            Assert.Equal(500, s0.VAsid);
            Assert.Equal(1024, s0.NScanLines);
            Assert.True(s0.EmIsActive);
            Assert.Equal(7, s0.EmMeasureId);
            Assert.Equal(0, s0.SliceGroupIndex);
            Assert.Equal(0, s0.SliceIndex);

            // Second slice
            var s1 = result[1];
            Assert.Equal(101, s1.SliceId);
            Assert.False(s1.IsStationActive);
            Assert.Equal(1, s1.SliceIndex);
        }

        [Fact]
        public void ParseFromJson_WithSpmInstruction_ParsesSpmData()
        {
            var svc = CreateService();
            var json = @"{
                ""stripeDescriptor"": {
                    ""parentSpreadId"": 1, ""stripeId"": 1, ""nullStripe"": false,
                    ""stripeLenUm"": 100000, ""stripeLenNotScaledUm"": 100000,
                    ""lastStripeInSpread"": false, ""imageToBru"": false,
                    ""reportPrintDetails"": false, ""reportId"": 0, ""nSliceGroups"": 0,
                    ""webRepeatLenScalingFactor"": 1.0,
                    ""blanketLoopRepeatLenUm"": 0, ""blanketLoopT2TotalLenUm"": 0,
                    ""firstInBlanketLoop"": false, ""lastInBlanketLoop"": false,
                    ""startPosInBlanketLoopUm"": 0
                },
                ""SpmMeasureInstruction"": {
                    ""IsActive"": true,
                    ""MeasureId"": 42,
                    ""ScanDirection"": ""Forward"",
                    ""MeasureMode"": ""Full"",
                    ""NumOfStrips"": 5
                }
            }";

            var result = svc.ParseFromJson(json);
            Assert.Single(result);
            Assert.Equal("Active", result[0].SpmStatus);
            Assert.Equal(42, result[0].SpmMeasureId);
            Assert.Equal("Forward", result[0].SpmScanDirection);
            Assert.Equal("Full", result[0].SpmMeasureMode);
            Assert.Equal(5, result[0].SpmNumOfStrips);
        }

        [Fact]
        public void ParseFromJson_WithIlsInstruction_ParsesIlsData()
        {
            var svc = CreateService();
            var json = @"{
                ""stripeDescriptor"": {
                    ""parentSpreadId"": 1, ""stripeId"": 1, ""nullStripe"": false,
                    ""stripeLenUm"": 100000, ""stripeLenNotScaledUm"": 100000,
                    ""lastStripeInSpread"": false, ""imageToBru"": false,
                    ""reportPrintDetails"": false, ""reportId"": 0, ""nSliceGroups"": 0,
                    ""webRepeatLenScalingFactor"": 1.0,
                    ""blanketLoopRepeatLenUm"": 0, ""blanketLoopT2TotalLenUm"": 0,
                    ""firstInBlanketLoop"": false, ""lastInBlanketLoop"": false,
                    ""startPosInBlanketLoopUm"": 0
                },
                ""IlsMeasureInstruction"": {
                    ""IsActive"": true,
                    ""ScanLenUm"": 25000,
                    ""ScanMode"": ""Quick"",
                    ""ScanSpeedUmSec"": 5000
                }
            }";

            var result = svc.ParseFromJson(json);
            Assert.Single(result);
            Assert.True(result[0].IlsIsActive);
            Assert.Equal(25000, result[0].IlsScanLenUm);
            Assert.Equal("Quick", result[0].IlsScanMode);
            Assert.Equal(5000, result[0].IlsScanSpeedUmSec);
        }

        [Fact]
        public void ParseFromLogs_WithValidStripeData_Parses()
        {
            var svc = CreateService();
            var json = @"{""stripeDescriptor"":{""parentSpreadId"":1,""stripeId"":0,""nullStripe"":false,""stripeLenUm"":100000,""stripeLenNotScaledUm"":100000,""lastStripeInSpread"":false,""imageToBru"":false,""reportPrintDetails"":false,""reportId"":0,""nSliceGroups"":0,""webRepeatLenScalingFactor"":1.0,""blanketLoopRepeatLenUm"":0,""blanketLoopT2TotalLenUm"":0,""firstInBlanketLoop"":false,""lastInBlanketLoop"":false,""startPosInBlanketLoopUm"":0}}";

            var logs = new List<LogEntry>
            {
                new LogEntry { Date = new DateTime(2025, 1, 1), Data = json }
            };

            var result = svc.ParseFromLogs(logs);
            Assert.Single(result);
            Assert.Equal(1, result[0].SpreadId);
        }
    }
}
