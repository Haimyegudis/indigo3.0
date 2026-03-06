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
    public class LogColoringService : Interfaces.ILogColoringService
    {
        // Cache compiled Regex to avoid recompiling every line
        private readonly ConcurrentDictionary<string, Regex> _regexCache = new ConcurrentDictionary<string, Regex>();

        // User-configurable default coloring rules (loaded from _defaults.json)
        public List<ColoringCondition>? UserDefaultMainRules { get; set; }
        public List<ColoringCondition>? UserDefaultAppRules { get; set; }

        /// <summary>
        /// Applies default colors. Uses user-configured rules if available, otherwise falls back to factory defaults.
        /// Regardless of which path is taken, always populates SystemDefaultColor so the PLC FILTERED tab
        /// can always display the factory-default colours (e.g. state-transition rows stay light blue).
        /// </summary>
        public async Task ApplyDefaultColorsAsync(IEnumerable<LogEntry> logs, bool isAppLog)
        {
            var colorSw = System.Diagnostics.Stopwatch.StartNew();
            var userRules = isAppLog ? UserDefaultAppRules : UserDefaultMainRules;

            if (userRules != null && userRules.Count > 0)
            {
                // User has custom default coloring — reset all colors then apply rules.
                // SystemDefaultColor is computed first so the FILTERED tab keeps factory colours.
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

                        // Always store the factory-default colour so the FILTERED tab is unaffected
                        // by whatever custom rules the user applies in the PLC tab.
                        SetSystemDefaultColor(log, isAppLog);

                        // Also apply factory-default colour to CustomColor for main PLC tab.
                        // User rules (ApplyCustomColoringAsync) may override this if they match.
                        if (!isAppLog && log.SystemDefaultColor.HasValue)
                            log.CustomColor = log.SystemDefaultColor.Value;
                    });
                }).ConfigureAwait(false);
                await ApplyCustomColoringAsync(logs, userRules).ConfigureAwait(false);
            }
            else
            {
                // No user defaults — use factory hardcoded logic (also sets SystemDefaultColor)
                await ApplyFactoryDefaultColorsAsync(logs, isAppLog).ConfigureAwait(false);
            }
            AppLogger.Info($"[Coloring] Default colors applied ({(isAppLog ? "APP" : "PLC")}) — {colorSw.ElapsedMilliseconds}ms");
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
                    // Reset existing colors
                    log.CustomColor = null;
                    log.SystemDefaultColor = null;
                    log.IsErrorOrEvents = false;
                    log.RowForeground = null;

                    // 2. PipelineCancellationProvider errors -> Strong Orange with Black text
                    if (isAppLog &&
                        string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase) &&
                        log.Logger != null &&
                        log.Logger.Contains("Press.BL.Printing.Pipeline.PipelineCancellationProvider"))
                    {
                        log.CustomColor = Color.FromRgb(255, 140, 0); // Strong Orange
                        log.SystemDefaultColor = Color.FromRgb(255, 140, 0);
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
                        log.SystemDefaultColor = Color.FromRgb(255, 165, 0);
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
                    // Events thread: mark as error/events (red text indicator)
                    if (string.Equals(log.ThreadName, "Events", StringComparison.OrdinalIgnoreCase))
                    {
                        log.IsErrorOrEvents = true;
                    }
                    // State transitions → light blue background in main PLC tab
                    if (StateTransitionHelper.IsS6StateTransition(log))
                    {
                        log.CustomColor = Color.FromRgb(173, 216, 230); // Light Blue
                        log.SystemDefaultColor = Color.FromRgb(173, 216, 230);
                    }
                    else if (log.Message != null && log.Message.Contains("==== STATE"))
                    {
                        // S4-5 binary PLC logs: state transitions contain "==== STATE"
                        log.CustomColor = Color.FromRgb(173, 216, 230); // Light Blue
                        log.SystemDefaultColor = Color.FromRgb(173, 216, 230);
                    }
                });
            }).ConfigureAwait(false);
        }

        public async Task ApplyCustomColoringAsync(IEnumerable<LogEntry> logs, List<ColoringCondition> conditions)
        {
            var colorSw = System.Diagnostics.Stopwatch.StartNew();
            if (conditions == null || conditions.Count == 0) return;

            var preparedConditions = PrepareConditions(conditions);

            await Task.Run(() =>
            {
                Parallel.ForEach(logs, log =>
                {
                    foreach (var condition in preparedConditions)
                    {
                        if (EvaluateConditionOptimized(log, condition))
                        {
                            log.CustomColor = condition.Rule.Color;
                            break;
                        }
                    }
                });
            }).ConfigureAwait(false);
            AppLogger.Info($"[Coloring] Custom rules ({conditions.Count} rules) applied — {colorSw.ElapsedMilliseconds}ms");
        }

        // --- System Default Color Helper ---

        /// <summary>
        /// Computes and stores the factory-hardcoded default colour for a log entry into
        /// <see cref="LogEntry.SystemDefaultColor"/>.  This is called even when user-defined
        /// coloring rules are active so that the PLC FILTERED tab always reflects the
        /// factory defaults regardless of what the user has customised in the PLC tab.
        /// </summary>
        private static void SetSystemDefaultColor(LogEntry log, bool isAppLog)
        {
            log.SystemDefaultColor = null;

            if (!isAppLog)
            {
                // PLC-only: state transitions (Manager thread + PlcMngr: prefix + arrow)
                if (StateTransitionHelper.IsS6StateTransition(log))
                {
                    log.SystemDefaultColor = Color.FromRgb(173, 216, 230); // Light Blue
                }
                else if (log.Message != null && log.Message.Contains("==== STATE"))
                {
                    // Binary APP PLC logs: state transitions contain "==== STATE"
                    log.SystemDefaultColor = Color.FromRgb(173, 216, 230); // Light Blue
                }
            }
        }

        // --- Helpers ---

        private bool Contains(string source, string text)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Helper struct for optimization (prevents recreating Regex for every row)
        private struct PreparedCondition
        {
            public ColoringCondition Rule;
            public Regex? CachedRegex;
            public string? FieldLower;
            public string? OpLower;
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
                    try { pc.CachedRegex = new Regex(r.Value, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)); }
                    catch (Exception ex) { AppLogger.Warn($"Invalid regex pattern '{r.Value}': {ex.Message}"); }
                }
                list.Add(pc);
            }
            return list;
        }

        private bool EvaluateConditionOptimized(LogEntry log, PreparedCondition cond)
        {
            string? textToCheck = null;
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
                default:
                    // Check ExtraFields for custom/plugin columns
                    if (log.ExtraFields != null)
                    {
                        if (!log.ExtraFields.TryGetValue(cond.Rule.Field, out textToCheck))
                        {
                            foreach (var kvp in log.ExtraFields)
                            {
                                if (string.Equals(kvp.Key, cond.Rule.Field, StringComparison.OrdinalIgnoreCase))
                                { textToCheck = kvp.Value; break; }
                            }
                        }
                    }
                    break;
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