using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Parses Indigo stripe/slice JSON data from log entries
    /// </summary>
    public class StripeDataParserService
    {

        /// <summary>
        /// Parses stripe data from log entries - optimized version
        /// </summary>
        public List<IndigoStripeEntry> ParseFromLogs(IEnumerable<LogEntry> logs)
        {
            var results = new List<IndigoStripeEntry>();
            var logsList = logs.ToList();

            // Pre-filter: only logs that might contain stripe data
            var candidates = logsList.Where(log =>
                (!string.IsNullOrEmpty(log.Data) && log.Data.Contains("stripeDescriptor")) ||
                (!string.IsNullOrEmpty(log.Message) && log.Message.Contains("stripeDescriptor"))
            ).ToList();

            Debug.WriteLine($"[StripeParser] Found {candidates.Count} candidate logs out of {logsList.Count}");

            foreach (var log in candidates)
            {
                string jsonString = ExtractValidJson(log);
                if (string.IsNullOrEmpty(jsonString))
                    continue;

                try
                {
                    var entries = ParseStripeJson(jsonString, log.Date);
                    results.AddRange(entries);
                }
                catch (JsonException)
                {
                    // Silently skip invalid JSON
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[StripeParser] Unexpected error: {ex.Message}");
                }
            }

            Debug.WriteLine($"[StripeParser] Parsed {results.Count} stripe entries");
            return results;
        }

        /// <summary>
        /// Parses stripe data directly from JSON string
        /// </summary>
        public List<IndigoStripeEntry> ParseFromJson(string jsonString, DateTime? timestamp = null)
        {
            return ParseStripeJson(jsonString, timestamp ?? DateTime.Now);
        }

        /// <summary>
        /// Extract valid JSON that contains stripeDescriptor
        /// </summary>
        private string ExtractValidJson(LogEntry log)
        {
            // First try the Data field - it's more likely to have clean JSON
            if (!string.IsNullOrEmpty(log.Data) && log.Data.Contains("stripeDescriptor"))
            {
                string json = ExtractJsonObject(log.Data);
                if (json != null && IsValidJson(json))
                    return json;
            }

            // Then try the Message field
            if (!string.IsNullOrEmpty(log.Message) && log.Message.Contains("stripeDescriptor"))
            {
                string json = ExtractJsonObject(log.Message);
                if (json != null && IsValidJson(json))
                    return json;
            }

            return null;
        }

        /// <summary>
        /// Extract a complete JSON object from text by matching braces
        /// </summary>
        private string ExtractJsonObject(string text)
        {
            int startIndex = text.IndexOf('{');
            if (startIndex < 0)
                return null;

            int depth = 0;
            int endIndex = -1;

            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = i;
                        break;
                    }
                }
            }

            if (endIndex > startIndex)
            {
                return text.Substring(startIndex, endIndex - startIndex + 1);
            }

            return null;
        }

        /// <summary>
        /// Quick validation check for JSON
        /// </summary>
        private bool IsValidJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("stripeDescriptor", out _);
            }
            catch
            {
                return false;
            }
        }

        private List<IndigoStripeEntry> ParseStripeJson(string jsonString, DateTime timestamp)
        {
            var entries = new List<IndigoStripeEntry>();

            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            // Get stripeDescriptor
            if (!root.TryGetProperty("stripeDescriptor", out JsonElement stripeDesc))
                return entries;

            // Common stripe data
            int spreadId = GetIntProperty(stripeDesc, "parentSpreadId");
            int stripeId = GetIntProperty(stripeDesc, "stripeId");
            bool isNull = GetBoolProperty(stripeDesc, "nullStripe");
            double lenUm = GetDoubleProperty(stripeDesc, "stripeLenUm");
            double lenMm = Math.Round(lenUm / 1000.0, 2);
            double lenNotScaledUm = GetDoubleProperty(stripeDesc, "stripeLenNotScaledUm");
            double lenNotScaledMm = Math.Round(lenNotScaledUm / 1000.0, 2);
            bool lastStripeInSpread = GetBoolProperty(stripeDesc, "lastStripeInSpread");
            bool imageToBru = GetBoolProperty(stripeDesc, "imageToBru");
            string dataTransferControl = GetStringProperty(stripeDesc, "dataTransferControl");
            bool reportPrintDetails = GetBoolProperty(stripeDesc, "reportPrintDetails");
            int reportId = GetIntProperty(stripeDesc, "reportId");
            int nSliceGroups = GetIntProperty(stripeDesc, "nSliceGroups");

            // Blanket Loop Data from stripeDescriptor
            double webRepeatLenScalingFactor = GetDoubleProperty(stripeDesc, "webRepeatLenScalingFactor");
            int blanketLoopRepeatLenUm = GetIntProperty(stripeDesc, "blanketLoopRepeatLenUm");
            int blanketLoopT2TotalLenUm = GetIntProperty(stripeDesc, "blanketLoopT2TotalLenUm");
            bool firstInBlanketLoop = GetBoolProperty(stripeDesc, "firstInBlanketLoop");
            bool lastInBlanketLoop = GetBoolProperty(stripeDesc, "lastInBlanketLoop");
            int startPosInBlanketLoopUm = GetIntProperty(stripeDesc, "startPosInBlanketLoopUm");

            // SPM data from root
            string spmStatus = "None";
            int spmMeasureId = 0;
            string spmScanDirection = null;
            string spmMeasureMode = null;
            int spmNumOfStrips = 0;
            if (root.TryGetProperty("SpmMeasureInstruction", out JsonElement spmElement))
            {
                if (GetBoolProperty(spmElement, "IsActive"))
                {
                    spmStatus = "Active";
                    spmMeasureId = GetIntProperty(spmElement, "MeasureId");
                    spmScanDirection = GetStringProperty(spmElement, "ScanDirection");
                    spmMeasureMode = GetStringProperty(spmElement, "MeasureMode");
                    spmNumOfStrips = GetIntProperty(spmElement, "NumOfStrips");
                }
            }

            // ILS data from root
            bool ilsIsActive = false;
            int ilsScanLenUm = 0;
            string ilsScanMode = null;
            int ilsScanSpeedUmSec = 0;
            if (root.TryGetProperty("IlsMeasureInstruction", out JsonElement ilsElement))
            {
                ilsIsActive = GetBoolProperty(ilsElement, "IsActive");
                ilsScanLenUm = GetIntProperty(ilsElement, "ScanLenUm");
                ilsScanMode = GetStringProperty(ilsElement, "ScanMode");
                ilsScanSpeedUmSec = GetIntProperty(ilsElement, "ScanSpeedUmSec");
            }

            // Parse sliceGroups
            if (stripeDesc.TryGetProperty("sliceGroups", out JsonElement sliceGroups))
            {
                int groupIndex = 0;
                foreach (var groupProp in sliceGroups.EnumerateObject())
                {
                    if (!groupProp.Name.StartsWith("#"))
                        continue;

                    // Try to get slices from this group
                    if (!groupProp.Value.TryGetProperty("slices", out JsonElement slices))
                        continue;

                    int sliceIndex = 0;
                    foreach (var sliceProp in slices.EnumerateObject())
                    {
                        if (!sliceProp.Name.StartsWith("#"))
                            continue;

                        var slice = sliceProp.Value;
                        var entry = CreateEntryFromSlice(
                            slice, timestamp, spreadId, stripeId,
                            isNull, lenMm, lenNotScaledMm, groupIndex, sliceIndex,
                            lastStripeInSpread, imageToBru, dataTransferControl,
                            reportPrintDetails, reportId, nSliceGroups,
                            webRepeatLenScalingFactor, blanketLoopRepeatLenUm, blanketLoopT2TotalLenUm,
                            firstInBlanketLoop, lastInBlanketLoop, startPosInBlanketLoopUm,
                            spmStatus, spmMeasureId, spmScanDirection, spmMeasureMode, spmNumOfStrips,
                            ilsIsActive, ilsScanLenUm, ilsScanMode, ilsScanSpeedUmSec);

                        if (entry != null)
                            entries.Add(entry);

                        sliceIndex++;
                    }
                    groupIndex++;
                }
            }

            // If no slices found, create a single entry for the stripe itself
            if (entries.Count == 0)
            {
                entries.Add(new IndigoStripeEntry
                {
                    Timestamp = timestamp,
                    SpreadId = spreadId,
                    StripeId = stripeId,
                    StripeType = isNull ? "Null-Gap" : "Print-Image",
                    LengthMm = lenMm,
                    LengthNotScaledMm = lenNotScaledMm,
                    LastStripeInSpread = lastStripeInSpread,
                    ImageToBru = imageToBru,
                    DataTransferControl = dataTransferControl,
                    ReportPrintDetails = reportPrintDetails,
                    ReportId = reportId,
                    NSliceGroups = nSliceGroups,
                    WebRepeatLenScalingFactor = webRepeatLenScalingFactor,
                    BlanketLoopRepeatLenUm = blanketLoopRepeatLenUm,
                    BlanketLoopT2TotalLenUm = blanketLoopT2TotalLenUm,
                    FirstInBlanketLoop = firstInBlanketLoop,
                    LastInBlanketLoop = lastInBlanketLoop,
                    StartPosInBlanketLoopUm = startPosInBlanketLoopUm,
                    SpmStatus = spmStatus,
                    SpmMeasureId = spmMeasureId,
                    SpmScanDirection = spmScanDirection,
                    SpmMeasureMode = spmMeasureMode,
                    SpmNumOfStrips = spmNumOfStrips,
                    IlsIsActive = ilsIsActive,
                    IlsScanLenUm = ilsScanLenUm,
                    IlsScanMode = ilsScanMode,
                    IlsScanSpeedUmSec = ilsScanSpeedUmSec,
                    SliceGroupIndex = -1,
                    SliceIndex = -1,
                    IsStationActive = true
                });
            }

            return entries;
        }

        private IndigoStripeEntry CreateEntryFromSlice(
            JsonElement slice, DateTime timestamp, int spreadId, int stripeId,
            bool isNull, double lenMm, double lenNotScaledMm, int groupIndex, int sliceIndex,
            bool lastStripeInSpread, bool imageToBru, string dataTransferControl,
            bool reportPrintDetails, int reportId, int nSliceGroups,
            double webRepeatLenScalingFactor, int blanketLoopRepeatLenUm, int blanketLoopT2TotalLenUm,
            bool firstInBlanketLoop, bool lastInBlanketLoop, int startPosInBlanketLoopUm,
            string spmStatus, int spmMeasureId, string spmScanDirection, string spmMeasureMode, int spmNumOfStrips,
            bool ilsIsActive, int ilsScanLenUm, string ilsScanMode, int ilsScanSpeedUmSec)
        {
            try
            {
                // Slice identification
                int sliceId = GetIntProperty(slice, "sliceId");
                int sliceStamp = GetIntProperty(slice, "sliceStamp");
                int parentSeparationId = GetIntProperty(slice, "parentSeparationId");
                int inkId = GetIntProperty(slice, "inkId");
                bool isActive = GetBoolProperty(slice, "stationIsActive");

                // Position data
                int startPos = GetIntProperty(slice, "startPosUm");
                int endPos = GetIntProperty(slice, "endPosUm");

                // HV target
                string hvTarget = GetStringProperty(slice, "hvTargetType") ?? "Unknown";

                // BID data (Binary Image Developer)
                int vDev = 0, vElec = 0, vSqueegee = 0, vCleaner = 0;
                if (slice.TryGetProperty("bidData", out JsonElement bidData))
                {
                    vDev = GetIntProperty(bidData, "vDeveloper");
                    vElec = GetIntProperty(bidData, "vElectrode");
                    vSqueegee = GetIntProperty(bidData, "vSqueegee");
                    vCleaner = GetIntProperty(bidData, "vCleaner");
                }

                // CR data (Charge Roller)
                int crVDc = 0, crVAc = 0;
                if (slice.TryGetProperty("crData", out JsonElement crData))
                {
                    crVDc = GetIntProperty(crData, "vDc");
                    crVAc = GetIntProperty(crData, "vAc");
                }

                // ASID data
                int vAsid = 0;
                if (slice.TryGetProperty("asidData", out JsonElement asidData))
                {
                    vAsid = GetIntProperty(asidData, "VAsid");
                }

                // LPH data (Laser Print Head)
                int nScanLines = 0;
                if (slice.TryGetProperty("lphData", out JsonElement lphData))
                {
                    nScanLines = GetIntProperty(lphData, "nScanLines");
                }

                // EM data (Electrostatic Measurement)
                bool emIsActive = false;
                int emMeasureId = 0;
                if (slice.TryGetProperty("emData", out JsonElement emData))
                {
                    emIsActive = GetBoolProperty(emData, "isActive");
                    emMeasureId = GetIntProperty(emData, "measureId");
                }

                return new IndigoStripeEntry
                {
                    // Basic info
                    Timestamp = timestamp,
                    SpreadId = spreadId,
                    StripeId = stripeId,
                    StripeType = isNull ? "Null-Gap" : "Print-Image",
                    LengthMm = lenMm,
                    LengthNotScaledMm = lenNotScaledMm,

                    // Slice location
                    SliceGroupIndex = groupIndex,
                    SliceIndex = sliceIndex,
                    SliceId = sliceId,
                    SliceStamp = sliceStamp,
                    ParentSeparationId = parentSeparationId,

                    // Ink info - just use the raw inkId, no name mapping
                    InkId = inkId,
                    InkName = null, // No name mapping - display inkId directly
                    IsStationActive = isActive,

                    // Position
                    StartPosUm = startPos,
                    EndPosUm = endPos,

                    // HV
                    HvTarget = hvTarget,

                    // BID data
                    VDeveloper = vDev,
                    VElectrode = vElec,
                    VSqueegee = vSqueegee,
                    VCleaner = vCleaner,

                    // CR data
                    CrVDc = crVDc,
                    CrVAc = crVAc,

                    // ASID data
                    VAsid = vAsid,

                    // LPH data
                    NScanLines = nScanLines,

                    // EM data
                    EmIsActive = emIsActive,
                    EmMeasureId = emMeasureId,

                    // Stripe-level data passed down
                    LastStripeInSpread = lastStripeInSpread,
                    ImageToBru = imageToBru,
                    DataTransferControl = dataTransferControl,
                    ReportPrintDetails = reportPrintDetails,
                    ReportId = reportId,
                    NSliceGroups = nSliceGroups,

                    // Blanket Loop data
                    WebRepeatLenScalingFactor = webRepeatLenScalingFactor,
                    BlanketLoopRepeatLenUm = blanketLoopRepeatLenUm,
                    BlanketLoopT2TotalLenUm = blanketLoopT2TotalLenUm,
                    FirstInBlanketLoop = firstInBlanketLoop,
                    LastInBlanketLoop = lastInBlanketLoop,
                    StartPosInBlanketLoopUm = startPosInBlanketLoopUm,

                    // SPM data
                    SpmStatus = spmStatus,
                    SpmMeasureId = spmMeasureId,
                    SpmScanDirection = spmScanDirection,
                    SpmMeasureMode = spmMeasureMode,
                    SpmNumOfStrips = spmNumOfStrips,

                    // ILS data
                    IlsIsActive = ilsIsActive,
                    IlsScanLenUm = ilsScanLenUm,
                    IlsScanMode = ilsScanMode,
                    IlsScanSpeedUmSec = ilsScanSpeedUmSec
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StripeParser] Error creating entry from slice: {ex.Message}");
                return null;
            }
        }

        #region JSON Helper Methods

        private int GetIntProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetInt32();
            }
            return 0;
        }

        private double GetDoubleProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDouble();
            }
            return 0;
        }

        private bool GetBoolProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.True)
                    return true;
                if (prop.ValueKind == JsonValueKind.False)
                    return false;
            }
            return false;
        }

        private string GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }
            return null;
        }

        #endregion
    }
}
