using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models.Cpr;

namespace IndiLogs_3._0.Services.Cpr
{
    public partial class CprAnalysisService
    {
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
            var sorted = new List<double>(data);
            sorted.Sort();
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
