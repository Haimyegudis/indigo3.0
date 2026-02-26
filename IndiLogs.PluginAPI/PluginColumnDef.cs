namespace IndiLogs.PluginAPI
{
    /// <summary>
    /// Describes a single column that a plugin wants IndiLogs to display
    /// for entries it produces.
    ///
    /// <para>
    /// Return a list of these from <see cref="ILogFilePlugin.GetColumns"/>.
    /// When the list is <c>null</c> IndiLogs falls back to its default columns
    /// (Timestamp, Level, Thread, Logger, Target, Message).
    /// </para>
    /// </summary>
    public class PluginColumnDef
    {
        /// <summary>Text shown in the column header.</summary>
        public string Header { get; set; }

        /// <summary>
        /// Which data to display.  Use one of the built-in field names OR
        /// any key you put in <see cref="LogEntryDto.ExtraFields"/>.
        ///
        /// <para>Built-in field names:</para>
        /// <list type="bullet">
        ///   <item><c>"Date"</c>       — timestamp</item>
        ///   <item><c>"Level"</c>      — severity string</item>
        ///   <item><c>"ThreadName"</c> — thread / subsystem</item>
        ///   <item><c>"Logger"</c>     — logger / component name</item>
        ///   <item><c>"ProcessName"</c>— process / target</item>
        ///   <item><c>"Message"</c>    — main message text</item>
        ///   <item><c>"Method"</c>     — method name</item>
        ///   <item><c>"Data"</c>       — structured payload</item>
        /// </list>
        ///
        /// <para>Any other value is treated as an <see cref="LogEntryDto.ExtraFields"/> key.</para>
        /// </summary>
        public string Field { get; set; }

        /// <summary>
        /// Column width in pixels.
        /// Set to <c>-1</c> to make the column fill the remaining space (equivalent to <c>Width="*"</c>).
        /// </summary>
        public double Width { get; set; } = 100;

        /// <summary>
        /// Optional .NET format string applied when <see cref="Field"/> is <c>"Date"</c>.
        /// Defaults to <c>"yyyy-MM-dd HH:mm:ss.fff"</c> when null.
        /// </summary>
        public string StringFormat { get; set; }
    }
}
