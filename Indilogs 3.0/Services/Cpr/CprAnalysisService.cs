using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models.Cpr;
using SkiaSharp;

namespace IndiLogs_3._0.Services.Cpr
{
    public partial class CprAnalysisService
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
                VerticalRefLines = null
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

            var columnGroups = data.GroupBy(r => (int)r.ElementLocationX).OrderBy(g => g.Key).ToList();

            for (int ci = 0; ci < columnGroups.Count; ci++)
            {
                int col = columnGroups[ci].Key;
                var colData = columnGroups[ci].ToList();

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
                float t = columnGroups.Count > 1 ? (float)ci / (columnGroups.Count - 1) : 0;
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

            var cycleGroups = data.GroupBy(r => r.CycleNumber).ToDictionary(g => g.Key, g => g.ToList());

            for (int i = 0; i < wantedCycles.Length; i++)
            {
                int cycle = wantedCycles[i];
                if (!cycleGroups.TryGetValue(cycle, out var cycleData)) continue;
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
            var dftCycleGroups = data.GroupBy(r => r.CycleNumber).OrderBy(g => g.Key).ToList();

            // Frequency range
            int nFreq = 1000;
            double[] frequency = Linspace(0.0005, 0.015, nFreq);

            for (int ci = 0; ci < dftCycleGroups.Count; ci++)
            {
                int cycle = dftCycleGroups[ci].Key;
                var cycleData = dftCycleGroups[ci].ToList();

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
            var revGroups = allData.GroupBy(r => r.Revolution).ToDictionary(g => g.Key, g => g.ToList());

            for (int ri = 0; ri < revTypes.Length; ri++)
            {
                var (revName, label) = revTypes[ri];
                if (!revGroups.TryGetValue(revName, out var revData)) continue;

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
    }
}
