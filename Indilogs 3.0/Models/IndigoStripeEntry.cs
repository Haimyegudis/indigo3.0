using System;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// Flat representation of Indigo stripe/slice data for DataGrid display
    /// Contains all fields from stripeDescriptor JSON
    /// </summary>
    public class IndigoStripeEntry
    {
        // === Basic Stripe Info ===
        public DateTime Timestamp { get; set; }
        public int SpreadId { get; set; }
        public int StripeId { get; set; }
        public string StripeType { get; set; } // Print-Image / Null-Gap
        public double LengthMm { get; set; }
        public double LengthNotScaledMm { get; set; }

        // === Slice Location ===
        public int SliceGroupIndex { get; set; }
        public int SliceIndex { get; set; }
        public int SliceId { get; set; }
        public int SliceStamp { get; set; }
        public int ParentSeparationId { get; set; }

        // === Ink Info ===
        public int InkId { get; set; }
        public string InkName { get; set; }
        public bool IsStationActive { get; set; }

        // === BID Data (Binary Image Developer) ===
        public int VDeveloper { get; set; }
        public int VElectrode { get; set; }
        public int VSqueegee { get; set; }
        public int VCleaner { get; set; }

        // === CR Data (Charge Roller) ===
        public int CrVDc { get; set; }
        public int CrVAc { get; set; }

        // === ASID Data ===
        public int VAsid { get; set; }

        // === LPH Data (Laser Print Head) ===
        public int NScanLines { get; set; }

        // === EM Data (Electrostatic Measurement) ===
        public bool EmIsActive { get; set; }
        public int EmMeasureId { get; set; }

        // === HV Target ===
        public string HvTarget { get; set; }

        // === SPM Data (Spectrophotometer) ===
        public string SpmStatus { get; set; }
        public int SpmMeasureId { get; set; }
        public string SpmScanDirection { get; set; }
        public string SpmMeasureMode { get; set; }
        public int SpmNumOfStrips { get; set; }

        // === ILS Data (Inline Scanner) ===
        public bool IlsIsActive { get; set; }
        public int IlsScanLenUm { get; set; }
        public string IlsScanMode { get; set; }
        public int IlsScanSpeedUmSec { get; set; }

        // === Position Data ===
        public int StartPosUm { get; set; }
        public int EndPosUm { get; set; }

        // === Blanket Loop Data ===
        public double WebRepeatLenScalingFactor { get; set; }
        public int BlanketLoopRepeatLenUm { get; set; }
        public int BlanketLoopT2TotalLenUm { get; set; }
        public bool FirstInBlanketLoop { get; set; }
        public bool LastInBlanketLoop { get; set; }
        public int StartPosInBlanketLoopUm { get; set; }

        // === Other Stripe Properties ===
        public bool LastStripeInSpread { get; set; }
        public bool ImageToBru { get; set; }
        public string DataTransferControl { get; set; }
        public bool ReportPrintDetails { get; set; }
        public int ReportId { get; set; }
        public int NSliceGroups { get; set; }

        // === Calculated/Display Properties ===
        public string StationStatus => IsStationActive ? "Active" : "Inactive";
        public string DisplayInk => !string.IsNullOrEmpty(InkName) ? InkName : $"Ink {InkId}";
        public double StartPosMm => Math.Round(StartPosUm / 1000.0, 2);
        public double EndPosMm => Math.Round(EndPosUm / 1000.0, 2);
        public double StartPosInBlanketLoopMm => Math.Round(StartPosInBlanketLoopUm / 1000.0, 2);
        public double BlanketLoopRepeatLenMm => Math.Round(BlanketLoopRepeatLenUm / 1000.0, 2);
        public double IlsScanLenMm => Math.Round(IlsScanLenUm / 1000.0, 2);

        // Color coding helpers - now more accurate
        public bool IsNullStripe => StripeType == "Null-Gap";
        public bool IsVjosTarget => HvTarget == "HV_TO_VJOS";
        public bool IsPrintTarget => HvTarget == "HV_TO_PRINT";
        public bool IsNullTarget => HvTarget == "HV_TO_NULL";

        // For HV mismatch detection (simplified: null stripe should have HV_TO_NULL)
        public bool IsHvMismatch => IsNullStripe && !IsNullTarget;
    }
}
