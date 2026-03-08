using IndiLogs_3._0.Services;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class SystabParserServiceTests
    {
        // ── ParseRegContent ──

        [Fact]
        public void ParseRegContent_NullOrEmpty_ReturnsEmptyDictionary()
        {
            Assert.Empty(SystabParserService.ParseRegContent(null!));
            Assert.Empty(SystabParserService.ParseRegContent(""));
        }

        [Fact]
        public void ParseRegContent_ParsesSectionHeaders()
        {
            string content = "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Test]\r\n\"Param1\"=\"Value1\"";
            var result = SystabParserService.ParseRegContent(content);

            Assert.Single(result);
            Assert.Equal("Value1", result[("HKEY_LOCAL_MACHINE\\SOFTWARE\\Test", "Param1")]);
        }

        [Fact]
        public void ParseRegContent_ParsesMultipleEntries()
        {
            string content =
                "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Test]\r\n" +
                "\"Param1\"=\"Value1\"\r\n" +
                "\"Param2\"=\"Value2\"\r\n";
            var result = SystabParserService.ParseRegContent(content);

            Assert.Equal(2, result.Count);
            Assert.Equal("Value1", result[("HKEY_LOCAL_MACHINE\\SOFTWARE\\Test", "Param1")]);
            Assert.Equal("Value2", result[("HKEY_LOCAL_MACHINE\\SOFTWARE\\Test", "Param2")]);
        }

        [Fact]
        public void ParseRegContent_ParsesDwordValues()
        {
            string content = "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Test]\r\n\"HexParam\"=dword:0000001e";
            var result = SystabParserService.ParseRegContent(content);

            Assert.Equal("30", result[("HKEY_LOCAL_MACHINE\\SOFTWARE\\Test", "HexParam")]);
        }

        [Fact]
        public void ParseRegContent_ParsesMultipleSections()
        {
            string content =
                "[Section1]\r\n" +
                "\"A\"=\"1\"\r\n" +
                "[Section2]\r\n" +
                "\"B\"=\"2\"\r\n";
            var result = SystabParserService.ParseRegContent(content);

            Assert.Equal(2, result.Count);
            Assert.Equal("1", result[("Section1", "A")]);
            Assert.Equal("2", result[("Section2", "B")]);
        }

        [Fact]
        public void ParseRegContent_IgnoresLinesBeforeFirstSection()
        {
            string content = "Windows Registry Editor Version 5.00\r\n\r\n\"Orphan\"=\"value\"";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Empty(result);
        }

        // ── ExtractTopicInfo ──

        [Fact]
        public void ExtractTopicInfo_ValidPath_ExtractsCorrectly()
        {
            string path = @"HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Production\Station1\TopicA\0";
            var (topic, station, index) = SystabParserService.ExtractTopicInfo(path);

            Assert.Equal("TopicA", topic);
            Assert.Equal("Station1", station);
            Assert.Equal("0", index);
        }

        [Fact]
        public void ExtractTopicInfo_NullOrEmpty_ReturnsNulls()
        {
            var (topic, station, index) = SystabParserService.ExtractTopicInfo(null!);
            Assert.Null(topic);
            Assert.Null(station);
            Assert.Null(index);
        }

        [Fact]
        public void ExtractTopicInfo_InvalidPath_ReturnsNulls()
        {
            var (topic, station, index) = SystabParserService.ExtractTopicInfo("some\\random\\path");
            Assert.Null(topic);
            Assert.Null(station);
            Assert.Null(index);
        }

        // ── BuildSystabTree ──

        [Fact]
        public void BuildSystabTree_NullInput_ReturnsEmptyCollection()
        {
            var result = SystabParserService.BuildSystabTree(null!);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildSystabTree_EmptyInput_ReturnsEmptyCollection()
        {
            var result = SystabParserService.BuildSystabTree(new Dictionary<string, string>());
            Assert.Empty(result);
        }

        [Fact]
        public void BuildSystabTree_SingleTopic_FlattenedNode()
        {
            string regContent =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Production\Sta1\Motor\0]" + "\r\n" +
                "\"Speed\"=\"100\"\r\n";

            var input = new Dictionary<string, string>
            {
                { "saved", regContent },
                { "default", regContent }
            };

            var tree = SystabParserService.BuildSystabTree(input);

            Assert.Single(tree);
            Assert.Equal("Motor", tree[0].Name);
            Assert.True(tree[0].IsTopLevel);
            Assert.Single(tree[0].Entries);
            Assert.Equal("Speed", tree[0].Entries[0].Parameter);
        }

        [Fact]
        public void BuildSystabTree_DetectsDifferences()
        {
            string savedContent =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Production\Sta1\Motor\0]" + "\r\n" +
                "\"Speed\"=\"200\"\r\n";
            string defaultContent =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Production\Sta1\Motor\0]" + "\r\n" +
                "\"Speed\"=\"100\"\r\n";

            var input = new Dictionary<string, string>
            {
                { "saved", savedContent },
                { "default", defaultContent }
            };

            var tree = SystabParserService.BuildSystabTree(input);

            Assert.Single(tree);
            Assert.True(tree[0].HasDifferences);
            Assert.True(tree[0].Entries[0].IsDifferent);
            Assert.Equal("200", tree[0].Entries[0].Saved);
            Assert.Equal("100", tree[0].Entries[0].Default);
        }

        [Fact]
        public void BuildSystabTree_MultipleIndices_CreatesChildNodes()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Station\TopicB\0]" + "\r\n" +
                "\"A\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\Mode\Station\TopicB\1]" + "\r\n" +
                "\"B\"=\"2\"\r\n";
            var tree = SystabParserService.BuildSystabTree(new Dictionary<string, string> { { "saved", saved } });
            Assert.Single(tree);
            Assert.Equal("TopicB", tree[0].Name);
            Assert.Equal(2, tree[0].Children.Count);
        }

        [Fact]
        public void BuildSystabTree_NumericIndex_DisplaysNameWithIndex()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\MyTopic\0]" + "\r\n" +
                "\"A\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\MyTopic\1]" + "\r\n" +
                "\"B\"=\"2\"\r\n";
            var tree = SystabParserService.BuildSystabTree(new Dictionary<string, string> { { "saved", saved } });
            var childNames = new System.Collections.Generic.List<string>();
            foreach (var c in tree[0].Children) childNames.Add(c.Name);
            Assert.Contains("MyTopic 0", childNames);
            Assert.Contains("MyTopic 1", childNames);
        }

        [Fact]
        public void BuildSystabTree_MultipleTopics_SortedAlphabetically()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Zebra\0]" + "\r\n" +
                "\"X\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Alpha\0]" + "\r\n" +
                "\"Y\"=\"2\"\r\n";
            var tree = SystabParserService.BuildSystabTree(new Dictionary<string, string> { { "saved", saved } });
            Assert.Equal(2, tree.Count);
            Assert.Equal("Alpha", tree[0].Name);
            Assert.Equal("Zebra", tree[1].Name);
        }

        [Fact]
        public void BuildSystabTree_AllFourFiles_PopulatesAllColumns()
        {
            string path = @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\0]" + "\r\n\"P\"=";
            var files = new Dictionary<string, string>
            {
                { "saved",   path + "\"10\"\r\n" },
                { "default", path + "\"20\"\r\n" },
                { "minimum", path + "\"0\"\r\n" },
                { "maximum", path + "\"100\"\r\n" }
            };
            var tree = SystabParserService.BuildSystabTree(files);
            Assert.Single(tree);
            var entry = tree[0].Entries[0];
            Assert.Equal("10", entry.Saved);
            Assert.Equal("20", entry.Default);
            Assert.Equal("0", entry.Minimum);
            Assert.Equal("100", entry.Maximum);
            Assert.True(entry.IsDifferent);
        }

        [Fact]
        public void BuildSystabTree_SameValues_NotDifferent()
        {
            string path = @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\0]" + "\r\n\"P\"=";
            var files = new Dictionary<string, string>
            {
                { "saved",   path + "\"100\"\r\n" },
                { "default", path + "\"100\"\r\n" }
            };
            var tree = SystabParserService.BuildSystabTree(files);
            Assert.False(tree[0].HasDifferences);
        }

        [Fact]
        public void BuildSystabTree_MissingDefaultFile_HandlesGracefully()
        {
            var files = new Dictionary<string, string>
            {
                { "saved", @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\0]" + "\r\n\"P\"=\"val\"\r\n" }
            };
            var tree = SystabParserService.BuildSystabTree(files);
            Assert.Single(tree);
            var entry = tree[0].Entries[0];
            Assert.Equal("val", entry.Saved);
            Assert.Equal("", entry.Default);
        }

        [Fact]
        public void BuildSystabTree_ChildNodeHasDifferences_PropagatedToParent()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\0]" + "\r\n\"A\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\1]" + "\r\n\"B\"=\"2\"\r\n";
            string def =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\0]" + "\r\n\"A\"=\"1\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\1]" + "\r\n\"B\"=\"999\"\r\n";
            var files = new Dictionary<string, string> { { "saved", saved }, { "default", def } };
            var tree = SystabParserService.BuildSystabTree(files);
            Assert.True(tree[0].HasDifferences);
        }

        [Fact]
        public void BuildSystabTree_CountAggregated_ForParentNode()
        {
            string saved =
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\0]" + "\r\n\"A\"=\"1\"\r\n\"B\"=\"2\"\r\n" +
                @"[HKEY_LOCAL_MACHINE\SOFTWARE\Indigo\Unicorn\M\S\Topic\1]" + "\r\n\"C\"=\"3\"\r\n";
            var files = new Dictionary<string, string> { { "saved", saved } };
            var tree = SystabParserService.BuildSystabTree(files);
            Assert.Equal(3, tree[0].Count); // 2 + 1 params total
        }

        [Fact]
        public void ParseRegContent_DwordZero()
        {
            var content = "[S]\r\n\"X\"=dword:00000000\r\n";
            var result = SystabParserService.ParseRegContent(content);
            Assert.Equal("0", result[("S", "X")]);
        }

        [Fact]
        public void ExtractTopicInfo_CaseInsensitive()
        {
            var path = @"HKEY_LOCAL_MACHINE\SOFTWARE\INDIGO\UNICORN\Mode\Station\Topic\0";
            var (topic, station, index) = SystabParserService.ExtractTopicInfo(path);
            Assert.Equal("Topic", topic);
        }
    }
}
