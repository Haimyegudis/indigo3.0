using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IndiLogs.PluginAPI
{
    /// <summary>
    /// Main contract for IndiLogs 3.0 log-parser plugins.
    ///
    /// <para><b>Quick-start guide for plugin developers</b></para>
    /// <list type="number">
    ///   <item>Create a .NET Framework 4.8 class library project.</item>
    ///   <item>Copy <c>IndiLogs.PluginAPI.dll</c> into your project and add it as a reference.</item>
    ///   <item>Implement this interface in one or more <c>public</c>, non-abstract classes.</item>
    ///   <item>Build your project to produce <c>MyPlugin.dll</c>.</item>
    ///   <item>
    ///     Copy <c>MyPlugin.dll</c> (and any private dependencies) to:
    ///     <code>%AppData%\IndiLogs3.0\Plugins\</code>
    ///   </item>
    ///   <item>Re-open IndiLogs — your parser is discovered and loaded automatically.</item>
    /// </list>
    ///
    /// <para><b>Threading</b>: <see cref="Parse"/> is called from a
    /// <c>Parallel.ForEach</c> worker thread. Implementations must be thread-safe;
    /// avoid shared mutable state unless protected by a lock.</para>
    /// </summary>
    public interface ILogFilePlugin
    {
        /// <summary>Human-readable plugin name shown in diagnostic logs and the About screen.</summary>
        string Name { get; }

        /// <summary>Plugin version string (e.g. "1.0.0").</summary>
        string Version { get; }

        /// <summary>
        /// File glob patterns this plugin handles, used to populate the
        /// "Open File" dialog filter automatically.
        /// Example: <c>new[] { "*.csvlog", "*_device.csv" }</c>
        /// Return <c>null</c> or empty array to skip adding to the filter.
        /// </summary>
        string[] SupportedExtensions { get; }

        /// <summary>
        /// Called for every file that the built-in parsers could not identify.
        /// Return <c>true</c> if this plugin is able to parse the file.
        ///
        /// <para>Keep this method fast — it runs on the UI thread during file scanning.
        /// Use only the file name and the first few sample lines for heuristic detection;
        /// do not open the stream here.</para>
        /// </summary>
        /// <param name="fileName">
        /// File name only (no directory), e.g. "device.log" or "data.csv".
        /// </param>
        /// <param name="sampleLines">
        /// The first 20 lines of the file as plain text.
        /// May be empty for binary files or very short files.
        /// </param>
        /// <returns><c>true</c> if this plugin should parse the file.</returns>
        bool CanHandle(string fileName, string[] sampleLines);

        /// <summary>
        /// Parses the file stream and yields <see cref="LogEntryDto"/> records.
        ///
        /// <para>The stream is positioned at the beginning (offset 0).
        /// You are responsible for reading it completely before returning.</para>
        ///
        /// <para>Throw exceptions freely — the host will catch, log, and skip the file.</para>
        /// </summary>
        /// <param name="stream">Readable stream of the file content (may be a MemoryStream for ZIP entries).</param>
        /// <param name="context">Contextual metadata about the source file.</param>
        /// <param name="progress">
        /// Optional. Report values in [0, 100] periodically so the loading progress bar
        /// stays responsive on large files.  May be null — check before calling.
        /// </param>
        /// <param name="ct">
        /// Cancellation token. Poll this on large files:
        /// <code>ct.ThrowIfCancellationRequested();</code>
        /// </param>
        /// <returns>An enumerable of parsed log entries (lazy is fine).</returns>
        IEnumerable<LogEntryDto> Parse(
            Stream stream,
            ParseContext context,
            IProgress<double> progress,
            CancellationToken ct);

        /// <summary>
        /// Returns the column layout IndiLogs should use when displaying entries
        /// produced by this plugin.
        ///
        /// <para>
        /// Return <c>null</c> (or an empty list) to use IndiLogs' default columns
        /// (Timestamp, Level, Thread, Logger, Target, Message).
        /// </para>
        ///
        /// <para>
        /// Column <see cref="PluginColumnDef.Field"/> values can be any of the
        /// built-in <see cref="LogEntryDto"/> property names (<c>"Date"</c>,
        /// <c>"Level"</c>, <c>"Message"</c>, etc.) or any key stored in
        /// <see cref="LogEntryDto.ExtraFields"/>.
        /// </para>
        /// </summary>
        IReadOnlyList<PluginColumnDef> GetColumns();
    }
}
