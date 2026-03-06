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

        #endregion
    }
}
