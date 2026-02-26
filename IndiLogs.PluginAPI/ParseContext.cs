namespace IndiLogs.PluginAPI
{
    /// <summary>
    /// Contextual metadata provided by the host to
    /// <see cref="ILogFilePlugin.Parse"/> and <see cref="ILogFilePlugin.CanHandle"/>.
    /// </summary>
    public class ParseContext
    {
        /// <summary>
        /// File name only (no directory path), e.g. "custom.log".
        /// When the file is inside a ZIP this is the entry name.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Full file-system path of the file being parsed.
        /// Null when the source is a ZIP entry (<see cref="IsInsideZip"/> is true).
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>True when the stream originates from a file inside a ZIP archive.</summary>
        public bool IsInsideZip { get; set; }

        /// <summary>
        /// Full entry path inside the ZIP (e.g. "IndigoLogs/Logger Files/custom.log").
        /// Null when <see cref="IsInsideZip"/> is false.
        /// </summary>
        public string ZipEntryPath { get; set; }
    }
}
