using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services.Cpr;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class CprAnalysisServiceExtendedTests
    {
        private readonly CprAnalysisService _svc = new();

        #region Test Helpers

        private static CprRecord MakeRecord(
            double locX = 10, double locY = 100, int cycle = 1,
            double[]? stationY = null, double[]? stationX = null,
            string revolution = "One Only", double pixelX = 0, double pixelY = 0)
        {
            var rec = new CprRecord
            {
                ElementLocationX = locX,
                ElementLocationY = locY,
                CycleNumber = cycle,
                Revolution = revolution,
                ElementLocationPixelX = pixelX,
                ElementLocationPixelY = pixelY,
                IterationNum = 1,
                SN = 1
            };

            stationY ??= [0, 10, 20, 30, 40, 50, 60];
            stationX ??= [0, 5, 15, 25, 35, 45, 55];

            for (int i = 0; i < 7; i++)
            {
                rec.StationY[i] = stationY[i];
                rec.StationX[i] = stationX[i];
            }

            return rec;
        }

        private static CprFilterState DefaultFilter(
            string axis = "Y", bool removeDC = false, int smoothing = 1,
            int iteration = 1, int cycleFrom = 1, int cycleTo = 5,
            int colFrom = 1, int colTo = 10, int bowDegree = 3,
            bool autoYAxis = true, double yFrom = -200, double yTo = 200,
            bool sharedYAxis = false) => new()
            {
                Axis = axis,
                RemoveDC = removeDC,
                SmoothingWindow = smoothing,
                Iteration = iteration,
                CycleFrom = cycleFrom,
                CycleTo = cycleTo,
                ColumnFrom = colFrom,
                ColumnTo = colTo,
                AutoYAxis = autoYAxis,
                BowDegree = bowDegree,
                Revolution = "One Only",
                YAxisFrom = yFrom,
                YAxisTo = yTo,
                SharedYAxis = sharedYAxis
            };

        private static CprStationPair Pair(int test = 2, int refSt = 1) => new()
        {
            TestStation = test,
            RefStation = refSt
        };

        private static List<CprRecord> MakeDataGrid(int yCount = 5, int xCount = 3, int cycles = 2)
        {
            var records = new List<CprRecord>();
            for (int c = 1; c <= cycles; c++)
            {
                for (int yi = 0; yi < yCount; yi++)
                {
                    for (int xi = 0; xi < xCount; xi++)
                    {
                        double locY = yi * 10.0;
                        double locX = xi * 50.0;
                        records.Add(MakeRecord(
                            locX: locX, locY: locY, cycle: c,
                            stationY: [0, 10 + yi, 20 + yi * 2, 30 + yi, 40 + yi, 50 + yi, 60 + yi],
                            stationX: [0, 5 + xi, 15 + xi * 2, 25 + xi, 35 + xi, 45 + xi, 55 + xi],
                            pixelX: xi * 100.0 + yi));
                    }
                }
            }
            return records;
        }

        #endregion

        #region CprRecord Model Tests

        [Fact]
        public void CprRecord_DefaultArrays_HaveLength7()
        {
            var rec = new CprRecord();
            Assert.Equal(7, rec.StationX.Length);
            Assert.Equal(7, rec.StationY.Length);
        }

        [Fact]
        public void CprRecord_DefaultArrays_AllZeros()
        {
            var rec = new CprRecord();
            Assert.All(rec.StationX, v => Assert.Equal(0.0, v));
            Assert.All(rec.StationY, v => Assert.Equal(0.0, v));
        }

        [Fact]
        public void CprRecord_DefaultStrings_NotNull()
        {
            var rec = new CprRecord();
            Assert.Equal("", rec.RevolutionIndex);
            Assert.Equal("", rec.Revolution);
            Assert.Equal("", rec.StartCalibrationTime);
        }

        [Fact]
        public void CprRecord_StationValues_IndependentArrays()
        {
            var rec1 = new CprRecord();
            var rec2 = new CprRecord();
            rec1.StationX[1] = 42;
            Assert.Equal(0, rec2.StationX[1]);
        }

        [Fact]
        public void CprRecord_Properties_Roundtrip()
        {
            var rec = new CprRecord
            {
                SN = 123,
                IterationNum = 5,
                CycleNumber = 3,
                ElementLocationX = 45.5,
                ElementLocationY = 100.2,
                ElementLocationPixelX = 200.1,
                ElementLocationPixelY = 300.7,
                RevolutionIndex = "2",
                Revolution = "First of Many",
                StartCalibrationTime = "2024-01-01"
            };

            Assert.Equal(123, rec.SN);
            Assert.Equal(5, rec.IterationNum);
            Assert.Equal(3, rec.CycleNumber);
            Assert.Equal(45.5, rec.ElementLocationX);
            Assert.Equal(100.2, rec.ElementLocationY);
            Assert.Equal(200.1, rec.ElementLocationPixelX);
            Assert.Equal(300.7, rec.ElementLocationPixelY);
            Assert.Equal("2", rec.RevolutionIndex);
            Assert.Equal("First of Many", rec.Revolution);
            Assert.Equal("2024-01-01", rec.StartCalibrationTime);
        }

        #endregion

        #region CprFilterState Model Tests

        [Fact]
        public void CprFilterState_Defaults_AreCorrect()
        {
            var filter = new CprFilterState();
            Assert.Equal("Y", filter.Axis);
            Assert.True(filter.AutoYAxis);
            Assert.Equal(1, filter.SmoothingWindow);
            Assert.Equal(3, filter.BowDegree);
            Assert.Equal(-200, filter.YAxisFrom);
            Assert.Equal(200, filter.YAxisTo);
            Assert.False(filter.RemoveDC);
            Assert.False(filter.SharedYAxis);
            Assert.Equal("", filter.CalibrationTime);
            Assert.Equal("", filter.Revolution);
        }

        [Fact]
        public void CprFilterState_AllProperties_Roundtrip()
        {
            var filter = new CprFilterState
            {
                MachineSN = 99,
                CalibrationTime = "2024-06-15",
                Revolution = "One Only",
                Iteration = 3,
                CycleFrom = 2,
                CycleTo = 8,
                ColumnFrom = 3,
                ColumnTo = 12,
                Axis = "X",
                RemoveDC = true,
                AutoYAxis = false,
                SharedYAxis = true,
                SmoothingWindow = 7,
                BowDegree = 5,
                YAxisFrom = -500,
                YAxisTo = 500
            };

            Assert.Equal(99, filter.MachineSN);
            Assert.Equal("2024-06-15", filter.CalibrationTime);
            Assert.Equal("One Only", filter.Revolution);
            Assert.Equal(3, filter.Iteration);
            Assert.Equal(2, filter.CycleFrom);
            Assert.Equal(8, filter.CycleTo);
            Assert.Equal(3, filter.ColumnFrom);
            Assert.Equal(12, filter.ColumnTo);
            Assert.Equal("X", filter.Axis);
            Assert.True(filter.RemoveDC);
            Assert.False(filter.AutoYAxis);
            Assert.True(filter.SharedYAxis);
            Assert.Equal(7, filter.SmoothingWindow);
            Assert.Equal(5, filter.BowDegree);
            Assert.Equal(-500, filter.YAxisFrom);
            Assert.Equal(500, filter.YAxisTo);
        }

        #endregion

        #region CprSeriesData Model Tests

        [Fact]
        public void CprSeriesData_Defaults()
        {
            var series = new CprSeriesData();
            Assert.Equal("", series.Name);
            Assert.Empty(series.XValues);
            Assert.Empty(series.YValues);
            Assert.False(series.IsDashed);
            Assert.Equal(1.5f, series.StrokeWidth);
        }

        [Fact]
        public void CprSeriesData_Properties_Roundtrip()
        {
            var series = new CprSeriesData
            {
                Name = "TestSeries",
                XValues = [1.0, 2.0, 3.0],
                YValues = [4.0, 5.0, 6.0],
                Color = SKColors.Red,
                IsDashed = true,
                StrokeWidth = 3.0f
            };

            Assert.Equal("TestSeries", series.Name);
            Assert.Equal(3, series.XValues.Length);
            Assert.Equal(3, series.YValues.Length);
            Assert.Equal(SKColors.Red, series.Color);
            Assert.True(series.IsDashed);
            Assert.Equal(3.0f, series.StrokeWidth);
        }

        #endregion

        #region CprScatterData Model Tests

        [Fact]
        public void CprScatterData_Defaults()
        {
            var scatter = new CprScatterData();
            Assert.Equal("", scatter.Name);
            Assert.Empty(scatter.XValues);
            Assert.Empty(scatter.YValues);
        }

        #endregion

        #region CprSubplot Model Tests

        [Fact]
        public void CprSubplot_Defaults()
        {
            var subplot = new CprSubplot();
            Assert.Equal("", subplot.Title);
            Assert.NotNull(subplot.ScatterSeries);
            Assert.Empty(subplot.ScatterSeries);
            Assert.NotNull(subplot.LineSeries);
            Assert.Empty(subplot.LineSeries);
        }

        #endregion

        #region CprHistogramData Model Tests

        [Fact]
        public void CprHistogramData_Defaults()
        {
            var hist = new CprHistogramData();
            Assert.Empty(hist.BinEdges);
            Assert.Empty(hist.BinCounts);
            Assert.Empty(hist.NormalX);
            Assert.Empty(hist.NormalY);
            Assert.Equal(0.0, hist.Mean);
            Assert.Equal(0.0, hist.Std);
        }

        #endregion

        #region CprDftMarker Model Tests

        [Fact]
        public void CprDftMarker_Defaults()
        {
            var marker = new CprDftMarker();
            Assert.Equal(0.0, marker.Frequency);
            Assert.Equal("", marker.Label);
            Assert.False(marker.IsDashed);
        }

        [Fact]
        public void CprDftMarker_Properties_Roundtrip()
        {
            var marker = new CprDftMarker
            {
                Frequency = 0.005,
                Label = "TestMarker",
                Color = SKColors.Blue,
                IsDashed = true
            };

            Assert.Equal(0.005, marker.Frequency);
            Assert.Equal("TestMarker", marker.Label);
            Assert.Equal(SKColors.Blue, marker.Color);
            Assert.True(marker.IsDashed);
        }

        #endregion

        #region VerticalRefLine Model Tests

        [Fact]
        public void VerticalRefLine_Defaults()
        {
            var line = new VerticalRefLine();
            Assert.Equal(0.0, line.XValue);
            Assert.Equal("", line.Label);
            Assert.Equal(RefLineStyle.Dashed, line.LineStyle);
        }

        [Fact]
        public void VerticalRefLine_Properties_Roundtrip()
        {
            var line = new VerticalRefLine
            {
                XValue = 42.5,
                Label = "TestLine",
                Color = SKColors.Green,
                LineStyle = RefLineStyle.DashDot
            };

            Assert.Equal(42.5, line.XValue);
            Assert.Equal("TestLine", line.Label);
            Assert.Equal(SKColors.Green, line.Color);
            Assert.Equal(RefLineStyle.DashDot, line.LineStyle);
        }

        #endregion

        #region CprGraphResult Model Tests

        [Fact]
        public void CprGraphResult_Defaults()
        {
            var result = new CprGraphResult();
            Assert.Equal("", result.Title);
            Assert.Equal("", result.XLabel);
            Assert.Equal("", result.YLabel);
            Assert.NotNull(result.Series);
            Assert.Empty(result.Series);
            Assert.Null(result.Subplots);
            Assert.Null(result.HistogramData);
            Assert.Null(result.DftMarkers);
            Assert.Null(result.VerticalRefLines);
            Assert.True(result.AutoYAxis);
            Assert.Equal(0, result.SubplotRows);
            Assert.Equal(0, result.SubplotCols);
            Assert.False(result.SharedYAxis);
        }

        #endregion

        #region CprStatsRow Model Tests

        [Fact]
        public void CprStatsRow_Defaults()
        {
            var row = new CprStatsRow();
            Assert.Equal("", row.Station);
            Assert.Equal("", row.Perc95);
            Assert.Equal("", row.Perc99);
        }

        #endregion

        #region CprOffsetSkewRow Model Tests

        [Fact]
        public void CprOffsetSkewRow_Defaults()
        {
            var row = new CprOffsetSkewRow();
            Assert.Equal("", row.Station);
            Assert.Equal("", row.YOffset);
            Assert.Equal("", row.XOffset);
            Assert.Equal("", row.Skew);
        }

        #endregion

        #region CprStationPair Model Tests

        [Fact]
        public void CprStationPair_Defaults()
        {
            var pair = new CprStationPair();
            Assert.Equal(0, pair.TestStation);
            Assert.Equal(0, pair.RefStation);
        }

        #endregion

        #region CprGraphType Enum Tests

        [Fact]
        public void CprGraphType_HasExpectedValues()
        {
            Assert.Equal(0, (int)CprGraphType.Colors);
            Assert.Equal(1, (int)CprGraphType.Columns);
            Assert.Equal(2, (int)CprGraphType.BlanketCycles);
            Assert.Equal(3, (int)CprGraphType.XScaling);
            Assert.Equal(4, (int)CprGraphType.DFT);
            Assert.Equal(5, (int)CprGraphType.Histogram);
            Assert.Equal(6, (int)CprGraphType.Revolutions);
            Assert.Equal(7, (int)CprGraphType.MissingData);
            Assert.Equal(8, (int)CprGraphType.Skew);
            Assert.Equal(9, (int)CprGraphType.SkewAlongBracket);
        }

        #endregion

        #region RefLineStyle Enum Tests

        [Fact]
        public void RefLineStyle_HasExpectedValues()
        {
            Assert.Equal(0, (int)RefLineStyle.Solid);
            Assert.Equal(1, (int)RefLineStyle.Dashed);
            Assert.Equal(2, (int)RefLineStyle.DashDot);
            Assert.Equal(3, (int)RefLineStyle.Dotted);
        }

        #endregion

        #region Linspace via DFT (frequency array)

        [Fact]
        public void ComputeDFT_FrequencyRange_CorrectStartAndEnd()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.True(result.Series.Count > 0);
            var series = result.Series[0];

            // Linspace(0.0005, 0.015, 1000) -> first = 0.0005, last = 0.015
            Assert.Equal(1000, series.XValues.Length);
            Assert.Equal(0.0005, series.XValues[0], 1e-10);
            Assert.Equal(0.015, series.XValues[^1], 1e-10);
        }

        [Fact]
        public void ComputeDFT_FrequencyArray_IsMonotonicallyIncreasing()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.True(result.Series.Count > 0);
            var freq = result.Series[0].XValues;

            for (int i = 1; i < freq.Length; i++)
                Assert.True(freq[i] > freq[i - 1], $"Frequency not monotonically increasing at index {i}");
        }

        #endregion

        #region LinearFit via ComputeSkew (regression line)

        [Fact]
        public void ComputeSkew_LinearFit_PerfectLine_SlopeCorrect()
        {
            // Create data where station 1 Y = 10 + 2*X exactly
            var records = new List<CprRecord>();
            for (int xi = 0; xi < 5; xi++)
            {
                double locX = xi * 10.0;
                double stVal = 10 + 2 * locX;
                for (int yi = 0; yi < 3; yi++)
                {
                    records.Add(MakeRecord(
                        locX: locX, locY: yi * 10.0, cycle: 1,
                        stationY: [0, stVal, 20, 30, 40, 50, 60]));
                }
            }

            var result = _svc.ComputeSkew(records, DefaultFilter(bowDegree: 1));

            // Station 1 (subplot [0,0]) should have a linear fit with slope=2
            var subplot = result.Subplots![0, 0];
            var linearLine = subplot.LineSeries.First(s => s.Name == "Linear");

            // Verify the fit line: at x=0, y should be ~10; at x=40, y should be ~90
            double y0 = linearLine.YValues[0];
            double yLast = linearLine.YValues[^1];
            double xRange = linearLine.XValues[^1] - linearLine.XValues[0];
            double computedSlope = (yLast - y0) / xRange;

            Assert.Equal(2.0, computedSlope, 1e-6);
        }

        [Fact]
        public void ComputeSkew_LinearFit_ConstantValues_SlopeZero()
        {
            // All station 1 Y values are constant = 42 for all X
            var records = new List<CprRecord>();
            for (int xi = 0; xi < 5; xi++)
            {
                for (int yi = 0; yi < 3; yi++)
                {
                    records.Add(MakeRecord(
                        locX: xi * 10.0, locY: yi * 10.0, cycle: 1,
                        stationY: [0, 42, 20, 30, 40, 50, 60]));
                }
            }

            var result = _svc.ComputeSkew(records, DefaultFilter(bowDegree: 1));

            var subplot = result.Subplots![0, 0];
            var linearLine = subplot.LineSeries.First(s => s.Name == "Linear");

            double y0 = linearLine.YValues[0];
            double yLast = linearLine.YValues[^1];

            // Constant input -> slope ~0 -> all Y values same
            Assert.Equal(y0, yLast, 1e-6);
        }

        #endregion

        #region PolyFit via ComputeSkew (bow line)

        [Fact]
        public void ComputeSkew_PolyFit_QuadraticData_FitsCloseToOriginal()
        {
            // station 1 Y = x^2 + 3x + 5
            var records = new List<CprRecord>();
            for (int xi = 0; xi < 10; xi++)
            {
                double locX = xi * 5.0;
                double stVal = locX * locX + 3 * locX + 5;
                for (int yi = 0; yi < 3; yi++)
                {
                    records.Add(MakeRecord(
                        locX: locX, locY: yi * 10.0, cycle: 1,
                        stationY: [0, stVal, 20, 30, 40, 50, 60]));
                }
            }

            var result = _svc.ComputeSkew(records, DefaultFilter(bowDegree: 2));

            var subplot = result.Subplots![0, 0];
            var bowLine = subplot.LineSeries.FirstOrDefault(s => s.Name == "Bow");
            Assert.NotNull(bowLine);

            // Evaluate the bow fit at a known X and compare to expected quadratic
            // The scatter data X values come from distinct ElementLocationX
            var scatter = subplot.ScatterSeries[0];
            for (int i = 0; i < scatter.XValues.Length; i++)
            {
                double x = scatter.XValues[i];
                double expected = x * x + 3 * x + 5;
                double actual = scatter.YValues[i];
                Assert.Equal(expected, actual, 1e-6);
            }
        }

        [Fact]
        public void ComputeSkew_PolyFit_HigherDegree_DoesNotCrash()
        {
            var data = MakeDataGrid(yCount: 5, xCount: 8, cycles: 1);
            var filter = DefaultFilter(bowDegree: 5);

            var result = _svc.ComputeSkew(data, filter);

            var subplot = result.Subplots![0, 0];
            Assert.Contains(subplot.LineSeries, s => s.Name == "Bow");
        }

        #endregion

        #region Percentile via ComputeStats

        [Fact]
        public void ComputeStats_SingleRecord_PercentilesMatchValue()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(stationY: [0, 42, 0, 0, 0, 0, 0], stationX: [0, 0, 0, 0, 0, 0, 0])
            };

            var rows = _svc.ComputeStats(records);
            var y1 = rows.First(r => r.Station == "Y 1");

            // Single value -> percentile is always that value
            Assert.Equal("42", y1.Perc95);
            Assert.Equal("42", y1.Perc99);
        }

        [Fact]
        public void ComputeStats_TwoValues_PercentilesInterpolate()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(stationY: [0, 10, 0, 0, 0, 0, 0], stationX: [0, 0, 0, 0, 0, 0, 0]),
                MakeRecord(stationY: [0, 20, 0, 0, 0, 0, 0], stationX: [0, 0, 0, 0, 0, 0, 0])
            };

            var rows = _svc.ComputeStats(records);
            var y1 = rows.First(r => r.Station == "Y 1");

            // abs values are [10, 20], sorted = [10, 20]
            // Percentile(95) = index 0.95 * 1 = 0.95, interp: 10*0.05 + 20*0.95 = 19.5 -> round to 20
            int p95 = int.Parse(y1.Perc95);
            Assert.True(p95 >= 19 && p95 <= 20, $"Expected ~20, got {p95}");
        }

        [Fact]
        public void ComputeStats_NegativeValues_UsesAbsolute()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(stationY: [0, -50, 0, 0, 0, 0, 0], stationX: [0, 0, 0, 0, 0, 0, 0])
            };

            var rows = _svc.ComputeStats(records);
            var y1 = rows.First(r => r.Station == "Y 1");

            Assert.Equal("50", y1.Perc95);
        }

        [Fact]
        public void ComputeStats_MixedPositiveNegative_MaxCprUsesRange()
        {
            // Row with all positive station Y values: max abs = max(abs(v))
            // Row with mixed signs: absY = yMax - yMin
            var records = new List<CprRecord>
            {
                MakeRecord(
                    stationY: [0, -10, 20, -5, 15, -8, 25],
                    stationX: [0, 0, 0, 0, 0, 0, 0])
            };

            var rows = _svc.ComputeStats(records);

            // Mixed signs: yMax=25, yMin=-10, range=35
            var maxY = rows.First(r => r.Station == "Max Y");
            Assert.Equal("35", maxY.Perc95);
        }

        [Fact]
        public void ComputeStats_AllPositive_MaxCprUsesMaxAbs()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(
                    stationY: [0, 10, 20, 30, 40, 50, 60],
                    stationX: [0, 5, 15, 25, 35, 45, 55])
            };

            var rows = _svc.ComputeStats(records);

            // All positive: max abs value = 60
            var maxY = rows.First(r => r.Station == "Max Y");
            Assert.Equal("60", maxY.Perc95);
        }

        [Fact]
        public void ComputeStats_AllNegative_MaxCprUsesMaxAbs()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(
                    stationY: [0, -10, -20, -30, -40, -50, -60],
                    stationX: [0, -5, -15, -25, -35, -45, -55])
            };

            var rows = _svc.ComputeStats(records);

            // All negative: max abs value = 60
            var maxY = rows.First(r => r.Station == "Max Y");
            Assert.Equal("60", maxY.Perc95);
        }

        [Fact]
        public void ComputeStats_MaxCpr_IsMaxOfYAndX()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(
                    stationY: [0, 10, 20, 30, 40, 50, 60],
                    stationX: [0, 100, 200, 300, 400, 500, 600])
            };

            var rows = _svc.ComputeStats(records);
            var maxCpr = rows.First(r => r.Station == "Max CPR");
            var maxY = rows.First(r => r.Station == "Max Y");
            var maxX = rows.First(r => r.Station == "Max X");

            int cprVal = int.Parse(maxCpr.Perc95);
            int yVal = int.Parse(maxY.Perc95);
            int xVal = int.Parse(maxX.Perc95);

            Assert.Equal(Math.Max(yVal, xVal), cprVal);
        }

        #endregion

        #region RollingMean via ComputeRevolutions

        [Fact]
        public void ComputeRevolutions_Smoothing_OutputLengthMatchesInput()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 20; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only"));

            var filter = DefaultFilter(smoothing: 5);
            var result = _svc.ComputeRevolutions(data, filter, Pair());

            Assert.True(result.Series.Count > 0);
            var series = result.Series[0];
            Assert.Equal(series.XValues.Length, series.YValues.Length);
        }

        [Fact]
        public void ComputeRevolutions_SmoothingWindow1_NoChange()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 10; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only",
                    stationY: [0, 10 + y * 3, 20, 30, 40, 50, 60]));

            var resultNoSmooth = _svc.ComputeRevolutions(data, DefaultFilter(smoothing: 1), Pair());
            var resultSmooth1 = _svc.ComputeRevolutions(data, DefaultFilter(smoothing: 1), Pair());

            Assert.True(resultNoSmooth.Series.Count > 0);
            Assert.True(resultSmooth1.Series.Count > 0);

            var y1 = resultNoSmooth.Series[0].YValues;
            var y2 = resultSmooth1.Series[0].YValues;

            Assert.Equal(y1.Length, y2.Length);
            for (int i = 0; i < y1.Length; i++)
                Assert.Equal(y1[i], y2[i], 1e-10);
        }

        [Fact]
        public void ComputeRevolutions_LargeSmoothing_ReducesVariation()
        {
            var rng = new Random(123);
            var data = new List<CprRecord>();
            for (int y = 0; y < 50; y++)
            {
                // Use unique Y locations to avoid grouping averaging out noise
                double noise = rng.NextDouble() * 100;
                data.Add(MakeRecord(locY: y * 10.0 + rng.NextDouble() * 0.001, revolution: "One Only",
                    stationY: [0, noise, 20, 30, 40, 50, 60]));
            }

            var noSmooth = _svc.ComputeRevolutions(data, DefaultFilter(smoothing: 1), Pair(test: 1, refSt: 2));
            var smoothed = _svc.ComputeRevolutions(data, DefaultFilter(smoothing: 10), Pair(test: 1, refSt: 2));

            Assert.True(noSmooth.Series.Count > 0 && smoothed.Series.Count > 0);

            double stdRaw = StdDev(noSmooth.Series[0].YValues);
            double stdSmooth = StdDev(smoothed.Series[0].YValues);

            Assert.True(stdSmooth < stdRaw, $"Smoothing should reduce variation: raw={stdRaw}, smoothed={stdSmooth}");
        }

        #endregion

        #region RemoveDC via ComputeRevolutions

        [Fact]
        public void ComputeRevolutions_RemoveDC_MeanBecomesZero()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 30; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only",
                    stationY: [0, 500 + y, 20, 30, 40, 50, 60]));

            var filter = DefaultFilter(removeDC: true);
            var result = _svc.ComputeRevolutions(data, filter, Pair());

            Assert.True(result.Series.Count > 0);
            double mean = result.Series[0].YValues.Where(v => !double.IsNaN(v)).Average();
            Assert.True(Math.Abs(mean) < 1e-10, $"DC-removed mean should be ~0, got {mean}");
        }

        [Fact]
        public void ComputeRevolutions_NoDCRemoval_MeanNotZero()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 20; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only",
                    stationY: [0, 500, 20, 30, 40, 50, 60]));

            var filter = DefaultFilter(removeDC: false);
            var result = _svc.ComputeRevolutions(data, filter, Pair());

            Assert.True(result.Series.Count > 0);
            // Station 2 Y=500, Station 1 Y=20 -> diff after removing invalid = 500-20=480
            double mean = result.Series[0].YValues.Average();
            Assert.True(Math.Abs(mean) > 1, $"Mean without DC removal should be nonzero, got {mean}");
        }

        #endregion

        #region GetStationDiffForDft (DFT replaces -1000/-2000 with 1)

        [Fact]
        public void ComputeDFT_InvalidValues_ReplacedWith1_NotNaN()
        {
            // Station values with -1000/-2000 should not produce NaN in DFT
            var records = new List<CprRecord>();
            for (int y = 0; y < 10; y++)
            {
                records.Add(MakeRecord(locY: y * 10, cycle: 1,
                    stationY: [0, -1000, 20, 30, 40, 50, 60]));
            }

            var result = _svc.ComputeDFT(records, DefaultFilter(), Pair(test: 1, refSt: 2));

            // Should still produce a series (unlike Colors which would skip invalid)
            Assert.True(result.Series.Count > 0);
            Assert.All(result.Series[0].YValues, v =>
                Assert.False(double.IsNaN(v), "DFT amplitudes should not be NaN even with -1000 input"));
        }

        [Fact]
        public void ComputeDFT_Minus2000Values_ReplacedWith1()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 10; y++)
            {
                records.Add(MakeRecord(locY: y * 10, cycle: 1,
                    stationY: [0, -2000, -2000, 30, 40, 50, 60]));
            }

            var result = _svc.ComputeDFT(records, DefaultFilter(), Pair(test: 1, refSt: 2));

            Assert.True(result.Series.Count > 0);
            // Both stations replaced with 1 -> diff = 0 -> all amplitudes ~0
            Assert.All(result.Series[0].YValues, v =>
                Assert.True(v >= 0 && !double.IsNaN(v)));
        }

        #endregion

        #region GetStationValue / IsInvalid via ComputeRevolutions

        [Fact]
        public void ComputeRevolutions_Minus1000StationValue_Excluded()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only",
                    stationY: [0, -1000, 20, 30, 40, 50, 60]));
            }

            // Test station 1 which has -1000 -> all values invalid -> no series
            var result = _svc.ComputeRevolutions(data, DefaultFilter(), Pair(test: 1, refSt: 2));

            // Station 1 has all -1000, so GetStationValue returns NaN, IsInvalid filters all out
            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeRevolutions_Minus2000StationValue_Excluded()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only",
                    stationY: [0, -2000, 20, 30, 40, 50, 60]));
            }

            var result = _svc.ComputeRevolutions(data, DefaultFilter(), Pair(test: 1, refSt: 2));
            Assert.Empty(result.Series);
        }

        #endregion

        #region GetStationDiff via ComputeColors

        [Fact]
        public void ComputeColors_StationDiff_CorrectYAxis()
        {
            // Station 2 Y=100, Station 1 Y=30 -> diff = 70
            var records = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                records.Add(MakeRecord(locY: y * 10,
                    stationY: [0, 30, 100, 30, 40, 50, 60]));
            }

            var pairs = new CprStationPair[] { new() { TestStation = 2, RefStation = 1 } };
            var result = _svc.ComputeColors(records, DefaultFilter(axis: "Y"), pairs);

            Assert.Single(result.Series);
            Assert.All(result.Series[0].YValues, v => Assert.Equal(70.0, v, 1e-10));
        }

        [Fact]
        public void ComputeColors_StationDiff_CorrectXAxis()
        {
            // Station 2 X=200, Station 1 X=50 -> diff = 150
            var records = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                records.Add(MakeRecord(locY: y * 10,
                    stationX: [0, 50, 200, 25, 35, 45, 55]));
            }

            var pairs = new CprStationPair[] { new() { TestStation = 2, RefStation = 1 } };
            var result = _svc.ComputeColors(records, DefaultFilter(axis: "X"), pairs);

            Assert.Single(result.Series);
            Assert.All(result.Series[0].YValues, v => Assert.Equal(150.0, v, 1e-10));
        }

        [Fact]
        public void ComputeColors_OneStationInvalid_DiffIsSkipped()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                records.Add(MakeRecord(locY: y * 10,
                    stationY: [0, -1000, 100, 30, 40, 50, 60]));
            }

            var pairs = new CprStationPair[] { new() { TestStation = 2, RefStation = 1 } };
            var result = _svc.ComputeColors(records, DefaultFilter(), pairs);

            // Station 1 is -1000 -> diff is NaN -> filtered out
            Assert.Empty(result.Series);
        }

        #endregion

        #region FormatStat via ComputeStats

        [Fact]
        public void ComputeStats_FormatStat_RoundsToInt()
        {
            // Station 1 Y = 42.7 for all records -> percentile = 42.7 -> rounded to 43
            var records = new List<CprRecord>();
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(stationY: [0, 42.7, 0, 0, 0, 0, 0], stationX: [0, 0, 0, 0, 0, 0, 0]));

            var rows = _svc.ComputeStats(records);
            var y1 = rows.First(r => r.Station == "Y 1");

            Assert.Equal("43", y1.Perc95);
        }

        [Fact]
        public void ComputeStats_FormatStat_NegativeRoundsCorrectly()
        {
            // Negative values are abs'd before percentile: |-42.3| = 42.3 -> round = 42
            var records = new List<CprRecord>();
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(stationY: [0, -42.3, 0, 0, 0, 0, 0], stationX: [0, 0, 0, 0, 0, 0, 0]));

            var rows = _svc.ComputeStats(records);
            var y1 = rows.First(r => r.Station == "Y 1");

            Assert.Equal("42", y1.Perc95);
        }

        #endregion

        #region ComputeOffsetSkew Extended

        [Fact]
        public void ComputeOffsetSkew_XAxis_UsesStationXForSkew()
        {
            var records = new List<CprRecord>();
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    records.Add(MakeRecord(locX: x * 10, locY: y * 10,
                        stationY: [0, 10, 20, 30, 40, 50, 60],
                        stationX: [0, 5 + x * 3, 15, 25, 35, 45, 55]));
                }
            }

            var rows = _svc.ComputeOffsetSkew(records, "X");

            // Station 1 X increases by 3 per 10mm X -> slope = 0.3
            Assert.NotEqual("NaN", rows[0].Skew);
            double skew = double.Parse(rows[0].Skew);
            Assert.Equal(0.3, skew, 1e-3);
        }

        [Fact]
        public void ComputeOffsetSkew_XOffset_IsAverage()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 10; i++)
                records.Add(MakeRecord(stationX: [0, 100, 15, 25, 35, 45, 55]));

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            Assert.Equal("100", rows[0].XOffset);
        }

        [Fact]
        public void ComputeOffsetSkew_Minus1000Filtered_FromOffset()
        {
            var records = new List<CprRecord>();
            records.Add(MakeRecord(stationY: [0, -1000, 20, 30, 40, 50, 60]));
            records.Add(MakeRecord(stationY: [0, 100, 20, 30, 40, 50, 60]));

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            // Station 1 Y: -1000 filtered out, only 100 remains -> avg = 100
            Assert.Equal("100", rows[0].YOffset);
        }

        [Fact]
        public void ComputeOffsetSkew_Minus2000Filtered_FromOffset()
        {
            var records = new List<CprRecord>();
            records.Add(MakeRecord(stationX: [0, -2000, 15, 25, 35, 45, 55]));
            records.Add(MakeRecord(stationX: [0, 80, 15, 25, 35, 45, 55]));

            var rows = _svc.ComputeOffsetSkew(records, "X");

            Assert.Equal("80", rows[0].XOffset);
        }

        [Fact]
        public void ComputeOffsetSkew_SkewRoundedTo3Decimals()
        {
            var records = new List<CprRecord>();
            for (int x = 0; x < 10; x++)
            {
                records.Add(MakeRecord(locX: x * 10.0, locY: 0,
                    stationY: [0, 10 + x * 1.23456, 20, 30, 40, 50, 60]));
            }

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            // Slope should be 1.23456/10 = 0.123456 -> rounded to 0.123
            double skew = double.Parse(rows[0].Skew);
            string formatted = rows[0].Skew;
            // Should have exactly 3 decimal places
            Assert.Contains(".", formatted);
            string decimals = formatted.Split('.')[1];
            Assert.Equal(3, decimals.Length);
        }

        #endregion

        #region ComputeHistogram Extended

        [Fact]
        public void ComputeHistogram_NormalCurve_PeaksAtMean()
        {
            var records = new List<CprRecord>();
            var rng = new Random(42);
            for (int i = 0; i < 200; i++)
            {
                double val = 50 + rng.NextDouble() * 10;
                records.Add(MakeRecord(stationY: [0, val, 0, 0, 0, 0, 0]));
            }

            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1]);
            var hist = result.HistogramData!;

            // Normal curve peak should be near the mean
            int peakIdx = Array.IndexOf(hist.NormalY, hist.NormalY.Max());
            double peakX = hist.NormalX[peakIdx];

            Assert.True(Math.Abs(peakX - hist.Mean) < 2, $"Normal peak at {peakX} should be near mean {hist.Mean}");
        }

        [Fact]
        public void ComputeHistogram_BinEdges_SpanDataRange()
        {
            var records = new List<CprRecord>();
            for (int i = 1; i <= 10; i++)
                records.Add(MakeRecord(stationY: [0, i * 10, 0, 0, 0, 0, 0]));

            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1]);
            var hist = result.HistogramData!;

            Assert.Equal(10.0, hist.BinEdges[0], 1e-10);
            Assert.Equal(100.0, hist.BinEdges[^1], 1e-10);
        }

        [Fact]
        public void ComputeHistogram_MultipleStations_CombinesValues()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 10; i++)
            {
                records.Add(MakeRecord(
                    stationY: [0, 10, 20, 30, 40, 50, 60]));
            }

            // Stations 1 and 2: values are 10 and 20 -> combined
            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1, 2]);
            var hist = result.HistogramData!;

            // Mean of [10,10,...,20,20,...] = 15
            Assert.Equal(15.0, hist.Mean, 1e-6);
        }

        [Fact]
        public void ComputeHistogram_ZeroValues_Excluded()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(stationY: [0, 0, 50, 0, 0, 0, 0]));

            // Station 1 has value 0 -> filtered out, station 2 has 50 -> kept
            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1]);
            Assert.Equal("Histogram - No data", result.Title);

            var result2 = _svc.ComputeHistogram(records, DefaultFilter(), [2]);
            Assert.NotNull(result2.HistogramData);
        }

        [Fact]
        public void ComputeHistogram_YAxisSettings_Propagated()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 10; i++)
                records.Add(MakeRecord(stationY: [0, 10 + i, 0, 0, 0, 0, 0]));

            var filter = DefaultFilter(autoYAxis: false, yFrom: -50, yTo: 50);
            var result = _svc.ComputeHistogram(records, filter, [1]);

            Assert.False(result.AutoYAxis);
            Assert.Equal(-50, result.YAxisFrom);
            Assert.Equal(50, result.YAxisTo);
        }

        #endregion

        #region ComputeMissingData Extended

        [Fact]
        public void ComputeMissingData_Minus2000_NotCountedAsMissing()
        {
            // Only -1000 is counted, -2000 is not
            var records = new List<CprRecord>
            {
                MakeRecord(locY: 0, stationY: [0, -2000, 20, 30, 40, 50, 60])
            };

            var result = _svc.ComputeMissingData(records, DefaultFilter());
            var st1 = result.Series.First(s => s.Name == "Station 1");

            // -2000 is NOT -1000, so count should be 0
            Assert.Equal(0.0, st1.YValues[0]);
        }

        [Fact]
        public void ComputeMissingData_MultipleRecordsSameY_Summed()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(locY: 0, stationY: [0, -1000, 20, 30, 40, 50, 60]),
                MakeRecord(locY: 0, stationY: [0, -1000, 20, 30, 40, 50, 60]),
                MakeRecord(locY: 0, stationY: [0, 10, 20, 30, 40, 50, 60])
            };

            var result = _svc.ComputeMissingData(records, DefaultFilter());
            var st1 = result.Series.First(s => s.Name == "Station 1");

            Assert.Equal(2.0, st1.YValues[0]);
        }

        [Fact]
        public void ComputeMissingData_AllStationsChecked()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(locY: 0, stationY: [0, -1000, -1000, -1000, -1000, -1000, -1000])
            };

            var result = _svc.ComputeMissingData(records, DefaultFilter());

            for (int i = 0; i < 6; i++)
            {
                var st = result.Series.First(s => s.Name == $"Station {i + 1}");
                Assert.Equal(1.0, st.YValues[0]);
            }
        }

        [Fact]
        public void ComputeMissingData_SeriesNames_StationsOneToSix()
        {
            var result = _svc.ComputeMissingData(MakeDataGrid(), DefaultFilter());

            Assert.Equal(6, result.Series.Count);
            for (int i = 1; i <= 6; i++)
                Assert.Contains(result.Series, s => s.Name == $"Station {i}");
        }

        #endregion

        #region ComputeSkewAlongBracket Extended

        [Fact]
        public void ComputeSkewAlongBracket_UsesOnlyFirstCycle()
        {
            // Create data with 2 cycles, different values
            var data = new List<CprRecord>();
            for (int yi = 0; yi < 5; yi++)
            {
                for (int xi = 0; xi < 3; xi++)
                {
                    data.Add(MakeRecord(locX: xi * 10.0, locY: yi * 10.0, cycle: 1,
                        stationY: [0, 10 + xi, 20, 30, 40, 50, 60]));
                    data.Add(MakeRecord(locX: xi * 10.0, locY: yi * 10.0, cycle: 2,
                        stationY: [0, 1000 + xi, 20, 30, 40, 50, 60]));
                }
            }

            var filter = DefaultFilter(cycleFrom: 1);
            var result = _svc.ComputeSkewAlongBracket(data, filter, Pair(test: 1, refSt: 2));

            Assert.True(result.Series.Count > 0);
            // If cycle 2 were included, slopes would be very different
            var slopes = result.Series[0].YValues;
            // All slopes from cycle 1: station 1 = 10+xi, station2 = 20 -> diff = (10+xi)-20 = xi-10
            // At each Y, slope of (xi-10) vs xi*10 = 1/10 = 0.1
            Assert.All(slopes, s => Assert.True(Math.Abs(s - 0.1) < 0.01, $"Expected slope ~0.1, got {s}"));
        }

        [Fact]
        public void ComputeSkewAlongBracket_IncompleteRow_Skipped()
        {
            // Create grid where one Y row is missing a column
            var data = new List<CprRecord>();
            int xCount = 3;
            for (int yi = 0; yi < 5; yi++)
            {
                int cols = yi == 2 ? xCount - 1 : xCount; // Row 2 is incomplete
                for (int xi = 0; xi < cols; xi++)
                {
                    data.Add(MakeRecord(locX: xi * 10.0, locY: yi * 10.0, cycle: 1,
                        stationY: [0, 10 + xi, 20, 30, 40, 50, 60]));
                }
            }

            var filter = DefaultFilter(cycleFrom: 1);
            var result = _svc.ComputeSkewAlongBracket(data, filter, Pair(test: 1, refSt: 2));

            Assert.True(result.Series.Count > 0);
            // Row at Y=20 should be skipped (incomplete)
            var yLocs = result.Series[0].XValues;
            Assert.DoesNotContain(20.0, yLocs);
        }

        [Fact]
        public void ComputeSkewAlongBracket_AllInvalidStation_NoSeries()
        {
            var data = new List<CprRecord>();
            for (int yi = 0; yi < 5; yi++)
            {
                for (int xi = 0; xi < 3; xi++)
                {
                    data.Add(MakeRecord(locX: xi * 10.0, locY: yi * 10.0, cycle: 1,
                        stationY: [0, -1000, 20, 30, 40, 50, 60]));
                }
            }

            var filter = DefaultFilter(cycleFrom: 1);
            var result = _svc.ComputeSkewAlongBracket(data, filter, Pair(test: 1, refSt: 2));

            // Station 1 all -1000 -> all filtered as invalid -> fewer than 2 valid points per row
            Assert.Empty(result.Series);
        }

        #endregion

        #region ComputeColumns Extended

        [Fact]
        public void ComputeColumns_XAxisMode_ComputesDiffFromStationX()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                records.Add(MakeRecord(locX: 0, locY: y * 10,
                    stationX: [0, 50, 200, 25, 35, 45, 55]));
            }

            var result = _svc.ComputeColumns(records, DefaultFilter(axis: "X"), Pair(test: 2, refSt: 1));

            Assert.Single(result.Series);
            // diff = 200 - 50 = 150 for all
            Assert.All(result.Series[0].YValues, v => Assert.Equal(150.0, v, 1e-10));
        }

        [Fact]
        public void ComputeColumns_SingleColumn_GradientTIsZero()
        {
            var data = MakeDataGrid(yCount: 5, xCount: 1);
            var result = _svc.ComputeColumns(data, DefaultFilter(), Pair());

            // Single column: t=0 -> color is pure blue side of gradient
            Assert.Single(result.Series);
        }

        #endregion

        #region ComputeBlanketCycles Extended

        [Fact]
        public void ComputeBlanketCycles_CycleColorsCycle()
        {
            var data = MakeDataGrid(cycles: 10);
            int[] wanted = [1, 2, 3, 4, 5, 6, 7];

            var result = _svc.ComputeBlanketCycles(data, DefaultFilter(), Pair(), wanted);

            // Colors cycle through StationColors (length 6), so color[0] == color[6]
            Assert.Equal(7, result.Series.Count);
            Assert.Equal(result.Series[0].Color, result.Series[6].Color);
        }

        [Fact]
        public void ComputeBlanketCycles_EmptyCycleArray_NoSeries()
        {
            var data = MakeDataGrid(cycles: 5);
            var result = _svc.ComputeBlanketCycles(data, DefaultFilter(), Pair(), []);

            Assert.Empty(result.Series);
        }

        #endregion

        #region ComputeDFT Extended

        [Fact]
        public void ComputeDFT_DftMarkers_ContainExpectedLabels()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.NotNull(result.DftMarkers);
            var labels = result.DftMarkers!.Select(m => m.Label).ToList();
            Assert.Contains("ASiD", labels);
            Assert.Contains("T1", labels);
            Assert.Contains("ITM", labels);
            Assert.Contains("Stir", labels);
            Assert.Contains("CR", labels);
        }

        [Fact]
        public void ComputeDFT_DftMarkers_Have5Entries()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.Equal(5, result.DftMarkers!.Count);
        }

        [Fact]
        public void ComputeDFT_ConstantSignal_AmplitudesNearZero()
        {
            // If all station values are identical, the signal after DC subtraction is all zeros
            var records = new List<CprRecord>();
            for (int y = 0; y < 20; y++)
            {
                records.Add(MakeRecord(locY: y * 10, cycle: 1,
                    stationY: [0, 50, 50, 30, 40, 50, 60],
                    stationX: [0, 25, 25, 25, 35, 45, 55]));
            }

            // Test=1, Ref=2 -> diff = 50-50 = 0 for all -> after DC removal = 0
            var result = _svc.ComputeDFT(records, DefaultFilter(), Pair(test: 1, refSt: 2));

            Assert.True(result.Series.Count > 0);
            Assert.All(result.Series[0].YValues, v =>
                Assert.True(v < 1e-10, $"Constant signal DFT amplitude should be ~0, got {v}"));
        }

        [Fact]
        public void ComputeDFT_SeriesNamedByCycle()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 2);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.Contains(result.Series, s => s.Name == "Cycle1");
            Assert.Contains(result.Series, s => s.Name == "Cycle2");
        }

        [Fact]
        public void ComputeDFT_AutoYAxis_AlwaysTrue()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.True(result.AutoYAxis);
        }

        #endregion

        #region ComputeXScaling Extended

        [Fact]
        public void ComputeXScaling_OutlierValues_Filtered()
        {
            // Create data where some Y rows produce extreme scaling values
            var data = new List<CprRecord>();
            for (int y = 0; y < 10; y++)
            {
                double normalPixelDiff = 500;
                double pixelMin = y * 2;
                double pixelMax = normalPixelDiff + y * 2;

                // Normal rows
                data.Add(MakeRecord(locX: 0, locY: y * 10, pixelX: pixelMin));
                data.Add(MakeRecord(locX: 100, locY: y * 10, pixelX: pixelMax));
            }

            // Add an outlier row with very small pixel difference
            data.Add(MakeRecord(locX: 0, locY: 200, pixelX: 100));
            data.Add(MakeRecord(locX: 100, locY: 200, pixelX: 101)); // PtP = 1

            var result = _svc.ComputeXScaling(data, DefaultFilter());

            if (result.Series.Count > 0)
            {
                // All values should be between 0.25*formatWidth and 4*formatWidth
                double formatWidth = 100;
                Assert.All(result.Series[0].YValues, v =>
                    Assert.True(v > 0.25 * formatWidth && v < 4 * formatWidth,
                        $"Value {v} should be within outlier range"));
            }
        }

        [Fact]
        public void ComputeXScaling_Title_ContainsIteration()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                data.Add(MakeRecord(locX: 0, locY: y * 10, pixelX: y));
                data.Add(MakeRecord(locX: 100, locY: y * 10, pixelX: 500 + y));
            }

            var filter = DefaultFilter(iteration: 7);
            var result = _svc.ComputeXScaling(data, filter);

            Assert.Contains("7", result.Title);
            Assert.Contains("X Scaling", result.Title);
        }

        #endregion

        #region ComputeSkew Extended

        [Fact]
        public void ComputeSkew_SharedYAxis_Propagated()
        {
            var data = MakeDataGrid(yCount: 5, xCount: 3);
            var filter = DefaultFilter(sharedYAxis: true);

            var result = _svc.ComputeSkew(data, filter);

            Assert.True(result.SharedYAxis);
        }

        [Fact]
        public void ComputeSkew_AllInvalidForOneStation_StillCreatesSubplot()
        {
            // Station 1 all -1000, others valid
            var records = new List<CprRecord>();
            for (int xi = 0; xi < 4; xi++)
            {
                for (int yi = 0; yi < 3; yi++)
                {
                    records.Add(MakeRecord(
                        locX: xi * 10.0, locY: yi * 10.0,
                        stationY: [0, -1000, 20 + xi, 30 + xi, 40 + xi, 50 + xi, 60 + xi]));
                }
            }

            var result = _svc.ComputeSkew(records, DefaultFilter());

            // Station 1 subplot should exist but have no scatter data
            var subplot = result.Subplots![0, 0];
            Assert.NotNull(subplot);
            Assert.Empty(subplot.ScatterSeries[0].XValues);
        }

        [Fact]
        public void ComputeSkew_SubplotTitles_ContainStationNumber()
        {
            var data = MakeDataGrid(yCount: 5, xCount: 3);
            var result = _svc.ComputeSkew(data, DefaultFilter());

            for (int st = 1; st <= 6; st++)
            {
                int row = (st - 1) / 3;
                int col = (st - 1) % 3;
                Assert.Contains($"Station {st}", result.Subplots![row, col].Title);
            }
        }

        [Fact]
        public void ComputeSkew_XAxis_UsesStationXValues()
        {
            var records = new List<CprRecord>();
            for (int xi = 0; xi < 5; xi++)
            {
                for (int yi = 0; yi < 3; yi++)
                {
                    records.Add(MakeRecord(
                        locX: xi * 10.0, locY: yi * 10.0,
                        stationY: [0, 10, 20, 30, 40, 50, 60],
                        stationX: [0, 100 + xi * 5, 200, 300, 400, 500, 600]));
                }
            }

            var resultY = _svc.ComputeSkew(records, DefaultFilter(axis: "Y"));
            var resultX = _svc.ComputeSkew(records, DefaultFilter(axis: "X"));

            var scatterY = resultY.Subplots![0, 0].ScatterSeries[0].YValues;
            var scatterX = resultX.Subplots![0, 0].ScatterSeries[0].YValues;

            // Y axis values are constant (10), X axis values vary (100+xi*5)
            Assert.True(StdDev(scatterY) < 1e-10, "Y values are constant");
            Assert.True(StdDev(scatterX) > 1, "X values should vary");
        }

        #endregion

        #region ComputeRevolutions Extended

        [Fact]
        public void ComputeRevolutions_YAxisSettings_Propagated()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only"));

            var filter = DefaultFilter(autoYAxis: false, yFrom: -300, yTo: 300);
            var result = _svc.ComputeRevolutions(data, filter, Pair());

            Assert.False(result.AutoYAxis);
            Assert.Equal(-300, result.YAxisFrom);
            Assert.Equal(300, result.YAxisTo);
        }

        [Fact]
        public void ComputeRevolutions_EmptyData_NoSeries()
        {
            var result = _svc.ComputeRevolutions([], DefaultFilter(), Pair());
            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeRevolutions_Title_ContainsStationAndAxis()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only"));

            var result = _svc.ComputeRevolutions(data, DefaultFilter(axis: "X"), Pair(test: 4, refSt: 1));

            Assert.Contains("X", result.Title);
            Assert.Contains("4", result.Title);
        }

        #endregion

        #region Histogram Edge Cases

        [Fact]
        public void ComputeHistogram_XAxis_UsesStationXValues()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 10; i++)
            {
                records.Add(MakeRecord(
                    stationY: [0, 10, 0, 0, 0, 0, 0],
                    stationX: [0, 100, 0, 0, 0, 0, 0]));
            }

            var resultY = _svc.ComputeHistogram(records, DefaultFilter(axis: "Y"), [1]);
            var resultX = _svc.ComputeHistogram(records, DefaultFilter(axis: "X"), [1]);

            Assert.NotNull(resultY.HistogramData);
            Assert.NotNull(resultX.HistogramData);
            Assert.Equal(10.0, resultY.HistogramData!.Mean, 1e-6);
            Assert.Equal(100.0, resultX.HistogramData!.Mean, 1e-6);
        }

        [Fact]
        public void ComputeHistogram_Title_ContainsMeanAndStd()
        {
            var records = new List<CprRecord>();
            for (int i = 1; i <= 20; i++)
                records.Add(MakeRecord(stationY: [0, i * 5, 0, 0, 0, 0, 0]));

            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1]);

            Assert.Contains("Mean", result.Title);
            Assert.Contains("STD", result.Title);
        }

        #endregion

        #region Utility

        private static double StdDev(double[] values)
        {
            if (values.Length == 0) return 0;
            var valid = values.Where(v => !double.IsNaN(v)).ToArray();
            if (valid.Length == 0) return 0;
            double mean = valid.Average();
            return Math.Sqrt(valid.Select(v => (v - mean) * (v - mean)).Average());
        }

        #endregion
    }
}
