using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static IndiLogs_3._0.Services.LogFileService;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ILogFileService
    {
        Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress);
        List<EventEntry> ParseEventsCsv(Stream stream);
        List<LogEntry> ParseLogStreamPartial(Stream stream);
        (List<LogEntry> AllLogs, List<LogEntry> Transitions, List<LogEntry> Failures) ParseLogStream(Stream stream, StringPool pool = null);
    }
}
