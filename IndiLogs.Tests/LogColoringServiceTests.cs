using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using Xunit;

namespace IndiLogs.Tests
{
    public class LogColoringServiceTests
    {
        private LogColoringService CreateService() => new LogColoringService();

        private LogEntry MakeLog(string level = "", string message = "", string thread = "", string logger = "", string method = "")
        {
            return new LogEntry
            {
                Level = level,
                Message = message,
                ThreadName = thread,
                Logger = logger,
                Method = method
            };
        }

        // ── Factory default coloring (PLC) ──

        [Fact]
        public async Task ApplyDefaultColors_PlcErrorLevel_SetsIsErrorOrEvents()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(level: "Error") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task ApplyDefaultColors_PlcEventsThread_SetsIsErrorOrEvents()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(thread: "Events") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task ApplyDefaultColors_PlcStateTransitionS45_LightBlue()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "==== STATE CHANGE ====") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.NotNull(logs[0].CustomColor);
            Assert.Equal(Color.FromRgb(173, 216, 230), logs[0].CustomColor!.Value);
        }

        [Fact]
        public async Task ApplyDefaultColors_PlcStateTransitionS6_LightBlue()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "PlcMngr: Idle -> Running", thread: "Manager") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.NotNull(logs[0].CustomColor);
            Assert.Equal(Color.FromRgb(173, 216, 230), logs[0].CustomColor!.Value);
        }

        [Fact]
        public async Task ApplyDefaultColors_PlcNormalLog_NoColor()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(level: "Info", message: "Normal log line") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.Null(logs[0].CustomColor);
            Assert.False(logs[0].IsErrorOrEvents);
        }

        // ── Factory default coloring (APP) ──

        [Fact]
        public async Task ApplyDefaultColors_AppError_SetsIsErrorOrEvents()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(level: "Error", message: "Something failed") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: true);

            Assert.True(logs[0].IsErrorOrEvents);
        }

        [Fact]
        public async Task ApplyDefaultColors_AppPipelineCancellation_StrongOrange()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                MakeLog(level: "Error", logger: "Press.BL.Printing.Pipeline.PipelineCancellationProvider")
            };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: true);

            Assert.NotNull(logs[0].CustomColor);
            Assert.Equal(Color.FromRgb(255, 140, 0), logs[0].CustomColor!.Value);
            Assert.NotNull(logs[0].RowForeground);
        }

        [Fact]
        public async Task ApplyDefaultColors_AppPressStateManager_Orange()
        {
            var svc = CreateService();
            var logs = new List<LogEntry>
            {
                MakeLog(logger: "PressStateManager", method: "FallToPressStateAsync")
            };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: true);

            Assert.NotNull(logs[0].CustomColor);
            Assert.Equal(Color.FromRgb(255, 165, 0), logs[0].CustomColor!.Value);
        }

        [Fact]
        public async Task ApplyDefaultColors_AppNormalLog_NoSpecialColor()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(level: "Info", message: "Normal app log") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: true);

            Assert.Null(logs[0].CustomColor);
            Assert.False(logs[0].IsErrorOrEvents);
        }

        // ── Custom coloring rules ──

        [Fact]
        public async Task ApplyCustomColoring_ContainsOperator_MatchesLog()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "Error in module XYZ") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Contains", Value = "module", Color = Colors.Red }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_EqualsOperator_MatchesExact()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(level: "Error") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Level", Operator = "Equals", Value = "Error", Color = Colors.Red }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_EqualsOperator_CaseInsensitive()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(level: "error") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Level", Operator = "Equals", Value = "ERROR", Color = Colors.Red }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_BeginsWithOperator()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "PlcMngr: Running") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Begins With", Value = "PlcMngr:", Color = Colors.Blue }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Blue, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_EndsWithOperator()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "Something ended with ERROR") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Ends With", Value = "ERROR", Color = Colors.Orange }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Orange, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_RegexOperator()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "Error code: 12345") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Regex", Value = @"code:\s*\d+", Color = Colors.Yellow }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Yellow, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_RegexOperator_NoMatch()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "Normal log") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Regex", Value = @"code:\s*\d+", Color = Colors.Yellow }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_InvalidRegex_DoesNotCrash()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "test") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Regex", Value = "[invalid", Color = Colors.Red }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_FirstRuleWins()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "Error in module") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Contains", Value = "Error", Color = Colors.Red },
                new ColoringCondition { Field = "Message", Operator = "Contains", Value = "module", Color = Colors.Blue }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Equal(Colors.Red, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_EmptyConditions_NoChange()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "test") };

            await svc.ApplyCustomColoringAsync(logs, new List<ColoringCondition>());

            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_NullConditions_NoChange()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "test") };

            await svc.ApplyCustomColoringAsync(logs, null!);

            Assert.Null(logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_AllFields_Supported()
        {
            var svc = CreateService();
            var fields = new[] { "Message", "Level", "ThreadName", "Logger", "Method", "Pattern", "Data", "Exception" };

            foreach (var field in fields)
            {
                var log = new LogEntry();
                switch (field)
                {
                    case "Message": log.Message = "testval"; break;
                    case "Level": log.Level = "testval"; break;
                    case "ThreadName": log.ThreadName = "testval"; break;
                    case "Logger": log.Logger = "testval"; break;
                    case "Method": log.Method = "testval"; break;
                    case "Pattern": log.Pattern = "testval"; break;
                    case "Data": log.Data = "testval"; break;
                    case "Exception": log.Exception = "testval"; break;
                }

                var rules = new List<ColoringCondition>
                {
                    new ColoringCondition { Field = field, Operator = "Contains", Value = "testval", Color = Colors.Green }
                };

                await svc.ApplyCustomColoringAsync(new[] { log }, rules);
                Assert.Equal(Colors.Green, log.CustomColor);
            }
        }

        [Fact]
        public async Task ApplyCustomColoring_ExtraFields_Supported()
        {
            var svc = CreateService();
            var log = new LogEntry
            {
                ExtraFields = new Dictionary<string, string> { ["CustomField"] = "match" }
            };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "CustomField", Operator = "Contains", Value = "match", Color = Colors.Purple }
            };

            await svc.ApplyCustomColoringAsync(new[] { log }, rules);

            Assert.Equal(Colors.Purple, log.CustomColor);
        }

        [Fact]
        public async Task ApplyCustomColoring_UnknownOperator_NoMatch()
        {
            var svc = CreateService();
            var logs = new List<LogEntry> { MakeLog(message: "test") };
            var rules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "BadOp", Value = "test", Color = Colors.Red }
            };

            await svc.ApplyCustomColoringAsync(logs, rules);

            Assert.Null(logs[0].CustomColor);
        }

        // ── User default rules ──

        [Fact]
        public async Task ApplyDefaultColors_WithUserRules_UsesUserRules()
        {
            var svc = CreateService();
            svc.UserDefaultMainRules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Contains", Value = "custom", Color = Colors.Magenta }
            };
            var logs = new List<LogEntry> { MakeLog(message: "custom log") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.Equal(Colors.Magenta, logs[0].CustomColor);
        }

        [Fact]
        public async Task ApplyDefaultColors_WithUserRules_ErrorStillGetsRedText()
        {
            var svc = CreateService();
            svc.UserDefaultMainRules = new List<ColoringCondition>
            {
                new ColoringCondition { Field = "Message", Operator = "Contains", Value = "nomatch", Color = Colors.Magenta }
            };
            var logs = new List<LogEntry> { MakeLog(level: "Error", message: "something failed") };

            await svc.ApplyDefaultColorsAsync(logs, isAppLog: false);

            Assert.True(logs[0].IsErrorOrEvents);
        }
    }
}
