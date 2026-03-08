using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using Indigo.Infra.ICL.Core.Logging;

namespace IndiLogs_3._0.Services
{
    public partial class GlobalGrepService
    {
        // ====================================================================
        //  File time filtering
        // ====================================================================

        /// <summary>
        /// Filters files by time range using filename timestamp pattern or file modification date as fallback.
        /// </summary>
        public List<string> FilterFilesByTimeRange(List<string> files, TimeRangeFilter filter)
        {
            if (filter == null || (!filter.From.HasValue && !filter.To.HasValue))
                return files;

            return files.Where(f =>
            {
                DateTime fileTime;

                // Try to parse timestamp from filename first
                string fileName = Path.GetFileNameWithoutExtension(f);
                var match = AppConstants.FileTimestampRegex().Match(fileName);
                if (match.Success)
                {
                    fileTime = new DateTime(
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value),
                        int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value));
                }
                else
                {
                    // Fallback to file modification date
                    try { fileTime = File.GetLastWriteTime(f); }
                    catch (Exception ex) { AppLogger.Warn($"[Grep] Cannot read file time for {f}: {ex.Message}"); return true; }
                }

                if (filter.From.HasValue && fileTime < filter.From.Value) return false;
                if (filter.To.HasValue && fileTime > filter.To.Value) return false;
                return true;
            }).ToList();
        }

        // ====================================================================
        //  Criteria evaluation (multi-field, groups, logical operators)
        // ====================================================================

        /// <summary>
        /// Evaluates whether a log entry matches the top-level search criteria.
        /// Groups are combined with the criteria's GroupOperator (AND/OR).
        /// </summary>
        public bool EvaluateCriteria(LogEntry entry, SearchCriteria criteria)
        {
            if (criteria.Groups == null || criteria.Groups.Count == 0) return true;

            if (criteria.GroupOperator == LogicalGroupOperator.And)
                return criteria.Groups.All(g => EvaluateGroup(entry, g));
            else
                return criteria.Groups.Any(g => EvaluateGroup(entry, g));
        }

        /// <summary>
        /// Evaluates a group of conditions with the group's operator (AND/OR/NOR).
        /// </summary>
        public bool EvaluateGroup(LogEntry entry, SearchConditionGroup group)
        {
            if (group.Conditions == null || group.Conditions.Count == 0) return true;

            bool result;
            switch (group.Operator)
            {
                case ConditionOperator.And:
                    result = group.Conditions.All(c => EvaluateCondition(entry, c));
                    break;
                case ConditionOperator.Or:
                    result = group.Conditions.Any(c => EvaluateCondition(entry, c));
                    break;
                case ConditionOperator.Nor:
                    result = !group.Conditions.Any(c => EvaluateCondition(entry, c));
                    break;
                default:
                    result = group.Conditions.All(c => EvaluateCondition(entry, c));
                    break;
            }
            return result;
        }

        /// <summary>
        /// Evaluates a single condition against a log entry field, with optional negation.
        /// </summary>
        public bool EvaluateCondition(LogEntry entry, SearchCondition condition)
        {
            // Get all field values to check
            var fieldsToCheck = GetFieldValues(entry, condition.Field);
            var compiledRegex = condition.CompiledRegex;
            bool match = fieldsToCheck.Any(text => MatchText(text, condition.Value, condition.Operator, compiledRegex));
            return condition.Negate ? !match : match;
        }

        private List<string> GetFieldValues(LogEntry entry, SearchField field)
        {
            var values = new List<string>();
            switch (field)
            {
                case SearchField.Message:   if (entry.Message != null) values.Add(entry.Message); break;
                case SearchField.Level:     if (entry.Level != null) values.Add(entry.Level); break;
                case SearchField.ThreadName: if (entry.ThreadName != null) values.Add(entry.ThreadName); break;
                case SearchField.Logger:    if (entry.Logger != null) values.Add(entry.Logger); break;
                case SearchField.Method:    if (entry.Method != null) values.Add(entry.Method); break;
                case SearchField.Data:      if (entry.Data != null) values.Add(entry.Data); break;
                case SearchField.Exception: if (entry.Exception != null) values.Add(entry.Exception); break;
                case SearchField.Any:
                    if (entry.Message != null) values.Add(entry.Message);
                    if (entry.Level != null) values.Add(entry.Level);
                    if (entry.ThreadName != null) values.Add(entry.ThreadName);
                    if (entry.Logger != null) values.Add(entry.Logger);
                    if (entry.Method != null) values.Add(entry.Method);
                    if (entry.Data != null) values.Add(entry.Data);
                    if (entry.Exception != null) values.Add(entry.Exception);
                    break;
            }
            return values;
        }

        private bool MatchText(string text, string value, SearchOperator op, System.Text.RegularExpressions.Regex? compiledRegex = null)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (string.IsNullOrEmpty(value)) return false;

            switch (op)
            {
                case SearchOperator.Contains:
                    return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
                case SearchOperator.Equals:
                    return string.Equals(text, value, StringComparison.OrdinalIgnoreCase);
                case SearchOperator.StartsWith:
                    return text.StartsWith(value, StringComparison.OrdinalIgnoreCase);
                case SearchOperator.EndsWith:
                    return text.EndsWith(value, StringComparison.OrdinalIgnoreCase);
                case SearchOperator.Regex:
                    try
                    {
                        if (compiledRegex != null)
                            return compiledRegex.IsMatch(text);
                        return Regex.IsMatch(text, value, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                    }
                    catch (Exception ex) { AppLogger.Warn($"[Grep] Regex match failed for pattern '{value}': {ex.Message}"); return false; }
                default:
                    return false;
            }
        }

        /// <summary>
        /// After a match is confirmed, determines which field(s) actually matched.
        /// Returns a comma-separated string like "Message" or "Message, Exception".
        /// </summary>
        public string DetermineMatchedFields(LogEntry entry, SearchCriteria criteria)
        {
            if (criteria.Groups == null || criteria.Groups.Count == 0) return "";

            var matchedFields = new HashSet<string>();
            var allFields = new[] { SearchField.Message, SearchField.Level, SearchField.ThreadName,
                                    SearchField.Logger, SearchField.Method, SearchField.Data, SearchField.Exception };

            foreach (var group in criteria.Groups)
            {
                if (group.Conditions == null) continue;
                foreach (var condition in group.Conditions)
                {
                    if (string.IsNullOrWhiteSpace(condition.Value)) continue;

                    if (condition.Field == SearchField.Any)
                    {
                        // Check each individual field to see which ones actually matched
                        var anyRegex = condition.CompiledRegex;
                        foreach (var field in allFields)
                        {
                            var values = GetFieldValues(entry, field);
                            if (values.Any(v => MatchText(v, condition.Value, condition.Operator, anyRegex)))
                                matchedFields.Add(field.ToString());
                        }
                    }
                    else
                    {
                        var values = GetFieldValues(entry, condition.Field);
                        var cRegex2 = condition.CompiledRegex;
                        if (values.Any(v => MatchText(v, condition.Value, condition.Operator, cRegex2)))
                            matchedFields.Add(condition.Field.ToString());
                    }
                }
            }

            return string.Join(", ", matchedFields);
        }
    }
}
