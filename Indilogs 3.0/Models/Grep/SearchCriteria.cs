using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Models.Grep
{
    /// <summary>
    /// Top-level search criteria containing condition groups with logical operators.
    /// </summary>
    public class SearchCriteria
    {
        public string Name { get; set; }

        /// <summary>
        /// Condition groups evaluated with GroupOperator between them.
        /// </summary>
        public List<SearchConditionGroup> Groups { get; set; } = new List<SearchConditionGroup>();

        /// <summary>
        /// Logical operator applied between groups (AND/OR).
        /// </summary>
        public LogicalGroupOperator GroupOperator { get; set; } = LogicalGroupOperator.And;

        /// <summary>
        /// Filter files by time range (based on filename timestamp or file modification date).
        /// </summary>
        public TimeRangeFilter FileTimeFilter { get; set; }

        /// <summary>
        /// Filter results by log entry timestamp.
        /// </summary>
        public TimeRangeFilter ResultTimeFilter { get; set; }

        /// <summary>
        /// Which location IDs to search. Empty = search all active locations.
        /// </summary>
        public List<Guid> LocationIds { get; set; } = new List<Guid>();

        /// <summary>
        /// Search PLC log files.
        /// </summary>
        public bool SearchPLC { get; set; } = true;

        /// <summary>
        /// Search APP log files.
        /// </summary>
        public bool SearchAPP { get; set; } = true;
    }

    /// <summary>
    /// A group of conditions evaluated with a single logical operator.
    /// </summary>
    public class SearchConditionGroup
    {
        public List<SearchCondition> Conditions { get; set; } = new List<SearchCondition>();

        /// <summary>
        /// Logical operator applied between conditions within this group.
        /// </summary>
        public ConditionOperator Operator { get; set; } = ConditionOperator.And;
    }

    /// <summary>
    /// A single search condition: field + operator + value, with optional negation.
    /// </summary>
    public class SearchCondition
    {
        public SearchField Field { get; set; } = SearchField.Any;
        public SearchOperator Operator { get; set; } = SearchOperator.Contains;
        public string Value { get; set; }
        public bool Negate { get; set; }
    }

    /// <summary>
    /// Time range with optional from/to bounds.
    /// </summary>
    public class TimeRangeFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public enum SearchField
    {
        Any,
        Message,
        Level,
        ThreadName,
        Logger,
        Method,
        Data,
        Exception
    }

    public enum SearchOperator
    {
        Contains,
        Equals,
        StartsWith,
        EndsWith,
        Regex
    }

    public enum ConditionOperator
    {
        And,
        Or,
        Nor
    }

    public enum LogicalGroupOperator
    {
        And,
        Or
    }
}
