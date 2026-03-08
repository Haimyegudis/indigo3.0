using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class CoverageBoostServiceATests
    {
        // ====================================================================
        //  Helper: create uninitialized GlobalGrepService for testing public methods
        // ====================================================================
        private static GlobalGrepService CreateGrepService()
        {
            return (GlobalGrepService)RuntimeHelpers.GetUninitializedObject(typeof(GlobalGrepService));
        }

        // ====================================================================
        //  LogFileClassifier tests
        // ====================================================================

        [Theory]
        [InlineData("engineGroupA.file.log", true, false, true)]
        [InlineData("engineGroupA.file.log", false, true, false)]
        [InlineData("engineGroupB.file.log", true, false, true)]
        [InlineData("some.file.log", true, false, true)]
        [InlineData("appdev.log", false, true, true)]
        [InlineData("press.host.app.log", false, true, true)]
        [InlineData("50300001.file", false, true, true)]
        [InlineData("50300001.file", true, false, false)]
        [InlineData("random.txt", true, true, false)]
        [InlineData("test.zip", true, true, true)]
        public void LogFileClassifier_IsLogFile(string path, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsLogFile(path, plc, app));
        }

        [Theory]
        [InlineData("engineGroupA.file.log", true, false, true)]
        [InlineData("test.zip", true, true, false)] // ZIPs excluded for entries
        [InlineData("appdev.log", false, true, true)]
        [InlineData("50300001.file", false, true, true)]
        [InlineData("random.txt", true, true, false)]
        public void LogFileClassifier_IsLogEntry(string entryName, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsLogEntry(entryName, plc, app));
        }

        [Theory]
        [InlineData("appdev.log", "APP")]
        [InlineData("PRESS.HOST.APP.log", "APP")]
        [InlineData("50300001.file", "APP")]
        [InlineData("engineGroupA.file.log", "PLC")]
        [InlineData("random.log", "PLC")]
        public void LogFileClassifier_DetermineLogType(string path, string expected)
        {
            Assert.Equal(expected, LogFileClassifier.DetermineLogType(path));
        }

        [Theory]
        [InlineData("50300001.file", true)]
        [InlineData("123.file.log", true)]
        [InlineData("enginegroupa.file.log", false)]
        [InlineData("abc.file", false)]
        [InlineData("nofile.log", false)]
        [InlineData("", false)]
        public void LogFileClassifier_IsNumericAppFileName(string name, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsNumericAppFileName(name));
        }

        [Theory]
        [InlineData("enginegroupa.file.log", true, false, true)]
        [InlineData("enginegroupb.file.log", true, false, true)]
        [InlineData("no-sn-something.file", true, false, true)]
        [InlineData("appdev.something", false, true, true)]
        [InlineData("press.host.app.txt", false, true, true)]
        [InlineData("random.txt", true, true, false)]
        public void LogFileClassifier_IsSearchableLogFile(string lp, bool plc, bool app, bool expected)
        {
            Assert.Equal(expected, LogFileClassifier.IsSearchableLogFile(lp, plc, app));
        }

        // ====================================================================
        //  ZipClassificationHelpers tests
        // ====================================================================

        [Theory]
        [InlineData("whel3_data.csv", true)]
        [InlineData("ecm_data.csv", true)]
        [InlineData("COM1_serial.log", true)]
        [InlineData("0001_data.txt", true)]
        [InlineData("Io-BIM.csv", true)]
        [InlineData("Stab-test.csv", true)]
        [InlineData("PRE_analysis.csv", true)]
        [InlineData("POST_analysis.csv", true)]
        [InlineData("random.txt", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ZipClassificationHelpers_IsCustomTerminalLog(string? fileName, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsCustomTerminalLog(fileName!));
        }

        [Theory]
        [InlineData("diagnosticslogs/systab_saved.txt", true)]
        [InlineData("path/diagnosticslogs/systab_default.txt", true)]
        [InlineData(@"path\diagnosticslogs\systab_minimum.txt", true)]
        [InlineData("diagnosticslogs/other.txt", false)]
        [InlineData("systab_saved.txt", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ZipClassificationHelpers_IsSystabFile(string? name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsSystabFile(name!));
        }

        [Theory]
        [InlineData("test.log", true)]
        [InlineData("test.txt", true)]
        [InlineData("test.csv", true)]
        [InlineData("test.json", true)]
        [InlineData("test.xml", true)]
        [InlineData("test.cfg", true)]
        [InlineData("test.ini", true)]
        [InlineData("test.config", true)]
        [InlineData("test.tsv", true)]
        [InlineData("test.file", true)]
        [InlineData("test.dll", false)]
        [InlineData("test.exe", false)]
        [InlineData("test.dat", false)]
        [InlineData("test", false)]
        public void ZipClassificationHelpers_IsPluginCandidateExtension(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsPluginCandidateExtension(name));
        }

        [Theory]
        [InlineData("datamanagement/ecommon/globals/file.xml", true)]
        [InlineData(@"datamanagement\ecommon\globals\file.xml", true)]
        [InlineData("path/datamanagement/ecommon/globals/file.xml", true)]
        [InlineData("datamanagement/ecommon/globals/file.txt", false)]
        [InlineData("globals/file.xml", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ZipClassificationHelpers_IsGlobalsXmlFile(string? name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsGlobalsXmlFile(name!));
        }

        [Theory]
        [InlineData("path/terminallogs/file.csv", true)]
        [InlineData(@"path\terminallogs\file.csv", true)]
        [InlineData("terminallogs/file.csv", true)]
        [InlineData(@"terminallogs\file.csv", true)]
        [InlineData("other/path/file.csv", false)]
        public void ZipClassificationHelpers_IsTerminalLogsPath(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsTerminalLogsPath(name));
        }

        [Theory]
        [InlineData("path/lrs/file.csv", true)]
        [InlineData(@"path\lrs\file.csv", true)]
        [InlineData("lrs/file.csv", true)]
        [InlineData(@"lrs\file.csv", true)]
        [InlineData("other/path/file.csv", false)]
        public void ZipClassificationHelpers_IsLrsPath(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsLrsPath(name));
        }

        [Theory]
        [InlineData("path/configuration/file.db", true)]
        [InlineData(@"path\configuration\file.db", true)]
        [InlineData("configuration/file.db", true)]
        [InlineData(@"configuration\file.db", true)]
        [InlineData("other/path/file.db", false)]
        public void ZipClassificationHelpers_IsConfigurationPath(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsConfigurationPath(name));
        }

        [Theory]
        [InlineData("Indigo.Infra.EM_Statistics.csv", true)]
        [InlineData("EM_Statistics.csv", true)]
        [InlineData("em_statistics.CSV", true)]
        [InlineData("EM_Statistics.txt", false)]
        [InlineData("random.csv", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ZipClassificationHelpers_IsEmStatisticsFile(string? name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.IsEmStatisticsFile(name!));
        }

        [Theory]
        [InlineData("path/backup/file.log", true)]
        [InlineData(@"path\backup\file.log", true)]
        [InlineData("path/old/file.log", true)]
        [InlineData("path/temp/file.log", true)]
        [InlineData("path/archive/file.log", true)]
        [InlineData("path/normal/file.log", false)]
        public void ZipClassificationHelpers_ShouldSkipEntry(string name, bool expected)
        {
            Assert.Equal(expected, ZipClassificationHelpers.ShouldSkipEntry(name));
        }

        // ====================================================================
        //  GlobalGrepService.MultiLocationHelpers — EvaluateCriteria tests
        // ====================================================================

        [Fact]
        public void EvaluateCriteria_NullGroups_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria { Groups = null! };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_EmptyGroups_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria { Groups = new List<SearchConditionGroup>() };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_ContainsMatch_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Hello World Error occurred" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_ContainsNoMatch_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Hello World" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" }
                        }
                    }
                }
            };
            Assert.False(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_EqualsMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Level = "ERROR" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "error" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_StartsWithMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Logger = "HP.Indigo.Press.Engine" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Logger, Operator = SearchOperator.StartsWith, Value = "HP.Indigo" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_EndsWithMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Logger = "HP.Indigo.Press.Engine" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Logger, Operator = SearchOperator.EndsWith, Value = "Engine" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_RegexMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Temperature is 123.4 degrees" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"\d+\.\d+" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_RegexNoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "No numbers here" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = @"\d+\.\d+" }
                        }
                    }
                }
            };
            Assert.False(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_NegateCondition()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Normal message" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error", Negate = true }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_NegateCondition_MatchBecomesNoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error occurred" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error", Negate = true }
                        }
                    }
                }
            };
            Assert.False(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateGroup_AndOperator_BothMustMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error in module", Level = "ERROR" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "ERROR" }
                }
            };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_AndOperator_OneFails()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Info message", Level = "INFO" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.And,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "INFO" }
                }
            };
            Assert.False(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_OrOperator_OneMatches()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Info message", Level = "ERROR" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "ERROR" }
                }
            };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_OrOperator_NoneMatches()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Info message", Level = "INFO" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Or,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "FATAL" }
                }
            };
            Assert.False(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_NoneMatches_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Info message", Level = "INFO" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" },
                    new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "FATAL" }
                }
            };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NorOperator_OneMatches_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error message", Level = "ERROR" };
            var group = new SearchConditionGroup
            {
                Operator = ConditionOperator.Nor,
                Conditions = new List<SearchCondition>
                {
                    new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" }
                }
            };
            Assert.False(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_EmptyConditions_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var group = new SearchConditionGroup { Conditions = new List<SearchCondition>() };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateGroup_NullConditions_ReturnsTrue()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var group = new SearchConditionGroup { Conditions = null! };
            Assert.True(svc.EvaluateGroup(entry, group));
        }

        [Fact]
        public void EvaluateCriteria_MultipleGroups_AndOperator()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error message", Level = "ERROR" };
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.And,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "ERROR" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        [Fact]
        public void EvaluateCriteria_MultipleGroups_OrOperator()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Normal message", Level = "ERROR" };
            var criteria = new SearchCriteria
            {
                GroupOperator = LogicalGroupOperator.Or,
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Fatal" }
                        }
                    },
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Level, Operator = SearchOperator.Equals, Value = "ERROR" }
                        }
                    }
                }
            };
            Assert.True(svc.EvaluateCriteria(entry, criteria));
        }

        // ====================================================================
        //  EvaluateCondition — SearchField coverage
        // ====================================================================

        [Theory]
        [InlineData(SearchField.Message, "hello msg")]
        [InlineData(SearchField.Level, "ERROR")]
        [InlineData(SearchField.ThreadName, "Manager")]
        [InlineData(SearchField.Logger, "HP.Indigo")]
        [InlineData(SearchField.Method, "DoWork")]
        [InlineData(SearchField.Data, "key=value")]
        [InlineData(SearchField.Exception, "NullReferenceException")]
        public void EvaluateCondition_EachField_MatchesCorrectly(SearchField field, string value)
        {
            var svc = CreateGrepService();
            var entry = new LogEntry
            {
                Message = "hello msg",
                Level = "ERROR",
                ThreadName = "Manager",
                Logger = "HP.Indigo",
                Method = "DoWork",
                Data = "key=value",
                Exception = "NullReferenceException"
            };
            var condition = new SearchCondition { Field = field, Operator = SearchOperator.Contains, Value = value };
            Assert.True(svc.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_AnyField_MatchesAcrossFields()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "normal", Exception = "StackOverflow" };
            var condition = new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "StackOverflow" };
            Assert.True(svc.EvaluateCondition(entry, condition));
        }

        [Fact]
        public void EvaluateCondition_AnyField_NoMatch()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "normal", Level = "INFO" };
            var condition = new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "StackOverflow" };
            Assert.False(svc.EvaluateCondition(entry, condition));
        }

        // ====================================================================
        //  DetermineMatchedFields tests
        // ====================================================================

        [Fact]
        public void DetermineMatchedFields_NoGroups_ReturnsEmpty()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria { Groups = null! };
            Assert.Equal("", svc.DetermineMatchedFields(entry, criteria));
        }

        [Fact]
        public void DetermineMatchedFields_SpecificField_ReturnsFieldName()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error occurred" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "Error" }
                        }
                    }
                }
            };
            Assert.Equal("Message", svc.DetermineMatchedFields(entry, criteria));
        }

        [Fact]
        public void DetermineMatchedFields_AnyField_ReturnsActualMatchedFields()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "Error", Exception = "Error too" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Any, Operator = SearchOperator.Contains, Value = "Error" }
                        }
                    }
                }
            };
            var result = svc.DetermineMatchedFields(entry, criteria);
            Assert.Contains("Message", result);
            Assert.Contains("Exception", result);
        }

        [Fact]
        public void DetermineMatchedFields_EmptyValue_Skipped()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var criteria = new SearchCriteria
            {
                Groups = new List<SearchConditionGroup>
                {
                    new SearchConditionGroup
                    {
                        Conditions = new List<SearchCondition>
                        {
                            new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "" }
                        }
                    }
                }
            };
            Assert.Equal("", svc.DetermineMatchedFields(entry, criteria));
        }

        // ====================================================================
        //  LogStatisticsService.Computation tests
        // ====================================================================

        [Fact]
        public void ComputeStatistics_EmptyLists_ReturnsDefaults()
        {
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), new List<LogEntry>());
            Assert.Equal(0, result.TotalPlcLogs);
            Assert.Equal(0, result.TotalAppLogs);
            Assert.Null(result.EarliestTimestamp);
            Assert.Null(result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_WithPlcLogs_ComputesCorrectly()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = now.AddMinutes(-10), Level = "INFO", Message = "Starting", ThreadName = "Manager" },
                new LogEntry { Date = now.AddMinutes(-5), Level = "ERROR", Message = "Something failed", ThreadName = "Manager" },
                new LogEntry { Date = now, Level = "ERROR", Message = "Something failed", ThreadName = "Worker" }
            };
            var result = LogStatisticsService.ComputeStatistics(plcLogs, new List<LogEntry>());
            Assert.Equal(3, result.TotalPlcLogs);
            Assert.Equal(2, result.TotalPlcErrors);
            Assert.NotNull(result.EarliestTimestamp);
            Assert.NotNull(result.LatestTimestamp);
        }

        [Fact]
        public void ComputeStatistics_WithAppLogs_ComputesCorrectly()
        {
            var now = DateTime.Now;
            var appLogs = new List<LogEntry>
            {
                new LogEntry { Date = now.AddMinutes(-10), Level = "INFO", Message = "Starting", Logger = "HP.Indigo.Press.Engine" },
                new LogEntry { Date = now.AddMinutes(-5), Level = "ERROR", Message = "Failed", Logger = "HP.Indigo.Press.Engine", Method = "DoWork" },
                new LogEntry { Date = now, Level = "WARN", Message = "Slow", Logger = "HP.Indigo.Press.Module" }
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs);
            Assert.Equal(3, result.TotalAppLogs);
            Assert.Equal(1, result.TotalAppErrors);
        }

        [Fact]
        public void ComputeStatistics_WithBinaryAppLogs_SkipsMethodStats()
        {
            var now = DateTime.Now;
            var appLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "ERROR", Message = "Failed", Logger = "HP.Indigo.Press", Method = "DoWork" }
            };
            var result = LogStatisticsService.ComputeStatistics(new List<LogEntry>(), appLogs, hasBinaryAppLogs: true);
            Assert.Empty(result.AppMethodErrors);
            Assert.Empty(result.AppMethodLoad);
        }

        [Fact]
        public void GetErrorLogs_FiltersOnlyErrors()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Level = "INFO" },
                new LogEntry { Level = "ERROR" },
                new LogEntry { Level = "WARN" },
                new LogEntry { Level = "FATAL" },
                new LogEntry { Level = "DEBUG" }
            };
            var errors = LogStatisticsService.GetErrorLogs(logs);
            Assert.Equal(2, errors.Count); // ERROR and FATAL
        }

        [Fact]
        public void TopN_ReturnsTopItemsSorted()
        {
            var dict = new Dictionary<string, int>
            {
                { "a", 5 }, { "b", 10 }, { "c", 3 }, { "d", 8 }, { "e", 1 }
            };
            var top = LogStatisticsService.TopN(dict, 3);
            Assert.Equal(3, top.Count);
            Assert.Equal("b", top[0].Key);
            Assert.Equal(10, top[0].Value);
            Assert.Equal("d", top[1].Key);
            Assert.Equal("a", top[2].Key);
        }

        [Fact]
        public void TopN_FewerItemsThanN_ReturnsAll()
        {
            var dict = new Dictionary<string, int> { { "a", 5 }, { "b", 3 } };
            var top = LogStatisticsService.TopN(dict, 10);
            Assert.Equal(2, top.Count);
            Assert.Equal("a", top[0].Key);
        }

        [Fact]
        public void TopN_EmptyDict_ReturnsEmpty()
        {
            var top = LogStatisticsService.TopN(new Dictionary<string, int>(), 5);
            Assert.Empty(top);
        }

        [Fact]
        public void CalculateErrorHistogram_ReturnsCorrectCounts()
        {
            var errors = new List<LogEntry>
            {
                new LogEntry { Message = "Error A" },
                new LogEntry { Message = "Error A" },
                new LogEntry { Message = "Error B" },
                new LogEntry { Message = "Error A" }
            };
            var histogram = LogStatisticsService.CalculateErrorHistogram(errors, 5);
            Assert.True(histogram.Count > 0);
            Assert.Equal("Error A", histogram[0].Name);
            Assert.Equal(3, histogram[0].Count);
        }

        [Fact]
        public void CalculateErrorHistogram_EmptyList_ReturnsEmpty()
        {
            var histogram = LogStatisticsService.CalculateErrorHistogram(new List<LogEntry>(), 5);
            Assert.Empty(histogram);
        }

        [Fact]
        public void CalculateErrorHistogram_CustomKeySelector()
        {
            var errors = new List<LogEntry>
            {
                new LogEntry { Message = "Error A", Logger = "HP.Indigo.Press.Engine" },
                new LogEntry { Message = "Error B", Logger = "HP.Indigo.Press.Engine" },
                new LogEntry { Message = "Error C", Logger = "HP.Indigo.Press.Module" }
            };
            var histogram = LogStatisticsService.CalculateErrorHistogram(errors, 5, l => LogStatisticsService.GetShortLoggerName(l.Logger));
            Assert.True(histogram.Count > 0);
            Assert.Equal(2, histogram[0].Count);
        }

        [Fact]
        public void CalculateLoadDistribution_ReturnsCorrectCounts()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { ThreadName = "Manager" },
                new LogEntry { ThreadName = "Manager" },
                new LogEntry { ThreadName = "Worker" },
                new LogEntry { ThreadName = "Manager" },
                new LogEntry { ThreadName = "IO" }
            };
            var dist = LogStatisticsService.CalculateLoadDistribution(logs, l => l.ThreadName, 5);
            Assert.True(dist.Count > 0);
            Assert.Equal("Manager", dist[0].Name);
            Assert.Equal(3, dist[0].Count);
        }

        [Fact]
        public void CalculateLoadDistribution_WithFullNameSelector()
        {
            var logs = new List<LogEntry>
            {
                new LogEntry { Logger = "HP.Indigo.Press.Engine.Module1" },
                new LogEntry { Logger = "HP.Indigo.Press.Engine.Module1" },
                new LogEntry { Logger = "HP.Indigo.Press.Engine.Module2" }
            };
            var dist = LogStatisticsService.CalculateLoadDistribution(
                logs, l => LogStatisticsService.GetShortLoggerName(l.Logger), 5, l => l.Logger);
            Assert.True(dist.Count > 0);
        }

        [Fact]
        public void FindGaps_DetectsGaps()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = now, Message = "Before gap" },
                new LogEntry { Date = now.AddSeconds(5), Message = "After gap" }
            };
            var gaps = LogStatisticsService.FindGaps(logs);
            Assert.Single(gaps);
            Assert.Equal(1, gaps[0].Index);
            Assert.Equal(5, gaps[0].Duration.TotalSeconds, 0);
        }

        [Fact]
        public void FindGaps_NoGaps()
        {
            var now = DateTime.Now;
            var logs = new List<LogEntry>
            {
                new LogEntry { Date = now },
                new LogEntry { Date = now.AddMilliseconds(500) }
            };
            var gaps = LogStatisticsService.FindGaps(logs);
            Assert.Empty(gaps);
        }

        [Fact]
        public void FindGaps_TooFewLogs_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.FindGaps(new List<LogEntry> { new LogEntry() }));
            Assert.Empty(LogStatisticsService.FindGaps(null!));
        }

        [Fact]
        public void TruncateMessage_ShortMessage_ReturnsSame()
        {
            Assert.Equal("Hello", LogStatisticsService.TruncateMessage("Hello", 100));
        }

        [Fact]
        public void TruncateMessage_LongMessage_Truncates()
        {
            var msg = new string('x', 200);
            var result = LogStatisticsService.TruncateMessage(msg, 50);
            Assert.Equal(53, result.Length); // 50 + "..."
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void TruncateMessage_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage(null!, 100));
            Assert.Equal("(empty)", LogStatisticsService.TruncateMessage("", 100));
        }

        [Fact]
        public void GetShortLoggerName_ShortName_ReturnsSame()
        {
            Assert.Equal("Module", LogStatisticsService.GetShortLoggerName("Module"));
        }

        [Fact]
        public void GetShortLoggerName_LongName_ReturnsLastTwo()
        {
            Assert.Equal("Press.Engine", LogStatisticsService.GetShortLoggerName("HP.Indigo.Press.Engine"));
        }

        [Fact]
        public void GetShortLoggerName_TwoParts_ReturnsSame()
        {
            Assert.Equal("Press.Engine", LogStatisticsService.GetShortLoggerName("Press.Engine"));
        }

        [Fact]
        public void GetShortLoggerName_NullOrEmpty_ReturnsUnknown()
        {
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(null!));
            Assert.Equal("Unknown", LogStatisticsService.GetShortLoggerName(""));
        }

        [Fact]
        public void FormatDuration_Minutes()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromMinutes(2.5));
            Assert.Contains("min", result);
        }

        [Fact]
        public void FormatDuration_Seconds()
        {
            var result = LogStatisticsService.FormatDuration(TimeSpan.FromSeconds(30));
            Assert.Contains("sec", result);
        }

        // ====================================================================
        //  CalculateStateEntries tests (S6 path — PlcMngr transitions)
        // ====================================================================

        [Fact]
        public void CalculateStateEntries_S6_DetectsTransitions()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, ThreadName = "Manager", Message = "PlcMngr: IDLE -> RUNNING" },
                new LogEntry { Date = now.AddSeconds(1), ThreadName = "Manager", Message = "PlcMngr: RUNNING -> COOLDOWN" },
                new LogEntry { Date = now.AddSeconds(2), ThreadName = "Worker", Message = "Some other log" }
            };
            var states = LogStatisticsService.CalculateStateEntries(plcLogs);
            Assert.True(states.Count >= 2);
        }

        [Fact]
        public void CalculateStateEntries_S4_DetectsStateEnter()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Message = "==== STATE_IDLE - Enter ======" },
                new LogEntry { Date = now.AddSeconds(1), Message = "Normal log" },
                new LogEntry { Date = now.AddSeconds(2), Message = "==== STATE_RUNNING - Enter ======" }
            };
            var states = LogStatisticsService.CalculateStateEntries(plcLogs);
            Assert.Equal(2, states.Count);
        }

        [Fact]
        public void CalculateStateEntries_Empty_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.CalculateStateEntries(new List<LogEntry>()));
        }

        // ====================================================================
        //  MapErrorsToStates tests
        // ====================================================================

        [Fact]
        public void MapErrorsToStates_MapsCorrectly()
        {
            var now = DateTime.Now;
            var stateEntries = new List<StateEntry>
            {
                new StateEntry { StateName = "IDLE", StartTime = now, EndTime = now.AddSeconds(5) },
                new StateEntry { StateName = "RUNNING", StartTime = now.AddSeconds(5), EndTime = now.AddSeconds(10) }
            };
            var errors = new List<LogEntry>
            {
                new LogEntry { Date = now.AddSeconds(1), Level = "ERROR" },
                new LogEntry { Date = now.AddSeconds(2), Level = "ERROR" },
                new LogEntry { Date = now.AddSeconds(7), Level = "ERROR" }
            };
            var mapped = LogStatisticsService.MapErrorsToStates(errors, stateEntries);
            Assert.True(mapped.Count >= 1);
            var idleCount = mapped.FirstOrDefault(m => m.Name == "IDLE");
            Assert.NotNull(idleCount);
            Assert.Equal(2, idleCount.Count);
        }

        [Fact]
        public void MapErrorsToStates_EmptyErrors_ReturnsEmpty()
        {
            Assert.Empty(LogStatisticsService.MapErrorsToStates(new List<LogEntry>(), new List<StateEntry>()));
        }

        [Fact]
        public void MapErrorsToStates_EmptyStates_ReturnsEmpty()
        {
            var errors = new List<LogEntry> { new LogEntry { Date = DateTime.Now, Level = "ERROR" } };
            Assert.Empty(LogStatisticsService.MapErrorsToStates(errors, new List<StateEntry>()));
        }

        // ====================================================================
        //  SearchCriteria model tests
        // ====================================================================

        [Fact]
        public void SearchCondition_CompiledRegex_NullForNonRegex()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Contains, Value = "test" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_CreatedForRegex()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"\d+" };
            Assert.NotNull(cond.CompiledRegex);
            Assert.True(cond.CompiledRegex!.IsMatch("123"));
        }

        [Fact]
        public void SearchCondition_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = @"[invalid" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void SearchCondition_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var cond = new SearchCondition { Operator = SearchOperator.Regex, Value = "" };
            Assert.Null(cond.CompiledRegex);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_None_ReturnsSelf()
        {
            var filter = new TimeRangeFilter { From = DateTime.Today, To = DateTime.Today.AddDays(1), RelativeRange = RelativeTimeRange.None };
            var resolved = filter.Resolve();
            Assert.Same(filter, resolved);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_Last24Hours()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.True(resolved.From!.Value > DateTime.Now.AddHours(-25));
            Assert.Null(resolved.To);
        }

        [Fact]
        public void TimeRangeFilter_Resolve_LastWeek()
        {
            var filter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
            var resolved = filter.Resolve();
            Assert.NotNull(resolved.From);
            Assert.True(resolved.From!.Value > DateTime.Now.AddDays(-8));
            Assert.Null(resolved.To);
        }

        // ====================================================================
        //  ScheduledSearch model tests
        // ====================================================================

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Hours()
        {
            var sched = new ScheduledSearch { RepeatIntervalValue = 2, IntervalUnit = IntervalUnit.Hours };
            Assert.Equal(120, sched.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Days()
        {
            var sched = new ScheduledSearch { RepeatIntervalValue = 1, IntervalUnit = IntervalUnit.Days };
            Assert.Equal(1440, sched.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Minutes()
        {
            var sched = new ScheduledSearch { RepeatIntervalValue = 30, IntervalUnit = IntervalUnit.Minutes };
            Assert.Equal(30, sched.RepeatIntervalMinutes);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_NoCriteria()
        {
            var sched = new ScheduledSearch();
            Assert.Equal("(no criteria)", sched.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_WithConditions()
        {
            var sched = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "error" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("Message:error", sched.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_PlcOnly()
        {
            var sched = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = false,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[PLC]", sched.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_AppOnly()
        {
            var sched = new ScheduledSearch
            {
                Criteria = new SearchCriteria
                {
                    SearchPLC = false,
                    SearchAPP = true,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[APP]", sched.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_StatsOnly()
        {
            var sched = new ScheduledSearch
            {
                ScanMode = ScanMode.StatisticsOnly,
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[Stats]", sched.SearchSummary);
        }

        [Fact]
        public void ScheduledSearch_SearchSummary_SearchAndStats()
        {
            var sched = new ScheduledSearch
            {
                ScanMode = ScanMode.SearchAndStatistics,
                Criteria = new SearchCriteria
                {
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Value = "test" }
                            }
                        }
                    }
                }
            };
            Assert.Contains("[Search+Stats]", sched.SearchSummary);
        }

        // ====================================================================
        //  ShouldRun tests (SearchSchedulerService)
        // ====================================================================

        private static SearchSchedulerService CreateSchedulerService()
        {
            return (SearchSchedulerService)RuntimeHelpers.GetUninitializedObject(typeof(SearchSchedulerService));
        }

        [Fact]
        public void ShouldRun_DisabledSchedule_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch { IsEnabled = false };
            Assert.False(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_AlreadyRan_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                LastRunTime = DateTime.Now.AddHours(-1)
            };
            Assert.False(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_WithDate_NotYet_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunDate = DateTime.Today.AddDays(1),
                RunTime = TimeSpan.FromHours(10)
            };
            Assert.False(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Once_WithDate_TimeReached_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunDate = DateTime.Today,
                RunTime = TimeSpan.Zero
            };
            Assert.True(svc.ShouldRun(schedule, DateTime.Now));
        }

        [Fact]
        public void ShouldRun_Daily_TimeNotReached_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 8, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                RunTime = TimeSpan.FromHours(23)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Daily_TimeReached_NoLastRun_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                RunTime = TimeSpan.FromHours(9),
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Daily_AlreadyRanToday_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Daily,
                RunTime = TimeSpan.FromHours(9),
                LastRunTime = new DateTime(2026, 1, 1, 9, 30, 0)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_WrongDay_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 5, 10, 0, 0); // Monday
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunTime = TimeSpan.FromHours(9),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Friday }
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_CorrectDay_TimeReached_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            // Find a Monday
            var now = new DateTime(2026, 1, 5, 10, 0, 0); // Monday
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunTime = TimeSpan.FromHours(9),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                LastRunTime = null
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Weekly_AlreadyRanToday_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 5, 10, 0, 0); // Monday
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunTime = TimeSpan.FromHours(9),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
                LastRunTime = new DateTime(2026, 1, 5, 9, 30, 0)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = null,
                RunTime = TimeSpan.Zero
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_FirstRun_BeforeStartTime_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 8, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = null,
                RunTime = TimeSpan.FromHours(10)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_ElapsedEnough_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = now.AddMinutes(-35)
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Interval_NotElapsedEnough_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 10, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Interval,
                RepeatIntervalValue = 30,
                IntervalUnit = IntervalUnit.Minutes,
                LastRunTime = now.AddMinutes(-10)
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        // ====================================================================
        //  SearchSchedulerService.Escape tests (via reflection)
        // ====================================================================

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("simple", "simple")]
        [InlineData("has,comma", "\"has,comma\"")]
        [InlineData("has\"quote", "\"has\"\"quote\"")]
        [InlineData("has\nnewline", "\"has\nnewline\"")]
        public void SearchSchedulerService_Escape(string? input, string expected)
        {
            var method = typeof(SearchSchedulerService).GetMethod("Escape", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = (string)method!.Invoke(null, new object?[] { input })!;
            Assert.Equal(expected, result);
        }

        // ====================================================================
        //  LogFileService helper tests (via reflection)
        // ====================================================================

        [Theory]
        [InlineData("2024-01-15 10:30:45,123 something", true)]
        [InlineData("short", false)]
        [InlineData("xxxx-xx-xx xx:xx:xx,xxx", false)]
        [InlineData("2024-01-15 10:30:45,123", true)]
        public void LogFileService_IsDateStart(string line, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsDateStart", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = (bool)method!.Invoke(null, new object[] { line })!;
            Assert.Equal(expected, result);
        }

        [Fact]
        public void LogFileService_ParseTimestampFast_ValidTimestamp()
        {
            var method = typeof(LogFileService).GetMethod("ParseTimestampFast", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = (DateTime)method!.Invoke(null, new object[] { "2024-01-15 10:30:45,123" })!;
            Assert.Equal(2024, result.Year);
            Assert.Equal(1, result.Month);
            Assert.Equal(15, result.Day);
            Assert.Equal(10, result.Hour);
            Assert.Equal(30, result.Minute);
            Assert.Equal(45, result.Second);
        }

        [Fact]
        public void LogFileService_ParseTimestampFast_7DigitFraction()
        {
            var method = typeof(LogFileService).GetMethod("ParseTimestampFast", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = (DateTime)method!.Invoke(null, new object[] { "2024-01-15 10:30:45,1234567" })!;
            Assert.Equal(2024, result.Year);
        }

        [Theory]
        [InlineData("event-history__FromX.csv", true)]
        [InlineData("pressEvents.data.csv", true)]
        [InlineData("pressEvents.data.xml", true)]
        [InlineData("event-history__FromX.xml", true)]
        [InlineData("random.csv", false)]
        [InlineData("event-history__FromX.txt", false)]
        public void LogFileService_IsEventsFile(string fileName, bool expected)
        {
            var method = typeof(LogFileService).GetMethod("IsEventsFile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var parameters = new object[] { fileName, default(object)! };
            var result = (bool)method!.Invoke(null, parameters)!;
            Assert.Equal(expected, result);
        }

        [Fact]
        public void LogFileService_IsNumericAppFile()
        {
            var method = typeof(LogFileService).GetMethod("IsNumericAppFile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "50300001.file" })!);
            Assert.False((bool)method!.Invoke(null, new object[] { "engineGroupA.file.log" })!);
            Assert.False((bool)method!.Invoke(null, new object[] { "abc.file" })!);
        }

        [Fact]
        public void LogFileService_IsPluginCandidateExtension()
        {
            var method = typeof(LogFileService).GetMethod("IsPluginCandidateExtension", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "test.log" })!);
            Assert.True((bool)method!.Invoke(null, new object[] { "test.csv" })!);
            Assert.False((bool)method!.Invoke(null, new object[] { "test.dll" })!);
        }

        [Fact]
        public void LogFileService_IsSystabFile()
        {
            var method = typeof(LogFileService).GetMethod("IsSystabFile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "diagnosticslogs/systab_saved.txt" })!);
            Assert.False((bool)method!.Invoke(null, new object[] { "systab_saved.txt" })!);
        }

        [Fact]
        public void LogFileService_IsGlobalsXmlFile()
        {
            var method = typeof(LogFileService).GetMethod("IsGlobalsXmlFile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True((bool)method!.Invoke(null, new object[] { "datamanagement/ecommon/globals/file.xml" })!);
            Assert.False((bool)method!.Invoke(null, new object[] { "globals/file.xml" })!);
        }

        [Theory]
        [InlineData("Version: 1.2.3", "PressPlcVersion: 4.5.6", "1.2.3", "4.5.6")]
        [InlineData("Version= 1.2.3", "PressPlcVersion= 4.5.6", "1.2.3", "4.5.6")]
        [InlineData("No version here", "", "Unknown", "Unknown")]
        public void LogFileService_ParseReadmeVersions(string line1, string line2, string expectedSw, string expectedPlc)
        {
            var instance = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("ParseReadmeVersions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = (ValueTuple<string, string>)method!.Invoke(instance, new object[] { $"{line1}\n{line2}" })!;
            Assert.Equal(expectedSw, result.Item1);
            Assert.Equal(expectedPlc, result.Item2);
        }

        [Fact]
        public void LogFileService_SplitCsvLine()
        {
            var instance = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("SplitCsvLine", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = (List<string>)method!.Invoke(instance, new object[] { "a,b,\"hello,world\",c" })!;
            Assert.Equal(4, result.Count);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("hello,world", result[2]);
            Assert.Equal("c", result[3]);
        }

        [Fact]
        public void LogFileService_SortLogEntriesCacheFriendly()
        {
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var now = DateTime.Now;
            var list = new List<LogEntry>
            {
                new LogEntry { Date = now.AddMinutes(2) },
                new LogEntry { Date = now },
                new LogEntry { Date = now.AddMinutes(1) }
            };
            var sorted = (List<LogEntry>)method!.Invoke(null, new object[] { list })!;
            Assert.Equal(now, sorted[0].Date);
            Assert.Equal(now.AddMinutes(1), sorted[1].Date);
            Assert.Equal(now.AddMinutes(2), sorted[2].Date);
        }

        [Fact]
        public void LogFileService_SortLogEntriesCacheFriendly_SingleItem()
        {
            var method = typeof(LogFileService).GetMethod("SortLogEntriesCacheFriendly", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var list = new List<LogEntry> { new LogEntry { Date = DateTime.Now } };
            var sorted = (List<LogEntry>)method!.Invoke(null, new object[] { list })!;
            Assert.Single(sorted);
        }

        [Fact]
        public void LogFileService_CalculatePercent()
        {
            var instance = (LogFileService)RuntimeHelpers.GetUninitializedObject(typeof(LogFileService));
            var method = typeof(LogFileService).GetMethod("CalculatePercent", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = (double)method!.Invoke(instance, new object[] { 50L, 100L })!;
            Assert.Equal(50, result, 1);

            // Zero total
            var resultZero = (double)method!.Invoke(instance, new object[] { 50L, 0L })!;
            Assert.Equal(0, resultZero, 1);
        }

        [Fact]
        public void LogFileService_MapDtoToLogEntry_Public()
        {
            var dto = new IndiLogs.PluginAPI.LogEntryDto
            {
                Date = new DateTime(2024, 1, 1),
                Level = "ERROR",
                Message = "Test message",
                ThreadName = "Thread1",
                Logger = "MyLogger",
                ProcessName = "APP",
                Method = "DoWork",
                Data = "data",
                Exception = "exc"
            };
            var result = LogFileService.MapDtoToLogEntry(dto);
            Assert.Equal(new DateTime(2024, 1, 1), result.Date);
            Assert.Equal("ERROR", result.Level);
            Assert.Equal("Test message", result.Message);
            Assert.Equal("Thread1", result.ThreadName);
            Assert.Equal("MyLogger", result.Logger);
            Assert.Equal("APP", result.ProcessName);
            Assert.Equal("DoWork", result.Method);
            Assert.Equal("data", result.Data);
            Assert.Equal("exc", result.Exception);
        }

        [Fact]
        public void LogFileService_MapDtoToLogEntry_NullFields()
        {
            var dto = new IndiLogs.PluginAPI.LogEntryDto
            {
                Date = DateTime.Now,
                Level = null,
                Message = null,
                ThreadName = null,
                Logger = null,
                ProcessName = null,
                Method = null
            };
            var result = LogFileService.MapDtoToLogEntry(dto);
            Assert.Equal("Info", result.Level);
            Assert.Equal("", result.Message);
            Assert.Equal("", result.ThreadName);
            Assert.Equal("", result.Logger);
            Assert.Equal("", result.ProcessName);
            Assert.Equal("", result.Method);
        }

        // ====================================================================
        //  SearchLocation model tests
        // ====================================================================

        [Fact]
        public void SearchLocation_PropertiesWork()
        {
            var loc = new SearchLocation
            {
                Name = "Test",
                Address = "192.168.1.1",
                BasePath = @"\\server\share",
                IsActive = true,
                ConnectionStatus = ConnectionStatus.Connected,
                LastAccessed = DateTime.Now
            };
            Assert.Equal("Test", loc.Name);
            Assert.Equal("192.168.1.1", loc.Address);
            Assert.Equal(@"\\server\share", loc.BasePath);
            Assert.True(loc.IsActive);
            Assert.Equal(ConnectionStatus.Connected, loc.ConnectionStatus);
            Assert.NotNull(loc.LastAccessed);
        }

        [Fact]
        public void SearchLocation_PropertyChanged_Fires()
        {
            var loc = new SearchLocation();
            var changedProps = new List<string>();
            loc.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

            loc.Name = "New Name";
            loc.Address = "10.0.0.1";
            loc.BasePath = @"\\new\path";
            loc.IsActive = false;
            loc.ConnectionStatus = ConnectionStatus.Disconnected;

            Assert.Contains("Name", changedProps);
            Assert.Contains("Address", changedProps);
            Assert.Contains("BasePath", changedProps);
            Assert.Contains("IsActive", changedProps);
            Assert.Contains("ConnectionStatus", changedProps);
        }

        // ====================================================================
        //  GrepResult model tests
        // ====================================================================

        [Fact]
        public void GrepResult_TimestampDisplay_WithReferencedLogEntry()
        {
            var date = new DateTime(2024, 6, 15, 10, 30, 45, 123);
            var result = new GrepResult
            {
                ReferencedLogEntry = new LogEntry { Date = date }
            };
            Assert.Contains("2024-06-15", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_WithTimestamp()
        {
            var date = new DateTime(2024, 6, 15, 10, 30, 45);
            var result = new GrepResult { Timestamp = date };
            Assert.Contains("2024-06-15", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_TimestampDisplay_NoTimestamp()
        {
            var result = new GrepResult();
            Assert.Equal("N/A", result.TimestampDisplay);
        }

        [Fact]
        public void GrepResult_IsSelected_PropertyChanged()
        {
            var result = new GrepResult();
            var changed = false;
            result.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsSelected") changed = true;
            };
            result.IsSelected = true;
            Assert.True(changed);
            Assert.True(result.IsSelected);
        }

        // ====================================================================
        //  ScheduledSearch RepeatIntervalMinutes setter (backward compat)
        // ====================================================================

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Setter_Days()
        {
            var sched = new ScheduledSearch();
            sched.RepeatIntervalMinutes = 2880; // 2 days
            Assert.Equal(IntervalUnit.Days, sched.IntervalUnit);
            Assert.Equal(2, sched.RepeatIntervalValue);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Setter_Hours()
        {
            var sched = new ScheduledSearch();
            sched.RepeatIntervalMinutes = 120; // 2 hours
            Assert.Equal(IntervalUnit.Hours, sched.IntervalUnit);
            Assert.Equal(2, sched.RepeatIntervalValue);
        }

        [Fact]
        public void ScheduledSearch_RepeatIntervalMinutes_Setter_Minutes()
        {
            var sched = new ScheduledSearch();
            sched.RepeatIntervalMinutes = 45; // 45 minutes
            Assert.Equal(IntervalUnit.Minutes, sched.IntervalUnit);
            Assert.Equal(45, sched.RepeatIntervalValue);
        }

        // ====================================================================
        //  Once schedule without RunDate
        // ====================================================================

        [Fact]
        public void ShouldRun_Once_NoDate_BeforeTime_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 8, 0, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunTime = TimeSpan.FromHours(23),
                LastRunTime = null,
                RunDate = null
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        [Fact]
        public void ShouldRun_Once_NoDate_AfterTime_ReturnsTrue()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 1, 23, 30, 0);
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Once,
                RunTime = TimeSpan.FromHours(23),
                LastRunTime = null,
                RunDate = null
            };
            Assert.True(svc.ShouldRun(schedule, now));
        }

        // ====================================================================
        //  Weekly time not reached
        // ====================================================================

        [Fact]
        public void ShouldRun_Weekly_CorrectDay_BeforeTime_ReturnsFalse()
        {
            var svc = CreateSchedulerService();
            var now = new DateTime(2026, 1, 5, 8, 0, 0); // Monday 8am
            var schedule = new ScheduledSearch
            {
                ScheduleType = ScheduleType.Weekly,
                RunTime = TimeSpan.FromHours(10),
                RunDays = new HashSet<DayOfWeek> { DayOfWeek.Monday }
            };
            Assert.False(svc.ShouldRun(schedule, now));
        }

        // ====================================================================
        //  ComputeStatistics advanced: errors by source
        // ====================================================================

        [Fact]
        public void ComputeStatistics_ErrorsBySource_Combined()
        {
            var now = DateTime.Now;
            var plcLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "ERROR", ThreadName = "Manager", Message = "PLC error" }
            };
            var appLogs = new List<LogEntry>
            {
                new LogEntry { Date = now, Level = "ERROR", Logger = "HP.Indigo.Press.Engine", Message = "APP error" }
            };
            var result = LogStatisticsService.ComputeStatistics(plcLogs, appLogs);
            Assert.True(result.ErrorsBySource.Count >= 2);
            Assert.Contains(result.ErrorsBySource, s => s.Name.Contains("[PLC]"));
            Assert.Contains(result.ErrorsBySource, s => s.Name.Contains("[APP]"));
        }

        // ====================================================================
        //  FilterFilesByTimeRange tests (via GlobalGrepService)
        // ====================================================================

        [Fact]
        public void FilterFilesByTimeRange_NullFilter_ReturnsAll()
        {
            var svc = CreateGrepService();
            var files = new List<string> { "a.log", "b.log" };
            var result = svc.FilterFilesByTimeRange(files, null!);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void FilterFilesByTimeRange_NoFromTo_ReturnsAll()
        {
            var svc = CreateGrepService();
            var files = new List<string> { "a.log", "b.log" };
            var filter = new TimeRangeFilter();
            var result = svc.FilterFilesByTimeRange(files, filter);
            Assert.Equal(2, result.Count);
        }

        // ====================================================================
        //  MatchText edge cases (via EvaluateCondition)
        // ====================================================================

        [Fact]
        public void EvaluateCondition_EmptyValue_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_NullMessage_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = null! };
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "test" };
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        [Fact]
        public void EvaluateCondition_InvalidRegex_ReturnsFalse()
        {
            var svc = CreateGrepService();
            var entry = new LogEntry { Message = "test" };
            // Use a regex that won't compile for CompiledRegex, so it falls back to Regex.IsMatch
            var cond = new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Regex, Value = "[invalid" };
            // CompiledRegex is null due to invalid pattern, falls back to Regex.IsMatch which also fails
            Assert.False(svc.EvaluateCondition(entry, cond));
        }

        // ====================================================================
        //  LogFileService.ReadSampleLines via reflection
        // ====================================================================

        [Fact]
        public void LogFileService_ReadSampleLines_ReadsCorrectCount()
        {
            var method = typeof(LogFileService).GetMethod("ReadSampleLines", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var content = "line1\nline2\nline3\nline4\nline5";
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var result = (string[])method!.Invoke(null, new object[] { ms, 3 })!;
            Assert.Equal(3, result.Length);
            Assert.Equal("line1", result[0]);
        }

        [Fact]
        public void LogFileService_ReadSampleLines_EmptyStream()
        {
            var method = typeof(LogFileService).GetMethod("ReadSampleLines", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            using var ms = new MemoryStream(Array.Empty<byte>());
            var result = (string[])method!.Invoke(null, new object[] { ms, 10 })!;
            Assert.Empty(result);
        }
    }
}
