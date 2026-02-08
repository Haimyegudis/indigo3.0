using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IndiLogs_3._0.Models.Cpr;

namespace IndiLogs_3._0.Services.Cpr
{
    public class CprDataService
    {
        private List<CprRecord> _allRecords = new List<CprRecord>();
        public bool IsLoaded => _allRecords.Count > 0;

        // Revolution index mapping (matching Python app)
        private static readonly Dictionary<string, string> RevolutionMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "RevolutionOneOnly", "One Only" },
            { "RevolutionFirstOfMany", "First of Many" },
            { "RevolutionLastOfMany", "Last of Many" },
            { "RevolutionMiddle", "Middle of Many" },
            { "RevType", "RevType" }
        };

        // Column name normalization (lowercase → camelCase, matching Python)
        private static readonly Dictionary<string, string> ColumnMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "iterationnum", "IterationNum" },
            { "cyclenumber", "CycleNumber" },
            { "elementlocationx", "ElementLocationX" },
            { "elementlocationy", "ElementLocationY" },
            { "elementlocationpixelx", "ElementLocationPixelX" },
            { "elementlocationpixely", "ElementLocationPixelY" },
            { "registrationdatastationx1", "RegistrationDataStationX1" },
            { "registrationdatastationx2", "RegistrationDataStationX2" },
            { "registrationdatastationx3", "RegistrationDataStationX3" },
            { "registrationdatastationx4", "RegistrationDataStationX4" },
            { "registrationdatastationx5", "RegistrationDataStationX5" },
            { "registrationdatastationx6", "RegistrationDataStationX6" },
            { "registrationdatastationy1", "RegistrationDataStationY1" },
            { "registrationdatastationy2", "RegistrationDataStationY2" },
            { "registrationdatastationy3", "RegistrationDataStationY3" },
            { "registrationdatastationy4", "RegistrationDataStationY4" },
            { "registrationdatastationy5", "RegistrationDataStationY5" },
            { "registrationdatastationy6", "RegistrationDataStationY6" },
            { "revolutionindex", "RevolutionIndex" },
            { "startcalibrationtime", "StartCalibrationTime" },
            { "sn", "sn" }
        };

        public void LoadCsv(string filePath)
        {
            _allRecords.Clear();
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return;

            // Parse header — normalize column names
            var rawHeaders = lines[0].Split(',');
            var headers = new string[rawHeaders.Length];
            for (int i = 0; i < rawHeaders.Length; i++)
            {
                string h = rawHeaders[i].Trim().Trim('"');
                headers[i] = ColumnMapping.ContainsKey(h) ? ColumnMapping[h] : h;
            }

            // Build column index map
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                colMap[headers[i]] = i;

            for (int row = 1; row < lines.Length; row++)
            {
                var parts = lines[row].Split(',');
                if (parts.Length < headers.Length) continue;

                var rec = new CprRecord();

                rec.SN = colMap.ContainsKey("sn") ? ParseInt(parts[colMap["sn"]]) : 0;
                rec.IterationNum = colMap.ContainsKey("IterationNum") ? ParseInt(parts[colMap["IterationNum"]]) : 0;
                rec.CycleNumber = colMap.ContainsKey("CycleNumber") ? ParseInt(parts[colMap["CycleNumber"]]) : 0;
                rec.ElementLocationX = colMap.ContainsKey("ElementLocationX") ? ParseDouble(parts[colMap["ElementLocationX"]]) : 0;
                rec.ElementLocationY = colMap.ContainsKey("ElementLocationY") ? ParseDouble(parts[colMap["ElementLocationY"]]) : 0;
                rec.ElementLocationPixelX = colMap.ContainsKey("ElementLocationPixelX") ? ParseDouble(parts[colMap["ElementLocationPixelX"]]) : 0;
                rec.ElementLocationPixelY = colMap.ContainsKey("ElementLocationPixelY") ? ParseDouble(parts[colMap["ElementLocationPixelY"]]) : 0;

                // Station data: X0=0 (reference), X1-X6
                rec.StationX[0] = 0;
                rec.StationY[0] = 0;
                for (int s = 1; s <= 6; s++)
                {
                    string kx = "RegistrationDataStationX" + s;
                    string ky = "RegistrationDataStationY" + s;
                    rec.StationX[s] = colMap.ContainsKey(kx) ? ParseDouble(parts[colMap[kx]]) : 0;
                    rec.StationY[s] = colMap.ContainsKey(ky) ? ParseDouble(parts[colMap[ky]]) : 0;
                }

                // Revolution
                string revRaw = colMap.ContainsKey("RevolutionIndex") ? parts[colMap["RevolutionIndex"]].Trim().Trim('"') : "";
                rec.RevolutionIndex = revRaw;
                rec.Revolution = RevolutionMapping.ContainsKey(revRaw) ? RevolutionMapping[revRaw] : revRaw;

                // Calibration time
                string timeRaw = colMap.ContainsKey("StartCalibrationTime") ? parts[colMap["StartCalibrationTime"]].Trim().Trim('"') : "";
                rec.StartCalibrationTime = FormatCalibrationTime(timeRaw);

                _allRecords.Add(rec);
            }
        }

        private string FormatCalibrationTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            // Handle ISO 8601 with timezone offset (e.g. 2026-01-26T14:50:41.1578298+02:00)
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset dto))
                return dto.ToString("MMMM dd, yyyy hh:mm:ss tt zzz", CultureInfo.InvariantCulture);
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                return dt.ToString("MMMM dd, yyyy hh:mm:ss tt", CultureInfo.InvariantCulture);
            return raw;
        }

        private int ParseInt(string s)
        {
            s = s.Trim().Trim('"');
            if (int.TryParse(s, out int v)) return v;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return (int)d;
            return 0;
        }

        private double ParseDouble(string s)
        {
            s = s.Trim().Trim('"');
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
            return 0;
        }

        public int[] GetMachineNumbers()
        {
            return _allRecords.Select(r => r.SN).Distinct().OrderBy(x => x).ToArray();
        }

        public string[] GetCalibrationTimes(int sn)
        {
            return _allRecords.Where(r => r.SN == sn)
                .Select(r => r.StartCalibrationTime)
                .Distinct().OrderBy(x => x).ToArray();
        }

        public string[] GetRevolutions(int sn, string calibTime)
        {
            return FilterBase(sn, calibTime)
                .Select(r => r.Revolution).Distinct().OrderBy(x => x).ToArray();
        }

        public int[] GetIterations(int sn, string calibTime)
        {
            return FilterBase(sn, calibTime)
                .Select(r => r.IterationNum).Distinct().OrderBy(x => x).ToArray();
        }

        public int[] GetCycles(int sn, string calibTime)
        {
            return FilterBase(sn, calibTime)
                .Select(r => r.CycleNumber).Distinct().OrderBy(x => x).ToArray();
        }

        public int[] GetColumns(int sn, string calibTime)
        {
            return FilterBase(sn, calibTime)
                .Select(r => (int)r.ElementLocationX).Distinct().OrderBy(x => x).ToArray();
        }

        private IEnumerable<CprRecord> FilterBase(int sn, string calibTime)
        {
            var q = _allRecords.AsEnumerable();
            q = q.Where(r => r.SN == sn);
            if (!string.IsNullOrEmpty(calibTime))
                q = q.Where(r => r.StartCalibrationTime == calibTime);
            return q;
        }

        public List<CprRecord> ApplyFilters(CprFilterState f)
        {
            var q = _allRecords.AsEnumerable();
            q = q.Where(r => r.SN == f.MachineSN);
            if (!string.IsNullOrEmpty(f.CalibrationTime))
                q = q.Where(r => r.StartCalibrationTime == f.CalibrationTime);
            if (!string.IsNullOrEmpty(f.Revolution))
                q = q.Where(r => r.Revolution == f.Revolution);
            q = q.Where(r => r.IterationNum == f.Iteration);

            int cycFrom = Math.Min(f.CycleFrom, f.CycleTo);
            int cycTo = Math.Max(f.CycleFrom, f.CycleTo);
            q = q.Where(r => r.CycleNumber >= cycFrom && r.CycleNumber <= cycTo);

            int colFrom = Math.Min(f.ColumnFrom, f.ColumnTo);
            int colTo = Math.Max(f.ColumnFrom, f.ColumnTo);
            q = q.Where(r => r.ElementLocationX >= colFrom && r.ElementLocationX <= colTo);

            return q.ToList();
        }

        /// <summary>
        /// Apply filters but without cycle/column range (for stats that don't use them)
        /// </summary>
        public List<CprRecord> ApplyBaseFilters(CprFilterState f)
        {
            var q = _allRecords.AsEnumerable();
            q = q.Where(r => r.SN == f.MachineSN);
            if (!string.IsNullOrEmpty(f.CalibrationTime))
                q = q.Where(r => r.StartCalibrationTime == f.CalibrationTime);
            q = q.Where(r => r.IterationNum == f.Iteration);
            if (!string.IsNullOrEmpty(f.Revolution))
                q = q.Where(r => r.Revolution == f.Revolution);
            return q.ToList();
        }

        /// <summary>
        /// Get station value (X or Y) for a record
        /// </summary>
        public static double GetStationValue(CprRecord rec, string axis, int station)
        {
            if (axis == "Y") return rec.StationY[station];
            return rec.StationX[station];
        }
    }
}
