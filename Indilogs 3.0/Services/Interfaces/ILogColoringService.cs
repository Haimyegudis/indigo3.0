using IndiLogs_3._0.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ILogColoringService
    {
        List<ColoringCondition> UserDefaultMainRules { get; set; }
        List<ColoringCondition> UserDefaultAppRules { get; set; }

        Task ApplyDefaultColorsAsync(IEnumerable<LogEntry> logs, bool isAppLog);
        Task ApplyCustomColoringAsync(IEnumerable<LogEntry> logs, List<ColoringCondition> conditions);
    }
}
