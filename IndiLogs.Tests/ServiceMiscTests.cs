using System;
using System.Collections.Generic;
using System.Reflection;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using Xunit;

namespace IndiLogs.Tests
{
    public class ServiceMiscTests
    {
        // ════════════════════════════════════════════
        //  TextFilterParser.Parse
        // ════════════════════════════════════════════

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TextFilterParser_Parse_BlankInput_ReturnsNull(string? input)
        {
            var result = TextFilterParser.Parse(input);
            Assert.Null(result);
        }

        [Fact]
        public void TextFilterParser_Parse_SingleContains_ReturnsCondition()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'hello')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Condition, node!.Type);
            Assert.Equal("Message", node.Field);
            Assert.Equal("Contains", node.Operator);
            Assert.Equal("hello", node.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_StartsWith_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("StartsWith([Thread], 'main')");
            Assert.NotNull(node);
            Assert.Equal("ThreadName", node!.Field);
            Assert.Equal("Begins With", node.Operator);
            Assert.Equal("main", node.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_EndsWith_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("EndsWith([Level], 'ERROR')");
            Assert.NotNull(node);
            Assert.Equal("Level", node!.Field);
            Assert.Equal("Ends With", node.Operator);
            Assert.Equal("ERROR", node.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_Equals_MapsCorrectly()
        {
            var node = TextFilterParser.Parse("Equals([Logger], 'MyLogger')");
            Assert.NotNull(node);
            Assert.Equal("Logger", node!.Field);
            Assert.Equal("Equals", node.Operator);
            Assert.Equal("MyLogger", node.Value);
        }

        [Fact]
        public void TextFilterParser_Parse_OrCombination_ReturnsGroupWithOR()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'a') Or Contains([Message], 'b')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_AndCombination_ReturnsGroupWithAND()
        {
            var node = TextFilterParser.Parse("Contains([Message], 'a') And Contains([Message], 'b')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
        }

        [Fact]
        public void TextFilterParser_Parse_AndOrPrecedence_AndBindsTighter()
        {
            // A Or B And C → A Or (B And C) — And has higher precedence
            var node = TextFilterParser.Parse(
                "Contains([Message], 'a') Or Contains([Message], 'b') And Contains([Message], 'c')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("OR", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
            // Second child should be an AND group
            Assert.Equal(NodeType.Group, node.Children[1].Type);
            Assert.Equal("AND", node.Children[1].LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_Parentheses_OverridePrecedence()
        {
            var node = TextFilterParser.Parse(
                "(Contains([Message], 'a') Or Contains([Message], 'b')) And Contains([Message], 'c')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("AND", node.LogicalOperator);
            Assert.Equal(2, node.Children.Count);
            // First child should be an OR group
            Assert.Equal(NodeType.Group, node.Children[0].Type);
            Assert.Equal("OR", node.Children[0].LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownField_PassedThrough()
        {
            var node = TextFilterParser.Parse("Contains([CustomField], 'val')");
            Assert.NotNull(node);
            Assert.Equal("CustomField", node!.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_UnknownFunction_DefaultsToContains()
        {
            var node = TextFilterParser.Parse("Matches([Message], 'val')");
            Assert.NotNull(node);
            Assert.Equal("Contains", node!.Operator);
        }

        [Fact]
        public void TextFilterParser_Parse_MissingClosingParen_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("(Contains([Message], 'val')"));
        }

        [Fact]
        public void TextFilterParser_Parse_UnexpectedToken_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Or"));
        }

        [Fact]
        public void TextFilterParser_Parse_TrailingTokens_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                TextFilterParser.Parse("Contains([Message], 'a') Contains([Message], 'b')"));
        }

        [Fact]
        public void TextFilterParser_Parse_CaseInsensitiveKeywords()
        {
            var node = TextFilterParser.Parse("contains([message], 'x') or contains([level], 'y')");
            Assert.NotNull(node);
            Assert.Equal(NodeType.Group, node!.Type);
            Assert.Equal("OR", node.LogicalOperator);
        }

        [Fact]
        public void TextFilterParser_Parse_ThreadNameAlias()
        {
            var node = TextFilterParser.Parse("Contains([ThreadName], 'pool')");
            Assert.NotNull(node);
            Assert.Equal("ThreadName", node!.Field);
        }

        [Fact]
        public void TextFilterParser_Parse_ProcessNameField()
        {
            var node = TextFilterParser.Parse("Contains([ProcessName], 'svc')");
            Assert.NotNull(node);
            Assert.Equal("ProcessName", node!.Field);
        }

        // ════════════════════════════════════════════
        //  MainViewModel.OpenUrl (internal static)
        // ════════════════════════════════════════════

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void OpenUrl_BlankInput_DoesNotThrow(string? url)
        {
            // Should silently return without throwing
            MainViewModel.OpenUrl(url!);
        }

        [Theory]
        [InlineData("ftp://example.com")]
        [InlineData("file:///c:/test")]
        [InlineData("javascript:alert(1)")]
        [InlineData("not-a-url")]
        public void OpenUrl_InvalidOrUnsafeScheme_DoesNotThrow(string url)
        {
            // Should log warning and return without throwing
            MainViewModel.OpenUrl(url);
        }

        // ════════════════════════════════════════════
        //  IoTerminalDataService.ExtractDeviceName
        // ════════════════════════════════════════════

        [Theory]
        [InlineData("Io-BIM[0]_e[1]-Mon_P000.csv", "BIM[0]")]
        [InlineData("Io-ECM_e[1]-something.csv", "ECM")]
        [InlineData("Io-PDC_xyz.csv", "PDC")]
        [InlineData("Io-SimpleDevice.csv", "SimpleDevice")]
        public void ExtractDeviceName_VariousFormats_ReturnsExpected(string fileName, string expected)
        {
            string result = IoTerminalDataService.ExtractDeviceName(fileName);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ExtractDeviceName_NoIoPrefix_ReturnsFirstSegment()
        {
            // If the file doesn't start with "Io-", it still takes before first underscore
            string result = IoTerminalDataService.ExtractDeviceName("Other_device.csv");
            Assert.Equal("Other", result);
        }

        // ════════════════════════════════════════════
        //  IoTerminalDataService.ParseIoFiles
        // ════════════════════════════════════════════

        [Fact]
        public void ParseIoFiles_EmptyDictionaries_ReturnsEmptyList()
        {
            var service = new IoTerminalDataService();
            var result = service.ParseIoFiles(new Dictionary<string, string>());
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_NonIoFiles_ReturnsEmptyList()
        {
            var service = new IoTerminalDataService();
            var files = new Dictionary<string, string>
            {
                { "SomethingElse.csv", "Ticks,rawTime,Timestamp\n1,2,3" }
            };
            var result = service.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_ValidIoCsv_ParsesDevice()
        {
            var service = new IoTerminalDataService();
            string csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal1,Signal2\n" +
                         "100,1000000000000,12:00:00.000,Running,50,1.5,2.5\n" +
                         "200,2000000000000,12:00:01.000,Idle,51,3.0,4.0\n";
            var files = new Dictionary<string, string>
            {
                { "Io-BIM[0]_e[1]-Mon.csv", csv }
            };
            var result = service.ParseIoFiles(files);
            Assert.Single(result);
            Assert.Equal("BIM[0]", result[0].DeviceName);
            Assert.Equal(2, result[0].Columns.Length);
            Assert.Contains("Signal1", result[0].Columns);
            Assert.Contains("Signal2", result[0].Columns);
            Assert.Equal(2, result[0].Rows.Count);
            Assert.Equal("Running", result[0].Rows[0].MachineState);
        }

        [Fact]
        public void ParseIoFiles_ByteSource_PreferredOverStringSource()
        {
            var service = new IoTerminalDataService();
            string csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Voltage\n" +
                         "1,100,10:00:00.000,On,1,3.3\n";
            var stringFiles = new Dictionary<string, string>();
            var byteFiles = new Dictionary<string, byte[]>
            {
                { "Io-ECM_e1.csv", System.Text.Encoding.UTF8.GetBytes(csv) }
            };
            var result = service.ParseIoFiles(stringFiles, byteFiles);
            Assert.Single(result);
            Assert.Equal("ECM", result[0].DeviceName);
        }

        [Fact]
        public void ParseIoFiles_EmptyCsvContent_SkipsDevice()
        {
            var service = new IoTerminalDataService();
            var files = new Dictionary<string, string>
            {
                { "Io-BIM[0]_test.csv", "" }
            };
            var result = service.ParseIoFiles(files);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseIoFiles_HeaderOnly_ParsesDeviceWithNoRows()
        {
            var service = new IoTerminalDataService();
            string csv = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Signal1\n";
            var files = new Dictionary<string, string>
            {
                { "Io-PDC_test.csv", csv }
            };
            var result = service.ParseIoFiles(files);
            Assert.Single(result);
            Assert.Empty(result[0].Rows);
            Assert.Single(result[0].Columns);
        }

        // ════════════════════════════════════════════
        //  IoTerminalDataService.GetAllComponents
        // ════════════════════════════════════════════

        [Fact]
        public void GetAllComponents_EmptyDevices_ReturnsEmptyList()
        {
            var service = new IoTerminalDataService();
            var result = service.GetAllComponents(new List<IoDeviceData>());
            Assert.Empty(result);
        }

        [Fact]
        public void GetAllComponents_MultipleDevices_ReturnsAllColumns()
        {
            var service = new IoTerminalDataService();
            var devices = new List<IoDeviceData>
            {
                new IoDeviceData { DeviceName = "BIM", Columns = new[] { "Sig1", "Sig2" } },
                new IoDeviceData { DeviceName = "ECM", Columns = new[] { "Volt" } },
            };
            var result = service.GetAllComponents(devices);
            Assert.Equal(3, result.Count);
            Assert.Equal("BIM", result[0].Category);
            Assert.Equal("Sig1", result[0].Name);
            Assert.Equal("ECM", result[2].Category);
            Assert.Equal("Volt", result[2].Name);
            Assert.All(result, item => Assert.False(item.IsSelected));
        }

        // ════════════════════════════════════════════
        //  IoTerminalDataService — private helpers via reflection
        // ════════════════════════════════════════════

        [Fact]
        public void SplitCsv_SimpleLine_SplitsCorrectly()
        {
            var method = typeof(IoTerminalDataService).GetMethod("SplitCsv",
                BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(int) }, null);
            Assert.NotNull(method);
            var result = (string[])method!.Invoke(null, new object[] { "a,b,c", 0 })!;
            Assert.Equal(3, result.Length);
            Assert.Equal("a", result[0]);
            Assert.Equal("b", result[1]);
            Assert.Equal("c", result[2]);
        }

        [Fact]
        public void SplitCsv_QuotedField_HandlesQuotes()
        {
            var method = typeof(IoTerminalDataService).GetMethod("SplitCsv",
                BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(int) }, null);
            Assert.NotNull(method);
            var result = (string[])method!.Invoke(null, new object[] { "\"hello,world\",b,c", 0 })!;
            Assert.Equal(3, result.Length);
            Assert.Equal("hello,world", result[0]);
        }

        [Fact]
        public void ParseLong_ValidNumber_ReturnsValue()
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseLong",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (long)method!.Invoke(null, new object?[] { "12345" })!;
            Assert.Equal(12345L, result);
        }

        [Fact]
        public void ParseLong_Null_ReturnsZero()
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseLong",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (long)method!.Invoke(null, new object?[] { null })!;
            Assert.Equal(0L, result);
        }

        [Fact]
        public void ParseLong_Invalid_ReturnsZero()
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseLong",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (long)method!.Invoke(null, new object?[] { "abc" })!;
            Assert.Equal(0L, result);
        }

        [Fact]
        public void ParseTimestamp_ValidTime_ReturnsParsedDateTime()
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseTimestamp",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (DateTime)method!.Invoke(null, new object?[] { "14:30:05.123" })!;
            Assert.Equal(14, result.Hour);
            Assert.Equal(30, result.Minute);
            Assert.Equal(5, result.Second);
        }

        [Fact]
        public void ParseTimestamp_NullOrEmpty_ReturnsMinValue()
        {
            var method = typeof(IoTerminalDataService).GetMethod("ParseTimestamp",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (DateTime)method!.Invoke(null, new object?[] { null })!;
            Assert.Equal(DateTime.MinValue, result);

            var result2 = (DateTime)method!.Invoke(null, new object?[] { "   " })!;
            Assert.Equal(DateTime.MinValue, result2);
        }

        [Fact]
        public void FormatTimestampUs_Zero_ReturnsEmpty()
        {
            var method = typeof(IoTerminalDataService).GetMethod("FormatTimestampUs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = (string)method!.Invoke(null, new object[] { 0L })!;
            Assert.Equal("", result);
        }

        [Fact]
        public void FormatTimestampUs_ValidNanoseconds_ReturnsFormattedTime()
        {
            var method = typeof(IoTerminalDataService).GetMethod("FormatTimestampUs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            // 1_000_000_000_000 ns = 1_000_000_000 us = 1000 seconds since epoch
            long ns = 1_700_000_000_000_000_000L; // ~2023-11-14
            var result = (string)method!.Invoke(null, new object[] { ns })!;
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains(":", result); // should contain time separators
            Assert.Contains(".", result); // should contain microsecond separator
        }

        // ════════════════════════════════════════════
        //  FilterNode.DeepClone
        // ════════════════════════════════════════════

        [Fact]
        public void FilterNode_DeepClone_ClonesAllProperties()
        {
            var original = new FilterNode
            {
                Type = NodeType.Condition,
                Field = "Message",
                Operator = "Contains",
                Value = "test",
                LogicalOperator = "AND"
            };
            var clone = original.DeepClone();
            Assert.Equal(original.Type, clone.Type);
            Assert.Equal(original.Field, clone.Field);
            Assert.Equal(original.Operator, clone.Operator);
            Assert.Equal(original.Value, clone.Value);
            Assert.Equal(original.LogicalOperator, clone.LogicalOperator);
            // Modifying clone should not affect original
            clone.Value = "modified";
            Assert.Equal("test", original.Value);
        }

        [Fact]
        public void FilterNode_DeepClone_ClonesChildren()
        {
            var parent = new FilterNode
            {
                Type = NodeType.Group,
                LogicalOperator = "OR"
            };
            parent.Children.Add(new FilterNode { Type = NodeType.Condition, Field = "Level", Value = "ERROR" });
            parent.Children.Add(new FilterNode { Type = NodeType.Condition, Field = "Level", Value = "WARN" });

            var clone = parent.DeepClone();
            Assert.Equal(2, clone.Children.Count);
            Assert.Equal("ERROR", clone.Children[0].Value);
            // Children are separate instances
            clone.Children[0].Value = "INFO";
            Assert.Equal("ERROR", parent.Children[0].Value);
        }

        // ════════════════════════════════════════════
        //  FilterNode.CompiledRegex
        // ════════════════════════════════════════════

        [Fact]
        public void FilterNode_CompiledRegex_NonRegexOperator_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Contains", Value = "test" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_RegexOperator_ReturnsCompiledRegex()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var regex = node.CompiledRegex;
            Assert.NotNull(regex);
            Assert.True(regex!.IsMatch("abc123"));
            Assert.False(regex.IsMatch("abc"));
        }

        [Fact]
        public void FilterNode_CompiledRegex_InvalidPattern_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Regex", Value = "[invalid" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_EmptyValue_ReturnsNull()
        {
            var node = new FilterNode { Operator = "Regex", Value = "" };
            Assert.Null(node.CompiledRegex);
        }

        [Fact]
        public void FilterNode_CompiledRegex_CachesAndInvalidatesOnValueChange()
        {
            var node = new FilterNode { Operator = "Regex", Value = @"\d+" };
            var regex1 = node.CompiledRegex;
            var regex2 = node.CompiledRegex;
            Assert.Same(regex1, regex2); // should be cached

            node.Value = @"\w+"; // changing value should invalidate cache
            var regex3 = node.CompiledRegex;
            Assert.NotSame(regex1, regex3);
        }

        // ════════════════════════════════════════════
        //  PluginLoader.GetDllPath
        // ════════════════════════════════════════════

        [Fact]
        public void PluginLoader_GetDllPath_NullPlugin_ReturnsNull()
        {
            // PluginLoader constructor triggers Reload which scans folders,
            // so we use GetUninitializedObject to avoid side effects
            var loader = (PluginLoader)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(PluginLoader));
            var result = loader.GetDllPath(null!);
            Assert.Null(result);
        }

        // ════════════════════════════════════════════
        //  AuthenticodeVerifier.IsTrustedSubject
        // ════════════════════════════════════════════

        [Theory]
        [InlineData("CN=Test, O=HP Inc, C=US", true)]
        [InlineData("CN=Test, O=HP, C=US", true)]
        [InlineData("CN=Test, O=HP Indigo, C=US", true)]
        [InlineData("CN=Test, O=Hewlett-Packard, C=US", true)]
        [InlineData("CN=Test, O=Unknown Corp, C=US", false)]
        [InlineData("CN=Test, C=US", false)]
        [InlineData("", false)]
        public void AuthenticodeVerifier_IsTrustedSubject_VariousSubjects(string subject, bool expected)
        {
            var result = AuthenticodeVerifier.IsTrustedSubject(subject);
            Assert.Equal(expected, result);
        }

        // ════════════════════════════════════════════
        //  AuthenticodeVerifier.ParseDnField
        // ════════════════════════════════════════════

        [Fact]
        public void ParseDnField_NullDn_ReturnsNull()
        {
            var result = AuthenticodeVerifier.ParseDnField(null!, "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_EmptyDn_ReturnsNull()
        {
            var result = AuthenticodeVerifier.ParseDnField("", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_FieldNotPresent_ReturnsNull()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, C=US", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_QuotedValue_ParsesCorrectly()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=\"HP Inc\", C=US", "O");
            Assert.Equal("HP Inc", result);
        }

        [Fact]
        public void ParseDnField_UnquotedValue_ParsesCorrectly()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=HP, C=US", "O");
            Assert.Equal("HP", result);
        }

        // ════════════════════════════════════════════
        //  IoTerminalDataService — multiple devices sorted
        // ════════════════════════════════════════════

        [Fact]
        public void ParseIoFiles_MultipleDevices_SortedByName()
        {
            var service = new IoTerminalDataService();
            string csv1 = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,X\n1,1,10:00:00,S,0,1\n";
            string csv2 = "Ticks,rawTime,Timestamp,MachineState,TotalImpressions,Y\n1,1,10:00:00,S,0,2\n";
            var files = new Dictionary<string, string>
            {
                { "Io-ZDevice_test.csv", csv1 },
                { "Io-ADevice_test.csv", csv2 },
            };
            var result = service.ParseIoFiles(files);
            Assert.Equal(2, result.Count);
            Assert.Equal("ADevice", result[0].DeviceName);
            Assert.Equal("ZDevice", result[1].DeviceName);
        }
    }
}
