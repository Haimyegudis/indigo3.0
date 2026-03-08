using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class ViewModelCoverageBatch2Tests
    {
        private const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PI = BindingFlags.Public | BindingFlags.Instance;

        private static T CreateUninitialized<T>() =>
            (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static FieldInfo? FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, NPI | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType!;
            }
            return null;
        }

        private static void SetField(object obj, string fieldName, object? value)
        {
            var field = FindField(obj.GetType(), fieldName);
            field?.SetValue(obj, value);
        }

        private static object? GetField(object obj, string fieldName)
        {
            var field = FindField(obj.GetType(), fieldName);
            return field?.GetValue(obj);
        }

        private static object? InvokeMethod(object obj, string methodName, params object?[] args)
        {
            var type = obj.GetType();
            MethodInfo? method = null;
            while (type != null && method == null)
            {
                method = type.GetMethod(methodName, NPI | PI | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            return method?.Invoke(obj, args);
        }

        private static object? GetProperty(object obj, string propName)
        {
            var prop = obj.GetType().GetProperty(propName, PI | NPI);
            return prop?.GetValue(obj);
        }

        private static void SetProperty(object obj, string propName, object? value)
        {
            var prop = obj.GetType().GetProperty(propName, PI | NPI);
            prop?.SetValue(obj, value);
        }

        // ═══════════════════════════════════════════════════════════
        // 1. MainViewModel.OpenUrl — internal static
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void OpenUrl_NullUrl_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl(null!));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_EmptyUrl_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl(""));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_WhitespaceUrl_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("   "));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_InvalidScheme_FtpBlocked()
        {
            // ftp is not in the allowed schemes (https, http, mailto)
            // Should log warning but not throw
            var ex = Record.Exception(() => MainViewModel.OpenUrl("ftp://example.com"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_JavascriptScheme_Blocked()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("javascript:alert(1)"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_DataScheme_Blocked()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("data:text/html,<h1>hi</h1>"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_FileScheme_Blocked()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("file:///C:/Windows/System32"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_InvalidUri_DoesNotThrow()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("not a valid uri :::"));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenUrl_RelativeUri_Blocked()
        {
            var ex = Record.Exception(() => MainViewModel.OpenUrl("/relative/path"));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. QueryParserService.HasBooleanOperators (static)
        // ═══════════════════════════════════════════════════════════

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("simpleword", false)]
        [InlineData("two words", false)]
        public void HasBooleanOperators_NoBooleans_ReturnsFalse(string? query, bool expected)
        {
            Assert.Equal(expected, QueryParserService.HasBooleanOperators(query!));
        }

        [Theory]
        [InlineData("\"exact phrase\"", true)]
        [InlineData("(grouped)", true)]
        [InlineData("a OR b", true)]
        [InlineData("a AND b", true)]
        [InlineData("a NOT b", true)]
        [InlineData("-negated", true)]
        [InlineData("a | b", true)]
        [InlineData("a ) b", true)]
        public void HasBooleanOperators_WithOperators_ReturnsTrue(string query, bool expected)
        {
            Assert.Equal(expected, QueryParserService.HasBooleanOperators(query));
        }

        // ═══════════════════════════════════════════════════════════
        // 3. QueryParserService.Parse
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Parse_EmptyQuery_ReturnsNull()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("", out string? error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void Parse_NullQuery_ReturnsNull()
        {
            var parser = new QueryParserService();
            var result = parser.Parse(null, out string? error);
            Assert.Null(result);
        }

        [Fact]
        public void Parse_SimpleWord_ReturnsCondition()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Condition, result!.Type);
            Assert.Equal("error", result.Value);
        }

        [Fact]
        public void Parse_OrExpression_ReturnsOrGroup()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error OR warning", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("OR", result.LogicalOperator);
            Assert.Equal(2, result.Children.Count);
        }

        [Fact]
        public void Parse_AndExpression_ReturnsAndGroup()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error AND warning", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
        }

        [Fact]
        public void Parse_NotExpression_ReturnsNotAndGroup()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("-error", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("NOT AND", result.LogicalOperator);
        }

        [Fact]
        public void Parse_Phrase_ReturnsConditionWithPhrase()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"exact match\"", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal("exact match", result!.Value);
        }

        [Fact]
        public void Parse_Parentheses_ParsesGrouped()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(error OR warning) AND fatal", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Group, result!.Type);
        }

        [Fact]
        public void Parse_UnclosedQuote_ReturnsNull()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("\"unclosed", out string? error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void Parse_UnmatchedParen_ReturnsNull()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(error", out string? error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        // ═══════════════════════════════════════════════════════════
        // 4. SystabTopicNode and SystabEntry model tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SystabEntry_DefaultValues()
        {
            var entry = new SystabEntry();
            Assert.Equal("", entry.Parameter);
            Assert.Equal("", entry.Saved);
            Assert.Equal("", entry.Default);
            Assert.Equal("", entry.Minimum);
            Assert.Equal("", entry.Maximum);
            Assert.False(entry.IsDifferent);
        }

        [Fact]
        public void SystabTopicNode_DefaultValues()
        {
            var node = new SystabTopicNode();
            Assert.Equal("", node.Name);
            Assert.Equal("", node.FullPath);
            Assert.False(node.IsExpanded);
            Assert.False(node.IsSelected);
            Assert.False(node.HasDifferences);
            Assert.NotNull(node.Children);
            Assert.Empty(node.Children);
            Assert.NotNull(node.Entries);
            Assert.Empty(node.Entries);
        }

        [Fact]
        public void SystabTopicNode_SetName_RaisesPropertyChanged()
        {
            var node = new SystabTopicNode();
            var raised = new List<string>();
            node.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            node.Name = "TestTopic";
            node.FullPath = "/test";
            node.IsExpanded = true;
            node.IsSelected = true;
            node.HasDifferences = true;

            Assert.Contains("Name", raised);
            Assert.Contains("FullPath", raised);
            Assert.Contains("IsExpanded", raised);
            Assert.Contains("IsSelected", raised);
            Assert.Contains("HasDifferences", raised);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. MainViewModel.Systab — FilterSystabEntries via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void FilterSystabEntries_NoSearch_ShowsAll()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Param1", Saved = "1", Default = "1" },
                new SystabEntry { Parameter = "Param2", Saved = "2", Default = "3", IsDifferent = true }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public void FilterSystabEntries_WithSearchText_FiltersMatching()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Temperature", Saved = "100" },
                new SystabEntry { Parameter = "Pressure", Saved = "200" }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "Temp");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Single(entries);
            Assert.Equal("Temperature", entries[0].Parameter);
        }

        [Fact]
        public void FilterSystabEntries_DiffsOnly_ShowsOnlyDifferent()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "A", IsDifferent = false },
                new SystabEntry { Parameter = "B", IsDifferent = true },
                new SystabEntry { Parameter = "C", IsDifferent = true }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", true);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.True(e.IsDifferent));
        }

        [Fact]
        public void FilterSystabEntries_DiffsAndSearch_CombinesFilters()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "Temp", IsDifferent = true },
                new SystabEntry { Parameter = "Press", IsDifferent = true },
                new SystabEntry { Parameter = "Temp2", IsDifferent = false }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "Temp");
            SetField(vm, "_systabShowDiffsOnly", true);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Single(entries);
            Assert.Equal("Temp", entries[0].Parameter);
        }

        [Fact]
        public void FilterSystabEntries_SearchInSavedField()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "X", Saved = "UniqueValue123" }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "UniqueValue");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Single(entries);
        }

        [Fact]
        public void FilterSystabEntries_SearchInDefaultField()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "X", Default = "DefaultVal" }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "DefaultVal");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Single(entries);
        }

        [Fact]
        public void FilterSystabEntries_SearchInMinMaxFields()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "X", Minimum = "MinVal42" },
                new SystabEntry { Parameter = "Y", Maximum = "MaxVal99" }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", "MinVal");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Single(entries);
            Assert.Equal("X", entries[0].Parameter);
        }

        [Fact]
        public void FilterSystabEntries_NullSearchText_TreatedAsEmpty()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>
            {
                new SystabEntry { Parameter = "A" }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_systabSearchText", null);
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "FilterSystabEntries");

            Assert.Single(entries);
        }

        // ═══════════════════════════════════════════════════════════
        // 6. MainViewModel.LoadSystabEntries via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void LoadSystabEntries_NullSelectedNode_ClearsEntries()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>();
            entries.Add(new SystabEntry { Parameter = "old" });

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_selectedSystabNode", null);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "LoadSystabEntries");

            Assert.Empty(entries);
        }

        [Fact]
        public void LoadSystabEntries_NodeWithEntries_LoadsEntries()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>();

            var node = new SystabTopicNode();
            node.Entries = new List<SystabEntry>
            {
                new SystabEntry { Parameter = "P1" },
                new SystabEntry { Parameter = "P2" }
            };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_selectedSystabNode", node);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "LoadSystabEntries");

            Assert.Equal(2, entries.Count);
            Assert.Equal(2, allEntries.Count);
        }

        [Fact]
        public void LoadSystabEntries_TopicNodeWithChildren_AggregatesChildEntries()
        {
            var vm = CreateUninitialized<MainViewModel>();
            var entries = new ObservableCollection<SystabEntry>();
            var allEntries = new ObservableCollection<SystabEntry>();

            var child1 = new SystabTopicNode();
            child1.Entries = new List<SystabEntry> { new SystabEntry { Parameter = "C1P1" } };

            var child2 = new SystabTopicNode();
            child2.Entries = new List<SystabEntry> { new SystabEntry { Parameter = "C2P1" }, new SystabEntry { Parameter = "C2P2" } };

            var parent = new SystabTopicNode();
            parent.Entries = new List<SystabEntry>(); // empty entries on parent
            parent.Children = new ObservableCollection<SystabTopicNode> { child1, child2 };

            SetField(vm, "_systabEntries", entries);
            SetField(vm, "_allSystabEntries", allEntries);
            SetField(vm, "_selectedSystabNode", parent);
            SetField(vm, "_systabSearchText", "");
            SetField(vm, "_systabShowDiffsOnly", false);

            InvokeMethod(vm, "LoadSystabEntries");

            Assert.Equal(3, entries.Count);
            Assert.Equal(3, allEntries.Count);
        }

        // ═══════════════════════════════════════════════════════════
        // 7. FilterSearchViewModel — FilterState properties via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void FilterState_IsGlobalTimeRangeActive_BothSet_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now.AddHours(-1));
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);

            var result = (bool)GetProperty(vm, "IsGlobalTimeRangeActive")!;
            Assert.True(result);
        }

        [Fact]
        public void FilterState_IsGlobalTimeRangeActive_NullStart_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)DateTime.Now);

            var result = (bool)GetProperty(vm, "IsGlobalTimeRangeActive")!;
            Assert.False(result);
        }

        [Fact]
        public void FilterState_IsGlobalTimeRangeActive_NullEnd_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)DateTime.Now);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);

            var result = (bool)GetProperty(vm, "IsGlobalTimeRangeActive")!;
            Assert.False(result);
        }

        [Fact]
        public void FilterState_IsGlobalTimeRangeActive_BothNull_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);

            var result = (bool)GetProperty(vm, "IsGlobalTimeRangeActive")!;
            Assert.False(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 8. HasMainStoredFilter / HasAppStoredFilter
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void HasMainStoredFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_isTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasMainStoredFilter")!;
            Assert.False(result);
        }

        [Fact]
        public void HasMainStoredFilter_WithThreadFilters_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread-1" });
            SetField(vm, "_isTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasMainStoredFilter")!;
            Assert.True(result);
        }

        [Fact]
        public void HasMainStoredFilter_WithTimeFocus_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_isTimeFocusActive", true);

            var result = (bool)GetProperty(vm, "HasMainStoredFilter")!;
            Assert.True(result);
        }

        [Fact]
        public void HasMainStoredFilter_WithAdvancedFilter_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            var root = new FilterNode { Type = NodeType.Group };
            root.Children.Add(new FilterNode { Type = NodeType.Condition, Value = "test" });
            SetField(vm, "_mainFilterRoot", root);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_isTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasMainStoredFilter")!;
            Assert.True(result);
        }

        [Fact]
        public void HasMainStoredFilterOut_NoFilters_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_negativeFilters", new List<string>());

            var result = (bool)GetProperty(vm, "HasMainStoredFilterOut")!;
            Assert.False(result);
        }

        [Fact]
        public void HasMainStoredFilterOut_WithFilters_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_negativeFilters", new List<string> { "error" });

            var result = (bool)GetProperty(vm, "HasMainStoredFilterOut")!;
            Assert.True(result);
        }

        [Fact]
        public void HasAppStoredFilterOut_NoFilters_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appNegativeFilters", new List<string>());

            var result = (bool)GetProperty(vm, "HasAppStoredFilterOut")!;
            Assert.False(result);
        }

        [Fact]
        public void HasAppStoredFilterOut_WithFilters_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appNegativeFilters", new List<string> { "warn" });

            var result = (bool)GetProperty(vm, "HasAppStoredFilterOut")!;
            Assert.True(result);
        }

        [Fact]
        public void HasAppStoredFilter_NoFilters_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasAppStoredFilter")!;
            Assert.False(result);
        }

        [Fact]
        public void HasAppStoredFilter_WithLoggerFilter_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string> { "MyLogger" });
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasAppStoredFilter")!;
            Assert.True(result);
        }

        [Fact]
        public void HasAppStoredFilter_WithTreeShowOnlyLogger_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", "SomeLogger");
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasAppStoredFilter")!;
            Assert.True(result);
        }

        [Fact]
        public void HasAppStoredFilter_WithHiddenLoggers_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string> { "hidden.logger" });
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", false);

            var result = (bool)GetProperty(vm, "HasAppStoredFilter")!;
            Assert.True(result);
        }

        [Fact]
        public void HasAppStoredFilter_WithAppTimeFocus_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_isAppTimeFocusActive", true);

            var result = (bool)GetProperty(vm, "HasAppStoredFilter")!;
            Assert.True(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 9. FilterSearchViewModel — IsPlcTreeFilterActive
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsPlcTreeFilterActive_NoFilters_ReturnsFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);

            var result = (bool)GetProperty(vm, "IsPlcTreeFilterActive")!;
            Assert.False(result);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithShowOnlyLogger_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_plcTreeShowOnlyLogger", "SomeLogger");
            SetField(vm, "_plcTreeShowOnlyPrefix", null);

            var result = (bool)GetProperty(vm, "IsPlcTreeFilterActive")!;
            Assert.True(result);
        }

        [Fact]
        public void IsPlcTreeFilterActive_WithHiddenPrefixes_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_plcTreeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_plcTreeHiddenPrefixes", new HashSet<string> { "prefix.a" });
            SetField(vm, "_plcTreeShowOnlyLogger", null);
            SetField(vm, "_plcTreeShowOnlyPrefix", null);

            var result = (bool)GetProperty(vm, "IsPlcTreeFilterActive")!;
            Assert.True(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 10. FilterSearchViewModel — SetFilterActive / CheckIfFiltersEmpty
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SetFilterActive_AppTab_SetsAppFilterActive()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppFilterActive", false);

            InvokeMethod(vm, "SetFilterActive", true);

            var result = (bool)GetProperty(vm, "IsAppFilterActive")!;
            Assert.True(result);
        }

        [Fact]
        public void SetFilterActive_MainTab_SetsMainFilterActive()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isMainFilterActive", false);

            InvokeMethod(vm, "SetFilterActive", false);

            var result = (bool)GetProperty(vm, "IsMainFilterActive")!;
            Assert.True(result);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_AllEmpty_ClearsFlag()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string>());
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());
            SetField(vm, "_parent", null); // _parent is null, so NotifyPropertyChanged won't crash

            InvokeMethod(vm, "CheckIfFiltersEmpty", true);

            var result = (bool)GetProperty(vm, "IsAppFilterActive")!;
            Assert.False(result);
        }

        [Fact]
        public void CheckIfFiltersEmpty_AppTab_HasLoggerFilters_DoesNotClear()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppFilterActive", true);
            SetField(vm, "_appActiveThreadFilters", new List<string>());
            SetField(vm, "_activeLoggerFilters", new List<string> { "SomeLogger" });
            SetField(vm, "_activeMethodFilters", new List<string>());
            SetField(vm, "_appFilterRoot", null);
            SetField(vm, "_treeShowOnlyLogger", null);
            SetField(vm, "_treeShowOnlyPrefix", null);
            SetField(vm, "_treeHiddenLoggers", new HashSet<string>());
            SetField(vm, "_treeHiddenPrefixes", new HashSet<string>());

            InvokeMethod(vm, "CheckIfFiltersEmpty", true);

            var result = (bool)GetProperty(vm, "IsAppFilterActive")!;
            Assert.True(result);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_AllEmpty_ClearsFlag()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isMainFilterActive", true);
            SetField(vm, "_activeThreadFilters", new List<string>());
            SetField(vm, "_mainFilterRoot", null);
            SetField(vm, "_parent", null);

            InvokeMethod(vm, "CheckIfFiltersEmpty", false);

            var result = (bool)GetProperty(vm, "IsMainFilterActive")!;
            Assert.False(result);
        }

        [Fact]
        public void CheckIfFiltersEmpty_MainTab_HasThreadFilters_DoesNotClear()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isMainFilterActive", true);
            SetField(vm, "_activeThreadFilters", new List<string> { "Thread-5" });
            SetField(vm, "_mainFilterRoot", null);

            InvokeMethod(vm, "CheckIfFiltersEmpty", false);

            var result = (bool)GetProperty(vm, "IsMainFilterActive")!;
            Assert.True(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 11. FilterSearchViewModel — Range state
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void HasRangeStart_DefaultFalse()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_hasRangeStart", false);

            var result = (bool)GetProperty(vm, "HasRangeStart")!;
            Assert.False(result);
        }

        [Fact]
        public void HasRangeStart_SetTrue_ReturnsTrue()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_hasRangeStart", true);

            var result = (bool)GetProperty(vm, "HasRangeStart")!;
            Assert.True(result);
        }

        // ═══════════════════════════════════════════════════════════
        // 12. FilterSearchViewModel — Filter state setters
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsMainFilterActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isMainFilterActive", false);
            SetProperty(vm, "IsMainFilterActive", true);
            Assert.True((bool)GetProperty(vm, "IsMainFilterActive")!);
        }

        [Fact]
        public void IsAppFilterActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppFilterActive", false);
            SetProperty(vm, "IsAppFilterActive", true);
            Assert.True((bool)GetProperty(vm, "IsAppFilterActive")!);
        }

        [Fact]
        public void IsMainFilterOutActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isMainFilterOutActive", false);
            SetProperty(vm, "IsMainFilterOutActive", true);
            Assert.True((bool)GetProperty(vm, "IsMainFilterOutActive")!);
        }

        [Fact]
        public void IsAppFilterOutActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppFilterOutActive", false);
            SetProperty(vm, "IsAppFilterOutActive", true);
            Assert.True((bool)GetProperty(vm, "IsAppFilterOutActive")!);
        }

        [Fact]
        public void IsTimeFocusActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isTimeFocusActive", false);
            SetProperty(vm, "IsTimeFocusActive", true);
            Assert.True((bool)GetProperty(vm, "IsTimeFocusActive")!);
        }

        [Fact]
        public void IsAppTimeFocusActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppTimeFocusActive", false);
            SetProperty(vm, "IsAppTimeFocusActive", true);
            Assert.True((bool)GetProperty(vm, "IsAppTimeFocusActive")!);
        }

        [Fact]
        public void IsAppErrorFilterActive_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_isAppErrorFilterActive", false);
            SetProperty(vm, "IsAppErrorFilterActive", true);
            Assert.True((bool)GetProperty(vm, "IsAppErrorFilterActive")!);
        }

        // ═══════════════════════════════════════════════════════════
        // 13. MainViewModel.ValidateSearchSyntax via reflection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ValidateSearchSyntax_NullFilterVM_DoesNotThrow()
        {
            var vm = CreateUninitialized<MainViewModel>();
            SetField(vm, "_isSearchSyntaxValid", false);
            // FilterVM is null since uninitialized

            var ex = Record.Exception(() => InvokeMethod(vm, "ValidateSearchSyntax"));
            Assert.Null(ex);
            // Should set valid=true when FilterVM?.SearchText is null/whitespace
            Assert.True((bool)GetProperty(vm, "IsSearchSyntaxValid")!);
        }

        // ═══════════════════════════════════════════════════════════
        // 14. MainViewModel — IsSearchSyntaxValid / SearchSyntaxError
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsSearchSyntaxValid_DefaultTrue()
        {
            var vm = CreateUninitialized<MainViewModel>();
            SetField(vm, "_isSearchSyntaxValid", true);
            Assert.True((bool)GetProperty(vm, "IsSearchSyntaxValid")!);
        }

        [Fact]
        public void SearchSyntaxError_SetAndGet()
        {
            var vm = CreateUninitialized<MainViewModel>();
            SetField(vm, "_searchSyntaxError", null);
            SetProperty(vm, "SearchSyntaxError", "Test error");
            Assert.Equal("Test error", (string?)GetProperty(vm, "SearchSyntaxError"));
        }

        // ═══════════════════════════════════════════════════════════
        // 15. FilterSearchViewModel — GlobalTimeRange properties
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void GlobalTimeRangeStart_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            var dt = new DateTime(2024, 6, 15, 10, 30, 0);
            SetField(vm, "_globalTimeRangeStart", (DateTime?)null);
            SetProperty(vm, "GlobalTimeRangeStart", (DateTime?)dt);
            Assert.Equal(dt, (DateTime?)GetProperty(vm, "GlobalTimeRangeStart"));
        }

        [Fact]
        public void GlobalTimeRangeEnd_SetAndGet()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            var dt = new DateTime(2024, 6, 15, 12, 0, 0);
            SetField(vm, "_globalTimeRangeEnd", (DateTime?)null);
            SetProperty(vm, "GlobalTimeRangeEnd", (DateTime?)dt);
            Assert.Equal(dt, (DateTime?)GetProperty(vm, "GlobalTimeRangeEnd"));
        }

        // ═══════════════════════════════════════════════════════════
        // 16. FilterSearchViewModel — NegativeFilters list access
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void NegativeFilters_ReturnsInternalList()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            var list = new List<string> { "a", "b" };
            SetField(vm, "_negativeFilters", list);
            Assert.Same(list, GetProperty(vm, "NegativeFilters"));
        }

        [Fact]
        public void AppNegativeFilters_ReturnsInternalList()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            var list = new List<string> { "x" };
            SetField(vm, "_appNegativeFilters", list);
            Assert.Same(list, GetProperty(vm, "AppNegativeFilters"));
        }

        [Fact]
        public void ActiveThreadFilters_ReturnsInternalList()
        {
            var vm = CreateUninitialized<FilterSearchViewModel>();
            var list = new List<string> { "t1", "t2" };
            SetField(vm, "_activeThreadFilters", list);
            Assert.Same(list, GetProperty(vm, "ActiveThreadFilters"));
        }

        // ═══════════════════════════════════════════════════════════
        // 17. Parse complex expressions
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Parse_ComplexNestedExpression()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("(a OR b) AND (c OR d)", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal(NodeType.Group, result!.Type);
            Assert.Equal("AND", result.LogicalOperator);
        }

        [Fact]
        public void Parse_MultipleImplicitAnd()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error warning fatal", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            // Should be nested AND groups
            Assert.Equal(NodeType.Group, result!.Type);
        }

        [Fact]
        public void Parse_PipeAsOr()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("error | warning", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal("OR", result!.LogicalOperator);
        }

        [Fact]
        public void Parse_NotWithPhrase()
        {
            var parser = new QueryParserService();
            var result = parser.Parse("-\"bad phrase\"", out string? error);
            Assert.NotNull(result);
            Assert.Null(error);
            Assert.Equal("NOT AND", result!.LogicalOperator);
            Assert.Equal("bad phrase", result.Children[0].Value);
        }
    }
}
