using System;
using System.IO;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Shared log file classification logic used by GlobalGrepService and LogStatisticsService.
    /// Determines whether a file is a PLC log, APP log, or neither.
    /// </summary>
    internal static class LogFileClassifier
    {
        public static bool IsLogFile(string path, bool plc, bool app)
        {
            string lp = path.ToLowerInvariant();
            if (lp.EndsWith(".zip")) return true;
            return IsSearchableLogFile(lp, plc, app);
        }

        public static bool IsLogEntry(string entryName, bool plc, bool app)
        {
            string lp = entryName.ToLowerInvariant();
            if (lp.EndsWith(".zip")) return false;
            return IsSearchableLogFile(lp, plc, app);
        }

        public static bool IsSearchableLogFile(string lp, bool plc, bool app)
        {
            string fileName = lp;
            int lastSlash = lp.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSlash >= 0) fileName = lp.Substring(lastSlash + 1);

            bool isPLC = fileName.Contains("enginegroupa.file") ||
                         fileName.Contains("enginegroupb.file") ||
                         fileName.EndsWith(".file.log") ||
                         (fileName.Contains("no-sn") && fileName.Contains("file"));

            bool isAPP = fileName.Contains("appdev") || fileName.Contains("press.host.app");
            if (!isAPP) isAPP = IsNumericAppFileName(fileName);

            return (plc && isPLC) || (app && isAPP);
        }

        public static bool IsNumericAppFileName(string lowerFileName)
        {
            if (lowerFileName.Contains("enginegroup")) return false;
            int dotFileIdx = lowerFileName.IndexOf(".file");
            if (dotFileIdx <= 0) return false;
            string prefix = lowerFileName.Substring(0, dotFileIdx);
            return prefix.Length > 0 && char.IsDigit(prefix[prefix.Length - 1]);
        }

        public static string DetermineLogType(string path)
        {
            string lp = path.ToLowerInvariant();
            string fileName = Path.GetFileName(lp);
            if (fileName.Contains("appdev") || fileName.Contains("press.host.app")) return "APP";
            if (IsNumericAppFileName(fileName)) return "APP";
            return "PLC";
        }
    }
}
