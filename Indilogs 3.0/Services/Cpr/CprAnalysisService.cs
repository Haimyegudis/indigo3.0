using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models.Cpr;
using SkiaSharp;

namespace IndiLogs_3._0.Services.Cpr
{
    public class CprAnalysisService
    {
        // Station colors matching the Python app exactly
        private static readonly SKColor[] StationColors = new[]
        {
            SKColor.Parse("#800080"), // purple  (station 1)
            SKColor.Parse("#FFA500"), // orange  (station 2)
            SKColor.Parse("#B22222"), // firebrick (station 3)
            SKColor.Parse("#4169E1"), // royalblue (station 4)
            SKColor.Parse("#000000"), // black   (station 5)
            SKColor.Parse("#008000"), // green   (station 6)
        };

        // DFT reference frequencies (1/mm)
        private static readonly double FreqASiD = 0.0013153;
        private static readonly double FreqT1 = 0.00229;
        private static readonly double FreqITM = 0.0022298;
        private static readonly double FreqStir = 0.0055843;
        private static readonly double FreqCR = 0.01326;

        private static readonly List<CprDftMarker> DftReferenceMarkers = new List<CprDftMarker>
        {
            new CprDftMarker { Frequency = FreqASiD, Label = "ASiD", Color = SKColors.Black, IsDashed = false },
            new CprDftMarker { Frequency = FreqT1,   Label = "T1",   Color = SKColor.Parse("#800080"), IsDashed = false },
            new CprDftMarker { Frequency = FreqITM,  Label = "ITM",  Color = SKColors.Black, IsDashed = true },
            new CprDftMarker { Frequency = FreqStir, Label = "Stir", Color = SKColors.Black, IsDashed = true },
            new CprDftMarker { Frequency = FreqCR,   Label = "CR",   Color = SKColors.Black, IsDashed = true },
        };

        // Colors graph: vertical reference lines at wavelength positions (mm)
        // These show where each periodic error source has its spatial repeat
        private static readonly List<VerticalRefLine> ColorsReferenceLines = new List<VerticalRefLine>
        {
            new VerticalRefLine { XValue = 1.0 / FreqASiD, Label = "ASiD", Color = SKColors.Black, LineStyle = RefLineStyle.Solid },
            new VerticalRefLine { XValue = 1.0 / FreqT1,   Label = "T1",   Color = SKColor.Parse("#800080"), LineStyle = RefLineStyle.Solid },
            new VerticalRefLine { XValue = 1.0 / FreqITM,  Label = "ITM",  Color = SKColors.Black, LineStyle = RefLineStyle.Dashed },
            new VerticalRefLine { XValue = 1.0 / FreqStir, Label = "Stir", Color = SKColors.Black, LineStyle = RefLineStyle.DashDot },
            new VerticalRefLine { XValue = 1.0 / FreqCR,   Label = "CR",   Color = SKColors.Black, LineStyle = RefLineStyle.Dotted },
        };

        #region Graph Computations

        /// <summary>
        /// Colors graph: 6 station pairs, grouped by ElementLocationY, mean, DC removal, smoothing
        /// </summary>
        public CprGraphResult ComputeColors(List<CprRecord> data, CprFilterState filter, CprStationPair[] pairs)
        {
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.Colors,
                XLabel = "Process Direction (mm)",
                YLabel = "CPR Error (um)",
                AutoYAxis = filter.AutoYAxis,
                YAxisFrom = filter.YAxisFrom,
                YAxisTo = filter.YAxisTo,
                VerticalRefLines = ColorsReferenceLines
            };

            string axis = filter.Axis;
            result.Title = $"Average {axis} CPR of Iteration {filter.Iteration} Cycles {filter.CycleFrom}-{filter.CycleTo} Columns {filter.ColumnFrom}-{filter.ColumnTo} Revolution {filter.Revolution}";

            for (int i = 0; i < Math.Min(6, pairs.Length); i++)
            {
                var pair = pairs[i];
                // Compute station difference per record, then group by ElementLocationY and mean
                var grouped = data
                    .Select(r => new { Y = r.ElementLocationY, Diff = GetStationDiff(r, axis, pair.TestStation, pair.RefStation) })
                    .Where(x => !IsInvalid(x.Diff))
                    .GroupBy(x => x.Y)
                    .OrderBy(g => g.Key)
                    .Select(g => new { Y = g.Key, Mean = g.Average(x => x.Diff) })
                    .ToList();

                if (grouped.Count == 0) continue;

                double[] xVals = grouped.Select(g => g.Y).ToArray();
                double[] yVals = grouped.Select(g => g.Mean).ToArray();

                if (filter.RemoveDC)
                    RemoveDC(yVals);

                if (filter.SmoothingWindow > 1)
                    yVals = RollingMean(yVals, filter.SmoothingWindow);

                result.Series.Add(new CprSeriesData
                {
                    Name = $"St {pair.TestStation}",
                    XValues = xVals,
                    YValues = yVals,
                    Color = StationColors[i % StationColors.Length]
                });
            }

