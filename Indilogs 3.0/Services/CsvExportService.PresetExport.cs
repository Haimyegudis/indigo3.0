using IndiLogs_3._0.Models;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    public partial class CsvExportService
    {
        private async Task<string?> ExportLogsWithPresetAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{defaultFileName}_Filtered.csv",
                InitialDirectory = AppPaths.Root
            };

            if (saveFileDialog.ShowDialog() != true) return null;

            string filePath = saveFileDialog.FileName;

            // Show progress window (NON-MODAL)
            var progressWindow = new ExportProgressWindow();
            progressWindow.Show(); // NON-MODAL - allows user to continue working

            var progressReporter = new ProgressReporter(progressWindow);

            // Run export in background - don't block UI
            _ = Task.Run(() =>
            {
                try
                {
                    ExportWithForwardFill(logs, filePath, preset, progressReporter);
                    progressWindow.Complete(true, $"Saved to:\n{Path.GetFileName(filePath)}");
                }
                catch (OperationCanceledException)
                {
                    progressWindow.Complete(false, "Export cancelled by user");
                }
                catch (Exception ex)
                {
                    progressWindow.Complete(false, $"Error: {ex.Message}");
                }
            });

            // Return file path immediately - export continues in background
            return filePath;
        }

        // ===================================================================
        // ULTRA-OPTIMIZED PARSING HELPERS - NO REGEX!
        // ===================================================================

        // Fast PlcMngr state parsing
        private static bool TryParsePlcMngrState(string msg, out string? stateName)
        {
            stateName = null;
            // CHStep: PlcMngr, STATE_NAME, ...
            // IMPORTANT: PlcMngr must be the CHName, not the Parent!

            if (string.IsNullOrEmpty(msg) || msg.Length < 20) return false;

            // Must start with "CHStep:"
            if (!msg.StartsWith("CHStep:", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                // Find first comma after "CHStep:"
                int chStepEnd = 7; // "CHStep:".Length
                int firstComma = msg.IndexOf(',', chStepEnd);
                if (firstComma < 0) return false;

                // Extract CHName (between "CHStep:" and first comma)
                string chName = msg.Substring(chStepEnd, firstComma - chStepEnd).Trim();

                // CRITICAL: Only proceed if CHName is "PlcMngr"
                if (!chName.Equals("PlcMngr", StringComparison.OrdinalIgnoreCase)) return false;

                // Find second comma (after state name)
                int secondComma = msg.IndexOf(',', firstComma + 1);
                if (secondComma < 0)
                {
                    // Try to find " State " instead
                    int statePos = msg.IndexOf(" State ", firstComma, StringComparison.OrdinalIgnoreCase);
                    if (statePos > 0)
                        secondComma = statePos;
                    else
                        return false;
                }

                // Extract state name between first and second comma
                stateName = msg.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();

                return !string.IsNullOrEmpty(stateName);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"StateChange parse failed: {ex.Message}");
                return false;
            }
        }

        // Fast CHStep parsing - replaces complex Regex
        private static bool TryParseCHStep(string msg, out string? chName, out string? stepMessage, out string? stateId,
            out string? chParentName, out string? subsysID, out string? prevStepNo, out string? diffTime, out string? subStepNo, out string? chObjType)
        {
            chName = stepMessage = stateId = chParentName = subsysID = prevStepNo = diffTime = subStepNo = chObjType = null;

            // CHStep: CHName, StepMessage, State X <Parent, SubsysID, PrevStepNo, DiffTime, SubStepNo, CHObjType>
            if (msg.Length < 30) return false;

            try
            {
                // Find first comma after "CHStep:"
                int stepIndex = msg.IndexOf("CHStep:", StringComparison.OrdinalIgnoreCase);
                if (stepIndex < 0) return false;

                int startPos = stepIndex + 7; // "CHStep:".Length
                while (startPos < msg.Length && char.IsWhiteSpace(msg[startPos])) startPos++;

                // Extract CHName
                int comma1 = msg.IndexOf(',', startPos);
                if (comma1 < 0) return false;
                chName = msg.Substring(startPos, comma1 - startPos).Trim();

                // Extract StepMessage
                int comma2Start = comma1 + 1;
                while (comma2Start < msg.Length && char.IsWhiteSpace(msg[comma2Start])) comma2Start++;
                int comma2 = msg.IndexOf(',', comma2Start);
                if (comma2 < 0) return false;
                stepMessage = msg.Substring(comma2Start, comma2 - comma2Start).Trim();

                // Extract State
                int stateStart = msg.IndexOf("State ", comma2, StringComparison.OrdinalIgnoreCase);
                if (stateStart < 0) return false;
                stateStart += 6; // "State ".Length
                while (stateStart < msg.Length && char.IsWhiteSpace(msg[stateStart])) stateStart++;

                int stateEnd = stateStart;
                while (stateEnd < msg.Length && char.IsDigit(msg[stateEnd])) stateEnd++;
                if (stateEnd == stateStart) return false;
                stateId = msg.Substring(stateStart, stateEnd - stateStart);

                // Find < >
                int openBracket = msg.IndexOf('<', stateEnd);
                if (openBracket < 0) return false;

                int closeBracket = msg.IndexOf('>', openBracket);
                if (closeBracket < 0) return false;

                // Parse content inside < >
                string bracketContent = msg.Substring(openBracket + 1, closeBracket - openBracket - 1);
                string[] parts = bracketContent.Split(',');
                if (parts.Length < 6) return false;

                chParentName = parts[0].Trim();
                subsysID = parts[1].Trim();
                prevStepNo = parts[2].Trim();
                diffTime = parts[3].Trim();
                subStepNo = parts[4].Trim();
                chObjType = parts[5].Trim();

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"CHStep parse failed: {ex.Message}");
                return false;
            }
        }

        // Fast LogStats parsing - replaces multiple Regex
        private static bool TryParseLogStats(string msg, out string? total, out string? isReady, out string? semTotal,
            out string? semMult, out string? lost, out string? bufFull, out string? maxNum, out string? maxCat)
        {
            total = isReady = semTotal = semMult = lost = bufFull = maxNum = maxCat = null;

            // LogStat: Logs(Total=X IsReady=Y) nSemMissed(total=Z Mult=W) Lost=L bufFull=B Max(num=N cat=C)
            if (!msg.StartsWith("LogStat:", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                // Parse Logs(Total=X IsReady=Y)
                int logsStart = msg.IndexOf("Logs(Total=");
                if (logsStart >= 0)
                {
                    int totalStart = logsStart + 11; // "Logs(Total=".Length
                    int totalEnd = msg.IndexOf(' ', totalStart);
                    if (totalEnd > totalStart)
                        total = msg.Substring(totalStart, totalEnd - totalStart);

                    int isReadyStart = msg.IndexOf("IsReady=", totalEnd);
                    if (isReadyStart > 0)
                    {
                        isReadyStart += 8; // "IsReady=".Length
                        int isReadyEnd = msg.IndexOf(')', isReadyStart);
                        if (isReadyEnd > isReadyStart)
                            isReady = msg.Substring(isReadyStart, isReadyEnd - isReadyStart);
                    }
                }

                // Parse nSemMissed(total=Z Mult=W)
                int semStart = msg.IndexOf("nSemMissed(total=");
                if (semStart >= 0)
                {
                    int semTotalStart = semStart + 17; // "nSemMissed(total=".Length
                    int semTotalEnd = msg.IndexOf(' ', semTotalStart);
                    if (semTotalEnd > semTotalStart)
                        semTotal = msg.Substring(semTotalStart, semTotalEnd - semTotalStart);

                    int multStart = msg.IndexOf("Mult=", semTotalEnd);
                    if (multStart > 0)
                    {
                        multStart += 5; // "Mult=".Length
                        int multEnd = msg.IndexOf(')', multStart);
                        if (multEnd > multStart)
                            semMult = msg.Substring(multStart, multEnd - multStart);
                    }
                }

                // Parse Lost=L
                int lostStart = msg.IndexOf("Lost=");
                if (lostStart >= 0)
                {
                    lostStart += 5; // "Lost=".Length
                    int lostEnd = lostStart;
                    while (lostEnd < msg.Length && char.IsDigit(msg[lostEnd])) lostEnd++;
                    if (lostEnd > lostStart)
                        lost = msg.Substring(lostStart, lostEnd - lostStart);
                }

                // Parse bufFull=B
                int bufFullStart = msg.IndexOf("bufFull=");
                if (bufFullStart >= 0)
                {
                    bufFullStart += 8; // "bufFull=".Length
                    int bufFullEnd = bufFullStart;
                    while (bufFullEnd < msg.Length && char.IsDigit(msg[bufFullEnd])) bufFullEnd++;
                    if (bufFullEnd > bufFullStart)
                        bufFull = msg.Substring(bufFullStart, bufFullEnd - bufFullStart);
                }

                // Parse Max(num=N cat=C)
                int maxStart = msg.IndexOf("Max(num=");
                if (maxStart >= 0)
                {
                    int maxNumStart = maxStart + 8; // "Max(num=".Length
                    int maxNumEnd = msg.IndexOf(' ', maxNumStart);
                    if (maxNumEnd > maxNumStart)
                        maxNum = msg.Substring(maxNumStart, maxNumEnd - maxNumStart);

                    int catStart = msg.IndexOf("cat=", maxNumEnd);
                    if (catStart > 0)
                    {
                        catStart += 4; // "cat=".Length
                        int catEnd = msg.IndexOf(')', catStart);
                        if (catEnd > catStart)
                            maxCat = msg.Substring(catStart, catEnd - catStart);
                    }
                }

                return !string.IsNullOrEmpty(total);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"LogStats parse failed: {ex.Message}");
                return false;
            }
        }
    }
}
