using IndiLogs_3._0.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ICsvExportService
    {
        Task<string> ExportLogsToCsvAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset preset = null);
    }
}
