using System;
using System.Collections.Generic;
using SkiaSharp;

namespace IndiLogs_3._0.Models.Cpr
{
    public enum CprGraphType
    {
        Colors,
        Columns,
        BlanketCycles,
        XScaling,
        DFT,
        Histogram,
        Revolutions,
        MissingData,
        Skew,
        SkewAlongBracket
    }

    public class CprRecord
    {
        public int SN { get; set; }
        public int IterationNum { get; set; }
        public int CycleNumber { get; set; }
        public double ElementLocationX { get; set; }
        public double ElementLocationY { get; set; }
        public double ElementLocationPixelX { get; set; }
        public double ElementLocationPixelY { get; set; }
        public double[] StationX { get; set; } = new double[7]; // 0=ref(0), 1-6=stations
        public double[] StationY { get; set; } = new double[7]; // 0=ref(0), 1-6=stations
        public string RevolutionIndex { get; set; }
        public string Revolution { get; set; }
        public string StartCalibrationTime { get; set; }
    }

    public class CprStationPair
    {
        public int TestStation { get; set; }
        public int RefStation { get; set; }
    }

    public class CprFilterState
    {
        public int MachineSN { get; set; }
        public string CalibrationTime { get; set; }
        public string Revolution { get; set; }
        public int Iteration { get; set; }
        public int CycleFrom { get; set; }
        public int CycleTo { get; set; }
        public int ColumnFrom { get; set; }
        public int ColumnTo { get; set; }
        public string Axis { get; set; } = "Y"; // "Y" or "X"
        public bool RemoveDC { get; set; }
        public bool AutoYAxis { get; set; } = true;
        public bool SharedYAxis { get; set; }
        public int SmoothingWindow { get; set; } = 1;
        public int BowDegree { get; set; } = 3;
        public double YAxisFrom { get; set; } = -200;
        public double YAxisTo { get; set; } = 200;
    }

    public class CprSeriesData
    {
        public string Name { get; set; }
        public double[] XValues { get; set; }
        public double[] YValues { get; set; }
        public SKColor Color { get; set; }
        public bool IsDashed { get; set; }
        public float StrokeWidth { get; set; } = 1.5f;
    }

    public class CprScatterData
    {
        public string Name { get; set; }
        public double[] XValues { get; set; }
        public double[] YValues { get; set; }
        public SKColor Color { get; set; }
    }

    public class CprSubplot
    {
        public string Title { get; set; }
        public List<CprScatterData> ScatterSeries { get; set; } = new List<CprScatterData>();
        public List<CprSeriesData> LineSeries { get; set; } = new List<CprSeriesData>();
    }

    public class CprHistogramData
    {
        public double[] BinEdges { get; set; }
        public double[] BinCounts { get; set; }
        public double[] NormalX { get; set; }
        public double[] NormalY { get; set; }
        public double Mean { get; set; }
        public double Std { get; set; }
    }

    public class CprDftMarker
    {
        public double Frequency { get; set; }
        public string Label { get; set; }
        public SKColor Color { get; set; }
        public bool IsDashed { get; set; }
    }

    public enum RefLineStyle
    {
        Solid,
        Dashed,
        DashDot,
        Dotted
    }

    public class VerticalRefLine
    {
        public double XValue { get; set; }
        public string Label { get; set; }
        public SKColor Color { get; set; }
        public RefLineStyle LineStyle { get; set; } = RefLineStyle.Dashed;
    }

    public class CprGraphResult
    {
        public CprGraphType GraphType { get; set; }
        public string Title { get; set; }
        public string XLabel { get; set; }
        public string YLabel { get; set; }
        public List<CprSeriesData> Series { get; set; } = new List<CprSeriesData>();

        // For subplot graphs (Skew)
        public CprSubplot[,] Subplots { get; set; }
        public int SubplotRows { get; set; }
        public int SubplotCols { get; set; }
        public bool SharedYAxis { get; set; }

        // For histogram
        public CprHistogramData HistogramData { get; set; }

        // For DFT reference lines
        public List<CprDftMarker> DftMarkers { get; set; }

        // Vertical reference lines (Colors graph: ASiD, T1, ITM, Stir, CR)
        public List<VerticalRefLine> VerticalRefLines { get; set; }

        // Y-axis control
        public bool AutoYAxis { get; set; } = true;
        public double YAxisFrom { get; set; }
        public double YAxisTo { get; set; }
    }

    public class CprStatsRow
    {
        public string Station { get; set; }
        public string Perc95 { get; set; }
        public string Perc99 { get; set; }
    }

    public class CprOffsetSkewRow
    {
        public string Station { get; set; }
        public string YOffset { get; set; }
        public string XOffset { get; set; }
        public string Skew { get; set; }
    }
}
