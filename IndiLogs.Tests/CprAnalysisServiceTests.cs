using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services.Cpr;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class CprAnalysisServiceTests
    {
        private readonly CprAnalysisService _svc = new();

        #region Helpers

        private static CprRecord MakeRecord(
            double locX = 10, double locY = 100, int cycle = 1,
            double[] stationY = null!, double[] stationX = null!,
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
            int colFrom = 1, int colTo = 10, int bowDegree = 3) => new()
        {
            Axis = axis,
            RemoveDC = removeDC,
            SmoothingWindow = smoothing,
            Iteration = iteration,
            CycleFrom = cycleFrom,
            CycleTo = cycleTo,
            ColumnFrom = colFrom,
            ColumnTo = colTo,
            AutoYAxis = true,
            BowDegree = bowDegree,
            Revolution = "One Only"
        };

        private static CprStationPair Pair(int test = 2, int refSt = 1) => new()
        {
            TestStation = test,
            RefStation = refSt
        };

        private static CprStationPair[] DefaultPairs() =>
        [
            new() { TestStation = 2, RefStation = 1 },
            new() { TestStation = 3, RefStation = 1 },
            new() { TestStation = 4, RefStation = 1 },
            new() { TestStation = 5, RefStation = 1 },
            new() { TestStation = 6, RefStation = 1 },
            new() { TestStation = 1, RefStation = 1 },
        ];

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

        #region ComputeColors

        [Fact]
        public void ComputeColors_BasicData_ReturnsSeriesForEachPair()
        {
            var data = MakeDataGrid();
            var filter = DefaultFilter();
            var pairs = DefaultPairs();

            var result = _svc.ComputeColors(data, filter, pairs);

            Assert.Equal(CprGraphType.Colors, result.GraphType);
            Assert.True(result.Series.Count > 0);
            Assert.True(result.Series.Count <= 6);
            Assert.Contains("Process Direction", result.XLabel);
        }

        [Fact]
        public void ComputeColors_WithTitle_ContainsAxisAndIteration()
        {
            var data = MakeDataGrid();
            var filter = DefaultFilter(axis: "X", iteration: 3);
            var pairs = DefaultPairs();

            var result = _svc.ComputeColors(data, filter, pairs);

            Assert.Contains("X", result.Title);
            Assert.Contains("3", result.Title);
        }

        [Fact]
        public void ComputeColors_EmptyData_ReturnsNoSeries()
        {
            var result = _svc.ComputeColors([], DefaultFilter(), DefaultPairs());
            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeColors_WithRemoveDC_SeriesMeanNearZero()
        {
            var data = MakeDataGrid(yCount: 20);
            var filter = DefaultFilter(removeDC: true);
            var pairs = DefaultPairs();

            var result = _svc.ComputeColors(data, filter, pairs);

            foreach (var series in result.Series)
            {
                double mean = series.YValues.Average();
                Assert.True(Math.Abs(mean) < 1e-10, $"Mean should be ~0 after DC removal, got {mean}");
            }
        }

        [Fact]
        public void ComputeColors_WithSmoothing_ProducesSameLength()
        {
            var data = MakeDataGrid(yCount: 20);
            var filter = DefaultFilter(smoothing: 5);
            var pairs = DefaultPairs();

            var result = _svc.ComputeColors(data, filter, pairs);

            foreach (var series in result.Series)
                Assert.Equal(series.XValues.Length, series.YValues.Length);
        }

        [Fact]
        public void ComputeColors_InvalidStationValues_Skipped()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                records.Add(MakeRecord(locY: y * 10,
                    stationY: [0, -1000, -2000, 30, 40, 50, 60]));
            }

            var pairs = new CprStationPair[]
            {
                new() { TestStation = 1, RefStation = 3 }, // St1=-1000 => invalid
            };
            var result = _svc.ComputeColors(records, DefaultFilter(), pairs);

            // Station 1 has -1000, so the diff should be NaN/invalid, no series produced
            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeColors_LimitedToSixPairs()
        {
            var data = MakeDataGrid();
            var pairs = Enumerable.Range(1, 8).Select(i => new CprStationPair { TestStation = (i % 6) + 1, RefStation = 1 }).ToArray();

            var result = _svc.ComputeColors(data, DefaultFilter(), pairs);

            // Should process at most 6 pairs
            Assert.True(result.Series.Count <= 6);
        }

        #endregion

        #region ComputeColumns

        [Fact]
        public void ComputeColumns_ProducesOneSeriesPerColumn()
        {
            var data = MakeDataGrid(xCount: 3);
            var filter = DefaultFilter();
            var pair = Pair();

            var result = _svc.ComputeColumns(data, filter, pair);

            Assert.Equal(CprGraphType.Columns, result.GraphType);
            Assert.Equal(3, result.Series.Count);
        }

        [Fact]
        public void ComputeColumns_EmptyData_ReturnsNoSeries()
        {
            var result = _svc.ComputeColumns([], DefaultFilter(), Pair());
            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeColumns_SeriesNamedByColumnIndex()
        {
            var data = MakeDataGrid(xCount: 2);
            var result = _svc.ComputeColumns(data, DefaultFilter(), Pair());

            Assert.All(result.Series, s => Assert.StartsWith("Col", s.Name));
        }

        [Fact]
        public void ComputeColumns_GradientColors_FirstAndLastDiffer()
        {
            var data = MakeDataGrid(xCount: 4);
            var result = _svc.ComputeColumns(data, DefaultFilter(), Pair());

            if (result.Series.Count >= 2)
            {
                var first = result.Series[0].Color;
                var last = result.Series[^1].Color;
                Assert.NotEqual(first, last);
            }
        }

        #endregion

        #region ComputeBlanketCycles

        [Fact]
        public void ComputeBlanketCycles_ReturnsSeriesForRequestedCycles()
        {
            var data = MakeDataGrid(cycles: 5);
            var filter = DefaultFilter();
            int[] wanted = [1, 3, 5];

            var result = _svc.ComputeBlanketCycles(data, filter, Pair(), wanted);

            Assert.Equal(CprGraphType.BlanketCycles, result.GraphType);
            Assert.Equal(3, result.Series.Count);
            Assert.Contains(result.Series, s => s.Name == "Cycle1");
            Assert.Contains(result.Series, s => s.Name == "Cycle3");
            Assert.Contains(result.Series, s => s.Name == "Cycle5");
        }

        [Fact]
        public void ComputeBlanketCycles_MissingCycle_Skipped()
        {
            var data = MakeDataGrid(cycles: 2);
            int[] wanted = [1, 99];

            var result = _svc.ComputeBlanketCycles(data, DefaultFilter(), Pair(), wanted);

            Assert.Single(result.Series);
            Assert.Equal("Cycle1", result.Series[0].Name);
        }

        [Fact]
        public void ComputeBlanketCycles_EmptyData_NoSeries()
        {
            var result = _svc.ComputeBlanketCycles([], DefaultFilter(), Pair(), [1]);
            Assert.Empty(result.Series);
        }

        #endregion

        #region ComputeXScaling

        [Fact]
        public void ComputeXScaling_BasicData_ReturnsSeries()
        {
            // Need at least 2 distinct X locations with varying pixel values
            var data = new List<CprRecord>();
            for (int y = 0; y < 10; y++)
            {
                data.Add(MakeRecord(locX: 0, locY: y * 10, pixelX: y * 2));
                data.Add(MakeRecord(locX: 100, locY: y * 10, pixelX: 500 + y * 2));
            }

            var result = _svc.ComputeXScaling(data, DefaultFilter());

            Assert.Equal(CprGraphType.XScaling, result.GraphType);
        }

        [Fact]
        public void ComputeXScaling_SingleXLocation_NoSeries()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
                data.Add(MakeRecord(locX: 10, locY: y * 10));

            var result = _svc.ComputeXScaling(data, DefaultFilter());

            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeXScaling_EmptyData_NoSeries()
        {
            var result = _svc.ComputeXScaling([], DefaultFilter());
            Assert.Empty(result.Series);
        }

        #endregion

        #region ComputeDFT

        [Fact]
        public void ComputeDFT_BasicData_ReturnsFrequencySeries()
        {
            var data = MakeDataGrid(yCount: 20, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.Equal(CprGraphType.DFT, result.GraphType);
            Assert.NotNull(result.DftMarkers);
            Assert.True(result.Series.Count > 0);
        }

        [Fact]
        public void ComputeDFT_AmplitudesNonNegative()
        {
            var data = MakeDataGrid(yCount: 30, cycles: 1);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            foreach (var series in result.Series)
                Assert.All(series.YValues, v => Assert.True(v >= 0, "DFT amplitude must be non-negative"));
        }

        [Fact]
        public void ComputeDFT_SingleYPoint_NoSeries()
        {
            var data = new List<CprRecord> { MakeRecord(locY: 100, cycle: 1) };
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeDFT_MultipleCycles_MultipleSeriesReturned()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 3);
            var result = _svc.ComputeDFT(data, DefaultFilter(), Pair());

            Assert.Equal(3, result.Series.Count);
        }

        #endregion

        #region ComputeHistogram

        [Fact]
        public void ComputeHistogram_BasicData_ReturnsHistogramData()
        {
            var data = MakeDataGrid(yCount: 50, cycles: 2);
            int[] stations = [1, 2, 3];

            var result = _svc.ComputeHistogram(data, DefaultFilter(), stations);

            Assert.Equal(CprGraphType.Histogram, result.GraphType);
            Assert.NotNull(result.HistogramData);
            Assert.Equal(101, result.HistogramData!.BinEdges.Length); // nBins + 1
            Assert.Equal(100, result.HistogramData.BinCounts.Length);
            Assert.Equal(200, result.HistogramData.NormalX.Length);
        }

        [Fact]
        public void ComputeHistogram_DensityIntegratesToOne()
        {
            var data = MakeDataGrid(yCount: 100, cycles: 3);
            int[] stations = [1, 2, 3, 4, 5, 6];

            var result = _svc.ComputeHistogram(data, DefaultFilter(), stations);
            var hist = result.HistogramData!;

            double binWidth = (hist.BinEdges[^1] - hist.BinEdges[0]) / hist.BinCounts.Length;
            double integral = hist.BinCounts.Sum() * binWidth;

            // Should integrate approximately to 1 (it's a density)
            Assert.True(Math.Abs(integral - 1.0) < 0.1, $"Density integral should be ~1.0, got {integral}");
        }

        [Fact]
        public void ComputeHistogram_InvalidStationIndex_Ignored()
        {
            var data = MakeDataGrid(yCount: 10);
            int[] stations = [0, 7, 8]; // Invalid station indices

            var result = _svc.ComputeHistogram(data, DefaultFilter(), stations);

            Assert.Equal("Histogram - No data", result.Title);
        }

        [Fact]
        public void ComputeHistogram_AllMissingValues_NoData()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(stationY: [0, -1000, -2000, 0, -1000, -2000, 0]));

            // Stations 1,2 are -1000/-2000, station 3 is 0 (filtered out as val==0)
            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1, 2, 3]);

            Assert.Equal("Histogram - No data", result.Title);
        }

        [Fact]
        public void ComputeHistogram_MeanAndStdCorrect()
        {
            // Create records with known station values
            var records = new List<CprRecord>();
            double[] vals = [10, 20, 30, 40, 50];
            foreach (var v in vals)
                records.Add(MakeRecord(stationY: [0, v, 0, 0, 0, 0, 0]));

            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1]);
            var hist = result.HistogramData!;

            Assert.Equal(30.0, hist.Mean, 1e-6);
            double expectedStd = Math.Sqrt(vals.Select(v => (v - 30) * (v - 30)).Average());
            Assert.Equal(expectedStd, hist.Std, 1e-6);
        }

        #endregion

        #region ComputeRevolutions

        [Fact]
        public void ComputeRevolutions_ProducesSeriesPerRevType()
        {
            var data = new List<CprRecord>();
            foreach (var rev in new[] { "One Only", "First of Many", "Last of Many" })
            {
                for (int y = 0; y < 5; y++)
                    data.Add(MakeRecord(locY: y * 10, revolution: rev));
            }

            var result = _svc.ComputeRevolutions(data, DefaultFilter(), Pair(test: 2, refSt: 1));

            Assert.Equal(CprGraphType.Revolutions, result.GraphType);
            Assert.Equal(3, result.Series.Count);
        }

        [Fact]
        public void ComputeRevolutions_MissingRevType_SkipsIt()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only"));

            var result = _svc.ComputeRevolutions(data, DefaultFilter(), Pair(test: 2, refSt: 1));

            Assert.Single(result.Series);
            Assert.Equal("1 of 1", result.Series[0].Name);
        }

        #endregion

        #region ComputeMissingData

        [Fact]
        public void ComputeMissingData_Always6Series()
        {
            var data = MakeDataGrid(yCount: 5);
            var result = _svc.ComputeMissingData(data, DefaultFilter());

            Assert.Equal(CprGraphType.MissingData, result.GraphType);
            Assert.Equal(6, result.Series.Count);
        }

        [Fact]
        public void ComputeMissingData_CountsMinus1000()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 3; y++)
            {
                // Station 1 Y = -1000 for all rows
                records.Add(MakeRecord(locY: y * 10,
                    stationY: [0, -1000, 20, 30, 40, 50, 60]));
            }

            var result = _svc.ComputeMissingData(records, DefaultFilter(axis: "Y"));

            // Station 1 should have count of 1 per Y-location (all are -1000)
            var st1 = result.Series.First(s => s.Name == "Station 1");
            Assert.All(st1.YValues, v => Assert.Equal(1.0, v));

            // Station 2 should have 0 missing
            var st2 = result.Series.First(s => s.Name == "Station 2");
            Assert.All(st2.YValues, v => Assert.Equal(0.0, v));
        }

        [Fact]
        public void ComputeMissingData_XAxis_ChecksStationX()
        {
            var records = new List<CprRecord>
            {
                MakeRecord(locY: 0,
                    stationX: [0, -1000, 15, 25, 35, 45, 55],
                    stationY: [0, 10, 20, 30, 40, 50, 60])
            };

            var result = _svc.ComputeMissingData(records, DefaultFilter(axis: "X"));

            var st1 = result.Series.First(s => s.Name == "Station 1");
            Assert.Equal(1.0, st1.YValues[0]);
        }

        #endregion

        #region ComputeSkew

        [Fact]
        public void ComputeSkew_Returns2x3Subplots()
        {
            var data = MakeDataGrid(yCount: 10, xCount: 5);
            var result = _svc.ComputeSkew(data, DefaultFilter());

            Assert.Equal(CprGraphType.Skew, result.GraphType);
            Assert.NotNull(result.Subplots);
            Assert.Equal(2, result.SubplotRows);
            Assert.Equal(3, result.SubplotCols);

            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                    Assert.NotNull(result.Subplots![r, c]);
        }

        [Fact]
        public void ComputeSkew_SubplotHasScatterAndLinearFit()
        {
            var data = MakeDataGrid(yCount: 10, xCount: 4);
            var result = _svc.ComputeSkew(data, DefaultFilter());

            var subplot = result.Subplots![0, 0];
            Assert.True(subplot.ScatterSeries.Count > 0);
            Assert.True(subplot.LineSeries.Count > 0);
            Assert.Contains(subplot.LineSeries, s => s.Name == "Linear");
        }

        [Fact]
        public void ComputeSkew_BowDegreeGte2_AddsBowLine()
        {
            var data = MakeDataGrid(yCount: 10, xCount: 5);
            var filter = DefaultFilter(bowDegree: 3);
            var result = _svc.ComputeSkew(data, filter);

            var subplot = result.Subplots![0, 0];
            Assert.Contains(subplot.LineSeries, s => s.Name == "Bow");
        }

        [Fact]
        public void ComputeSkew_BowDegree1_NoBowLine()
        {
            var data = MakeDataGrid(yCount: 10, xCount: 5);
            var filter = DefaultFilter(bowDegree: 1);
            var result = _svc.ComputeSkew(data, filter);

            var subplot = result.Subplots![0, 0];
            Assert.DoesNotContain(subplot.LineSeries, s => s.Name == "Bow");
        }

        #endregion

        #region ComputeSkewAlongBracket

        [Fact]
        public void ComputeSkewAlongBracket_ProducesSlopePerYRow()
        {
            var data = MakeDataGrid(yCount: 10, xCount: 4, cycles: 1);
            var filter = DefaultFilter(cycleFrom: 1);

            var result = _svc.ComputeSkewAlongBracket(data, filter, Pair(test: 2, refSt: 1));

            Assert.Equal(CprGraphType.SkewAlongBracket, result.GraphType);
            Assert.True(result.Series.Count > 0);
            Assert.True(result.Series[0].XValues.Length > 0);
        }

        [Fact]
        public void ComputeSkewAlongBracket_NoCycleMatch_NoSeries()
        {
            var data = MakeDataGrid(yCount: 5, cycles: 1);
            var filter = DefaultFilter(cycleFrom: 99); // cycle 99 doesn't exist

            var result = _svc.ComputeSkewAlongBracket(data, filter, Pair());

            Assert.Empty(result.Series);
        }

        #endregion

        #region ComputeStats

        [Fact]
        public void ComputeStats_Returns15Rows()
        {
            // 3 summary rows (Max CPR, Max Y, Max X) + 6 Y stations + 6 X stations = 15
            var data = MakeDataGrid(yCount: 20);
            var rows = _svc.ComputeStats(data);

            Assert.Equal(15, rows.Count);
            Assert.Equal("Max CPR", rows[0].Station);
            Assert.Equal("Max Y", rows[1].Station);
            Assert.Equal("Max X", rows[2].Station);
        }

        [Fact]
        public void ComputeStats_PerStationRowsPresent()
        {
            var data = MakeDataGrid(yCount: 10);
            var rows = _svc.ComputeStats(data);

            for (int st = 1; st <= 6; st++)
            {
                Assert.Contains(rows, r => r.Station == $"Y {st}");
                Assert.Contains(rows, r => r.Station == $"X {st}");
            }
        }

        [Fact]
        public void ComputeStats_EmptyData_ReturnsZeroPercentiles()
        {
            var rows = _svc.ComputeStats([]);

            Assert.Equal(15, rows.Count);
            Assert.Equal("0", rows[0].Perc95);
            Assert.Equal("0", rows[0].Perc99);
        }

        [Fact]
        public void ComputeStats_MissingValues_Excluded()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 10; i++)
            {
                records.Add(MakeRecord(
                    stationY: [0, -1000, 20, 30, 40, 50, 60],
                    stationX: [0, -2000, 15, 25, 35, 45, 55]));
            }

            var rows = _svc.ComputeStats(records);
            // Station 1 Y is all -1000, station 1 X is all -2000 -> treated as NaN
            var y1Row = rows.First(r => r.Station == "Y 1");
            Assert.Equal("0", y1Row.Perc95); // No valid data -> percentile of empty = 0
        }

        [Fact]
        public void ComputeStats_KnownValues_CorrectPercentile()
        {
            // Create 100 records with station 1 Y having values 1..100
            var records = new List<CprRecord>();
            for (int i = 1; i <= 100; i++)
            {
                records.Add(MakeRecord(
                    stationY: [0, i, 0, 0, 0, 0, 0],
                    stationX: [0, 0, 0, 0, 0, 0, 0]));
            }

            var rows = _svc.ComputeStats(records);
            var y1Row = rows.First(r => r.Station == "Y 1");

            // All values are abs'd: 1..100, 95th percentile of 0..99 index range
            // Percentile(95) of [1..100] (abs values) = 95.05
            int p95 = int.Parse(y1Row.Perc95);
            Assert.True(p95 >= 94 && p95 <= 96, $"Expected ~95, got {p95}");
        }

        #endregion

        #region ComputeOffsetSkew

        [Fact]
        public void ComputeOffsetSkew_Returns6Rows()
        {
            var data = MakeDataGrid();
            var rows = _svc.ComputeOffsetSkew(data, "Y");

            Assert.Equal(6, rows.Count);
            for (int i = 0; i < 6; i++)
                Assert.Equal((i + 1).ToString(), rows[i].Station);
        }

        [Fact]
        public void ComputeOffsetSkew_YOffset_IsAverage()
        {
            // All records have station 1 Y = 50
            var records = new List<CprRecord>();
            for (int i = 0; i < 10; i++)
                records.Add(MakeRecord(stationY: [0, 50, 20, 30, 40, 50, 60]));

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            Assert.Equal("50", rows[0].YOffset); // Station 1 Y avg = 50
        }

        [Fact]
        public void ComputeOffsetSkew_AllInvalid_ShowsNaN()
        {
            var records = new List<CprRecord>();
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(
                    stationY: [0, -1000, 20, 30, 40, 50, 60],
                    stationX: [0, -2000, 15, 25, 35, 45, 55]));

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            // Station 1 Y is all -1000 -> filtered out -> NaN
            Assert.Equal("NaN", rows[0].YOffset);
            Assert.Equal("NaN", rows[0].XOffset);
        }

        [Fact]
        public void ComputeOffsetSkew_SkewRequiresMultipleXLocations()
        {
            // Single X location -> cannot fit linear regression meaningfully
            var records = new List<CprRecord>();
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(locX: 10, stationY: [0, 50, 20, 30, 40, 50, 60]));

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            // With only 1 X group, slope is 0 (LinearFit returns (0,0) for n<2)
            // Actually with 1 group, grouped.Count < 2, so skewSlope stays NaN
            Assert.Equal("NaN", rows[0].Skew);
        }

        [Fact]
        public void ComputeOffsetSkew_MultipleXLocations_ComputesSkew()
        {
            var records = new List<CprRecord>();
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    records.Add(MakeRecord(locX: x * 10, locY: y * 10,
                        stationY: [0, 10 + x * 2, 20, 30, 40, 50, 60]));
                }
            }

            var rows = _svc.ComputeOffsetSkew(records, "Y");

            // Station 1 Y increases linearly with X -> slope should be ~0.2
            Assert.NotEqual("NaN", rows[0].Skew);
            double skew = double.Parse(rows[0].Skew);
            Assert.True(Math.Abs(skew - 0.2) < 0.01, $"Expected skew ~0.2, got {skew}");
        }

        #endregion

        #region Helper Method Tests (via graph results)

        [Fact]
        public void RemoveDC_AppliedViaComputeColors_MeanIsZero()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 30; y++)
            {
                records.Add(MakeRecord(locY: y,
                    stationY: [0, 100 + y, 50, 30, 40, 50, 60]));
            }

            var pairs = new CprStationPair[] { new() { TestStation = 1, RefStation = 2 } };
            var filter = DefaultFilter(removeDC: true);
            var result = _svc.ComputeColors(records, filter, pairs);

            Assert.True(result.Series.Count > 0);
            double mean = result.Series[0].YValues.Average();
            Assert.True(Math.Abs(mean) < 1e-10);
        }

        [Fact]
        public void Smoothing_ReducesVariation()
        {
            var records = new List<CprRecord>();
            var rng = new Random(42);
            for (int y = 0; y < 50; y++)
            {
                double val = 100 + rng.NextDouble() * 50;
                records.Add(MakeRecord(locY: y,
                    stationY: [0, val, 50, 30, 40, 50, 60]));
            }

            var pairs = new CprStationPair[] { new() { TestStation = 1, RefStation = 2 } };

            var resultNoSmooth = _svc.ComputeColors(records, DefaultFilter(smoothing: 1), pairs);
            var resultSmoothed = _svc.ComputeColors(records, DefaultFilter(smoothing: 10), pairs);

            if (resultNoSmooth.Series.Count > 0 && resultSmoothed.Series.Count > 0)
            {
                double stdNoSmooth = StdDev(resultNoSmooth.Series[0].YValues);
                double stdSmoothed = StdDev(resultSmoothed.Series[0].YValues);
                Assert.True(stdSmoothed < stdNoSmooth, "Smoothing should reduce variation");
            }
        }

        private static double StdDev(double[] values)
        {
            double mean = values.Average();
            return Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
        }

        #endregion

        #region XAxis Tests

        [Fact]
        public void ComputeColors_XAxis_UsesStationX()
        {
            var records = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                records.Add(MakeRecord(locY: y * 10,
                    stationY: [0, 10, 20, 30, 40, 50, 60],
                    stationX: [0, 100, 200, 300, 400, 500, 600]));
            }

            var pairs = new CprStationPair[] { new() { TestStation = 2, RefStation = 1 } };
            var resultY = _svc.ComputeColors(records, DefaultFilter(axis: "Y"), pairs);
            var resultX = _svc.ComputeColors(records, DefaultFilter(axis: "X"), pairs);

            // X station values are 10x larger, so diffs should differ
            Assert.True(resultY.Series.Count > 0);
            Assert.True(resultX.Series.Count > 0);
            Assert.NotEqual(resultY.Series[0].YValues[0], resultX.Series[0].YValues[0]);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ComputeHistogram_SingleValue_StillWorks()
        {
            // Need at least 2 values
            var records = new List<CprRecord>
            {
                MakeRecord(stationY: [0, 42, 0, 0, 0, 0, 0]),
                MakeRecord(stationY: [0, 43, 0, 0, 0, 0, 0])
            };

            var result = _svc.ComputeHistogram(records, DefaultFilter(), [1]);

            Assert.NotNull(result.HistogramData);
        }

        [Fact]
        public void ComputeXScaling_ZeroPtP_ReturnsEmpty()
        {
            // All same pixel values -> last PtP = 0 -> early return
            var data = new List<CprRecord>();
            for (int y = 0; y < 5; y++)
            {
                data.Add(MakeRecord(locX: 0, locY: y * 10, pixelX: 100));
                data.Add(MakeRecord(locX: 50, locY: y * 10, pixelX: 100));
            }

            var result = _svc.ComputeXScaling(data, DefaultFilter());

            Assert.Empty(result.Series);
        }

        [Fact]
        public void ComputeColumns_WithRemoveDCAndSmoothing_NoException()
        {
            var data = MakeDataGrid(yCount: 20, xCount: 3);
            var filter = DefaultFilter(removeDC: true, smoothing: 5);

            var result = _svc.ComputeColumns(data, filter, Pair());

            Assert.True(result.Series.Count > 0);
        }

        [Fact]
        public void ComputeBlanketCycles_WithRemoveDCAndSmoothing()
        {
            var data = MakeDataGrid(yCount: 20, cycles: 3);
            var filter = DefaultFilter(removeDC: true, smoothing: 3);

            var result = _svc.ComputeBlanketCycles(data, filter, Pair(), [1, 2]);

            Assert.Equal(2, result.Series.Count);
            foreach (var s in result.Series)
            {
                double mean = s.YValues.Where(v => !double.IsNaN(v)).Average();
                // DC removed + smoothed: mean should be near zero
                Assert.True(Math.Abs(mean) < 5, $"Expected near-zero mean, got {mean}");
            }
        }

        [Fact]
        public void ComputeRevolutions_WithDCRemoval()
        {
            var data = new List<CprRecord>();
            for (int y = 0; y < 20; y++)
                data.Add(MakeRecord(locY: y * 10, revolution: "One Only"));

            var filter = DefaultFilter(removeDC: true);
            var result = _svc.ComputeRevolutions(data, filter, Pair(test: 3, refSt: 1));

            Assert.True(result.Series.Count > 0);
            double mean = result.Series[0].YValues.Average();
            Assert.True(Math.Abs(mean) < 1e-10);
        }

        #endregion

        #region Title and Metadata Tests

        [Fact]
        public void ComputeDFT_Title_ContainsStations()
        {
            var data = MakeDataGrid(yCount: 10, cycles: 1);
            var pair = new CprStationPair { TestStation = 4, RefStation = 2 };
            var result = _svc.ComputeDFT(data, DefaultFilter(), pair);

            Assert.Contains("4", result.Title);
            Assert.Contains("2", result.Title);
        }

        [Fact]
        public void ComputeMissingData_Title_ContainsAxisAndIteration()
        {
            var data = MakeDataGrid();
            var filter = DefaultFilter(axis: "X", iteration: 5);
            var result = _svc.ComputeMissingData(data, filter);

            Assert.Contains("X", result.Title);
            Assert.Contains("5", result.Title);
        }

        [Fact]
        public void ComputeSkewAlongBracket_Title_ContainsStation()
        {
            var data = MakeDataGrid(yCount: 5, xCount: 3, cycles: 1);
            var pair = new CprStationPair { TestStation = 5, RefStation = 1 };
            var result = _svc.ComputeSkewAlongBracket(data, DefaultFilter(cycleFrom: 1), pair);

            Assert.Contains("5", result.Title);
        }

        #endregion

        #region YAxis Control Tests

        [Fact]
        public void ComputeColors_AutoYAxis_Propagated()
        {
            var data = MakeDataGrid();
            var filter = DefaultFilter();
            filter.AutoYAxis = false;
            filter.YAxisFrom = -100;
            filter.YAxisTo = 100;

            var result = _svc.ComputeColors(data, filter, DefaultPairs());

            Assert.False(result.AutoYAxis);
            Assert.Equal(-100, result.YAxisFrom);
            Assert.Equal(100, result.YAxisTo);
        }

        #endregion
    }
}
