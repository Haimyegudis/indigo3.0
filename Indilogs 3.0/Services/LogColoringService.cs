using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace IndiLogs_3._0.Services
{
    public class LogColoringService
    {
        // Cache compiled Regex to avoid recompiling every line
        private readonly ConcurrentDictionary<string, Regex> _regexCache = new ConcurrentDictionary<string, Regex>();

        // User-configurable default coloring rules (loaded from _defaults.json)
        public List<ColoringCondition> UserDefaultMainRules { get; set; }
        public List<ColoringCondition> UserDefaultAppRules { get; set; }

        /// <summary>
        /// Applies default colors. Uses user-configured rules if available, otherwise falls back to factory defaults.
        /// </summary>
        public async Task ApplyDefaultColorsAsync(IEnumerable<LogEntry> logs, bool isAppLog)
        {
            var userRules = isAppLog ? UserDefaultAppRules : UserDefaultMainRules;

            if (userRules != null && userRules.Count > 0)
            {
                // User has custom default coloring — reset all colors then apply rules
                await Task.Run(() =>
                {
                    Parallel.ForEach(logs, log =>
                    {
                        log.CustomColor = null;
                        log.IsErrorOrEvents = false;
                        log.RowForeground = null;

                        // Always mark errors for red text regardless of user rules
                        if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                        {
                            log.IsErrorOrEvents = true;
                        }
                    });
                });
                await ApplyCustomColoringAsync(logs, userRules);
            }
            else
            {
                // No user defaults — use factory hardcoded logic
                await ApplyFactoryDefaultColorsAsync(logs, isAppLog);
            }
        }

        /// <summary>
        /// Factory-default coloring logic (original hardcoded rules).
        /// </summary>
        private async Task ApplyFactoryDefaultColorsAsync(IEnumerable<LogEntry> logs, bool isAppLog)
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(logs, log =>
                {
                    // 1. Reset existing colors
                    log.CustomColor = null;
                    log.IsErrorOrEvents = false;
                    log.RowForeground = null;

                    // 2. PipelineCancellationProvider errors -> Strong Orange with Black text
                    if (isAppLog &&
                        string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase) &&
                        log.Logger != null &&
                        log.Logger.Contains("Press.BL.Printing.Pipeline.PipelineCancellationProvider"))
                    {
                        log.CustomColor = Color.FromRgb(255, 140, 0); // Strong Orange
                        log.RowForeground = Brushes.Black;
                        return;
                    }

                    // 2b. PressStateManager + FallToPressStateAsync -> Orange
                    if (isAppLog &&
                        log.Logger != null &&
                        log.Logger.Contains("PressStateManager") &&
                        log.Method != null &&
                        log.Method.Contains("FallToPressStateAsync"))
                    {
                        log.CustomColor = Color.FromRgb(255, 165, 0); // Orange
                        log.RowForeground = Brushes.Black;
                        return;
                    }

                    // 3. Error -> Red text
                    if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        log.IsErrorOrEvents = true;
                        return;
                    }

                    // 4. APP logs stop here
                    if (isAppLog) return;

                    // 5. PLC-only rules
                    if (string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                    {
                        log.IsErrorOrEvents = true;
                    }
                    else if (string.Equals(log.ThreadName, "Manager", StringComparison.OrdinalIgnoreCase) &&
                             log.Message != null &&
                             log.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                             log.Message.Contains("->"))
                    {
                        log.CustomColor = Color.FromRgb(173, 216, 230); // Light Blue
                        log.RowForeground = Brushes.Black;
                    }
                });
            });
        }

        public async Task ApplyCustomColoringAsync(IEnumerable<LogEntry> logs, List<ColoringCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0) return;

            // הכנה מוקדמת של Regex לביצועים
            var preparedConditions = PrepareConditions(conditions);

            await Task.Run(() =>
            {
                Parallel.ForEach(logs, log =>
                {
                    // --- תיקון קריטי: מחקנו את הבדיקה if (log.IsMarked) return; ---
                    // כעת הצבע מחושב תמיד ושמור ב-CustomColor.
                    // הלוגיקה ב-LogEntry.RowBackground תדאג להציג סגול אם השורה מסומנת,
                    // או את הצבע המותאם אישית אם היא לא.

                    // החוקים הידניים דורסים את הדיפולט אם יש התאמה
                    foreach (var condition in preparedConditions)
                    {
                        if (EvaluateConditionOptimized(log, condition))
                        {
                            log.CustomColor = condition.Rule.Color;
                            break; // מצאנו התאמה, יוצאים מהלולאה הפנימית
                        }
                    }
                });
            });
        }

        // --- Helpers ---

        private bool Contains(string source, string text)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // מבנה עזר לאופטימיזציה (מונע יצירת Regex מחדש לכל שורה)
        private struct PreparedCondition
        {
            public ColoringCondition Rule;
            public Regex CachedRegex;
            public string FieldLower;
            public string OpLower;
        }

        private List<PreparedCondition> PrepareConditions(List<ColoringCondition> rawRules)
        {
            var list = new List<PreparedCondition>();
            foreach (var r in rawRules)
            {
                var pc = new PreparedCondition
                {
                    Rule = r,
                    FieldLower = r.Field?.ToLower(),
                    OpLower = r.Operator?.ToLower()
                };

                if (pc.OpLower == "regex" && !string.IsNullOrEmpty(r.Value))
                {
                    try { pc.CachedRegex = new Regex(r.Value, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
                    catch { }
                }
                list.Add(pc);
            }
            return list;
        }

        private bool EvaluateConditionOptimized(LogEntry log, PreparedCondition cond)
        {
            string textToCheck = null;
            switch (cond.FieldLower)
            {
                case "message": textToCheck = log.Message; break;
                case "level": textToCheck = log.Level; break;
                case "threadname": textToCheck = log.ThreadName; break;
                case "logger": textToCheck = log.Logger; break;
                case "method": textToCheck = log.Method; break;
                case "pattern": textToCheck = log.Pattern; break;
                case "data": textToCheck = log.Data; break;
                case "exception": textToCheck = log.Exception; break;
            }

            if (string.IsNullOrEmpty(textToCheck)) return false;

            string val = cond.Rule.Value;
            switch (cond.OpLower)
            {
                case "contains":
                    if (string.IsNullOrEmpty(val)) return false;
                    return textToCheck.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;
                case "equals":
                    return string.Equals(textToCheck, val, StringComparison.OrdinalIgnoreCase);
                case "begins with":
                    return textToCheck.StartsWith(val, StringComparison.OrdinalIgnoreCase);
                case "ends with":
                    return textToCheck.EndsWith(val, StringComparison.OrdinalIgnoreCase);
                case "regex":
                    return cond.CachedRegex != null && cond.CachedRegex.IsMatch(textToCheck);
                default:
                    return false;
            }
        }
    }
}