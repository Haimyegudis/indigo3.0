using System;
using System.Collections.Generic;

namespace IndiLogs.PluginAPI
{
    /// <summary>
    /// A plain data-transfer object that represents a single parsed log line.
    /// Plugins return instances of this type; the host application maps them
    /// to its own internal <c>LogEntry</c> model.
    ///
    /// <para>No WPF or INotifyPropertyChanged dependencies — this type is safe
    /// to use in any .NET 4.8 class-library assembly.</para>
    /// </summary>
    public class LogEntryDto
    {
        // ===== Required fields =====

        /// <summary>Log timestamp (local machine time).</summary>
        public DateTime Date { get; set; }

        /// <summary>The main log message text.</summary>
        public string Message { get; set; }

        // ===== Optional / enrichment fields =====

        /// <summary>
        /// Severity level string.
        /// Recognised values: "Debug", "Info", "Warning", "Error", "Fatal".
        /// Defaults to "Info" when null or empty.
        /// </summary>
        public string Level { get; set; }

        /// <summary>Thread or subsystem name (e.g. "Manager", "Worker-1").</summary>
        public string ThreadName { get; set; }

        /// <summary>Logger or component name (e.g. "MyDevice.Communication").</summary>
        public string Logger { get; set; }

        /// <summary>
        /// Which tab this entry should appear in.
        /// Use <c>"APP"</c> to route to the APP logs tab;
        /// any other value (or null) routes to the PLC / main tab.
        /// </summary>
        public string ProcessName { get; set; }

        /// <summary>Method or code location that emitted the log (optional).</summary>
        public string Method { get; set; }

        /// <summary>
        /// Extra structured payload attached to the entry (optional).
        /// Typically JSON or key-value pairs.
        /// </summary>
        public string Data { get; set; }

        /// <summary>Exception text or stack trace, if present (optional).</summary>
        public string Exception { get; set; }

        /// <summary>
        /// Arbitrary key-value pairs for fields that do not map to any of the
        /// built-in properties above.
        ///
        /// <para>
        /// Use this for domain-specific data such as sensor IDs, machine names,
        /// measurement values, etc.  Declare matching keys in
        /// <see cref="ILogFilePlugin.GetColumns"/> so IndiLogs can render them
        /// as their own columns.
        /// </para>
        ///
        /// <example>
        /// <code>
        /// ExtraFields = new Dictionary&lt;string, string&gt;
        /// {
        ///     { "MachineId", "M-001"  },
        ///     { "Sensor",    "Temp"   },
        ///     { "Value",     "72.5°C" }
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public Dictionary<string, string> ExtraFields { get; set; }
    }
}