            return result;
        }

        /// <summary>
        /// Columns graph: one line per column (ElementLocationX), blue→pink gradient
        /// </summary>
        public CprGraphResult ComputeColumns(List<CprRecord> data, CprFilterState filter, CprStationPair pair)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.Columns,
                Title = $"Average {axis} CPR of Station {pair.TestStation} Compared to {pair.RefStation} of Iteration {filter.Iteration} Cycles {filter.CycleFrom}-{filter.CycleTo}",
                XLabel = "Process Direction (mm)",
                YLabel = "CPR Error (um)",
                AutoYAxis = filter.AutoYAxis,
                YAxisFrom = filter.YAxisFrom,
                YAxisTo = filter.YAxisTo
            };

            var columns = data.Select(r => (int)r.ElementLocationX).Distinct().OrderBy(x => x).ToArray();

            for (int ci = 0; ci < columns.Length; ci++)
            {
                int col = columns[ci];
                var colData = data.Where(r => (int)r.ElementLocationX == col).ToList();

                var grouped = colData
                    .Select(r => new { Y = r.ElementLocationY, Diff = GetStationDiff(r, axis, pair.TestStation, pair.RefStation) })
                    .Where(x => !IsInvalid(x.Diff))
                    .GroupBy(x => x.Y)
                    .OrderBy(g => g.Key)
                    .Select(g => new { Y = g.Key, Mean = g.Average(x => x.Diff) })
                    .ToList();

                if (grouped.Count == 0) continue;

                double[] xVals = grouped.Select(g => g.Y).ToArray();
                double[] yVals = grouped.Select(g => g.Mean).ToArray();

                if (filter.RemoveDC) RemoveDC(yVals);
                if (filter.SmoothingWindow > 1) yVals = RollingMean(yVals, filter.SmoothingWindow);

                // Blue→Pink gradient
                float t = columns.Length > 1 ? (float)ci / (columns.Length - 1) : 0;
                byte r2 = (byte)(25 + (204 - 25) * t);
                byte g2 = (byte)(102 + (51 - 102) * t);
                byte b2 = (byte)(204 + (102 - 204) * t);

                result.Series.Add(new CprSeriesData
                {
                    Name = $"Col {col}",
                    XValues = xVals,
                    YValues = yVals,
                    Color = new SKColor(r2, g2, b2)
                });
            }

            return result;
        }

        /// <summary>
        /// Blanket Cycles: user-specified cycles, one line each
        /// </summary>
        public CprGraphResult ComputeBlanketCycles(List<CprRecord> data, CprFilterState filter, CprStationPair pair, int[] wantedCycles)
        {
            string axis = filter.Axis;
            // For blanket cycles, don't filter by cycle range — use full data then pick wanted cycles
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.BlanketCycles,
                Title = $"Average {axis} CPR of Station {pair.TestStation} Compared to {pair.RefStation} Iteration {filter.Iteration} Columns {filter.ColumnFrom}-{filter.ColumnTo}",
                XLabel = "Process Direction (mm)",
                YLabel = "CPR error (um)",
                AutoYAxis = filter.AutoYAxis,
                YAxisFrom = filter.YAxisFrom,
                YAxisTo = filter.YAxisTo
            };

            var availableCycles = data.Select(r => r.CycleNumber).Distinct().ToHashSet();

            for (int i = 0; i < wantedCycles.Length; i++)
            {
                int cycle = wantedCycles[i];
                if (!availableCycles.Contains(cycle)) continue;

                var cycleData = data.Where(r => r.CycleNumber == cycle).ToList();
                var grouped = cycleData
                    .Select(r => new { Y = r.ElementLocationY, Diff = GetStationDiff(r, axis, pair.TestStation, pair.RefStation) })
                    .Where(x => !IsInvalid(x.Diff))
                    .GroupBy(x => x.Y).OrderBy(g => g.Key)
                    .Select(g => new { Y = g.Key, Mean = g.Average(x => x.Diff) })
                    .ToList();

                if (grouped.Count == 0) continue;

                double[] xVals = grouped.Select(g => g.Y).ToArray();
                double[] yVals = grouped.Select(g => g.Mean).ToArray();

                if (filter.RemoveDC) RemoveDC(yVals);
                if (filter.SmoothingWindow > 1) yVals = RollingMean(yVals, filter.SmoothingWindow);

                result.Series.Add(new CprSeriesData
                {
                    Name = $"Cycle{cycle}",
                    XValues = xVals,
                    YValues = yVals,
                    Color = StationColors[i % StationColors.Length]
                });
            }

            return result;
        }

        /// <summary>
        /// X Scaling: front-rear pixel comparison
        /// </summary>
        public CprGraphResult ComputeXScaling(List<CprRecord> data, CprFilterState filter)
        {
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.XScaling,
                Title = $"X Scaling Iteration {filter.Iteration} (Last point to the right is format width)",
                XLabel = "Process Direction (mm)",
                YLabel = "Scaling Error (mm)",
                AutoYAxis = true
            };

            var xLocs = data.Select(r => r.ElementLocationX).Distinct().OrderBy(x => x).ToArray();
            if (xLocs.Length < 2) return result;

            double xMin = xLocs.Min();
            double xMax = xLocs.Max();

            // Filter to front and rear X locations only
            var frontRear = data.Where(r => r.ElementLocationX == xMin || r.ElementLocationX == xMax).ToList();

            // Group by ElementLocationY, compute peak-to-peak of PixelX
            var grouped = frontRear.GroupBy(r => r.ElementLocationY).OrderBy(g => g.Key)
                .Select(g => new
                {
                    Y = g.Key,
                    PtP = g.Max(r => r.ElementLocationPixelX) - g.Min(r => r.ElementLocationPixelX)
                }).ToList();

            if (grouped.Count == 0) return result;

            // Divide by last value and multiply by format width
            double lastVal = grouped.Last().PtP;
            if (Math.Abs(lastVal) < 1e-10) return result;

            double formatWidth = xMax - xMin;
            var scaled = grouped.Select(g => new { g.Y, Val = (g.PtP / lastVal) * formatWidth }).ToList();

            // Filter outliers
            scaled = scaled.Where(s => s.Val > 0.25 * formatWidth && s.Val < 4 * formatWidth).ToList();

            if (scaled.Count == 0) return result;

            result.Series.Add(new CprSeriesData
            {
                Name = "X Scaling",
                XValues = scaled.Select(s => s.Y).ToArray(),
                YValues = scaled.Select(s => s.Val).ToArray(),
                Color = SKColor.Parse("#3B82F6")
            });

            return result;
        }

        /// <summary>
        /// DFT (Discrete Fourier Transform): frequency domain analysis
        /// </summary>
        public CprGraphResult ComputeDFT(List<CprRecord> data, CprFilterState filter, CprStationPair pair)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.DFT,
                Title = $"{axis} CPR Frequency of Station {pair.TestStation} compared to {pair.RefStation} Iteration {filter.Iteration} Columns {filter.ColumnFrom}-{filter.ColumnTo}",
                XLabel = "Frequency (1/mm)",
                YLabel = "Amplitude (um)",
                AutoYAxis = true,
                DftMarkers = DftReferenceMarkers
            };

            // Replace -1000/-2000 with 1 (not NaN, to avoid division by zero in DFT)
            var cycles = data.Select(r => r.CycleNumber).Distinct().OrderBy(x => x).ToArray();

            // Frequency range
            int nFreq = 1000;
            double[] frequency = Linspace(0.0005, 0.015, nFreq);

            for (int ci = 0; ci < cycles.Length; ci++)
            {
                int cycle = cycles[ci];
                var cycleData = data.Where(r => r.CycleNumber == cycle).ToList();

                var grouped = cycleData
                    .Select(r => new { Y = r.ElementLocationY, Diff = GetStationDiffForDft(r, axis, pair.TestStation, pair.RefStation) })
                    .GroupBy(x => x.Y).OrderBy(g => g.Key)
                    .Select(g => new { Y = g.Key, Mean = g.Average(x => x.Diff) })
                    .ToList();

                if (grouped.Count < 2) continue;

                double[] position = grouped.Select(g => g.Y).ToArray();
                double[] signal = grouped.Select(g => g.Mean).ToArray();

                // Subtract DC
                double mean = signal.Average();
                double[] signalDft = signal.Select(s => s - mean).ToArray();

                int nTd = signalDft.Length;
                double[] amplitude = new double[nFreq];

                for (int fi = 0; fi < nFreq; fi++)
                {
                    double aS = 0, aC = 0;
                    for (int ti = 0; ti < nTd; ti++)
                    {
                        double angle = 2 * Math.PI * frequency[fi] * position[ti];
                        aS += signalDft[ti] * Math.Sin(angle);
                        aC += signalDft[ti] * Math.Cos(angle);
                    }
                    aS = 2.0 / nTd * aS;
                    aC = 2.0 / nTd * aC;
                    amplitude[fi] = Math.Sqrt(aS * aS + aC * aC);
                }

                result.Series.Add(new CprSeriesData
                {
                    Name = $"Cycle{cycle}",
                    XValues = frequency,
                    YValues = amplitude,
                    Color = StationColors[ci % StationColors.Length]
                });
            }

            return result;
        }

        /// <summary>
        /// Histogram: distribution of CPR values with normal fit
        /// </summary>
        public CprGraphResult ComputeHistogram(List<CprRecord> data, CprFilterState filter, int[] stations)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.Histogram,
                XLabel = "CPR Value (um)",
                YLabel = "Density",
                AutoYAxis = filter.AutoYAxis,
                YAxisFrom = filter.YAxisFrom,
                YAxisTo = filter.YAxisTo
            };

            // Collect all station values for selected stations
            var values = new List<double>();
            foreach (var rec in data)
            {
                foreach (int st in stations)
                {
                    if (st < 1 || st > 6) continue;
                    double val = axis == "Y" ? rec.StationY[st] : rec.StationX[st];
                    if (val != -1000 && val != -2000 && val != 0)
                        values.Add(val);
                }
            }

            if (values.Count < 2)
            {
                result.Title = "Histogram - No data";
                return result;
            }

            double mean = values.Average();
            double std = Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
            double minVal = values.Min();
            double maxVal = values.Max();

            // Create histogram bins (100 bins)
            int nBins = 100;
            double[] binEdges = Linspace(minVal, maxVal, nBins + 1);
            double[] binCounts = new double[nBins];

            foreach (var v in values)
            {
                int bin = (int)((v - minVal) / (maxVal - minVal) * nBins);
                if (bin >= nBins) bin = nBins - 1;
                if (bin < 0) bin = 0;
                binCounts[bin]++;
            }

            // Normalize to density
            double binWidth = (maxVal - minVal) / nBins;
            for (int i = 0; i < nBins; i++)
                binCounts[i] = binCounts[i] / (values.Count * binWidth);

            // Normal curve
            double[] normalX = Linspace(minVal, maxVal, 200);
            double[] normalY = normalX.Select(x =>
                (1.0 / (std * Math.Sqrt(2 * Math.PI))) * Math.Exp(-0.5 * Math.Pow((x - mean) / std, 2))
            ).ToArray();

            result.Title = $"Histogram {axis}, Iteration {filter.Iteration}, Cycles {filter.CycleFrom}-{filter.CycleTo}, Mean = {mean:F2}, STD*2 = {(std * 2):F2}";
            result.HistogramData = new CprHistogramData
            {
                BinEdges = binEdges,
                BinCounts = binCounts,
                NormalX = normalX,
                NormalY = normalY,
                Mean = mean,
                Std = std
            };

            return result;
        }

        /// <summary>
        /// Revolutions: compare OneOnly, FirstOfMany, LastOfMany
        /// </summary>
        public CprGraphResult ComputeRevolutions(List<CprRecord> allData, CprFilterState filter, CprStationPair pair)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.Revolutions,
                Title = $"Average {axis} CPR of Station {pair.TestStation} Iteration {filter.Iteration} All Revolutions",
                XLabel = "Process Direction (mm)",
                YLabel = "CPR Error (um)",
                AutoYAxis = filter.AutoYAxis,
                YAxisFrom = filter.YAxisFrom,
                YAxisTo = filter.YAxisTo
            };

            var revTypes = new[] { ("One Only", "1 of 1"), ("First of Many", "1 of 2"), ("Last of Many", "2 of 2") };
            var colors = new[] { SKColor.Parse("#3B82F6"), SKColor.Parse("#EF4444"), SKColor.Parse("#10B981") };

            for (int ri = 0; ri < revTypes.Length; ri++)
            {
                var (revName, label) = revTypes[ri];
                var revData = allData.Where(r => r.Revolution == revName).ToList();

                var grouped = revData
                    .Select(r => new { Y = r.ElementLocationY, Val = GetStationValue(r, axis, pair.TestStation) })
                    .Where(x => !IsInvalid(x.Val))
                    .GroupBy(x => x.Y).OrderBy(g => g.Key)
                    .Select(g => new { Y = g.Key, Mean = g.Average(x => x.Val) })
                    .ToList();

                if (grouped.Count == 0) continue;

                double[] xVals = grouped.Select(g => g.Y).ToArray();
                double[] yVals = grouped.Select(g => g.Mean).ToArray();

                if (filter.RemoveDC) RemoveDC(yVals);
                if (filter.SmoothingWindow > 1) yVals = RollingMean(yVals, filter.SmoothingWindow);

                result.Series.Add(new CprSeriesData
                {
                    Name = label,
                    XValues = xVals,
                    YValues = yVals,
                    Color = colors[ri]
                });
            }

            return result;
        }

        /// <summary>
        /// Missing Data: count of -1000 per station per Y-location
        /// </summary>
        public CprGraphResult ComputeMissingData(List<CprRecord> data, CprFilterState filter)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.MissingData,
                Title = $"Missing Data {axis} for Iteration {filter.Iteration} Cycles {filter.CycleFrom}-{filter.CycleTo} Columns {filter.ColumnFrom}-{filter.ColumnTo}",
                XLabel = "Process Direction (mm)",
                YLabel = "# of -1000",
                AutoYAxis = true
            };

            for (int st = 1; st <= 6; st++)
            {
                int station = st;
                var grouped = data.GroupBy(r => r.ElementLocationY).OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        Y = g.Key,
                        MissingCount = (double)g.Count(r =>
                        {
                            double val = axis == "Y" ? r.StationY[station] : r.StationX[station];
                            return val == -1000;
                        })
                    }).ToList();

                result.Series.Add(new CprSeriesData
                {
                    Name = $"Station {st}",
                    XValues = grouped.Select(g => g.Y).ToArray(),
                    YValues = grouped.Select(g => g.MissingCount).ToArray(),
                    Color = StationColors[st - 1]
                });
            }

            return result;
        }

        /// <summary>
        /// Skew: 2x3 subplot grid with scatter + linear regression + polynomial fit
        /// </summary>
        public CprGraphResult ComputeSkew(List<CprRecord> data, CprFilterState filter)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.Skew,
                Title = $"Skew {axis} Iteration {filter.Iteration}",
                SubplotRows = 2,
                SubplotCols = 3,
                SharedYAxis = filter.SharedYAxis,
                Subplots = new CprSubplot[2, 3]
            };

            var xLocs = data.Select(r => r.ElementLocationX).Distinct().OrderBy(x => x).ToArray();

            for (int st = 0; st < 6; st++)
            {
                int row = st / 3, col = st % 3;
                int station = st + 1;
                var subplot = new CprSubplot { Title = $"Station {station}" };

                // Group by ElementLocationX, compute mean
                var grouped = data
                    .Select(r => new { X = r.ElementLocationX, Val = GetStationValue(r, axis, station) })
                    .Where(x => !IsInvalid(x.Val))
                    .GroupBy(x => x.X).OrderBy(g => g.Key)
                    .Select(g => new { X = g.Key, Mean = g.Average(x => x.Val) })
                    .ToList();

                double[] xVals = grouped.Select(g => g.X).ToArray();
                double[] yVals = grouped.Select(g => g.Mean).ToArray();

                // Scatter data
                subplot.ScatterSeries.Add(new CprScatterData
                {
                    Name = $"Station {station}",
                    XValues = xVals,
                    YValues = yVals,
                    Color = StationColors[st]
                });

                if (xVals.Length >= 2)
                {
                    // Linear regression (skew line)
                    var (slope, intercept) = LinearFit(xVals, yVals);
                    double[] regY = xLocs.Select(x => slope * x + intercept).ToArray();
                    subplot.LineSeries.Add(new CprSeriesData
                    {
                        Name = "Linear",
                        XValues = xLocs,
                        YValues = regY,
                        Color = StationColors[st],
                        StrokeWidth = 2f
                    });

                    // Polynomial fit (bow)
                    if (filter.BowDegree >= 2)
                    {
                        double[] polyCoeffs = PolyFit(xVals, yVals, filter.BowDegree);
                        double[] polyY = xLocs.Select(x => EvalPoly(polyCoeffs, x)).ToArray();
                        subplot.LineSeries.Add(new CprSeriesData
                        {
                            Name = "Bow",
                            XValues = xLocs,
                            YValues = polyY,
                            Color = StationColors[st],
                            StrokeWidth = 1f,
                            IsDashed = true
                        });
                    }
                }

                result.Subplots[row, col] = subplot;
            }

            return result;
        }

        /// <summary>
        /// Skew Along Bracket: linear slope per Y-row
        /// </summary>
        public CprGraphResult ComputeSkewAlongBracket(List<CprRecord> data, CprFilterState filter, CprStationPair pair)
        {
            string axis = filter.Axis;
            var result = new CprGraphResult
            {
                GraphType = CprGraphType.SkewAlongBracket,
                Title = $"Skew {axis} of Station {pair.TestStation} Iteration {filter.Iteration}",
                XLabel = "Process Direction (mm)",
                YLabel = "Skew Slope (um/mm)",
                AutoYAxis = true
            };

            // Use only first selected cycle (matching Python behavior)
            var cycleData = data.Where(r => r.CycleNumber == filter.CycleFrom).ToList();
            var yLocs = cycleData.Select(r => r.ElementLocationY).Distinct().OrderBy(y => y).ToArray();

            // Expected number of columns
            int expectedCols = cycleData.Select(r => (int)r.ElementLocationX).Distinct().Count();

            var skewList = new List<double>();
            var yLocList = new List<double>();

            foreach (var yLoc in yLocs)
            {
                var rowData = cycleData.Where(r => r.ElementLocationY == yLoc)
                    .OrderBy(r => r.ElementLocationX).ToList();

                if (rowData.Count != expectedCols) continue;

                double[] xArr = rowData.Select(r => r.ElementLocationX).ToArray();
                double[] yArr = rowData.Select(r => GetStationValue(r, axis, pair.TestStation)).ToArray();

                // Filter NaN
                var valid = xArr.Zip(yArr, (x, y) => new { x, y }).Where(p => !IsInvalid(p.y)).ToArray();
                if (valid.Length < 2) continue;

                var (slope, _) = LinearFit(valid.Select(p => p.x).ToArray(), valid.Select(p => p.y).ToArray());
                skewList.Add(slope);
                yLocList.Add(yLoc);
            }

            if (yLocList.Count > 0)
            {
                result.Series.Add(new CprSeriesData
                {
                    Name = "Skew",
                    XValues = yLocList.ToArray(),
                    YValues = skewList.ToArray(),
                    Color = SKColor.Parse("#3B82F6")
                });
            }

            return result;
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Compute percentile statistics (matching Python's stats_legend)
        /// </summary>
        public List<CprStatsRow> ComputeStats(List<CprRecord> data)
        {
            var rows = new List<CprStatsRow>();

            // Compute row-wise max CPR for Y and X
            var yAbsList = new List<double>();
            var xAbsList = new List<double>();

            foreach (var rec in data)
            {
                double[] yVals = new double[6];
                double[] xVals = new double[6];
                bool anyValidY = false, anyValidX = false;

                for (int s = 1; s <= 6; s++)
                {
                    double yv = rec.StationY[s];
                    double xv = rec.StationX[s];
                    yVals[s - 1] = (yv == -1000 || yv == -2000) ? double.NaN : yv;
                    xVals[s - 1] = (xv == -1000 || xv == -2000) ? double.NaN : xv;
                    if (!double.IsNaN(yVals[s - 1])) anyValidY = true;
                    if (!double.IsNaN(xVals[s - 1])) anyValidX = true;
                }

                if (anyValidY)
                {
                    var validY = yVals.Where(v => !double.IsNaN(v)).ToArray();
                    double yMax = validY.Max(), yMin = validY.Min();
                    bool allPos = validY.All(v => v >= 0);
                    bool allNeg = validY.All(v => v <= 0);
                    double absY = (allPos || allNeg) ? validY.Max(v => Math.Abs(v)) : yMax - yMin;
                    yAbsList.Add(absY);
                }

                if (anyValidX)
                {
                    var validX = xVals.Where(v => !double.IsNaN(v)).ToArray();
                    double xMax = validX.Max(), xMin = validX.Min();
                    bool allPos = validX.All(v => v >= 0);
                    bool allNeg = validX.All(v => v <= 0);
                    double absX = (allPos || allNeg) ? validX.Max(v => Math.Abs(v)) : xMax - xMin;
                    xAbsList.Add(absX);
                }
            }

            double y95 = Percentile(yAbsList, 95);
            double y99 = Percentile(yAbsList, 99);
            double x95 = Percentile(xAbsList, 95);
            double x99 = Percentile(xAbsList, 99);
            double overall95 = Math.Max(y95, x95);
            double overall99 = Math.Max(y99, x99);

            rows.Add(new CprStatsRow { Station = "Max CPR", Perc95 = FormatStat(overall95), Perc99 = FormatStat(overall99) });
            rows.Add(new CprStatsRow { Station = "Max Y", Perc95 = FormatStat(y95), Perc99 = FormatStat(y99) });
            rows.Add(new CprStatsRow { Station = "Max X", Perc95 = FormatStat(x95), Perc99 = FormatStat(x99) });

            // Per-station percentiles (absolute values)
            for (int axis = 0; axis < 2; axis++)
            {
                string axisName = axis == 0 ? "Y" : "X";
                for (int st = 1; st <= 6; st++)
                {
                    int station = st;
                    int axIdx = axis;
                    var absVals = data.Select(r =>
                    {
                        double v = axIdx == 0 ? r.StationY[station] : r.StationX[station];
                        return (v == -1000 || v == -2000) ? double.NaN : Math.Abs(v);
                    }).Where(v => !double.IsNaN(v)).ToList();

                    double p95 = Percentile(absVals, 95);
                    double p99 = Percentile(absVals, 99);
                    rows.Add(new CprStatsRow { Station = $"{axisName} {st}", Perc95 = FormatStat(p95), Perc99 = FormatStat(p99) });
                }
            }

            return rows;
        }

        /// <summary>
        /// Compute offset and skew per station (matching Python's offsets_skew_legend)
        /// </summary>
        public List<CprOffsetSkewRow> ComputeOffsetSkew(List<CprRecord> data, string axis)
        {
            var rows = new List<CprOffsetSkewRow>();

            var xLocs = data.Select(r => r.ElementLocationX).Distinct().OrderBy(x => x).ToArray();

            for (int st = 1; st <= 6; st++)
            {
                int station = st;

                // Y offset
                var yVals = data.Select(r => r.StationY[station]).Where(v => v != -1000 && v != -2000).ToList();
                double yOff = yVals.Count > 0 ? yVals.Average() : double.NaN;

                // X offset
                var xVals = data.Select(r => r.StationX[station]).Where(v => v != -1000 && v != -2000).ToList();
                double xOff = xVals.Count > 0 ? xVals.Average() : double.NaN;

                // Skew (linear regression of grouped-by-X mean)
                double skewSlope = double.NaN;
                var grouped = data
                    .Select(r => new { X = r.ElementLocationX, Val = GetStationValue(r, axis, station) })
                    .Where(x => !IsInvalid(x.Val))
                    .GroupBy(x => x.X).OrderBy(g => g.Key)
                    .Select(g => new { X = g.Key, Mean = g.Average(x => x.Val) })
                    .ToList();

                if (grouped.Count >= 2)
                {
                    var (slope, _) = LinearFit(grouped.Select(g => g.X).ToArray(), grouped.Select(g => g.Mean).ToArray());
                    skewSlope = Math.Round(slope, 3);
                }

                rows.Add(new CprOffsetSkewRow
                {
                    Station = st.ToString(),
                    YOffset = double.IsNaN(yOff) ? "NaN" : ((int)Math.Round(yOff)).ToString(),
                    XOffset = double.IsNaN(xOff) ? "NaN" : ((int)Math.Round(xOff)).ToString(),
                    Skew = double.IsNaN(skewSlope) ? "NaN" : skewSlope.ToString("F3")
                });
            }

            return rows;
        }

        #endregion

        #region Helpers

        private static double GetStationDiff(CprRecord rec, string axis, int testStation, int refStation)
        {
            double test = axis == "Y" ? rec.StationY[testStation] : rec.StationX[testStation];
            double refVal = axis == "Y" ? rec.StationY[refStation] : rec.StationX[refStation];
            if (test == -1000 || test == -2000 || refVal == -1000 || refVal == -2000) return double.NaN;
            return test - refVal;
        }

        private static double GetStationDiffForDft(CprRecord rec, string axis, int testStation, int refStation)
        {
            double test = axis == "Y" ? rec.StationY[testStation] : rec.StationX[testStation];
            double refVal = axis == "Y" ? rec.StationY[refStation] : rec.StationX[refStation];
            // For DFT, replace -1000/-2000 with 1 (not NaN) to avoid division issues
            if (test == -1000 || test == -2000) test = 1;
            if (refVal == -1000 || refVal == -2000) refVal = 1;
            return test - refVal;
        }

        private static double GetStationValue(CprRecord rec, string axis, int station)
        {
            double val = axis == "Y" ? rec.StationY[station] : rec.StationX[station];
            if (val == -1000 || val == -2000) return double.NaN;
            return val;
        }

        private static bool IsInvalid(double v) => double.IsNaN(v) || double.IsInfinity(v);

        private static void RemoveDC(double[] data)
        {
            double mean = data.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Average();
            for (int i = 0; i < data.Length; i++)
                if (!double.IsNaN(data[i]))
                    data[i] -= mean;
        }

        private static double[] RollingMean(double[] data, int window)
        {
            if (window <= 1) return data;
            double[] result = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                int start = Math.Max(0, i - window + 1);
                int count = 0;
                double sum = 0;
                for (int j = start; j <= i; j++)
                {
                    if (!double.IsNaN(data[j]))
                    {
                        sum += data[j];
                        count++;
                    }
                }
                result[i] = count > 0 ? sum / count : double.NaN;
            }
            return result;
        }

        private static double[] Linspace(double start, double end, int count)
        {
            double[] result = new double[count];
            double step = (end - start) / (count - 1);
            for (int i = 0; i < count; i++)
                result[i] = start + i * step;
            return result;
        }

        private static (double slope, double intercept) LinearFit(double[] x, double[] y)
        {
            int n = x.Length;
            if (n < 2) return (0, 0);
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += x[i];
                sumY += y[i];
                sumXY += x[i] * y[i];
                sumX2 += x[i] * x[i];
            }
            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-15) return (0, sumY / n);
            double slope = (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;
            return (slope, intercept);
        }

        /// <summary>
        /// Polynomial fit using normal equations (least squares)
        /// Returns coefficients [a_n, a_{n-1}, ..., a_1, a_0] (highest degree first)
        /// </summary>
        private static double[] PolyFit(double[] x, double[] y, int degree)
        {
            int n = x.Length;
            int m = degree + 1;
            if (n < m) return new double[m];

            // Build Vandermonde matrix
            double[,] A = new double[n, m];
            for (int i = 0; i < n; i++)
            {
                double xp = 1;
                for (int j = 0; j < m; j++)
                {
                    A[i, m - 1 - j] = xp;
                    xp *= x[i];
                }
            }

            // Normal equations: (A^T * A) * c = A^T * y
            double[,] ATA = new double[m, m];
            double[] ATy = new double[m];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    double s = 0;
                    for (int k = 0; k < n; k++)
                        s += A[k, i] * A[k, j];
                    ATA[i, j] = s;
                }
                double sy = 0;
                for (int k = 0; k < n; k++)
                    sy += A[k, i] * y[k];
                ATy[i] = sy;
            }

            // Solve via Gaussian elimination
            return SolveLinearSystem(ATA, ATy, m);
        }

        private static double[] SolveLinearSystem(double[,] A, double[] b, int n)
        {
            double[,] aug = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    aug[i, j] = A[i, j];
                aug[i, n] = b[i];
            }

            for (int col = 0; col < n; col++)
            {
                int maxRow = col;
                for (int row = col + 1; row < n; row++)
                    if (Math.Abs(aug[row, col]) > Math.Abs(aug[maxRow, col]))
                        maxRow = row;

                for (int j = col; j <= n; j++)
                {
                    double tmp = aug[col, j];
                    aug[col, j] = aug[maxRow, j];
                    aug[maxRow, j] = tmp;
                }

                if (Math.Abs(aug[col, col]) < 1e-15) continue;

                for (int row = col + 1; row < n; row++)
                {
                    double factor = aug[row, col] / aug[col, col];
                    for (int j = col; j <= n; j++)
                        aug[row, j] -= factor * aug[col, j];
                }
            }

            double[] result = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                result[i] = aug[i, n];
                for (int j = i + 1; j < n; j++)
                    result[i] -= aug[i, j] * result[j];
                if (Math.Abs(aug[i, i]) > 1e-15)
                    result[i] /= aug[i, i];
            }
            return result;
        }

        private static double EvalPoly(double[] coeffs, double x)
        {
            double result = 0;
            for (int i = 0; i < coeffs.Length; i++)
                result = result * x + coeffs[i];
            return result;
        }

        private static double Percentile(List<double> data, double percentile)
        {
            if (data.Count == 0) return 0;
            var sorted = data.OrderBy(x => x).ToList();
            double index = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            if (lower == upper) return sorted[lower];
            double frac = index - lower;
            return sorted[lower] * (1 - frac) + sorted[upper] * frac;
        }

        private static string FormatStat(double val)
        {
            if (double.IsNaN(val)) return "NaN";
            return ((int)Math.Round(val)).ToString();
        }

        #endregion
    }
}
