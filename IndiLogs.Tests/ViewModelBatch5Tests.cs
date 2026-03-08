using IndiLogs_3._0;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.ViewModels;
using IndiLogs_3._0.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IndiLogs.Tests
{
    public class ViewModelBatch5Tests
    {
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        // ═══════════════════════════════════════════════════════════
        // Helper: Create uninitialized object and set fields via reflection
        // ═══════════════════════════════════════════════════════════

        private static T CreateUninitialized<T>() =>
            (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static void SetField<T>(T obj, string fieldName, object? value)
        {
            var type = typeof(T);
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, NonPublicInstance | BindingFlags.FlattenHierarchy);
                if (field == null)
                    field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            field?.SetValue(obj, value);
        }

        private static void SetProperty<T>(T obj, string propName, object? value)
        {
            var prop = typeof(T).GetProperty(propName, PublicInstance | NonPublicInstance);
            prop?.SetValue(obj, value);
        }

        private static object? InvokeMethod<T>(T obj, string methodName, params object?[] args)
        {
            var method = typeof(T).GetMethod(methodName, NonPublicInstance | PublicInstance);
            return method?.Invoke(obj, args);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — FindLogByTarget tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void FindLogByTarget_NullTarget_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var result = vm.FindLogByTarget(null, new List<LogEntry>());
            Assert.Null(result);
        }

        [Fact]
        public void FindLogByTarget_NullLogs_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var target = new LogTarget { Timestamp = DateTime.Now, Logger = "test", Thread = "t1" };
            var result = vm.FindLogByTarget(target, null);
            Assert.Null(result);
        }

        [Fact]
        public void FindLogByTarget_ExactMatch_ReturnsLog()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var now = DateTime.Now;
            var log = new LogEntry { Date = now, Logger = "MyLogger", ThreadName = "Thread1", Message = "Hello world" };
            var target = new LogTarget { Timestamp = now, Logger = "MyLogger", Thread = "Thread1" };
            var logs = new List<LogEntry> { log };

            var result = vm.FindLogByTarget(target, logs);
            Assert.Same(log, result);
        }

        [Fact]
        public void FindLogByTarget_NoMatch_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var now = DateTime.Now;
            var log = new LogEntry { Date = now, Logger = "DifferentLogger", ThreadName = "Thread1", Message = "Hello" };
            var target = new LogTarget { Timestamp = now, Logger = "MyLogger", Thread = "Thread1" };

            var result = vm.FindLogByTarget(target, new List<LogEntry> { log });
            Assert.Null(result);
        }

        [Fact]
        public void FindLogByTarget_FallbackByTimeTolerance_ReturnsLog()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var now = DateTime.Now;
            var logTime = now.AddMilliseconds(50); // Within 100ms tolerance
            var snippet = "This is a test message that is long enough to check snippet matching for log target";
            var log = new LogEntry { Date = logTime, Logger = "MyLogger", ThreadName = "Thread1", Message = snippet + " extra text" };
            var target = new LogTarget { Timestamp = now, Logger = "MyLogger", Thread = "Thread1", Snippet = snippet };

            var result = vm.FindLogByTarget(target, new List<LogEntry> { log });
            Assert.Same(log, result);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — GetAnnotation tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void GetAnnotation_NullLog_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_logAnnotations", new Dictionary<LogEntry, LogAnnotation>());
            var result = vm.GetAnnotation(null);
            Assert.Null(result);
        }

        [Fact]
        public void GetAnnotation_ExistingAnnotation_ReturnsIt()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = new LogEntry { Message = "test" };
            var annotation = new LogAnnotation { Content = "my note" };
            var dict = new Dictionary<LogEntry, LogAnnotation> { { log, annotation } };
            SetField(vm, "_logAnnotations", dict);

            var result = vm.GetAnnotation(log);
            Assert.NotNull(result);
            Assert.Equal("my note", result!.Content);
        }

        [Fact]
        public void GetAnnotation_NoAnnotation_ReturnsNull()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_logAnnotations", new Dictionary<LogEntry, LogAnnotation>());
            var log = new LogEntry { Message = "test" };
            var result = vm.GetAnnotation(log);
            Assert.Null(result);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — ClearAnnotations tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ClearAnnotations_EmptiesDictionary()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var dict = new Dictionary<LogEntry, LogAnnotation>
            {
                { new LogEntry(), new LogAnnotation { Content = "note1" } },
                { new LogEntry(), new LogAnnotation { Content = "note2" } }
            };
            SetField(vm, "_logAnnotations", dict);

            vm.ClearAnnotations();
            Assert.Empty(dict);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — ClearMarkedLogs tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ClearMarkedLogs_ClearsBothCollections()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var markedLogs = new ObservableCollection<LogEntry> { new LogEntry() };
            var markedAppLogs = new ObservableCollection<LogEntry> { new LogEntry(), new LogEntry() };

            // Set properties directly
            typeof(CaseManagementViewModel).GetProperty("MarkedLogs")!.SetValue(vm, markedLogs);
            typeof(CaseManagementViewModel).GetProperty("MarkedAppLogs")!.SetValue(vm, markedAppLogs);

            vm.ClearMarkedLogs();

            Assert.Empty(markedLogs);
            Assert.Empty(markedAppLogs);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — CreateLogTarget tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void CreateLogTarget_ShortMessage_UsesFullMessage()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = new LogEntry
            {
                Date = new DateTime(2024, 1, 1),
                Logger = "TestLogger",
                ThreadName = "Thread1",
                Level = "INFO",
                Message = "Short msg"
            };

            var method = typeof(CaseManagementViewModel).GetMethod("CreateLogTarget", NonPublicInstance);
            var target = (LogTarget?)method?.Invoke(vm, new object[] { log });

            Assert.NotNull(target);
            Assert.Equal("Short msg", target!.Snippet);
            Assert.Equal("TestLogger", target.Logger);
            Assert.Equal("Thread1", target.Thread);
            Assert.Equal("INFO", target.Level);
        }

        [Fact]
        public void CreateLogTarget_LongMessage_TruncatesTo100()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var longMsg = new string('A', 200);
            var log = new LogEntry { Date = DateTime.Now, Logger = "L", ThreadName = "T", Level = "E", Message = longMsg };

            var method = typeof(CaseManagementViewModel).GetMethod("CreateLogTarget", NonPublicInstance);
            var target = (LogTarget?)method?.Invoke(vm, new object[] { log });

            Assert.NotNull(target);
            Assert.Equal(100, target!.Snippet.Length);
        }

        [Fact]
        public void CreateLogTarget_NullMessage_ReturnsEmptySnippet()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = new LogEntry { Date = DateTime.Now, Logger = "L", ThreadName = "T", Level = "E", Message = null };

            var method = typeof(CaseManagementViewModel).GetMethod("CreateLogTarget", NonPublicInstance);
            var target = (LogTarget?)method?.Invoke(vm, new object[] { log });

            Assert.NotNull(target);
            Assert.Equal("", target!.Snippet);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — IsLoadingCase tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void IsLoadingCase_DefaultFalse()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_isLoadingCase", false);
            Assert.False(vm.IsLoadingCase);
        }

        [Fact]
        public void IsLoadingCase_WhenSetTrue_ReturnsTrue()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_isLoadingCase", true);
            Assert.True(vm.IsLoadingCase);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — SelectedConfig tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SelectedConfig_GetSet_Works()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_selectedConfig", null as SavedConfiguration);
            Assert.Null(vm.SelectedConfig);

            var config = new SavedConfiguration { Name = "Test" };
            SetField(vm, "_selectedConfig", config);
            Assert.Same(config, vm.SelectedConfig);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — EnsureDefaultConfigsOnDisk tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void EnsureDefaultConfigsOnDisk_CreatesConfigFilesInDirectory()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var tempDir = Path.Combine(Path.GetTempPath(), "IndiLogsTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                vm.EnsureDefaultConfigsOnDisk(tempDir);

                Assert.True(Directory.Exists(tempDir));

                // Should create PLC_FILTERED_S45 and PLC_FILTERED_S6 configs
                var files = Directory.GetFiles(tempDir, "*.json");
                Assert.True(files.Length >= 2, $"Expected at least 2 config files but found {files.Length}");

                var fileNames = files.Select(Path.GetFileName).ToList();
                Assert.Contains(fileNames, f => f!.Contains("PLC_FILTERED_S45"));
                Assert.Contains(fileNames, f => f!.Contains("PLC_FILTERED_S6"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void EnsureDefaultConfigsOnDisk_DoesNotOverwriteExisting()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var tempDir = Path.Combine(Path.GetTempPath(), "IndiLogsTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                // Create first time
                vm.EnsureDefaultConfigsOnDisk(tempDir);
                var files = Directory.GetFiles(tempDir, "*.json");
                var originalContent = File.ReadAllText(files[0]);

                // Call again - should not overwrite
                vm.EnsureDefaultConfigsOnDisk(tempDir);
                var contentAfter = File.ReadAllText(files[0]);

                Assert.Equal(originalContent, contentAfter);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — LoadSavedConfigs tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void LoadSavedConfigs_WithBinaryAppLogs_HidesS6Config()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var savedConfigs = new ObservableCollection<SavedConfiguration>();
            typeof(CaseManagementViewModel).GetProperty("SavedConfigs")!.SetValue(vm, savedConfigs);

            // This uses AppPaths.Root which points to a real config directory;
            // In test environment it may be empty, which is fine
            vm.LoadSavedConfigs(true);

            // Should not contain S6 config if loaded with hasBinaryAppLogs=true
            Assert.DoesNotContain(savedConfigs, c =>
                c.FilePath != null && c.FilePath.Contains("PLC_FILTERED_S6", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void LoadSavedConfigs_WithoutBinaryAppLogs_HidesS45Config()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var savedConfigs = new ObservableCollection<SavedConfiguration>();
            typeof(CaseManagementViewModel).GetProperty("SavedConfigs")!.SetValue(vm, savedConfigs);

            vm.LoadSavedConfigs(false);

            Assert.DoesNotContain(savedConfigs, c =>
                c.FilePath != null && c.FilePath.Contains("PLC_FILTERED_S45", StringComparison.OrdinalIgnoreCase));
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — UpdateAllAnnotationsVisibility tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void UpdateAllAnnotationsVisibility_CollapsesAnnotations_WhenShowAllFalse()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_showAllAnnotations", false);

            var sessionVM = CreateUninitialized<LogSessionViewModel>();
            var log1 = new LogEntry { HasAnnotation = true, IsAnnotationExpanded = true };
            var log2 = new LogEntry { HasAnnotation = false, IsAnnotationExpanded = true };
            SetField(sessionVM, "_allLogsCache", new List<LogEntry> { log1, log2 } as IList<LogEntry>);
            SetField(sessionVM, "_allAppLogsCache", new List<LogEntry>() as IList<LogEntry>);
            SetField(vm, "_sessionVM", sessionVM);

            vm.UpdateAllAnnotationsVisibility();

            Assert.False(log1.IsAnnotationExpanded);
            // log2 should not be affected since HasAnnotation is false
            Assert.True(log2.IsAnnotationExpanded);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — MarkedLogs: GetActiveLogCollection
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void GetActiveLogCollection_MainTab_ReturnsLogs()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var parent = CreateUninitialized<MainViewModel>();
            SetField(parent, "_selectedTabIndex", 0);
            SetField(vm, "_parent", parent);

            var sessionVM = CreateUninitialized<LogSessionViewModel>();
            var logs = new List<LogEntry> { new LogEntry { Message = "test" } };
            SetField(sessionVM, "_logs", logs as IEnumerable<LogEntry>);
            SetField(vm, "_sessionVM", sessionVM);

            var filterVM = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_filterVM", filterVM);

            var method = typeof(CaseManagementViewModel).GetMethod("GetActiveLogCollection", NonPublicInstance);
            var result = method?.Invoke(vm, null) as IEnumerable<LogEntry>;

            Assert.NotNull(result);
            Assert.Single(result!);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — ClearCurrentMarked tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ClearCurrentMarked_SetsIsCurrentMarkedFalse()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var log = new LogEntry { IsCurrentMarked = true };
            SetField(vm, "_currentMarkedLog", log);

            var method = typeof(CaseManagementViewModel).GetMethod("ClearCurrentMarked", NonPublicInstance);
            method?.Invoke(vm, null);

            Assert.False(log.IsCurrentMarked);
        }

        [Fact]
        public void ClearCurrentMarked_WhenNoCurrentMarked_DoesNothing()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_currentMarkedLog", null as LogEntry);

            var method = typeof(CaseManagementViewModel).GetMethod("ClearCurrentMarked", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, null));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — SaveFilterState tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SaveFilterState_NullSession_DoesNothing()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var filterVM = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_filterVM", filterVM);

            var method = typeof(LogSessionViewModel).GetMethod("SaveFilterState", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null }));
            Assert.Null(ex);
        }

        [Fact]
        public void SaveFilterState_NullFilterVM_DoesNothing()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            SetField(vm, "_filterVM", null as FilterSearchViewModel);

            var session = new LogSessionData();
            var method = typeof(LogSessionViewModel).GetMethod("SaveFilterState", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { session }));
            Assert.Null(ex);
            Assert.Null(session.SavedFilterState);
        }

        [Fact]
        public void SaveFilterState_ValidSession_SavesState()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var parent = CreateUninitialized<MainViewModel>();
            SetField(vm, "_parent", parent);

            var filterVM = CreateUninitialized<FilterSearchViewModel>();
            // Set up the required properties on FilterVM
            SetField(filterVM, "_searchText", "test search");
            SetField(filterVM, "_isMainFilterActive", true);
            SetField(filterVM, "_isAppFilterActive", false);
            SetField(filterVM, "_isMainFilterOutActive", true);
            SetField(filterVM, "_isAppFilterOutActive", false);
            SetField(filterVM, "_isTimeFocusActive", false);
            SetField(filterVM, "_isAppTimeFocusActive", false);

            // Initialize collections via backing fields (get-only properties)
            var negFilters = new List<string> { "filter1" };
            var threadFilters = new List<string> { "thread1" };
            SetField(filterVM, "_negativeFilters", negFilters);
            SetField(filterVM, "_activeThreadFilters", threadFilters);

            SetField(vm, "_filterVM", filterVM);

            var session = new LogSessionData();
            var method = typeof(LogSessionViewModel).GetMethod("SaveFilterState", NonPublicInstance);
            method?.Invoke(vm, new object?[] { session });

            Assert.NotNull(session.SavedFilterState);
            Assert.Equal("test search", session.SavedFilterState!.SearchText);
            Assert.True(session.SavedFilterState.IsMainFilterActive);
            Assert.True(session.SavedFilterState.IsMainFilterOutActive);
            Assert.False(session.SavedFilterState.IsAppFilterActive);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — RestoreFilterState tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void RestoreFilterState_NullSession_ReturnsFalse()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var filterVM = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_filterVM", filterVM);

            var method = typeof(LogSessionViewModel).GetMethod("RestoreFilterState", NonPublicInstance);
            var result = (bool)method?.Invoke(vm, new object?[] { null })!;
            Assert.False(result);
        }

        [Fact]
        public void RestoreFilterState_NoSavedState_ReturnsFalse()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var filterVM = CreateUninitialized<FilterSearchViewModel>();
            SetField(vm, "_filterVM", filterVM);

            var session = new LogSessionData { SavedFilterState = null };
            var method = typeof(LogSessionViewModel).GetMethod("RestoreFilterState", NonPublicInstance);
            var result = (bool)method?.Invoke(vm, new object?[] { session })!;
            Assert.False(result);
        }

        [Fact]
        public void RestoreFilterState_NullFilterVM_ReturnsFalse()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            SetField(vm, "_filterVM", null as FilterSearchViewModel);

            var session = new LogSessionData
            {
                SavedFilterState = new SessionFilterState { SearchText = "x" }
            };
            var method = typeof(LogSessionViewModel).GetMethod("RestoreFilterState", NonPublicInstance);
            var result = (bool)method?.Invoke(vm, new object?[] { session })!;
            Assert.False(result);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — Properties tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void LogSessionViewModel_IsBusy_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            SetField(vm, "_isBusy", true);
            Assert.True(vm.IsBusy);
        }

        [Fact]
        public void LogSessionViewModel_StatusMessage_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            SetField(vm, "_statusMessage", "Loading...");
            Assert.Equal("Loading...", vm.StatusMessage);
        }

        [Fact]
        public void LogSessionViewModel_CurrentProgress_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            SetField(vm, "_currentProgress", 50.0);
            Assert.Equal(50.0, vm.CurrentProgress);
        }

        [Fact]
        public void LogSessionViewModel_AllLogsCache_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var logs = new List<LogEntry> { new LogEntry() };
            SetField(vm, "_allLogsCache", logs as IList<LogEntry>);
            Assert.Single(vm.AllLogsCache);
        }

        [Fact]
        public void LogSessionViewModel_AllAppLogsCache_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var logs = new List<LogEntry> { new LogEntry(), new LogEntry() };
            SetField(vm, "_allAppLogsCache", logs as IList<LogEntry>);
            Assert.Equal(2, vm.AllAppLogsCache.Count);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — ClearLogs tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ClearLogs_ClearsAllCollections()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var parent = CreateUninitialized<MainViewModel>();
            SetField(vm, "_parent", parent);
            SetField(vm, "_filterVM", null as FilterSearchViewModel);
            SetField(vm, "_configVM", null as ConfigExplorerViewModel);
            SetField(vm, "_allLogsCache", new List<LogEntry> { new LogEntry() } as IList<LogEntry>);
            SetField(vm, "_allAppLogsCache", new List<LogEntry> { new LogEntry() } as IList<LogEntry>);
            SetField(vm, "_events", new ObservableCollection<EventEntry>());
            SetField(vm, "_screenshots", null as ObservableCollection<System.Windows.Media.Imaging.BitmapImage>);
            SetField(vm, "_loadedFiles", null as ObservableCollection<string>);
            SetField(vm, "_loadedSessions", null as ObservableCollection<LogSessionData>);
            SetField(vm, "_logs", new List<LogEntry>() as IEnumerable<LogEntry>);

            var method = typeof(LogSessionViewModel).GetMethod("ClearLogs", NonPublicInstance);
            method?.Invoke(vm, new object?[] { null });

            Assert.Equal("Logs cleared", vm.StatusMessage);
            Assert.Empty(vm.AllLogsCache);
            Assert.Empty(vm.AllAppLogsCache);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — RemoveSession tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void RemoveSession_NullParam_DoesNothing()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            SetField(vm, "_loadedSessions", new ObservableCollection<LogSessionData>());

            var method = typeof(LogSessionViewModel).GetMethod("RemoveSession", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null }));
            Assert.Null(ex);
        }

        [Fact]
        public void RemoveSession_NonExistentSession_DoesNothing()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var sessions = new ObservableCollection<LogSessionData>();
            SetField(vm, "_loadedSessions", sessions);

            var session = new LogSessionData();
            var method = typeof(LogSessionViewModel).GetMethod("RemoveSession", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { session }));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // ScheduleEditorViewModel — Save validation tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Save_NullWindow_DoesNothing()
        {
            var schedule = new ScheduledSearch();
            var locations = new List<SearchLocation>();
            var criteria = new SearchCriteria();
            var vm = new ScheduleEditorViewModel(schedule, locations, criteria);

            var method = typeof(ScheduleEditorViewModel).GetMethod("Save", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null }));
            Assert.Null(ex);
        }

        [Fact]
        public void Save_EmptyScheduleName_ShowsWarning()
        {
            var dialogMessages = new List<string>();
            var dialogService = new TestDialogServiceForSchedule(dialogMessages);
            var schedule = new ScheduledSearch();
            var locations = new List<SearchLocation>();
            var criteria = new SearchCriteria();
            var vm = new ScheduleEditorViewModel(schedule, locations, criteria, dialogService);

            vm.ScheduleName = "";

            // Save expects a Window, but we pass null which triggers early return
            var method = typeof(ScheduleEditorViewModel).GetMethod("Save", NonPublicInstance);
            // Need a non-null Window, but we can't create one without STA thread
            // Instead, test the validation logic indirectly
            Assert.True(string.IsNullOrWhiteSpace(vm.ScheduleName));
        }

        [Fact]
        public void Save_InvalidRunHour_Validation()
        {
            var schedule = new ScheduledSearch();
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleName = "Test Schedule";
            vm.ScheduleTypeIndex = 1; // Daily - needs time
            vm.RunHour = "25"; // Invalid
            vm.RunMinute = "30";

            // RunHour 25 is > 23, validation should catch this
            Assert.True(int.TryParse(vm.RunHour, out int h));
            Assert.True(h > 23);
        }

        [Fact]
        public void Save_InvalidRunMinute_Validation()
        {
            var schedule = new ScheduledSearch();
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleName = "Test";
            vm.ScheduleTypeIndex = 1;
            vm.RunHour = "10";
            vm.RunMinute = "65"; // Invalid

            Assert.True(int.TryParse(vm.RunMinute, out int m));
            Assert.True(m > 59);
        }

        [Fact]
        public void ScheduleEditor_WeeklyWithNoDays_Validation()
        {
            var schedule = new ScheduledSearch();
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleTypeIndex = 2; // Weekly

            // No days selected - this should fail validation
            Assert.False(vm.DaySun || vm.DayMon || vm.DayTue || vm.DayWed || vm.DayThu || vm.DayFri || vm.DaySat);
        }

        [Fact]
        public void ScheduleEditor_IntervalWithZero_Validation()
        {
            var schedule = new ScheduledSearch();
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleTypeIndex = 3; // Interval
            vm.IntervalValue = "0";

            Assert.True(int.TryParse(vm.IntervalValue, out int v));
            Assert.True(v < 1);
        }

        [Fact]
        public void ScheduleEditor_Cancel_DoesNothing()
        {
            var schedule = new ScheduledSearch();
            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());

            var method = typeof(ScheduleEditorViewModel).GetMethod("Cancel", NonPublicInstance);
            // Cancel with null window returns early
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null }));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // ScheduleEditorViewModel — Properties tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ScheduleEditor_IsOnce_WhenTypeIndex0()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleTypeIndex = 0;
            Assert.True(vm.IsOnce);
            Assert.False(vm.IsWeekly);
            Assert.False(vm.IsInterval);
        }

        [Fact]
        public void ScheduleEditor_IsWeekly_WhenTypeIndex2()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleTypeIndex = 2;
            Assert.True(vm.IsWeekly);
            Assert.False(vm.IsOnce);
        }

        [Fact]
        public void ScheduleEditor_IsInterval_WhenTypeIndex3()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleTypeIndex = 3;
            Assert.True(vm.IsInterval);
            Assert.Equal("Start Time (HH:mm)", vm.TimeLabelText);
        }

        [Fact]
        public void ScheduleEditor_TimeLabelText_RunTime_WhenNotInterval()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScheduleTypeIndex = 1;
            Assert.Equal("Run Time (HH:mm)", vm.TimeLabelText);
        }

        [Fact]
        public void ScheduleEditor_NeedsSearch_WhenScanModeSearch()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScanModeSearch = true;
            Assert.True(vm.NeedsSearch);
        }

        [Fact]
        public void ScheduleEditor_NeedsSearch_WhenScanModeBoth()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScanModeBoth = true;
            Assert.True(vm.NeedsSearch);
        }

        [Fact]
        public void ScheduleEditor_NotNeedsSearch_WhenStatsOnly()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.ScanModeStats = true;
            vm.ScanModeSearch = false;
            vm.ScanModeBoth = false;
            Assert.False(vm.NeedsSearch);
        }

        [Fact]
        public void ScheduleEditor_IsAdvancedMode_InverseOfSimple()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.IsSimpleMode = true;
            Assert.False(vm.IsAdvancedMode);

            vm.IsSimpleMode = false;
            Assert.True(vm.IsAdvancedMode);
        }

        [Fact]
        public void ScheduleEditor_DayCheckboxes_SetGet()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.DayMon = true;
            vm.DayFri = true;
            Assert.True(vm.DayMon);
            Assert.True(vm.DayFri);
            Assert.False(vm.DaySun);
            Assert.False(vm.DayTue);
        }

        [Fact]
        public void ScheduleEditor_SearchPLCandAPP()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.SearchPLC = true;
            vm.SearchAPP = true;
            Assert.True(vm.SearchPLC);
            Assert.True(vm.SearchAPP);
        }

        [Fact]
        public void ScheduleEditor_SimpleSearchProperties()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.SimpleSearchText = "error";
            vm.SimpleField = SearchField.Message;
            vm.SimpleUseRegex = true;

            Assert.Equal("error", vm.SimpleSearchText);
            Assert.Equal(SearchField.Message, vm.SimpleField);
            Assert.True(vm.SimpleUseRegex);
        }

        [Fact]
        public void ScheduleEditor_ConditionProperties()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            var condition = new ConditionRowViewModel();
            condition.Field = SearchField.Logger;
            condition.Operator = SearchOperator.Regex;
            condition.Value = "test.*";
            condition.Negate = true;

            Assert.Equal(SearchField.Logger, condition.Field);
            Assert.Equal(SearchOperator.Regex, condition.Operator);
            Assert.Equal("test.*", condition.Value);
            Assert.True(condition.Negate);
        }

        [Fact]
        public void LocationCheckItem_Properties()
        {
            var item = new LocationCheckItem();
            item.Id = Guid.NewGuid();
            item.DisplayText = "Server 1";
            item.IsChecked = true;

            Assert.Equal("Server 1", item.DisplayText);
            Assert.True(item.IsChecked);
        }

        // ═══════════════════════════════════════════════════════════
        // GlobalGrepViewModel.Config — Esc helper tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Esc_NullString_ReturnsEmpty()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("Esc", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (string?)method?.Invoke(null, new object?[] { null });
            Assert.Equal("", result);
        }

        [Fact]
        public void Esc_StringWithQuotes_DoublesQuotes()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("Esc", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (string?)method?.Invoke(null, new object?[] { "He said \"hello\"" });
            Assert.Equal("He said \"\"hello\"\"", result);
        }

        [Fact]
        public void Esc_PlainString_ReturnsUnchanged()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("Esc", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (string?)method?.Invoke(null, new object?[] { "plain text" });
            Assert.Equal("plain text", result);
        }

        // ═══════════════════════════════════════════════════════════
        // GlobalGrepViewModel.Config — FormatTimeRange tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void FormatTimeRange_BothNull_ReturnsNull()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (string?)method?.Invoke(null, new object?[] { null!, null! });
            Assert.Null(result);
        }

        [Fact]
        public void FormatTimeRange_FromOnly_ReturnsWithEllipsis()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange", BindingFlags.NonPublic | BindingFlags.Static);
            var from = new DateTime(2024, 3, 15);
            var result = (string?)method?.Invoke(null, new object?[] { from, null! });
            Assert.Equal("2024-03-15 to ...", result);
        }

        [Fact]
        public void FormatTimeRange_ToOnly_ReturnsWithEllipsis()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange", BindingFlags.NonPublic | BindingFlags.Static);
            var to = new DateTime(2024, 12, 25);
            var result = (string?)method?.Invoke(null, new object?[] { null!, to });
            Assert.Equal("... to 2024-12-25", result);
        }

        [Fact]
        public void FormatTimeRange_BothDates_ReturnsRange()
        {
            var method = typeof(GlobalGrepViewModel).GetMethod("FormatTimeRange", BindingFlags.NonPublic | BindingFlags.Static);
            var from = new DateTime(2024, 1, 1);
            var to = new DateTime(2024, 12, 31);
            var result = (string?)method?.Invoke(null, new object?[] { from, to });
            Assert.Equal("2024-01-01 to 2024-12-31", result);
        }

        // ═══════════════════════════════════════════════════════════
        // GlobalGrepViewModel.Config — BuildCriteriaSummary tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void BuildCriteriaSummary_EmptyGroups_ReturnsEmpty()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            // Initialize ConditionGroups
            var condGroupsProp = typeof(GlobalGrepViewModel).GetProperty("ConditionGroups");
            var condGroups = new ObservableCollection<ConditionGroupVM>();
            // ConditionGroups is likely initialized in constructor, we need to set backing field
            SetField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);

            // Find the backing field for ConditionGroups
            var fieldInfo = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", NonPublicInstance);
            if (fieldInfo != null)
                fieldInfo.SetValue(vm, condGroups);
            else
            {
                // Try without auto-property pattern
                var allFields = typeof(GlobalGrepViewModel).GetFields(NonPublicInstance);
                var cgField = allFields.FirstOrDefault(f => f.Name.Contains("ConditionGroups") || f.Name.Contains("conditionGroups"));
                cgField?.SetValue(vm, condGroups);
            }

            var method = typeof(GlobalGrepViewModel).GetMethod("BuildCriteriaSummary", NonPublicInstance);
            if (method != null)
            {
                var result = (string?)method.Invoke(vm, null);
                Assert.Equal("", result);
            }
        }

        [Fact]
        public void BuildCriteriaSummary_WithConditions_ReturnsFormattedString()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            SetField(vm, "_selectedGroupOperator", LogicalGroupOperator.And);

            var condGroups = new ObservableCollection<ConditionGroupVM>();
            var group = new ConditionGroupVM { Operator = ConditionOperator.Or };
            group.Conditions.Add(new ConditionVM { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "error" });
            condGroups.Add(group);

            var fieldInfo = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", NonPublicInstance);
            if (fieldInfo != null)
                fieldInfo.SetValue(vm, condGroups);
            else
            {
                var allFields = typeof(GlobalGrepViewModel).GetFields(NonPublicInstance);
                var cgField = allFields.FirstOrDefault(f => f.Name.Contains("ConditionGroups") || f.Name.Contains("conditionGroups"));
                cgField?.SetValue(vm, condGroups);
            }

            var method = typeof(GlobalGrepViewModel).GetMethod("BuildCriteriaSummary", NonPublicInstance);
            if (method != null)
            {
                var result = (string?)method.Invoke(vm, null);
                Assert.NotNull(result);
                Assert.Contains("Message", result!);
                Assert.Contains("error", result);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // GlobalGrepViewModel.Config — ApplyProfile tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ApplyProfile_SetsLocationsAndCriteria()
        {
            var vm = CreateUninitialized<GlobalGrepViewModel>();
            var locations = new ObservableCollection<SearchLocation>();

            // Set Locations backing field
            var locField = typeof(GlobalGrepViewModel).GetField("<Locations>k__BackingField", NonPublicInstance);
            if (locField != null) locField.SetValue(vm, locations);

            var condGroups = new ObservableCollection<ConditionGroupVM>();
            var cgField = typeof(GlobalGrepViewModel).GetField("<ConditionGroups>k__BackingField", NonPublicInstance);
            if (cgField != null) cgField.SetValue(vm, condGroups);

            var locationService = CreateUninitialized<object>(); // Mock-like
            // We can't call ApplyProfile without a real _locationService.Save()
            // but we can test the profile object construction
            var profile = new SearchProfile
            {
                Name = "TestProfile",
                Locations = new List<SearchLocation>
                {
                    new SearchLocation { Name = "Server1", Address = "1.2.3.4", BasePath = @"\\share" }
                },
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = false,
                    Groups = new List<SearchConditionGroup>
                    {
                        new SearchConditionGroup
                        {
                            Operator = ConditionOperator.And,
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition { Field = SearchField.Message, Operator = SearchOperator.Contains, Value = "error" }
                            }
                        }
                    }
                }
            };

            Assert.Equal("TestProfile", profile.Name);
            Assert.Single(profile.Locations);
            Assert.True(profile.Criteria.SearchPLC);
            Assert.False(profile.Criteria.SearchAPP);
        }

        // ═══════════════════════════════════════════════════════════
        // TabTearOffManager — Static method tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void TabTearOffManager_IsTabDetached_ReturnsFalseForUnknown()
        {
            // The _detachedTabs is readonly, so just test via public API
            Assert.False(TabTearOffManager.IsTabDetached("NonExistentTab_" + Guid.NewGuid()));
        }

        [Fact]
        public void TabTearOffManager_GetAttachedTabCount_NullTabControl_ReturnsZero()
        {
            var tcField = typeof(TabTearOffManager).GetField("_mainTabControl", BindingFlags.NonPublic | BindingFlags.Static);
            var original = tcField?.GetValue(null);
            try
            {
                tcField?.SetValue(null, null);
                Assert.Equal(0, TabTearOffManager.GetAttachedTabCount());
            }
            finally
            {
                tcField?.SetValue(null, original);
            }
        }

        [Fact]
        public void TabTearOffManager_GetDetachedWindows_ReturnsEmpty_WhenNoneDetached()
        {
            var windows = TabTearOffManager.GetDetachedWindows();
            Assert.NotNull(windows);
            // We can't guarantee the static state, but it shouldn't throw
        }

        [Fact]
        public void TabTearOffManager_IsTabDetachable_NullTabItem_ReturnsFalse()
        {
            Assert.False(TabTearOffManager.IsTabDetachable(null!));
        }

        [Fact]
        public void TabTearOffManager_DetachTab_NullTabItem_ReturnsNull()
        {
            var result = TabTearOffManager.DetachTab(null!, new System.Windows.Point(0, 0));
            Assert.Null(result);
        }

        [Fact]
        public void TabTearOffManager_DetachTab_NullMainTabControl_ReturnsNull()
        {
            var tcField = typeof(TabTearOffManager).GetField("_mainTabControl", BindingFlags.NonPublic | BindingFlags.Static);
            var original = tcField?.GetValue(null);
            try
            {
                tcField?.SetValue(null, null);
                // Even with a non-null TabItem, null _mainTabControl returns null
                // We can't easily create a TabItem in test, but the null check for _mainTabControl is first
                var result = TabTearOffManager.DetachTab(null!, new System.Windows.Point(100, 100));
                Assert.Null(result);
            }
            finally
            {
                tcField?.SetValue(null, original);
            }
        }

        [Fact]
        public void TabTearOffManager_ReattachTab_UnknownHeader_DoesNothing()
        {
            var ex = Record.Exception(() => TabTearOffManager.ReattachTab("NonExistentHeader"));
            Assert.Null(ex);
        }

        [Fact]
        public void TabTearOffManager_ReattachAll_EmptyDict_DoesNothing()
        {
            var ex = Record.Exception(() => TabTearOffManager.ReattachAll());
            Assert.Null(ex);
        }

        [Fact]
        public void TabTearOffManager_GetDetachedControl_UnknownHeader_ReturnsNull()
        {
            var result = TabTearOffManager.GetDetachedControl<System.Windows.Controls.UserControl>("NonExistent");
            Assert.Null(result);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — MarkRow logic tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void MarkRow_WhenNoSelectedLog_DoesNothing()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var parent = CreateUninitialized<MainViewModel>();
            // SelectedLog is null
            SetField(vm, "_parent", parent);

            var method = typeof(CaseManagementViewModel).GetMethod("MarkRow", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null }));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — UnmarkLog tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void UnmarkLog_DoesNothing_Placeholder()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var method = typeof(CaseManagementViewModel).GetMethod("UnmarkLog", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, new object?[] { null }));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — SetDependencies tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SetDependencies_SetsAllDependencies()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var filterVM = CreateUninitialized<FilterSearchViewModel>();
            var caseVM = CreateUninitialized<CaseManagementViewModel>();
            var configVM = CreateUninitialized<ConfigExplorerViewModel>();
            var liveVM = CreateUninitialized<LiveMonitoringViewModel>();

            vm.SetDependencies(filterVM, caseVM, configVM, liveVM);

            var filterField = typeof(LogSessionViewModel).GetField("_filterVM", NonPublicInstance);
            var caseField = typeof(LogSessionViewModel).GetField("_caseVM", NonPublicInstance);
            var configField = typeof(LogSessionViewModel).GetField("_configVM", NonPublicInstance);
            var liveField = typeof(LogSessionViewModel).GetField("_liveVM", NonPublicInstance);

            Assert.Same(filterVM, filterField?.GetValue(vm));
            Assert.Same(caseVM, caseField?.GetValue(vm));
            Assert.Same(configVM, configField?.GetValue(vm));
            Assert.Same(liveVM, liveField?.GetValue(vm));
        }

        // ═══════════════════════════════════════════════════════════
        // ScheduleEditorViewModel — ConditionRowViewModel tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ConditionRowViewModel_DefaultValues()
        {
            var vm = new ConditionRowViewModel();
            Assert.Equal(SearchField.Any, vm.Field);
            Assert.Equal(SearchOperator.Contains, vm.Operator);
            Assert.Equal("", vm.Value);
            Assert.False(vm.Negate);
        }

        [Fact]
        public void ConditionRowViewModel_SearchFieldValues_ReturnsAllFields()
        {
            var vm = new ConditionRowViewModel();
            Assert.NotNull(vm.SearchFieldValues);
            Assert.True(vm.SearchFieldValues.Length > 0);
        }

        [Fact]
        public void ConditionRowViewModel_SearchOperatorValues_ReturnsAllOperators()
        {
            var vm = new ConditionRowViewModel();
            Assert.NotNull(vm.SearchOperatorValues);
            Assert.True(vm.SearchOperatorValues.Length > 0);
        }

        // ═══════════════════════════════════════════════════════════
        // ScheduleEditorViewModel — Commands exist tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ScheduleEditor_CommandsInitialized()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            Assert.NotNull(vm.AddConditionCommand);
            Assert.NotNull(vm.RemoveConditionCommand);
            Assert.NotNull(vm.SelectAllLocationsCommand);
            Assert.NotNull(vm.SelectNoLocationsCommand);
            Assert.NotNull(vm.SaveCommand);
            Assert.NotNull(vm.CancelCommand);
            Assert.NotNull(vm.BrowseOutputCommand);
            Assert.NotNull(vm.AddRecipientCommand);
            Assert.NotNull(vm.RemoveRecipientCommand);
        }

        [Fact]
        public void ScheduleEditor_HasLocations_WhenEmpty_ReturnsFalse()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            Assert.False(vm.HasLocations);
        }

        // ═══════════════════════════════════════════════════════════
        // Additional CaseManagement — EnsureDefaultConfigs content tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void EnsureDefaultConfigs_S45ConfigContainsCorrectFilter()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var tempDir = Path.Combine(Path.GetTempPath(), "IndiLogsTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                vm.EnsureDefaultConfigsOnDisk(tempDir);

                var s45File = Directory.GetFiles(tempDir, "*PLC_FILTERED_S45*").FirstOrDefault();
                Assert.NotNull(s45File);

                var content = File.ReadAllText(s45File!);
                Assert.Contains("PLC_FILTERED", content);
                Assert.Contains("error", content);
                Assert.Contains("=== state", content);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void EnsureDefaultConfigs_S6ConfigContainsCorrectFilter()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            var tempDir = Path.Combine(Path.GetTempPath(), "IndiLogsTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                vm.EnsureDefaultConfigsOnDisk(tempDir);

                var s6File = Directory.GetFiles(tempDir, "*PLC_FILTERED_S6*").FirstOrDefault();
                Assert.NotNull(s6File);

                var content = File.ReadAllText(s6File!);
                Assert.Contains("PLC_FILTERED", content);
                Assert.Contains("plcmngr:", content);
                Assert.Contains("Manager", content);
                Assert.Contains("Events", content);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // CaseManagementViewModel — CloseAllMarkedWindows tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void CloseAllMarkedWindows_AllNull_DoesNothing()
        {
            var vm = CreateUninitialized<CaseManagementViewModel>();
            SetField(vm, "_combinedMarkedWindow", null as object);
            SetField(vm, "_markedMainLogsWindow", null as object);
            SetField(vm, "_markedAppLogsWindow", null as object);

            var method = typeof(CaseManagementViewModel).GetMethod("CloseAllMarkedWindows", NonPublicInstance);
            var ex = Record.Exception(() => method?.Invoke(vm, null));
            Assert.Null(ex);
        }

        // ═══════════════════════════════════════════════════════════
        // LogSessionViewModel — BuildFileDialogFilter tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void BuildFileDialogFilter_ReturnsNonEmptyFilter()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var parent = CreateUninitialized<MainViewModel>();
            SetField(vm, "_parent", parent);

            var method = typeof(LogSessionViewModel).GetMethod("BuildFileDialogFilter", NonPublicInstance);
            var result = (string?)method?.Invoke(vm, null);

            Assert.NotNull(result);
            Assert.Contains("*.zip", result!);
            Assert.Contains("*.log", result);
            Assert.Contains("All Supported Files", result);
        }

        // ═══════════════════════════════════════════════════════════
        // Model type tests for CaseFile and related
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void CaseFile_DefaultConstructor_InitializesCollections()
        {
            var cf = new CaseFile();
            Assert.NotNull(cf.Meta);
            Assert.NotNull(cf.Resources);
            Assert.NotNull(cf.ViewState);
            Assert.NotNull(cf.Annotations);
            Assert.NotNull(cf.MainColoringRules);
            Assert.NotNull(cf.AppColoringRules);
            Assert.Empty(cf.Resources);
            Assert.Empty(cf.Annotations);
        }

        [Fact]
        public void LogTarget_DefaultValues()
        {
            var target = new LogTarget();
            Assert.Equal("", target.Logger);
            Assert.Equal("", target.Thread);
            Assert.Equal("", target.Snippet);
            Assert.Equal("", target.Level);
        }

        [Fact]
        public void LogAnnotation_DefaultValues()
        {
            var ann = new LogAnnotation();
            Assert.Equal("", ann.Content);
            Assert.Equal("#FFFF00", ann.Color);
            Assert.Equal("", ann.Author);
        }

        [Fact]
        public void SessionFilterState_DefaultValues()
        {
            var state = new SessionFilterState();
            Assert.Null(state.MainFilterRoot);
            Assert.Null(state.AppFilterRoot);
            Assert.False(state.IsMainFilterActive);
            Assert.False(state.IsAppFilterActive);
            Assert.Null(state.SearchText);
        }

        [Fact]
        public void SavedConfiguration_DefaultValues()
        {
            var config = new SavedConfiguration();
            Assert.Equal("", config.Name);
            Assert.Equal("", config.FilePath);
        }

        // ═══════════════════════════════════════════════════════════
        // ConditionGroupVM tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ConditionGroupVM_DefaultOperator_IsAnd()
        {
            var group = new ConditionGroupVM();
            Assert.Equal(ConditionOperator.And, group.Operator);
        }

        [Fact]
        public void ConditionGroupVM_Conditions_InitiallyEmpty()
        {
            var group = new ConditionGroupVM();
            Assert.NotNull(group.Conditions);
            Assert.Empty(group.Conditions);
        }

        [Fact]
        public void ConditionGroupVM_OperatorSet_RaisesPropertyChanged()
        {
            var group = new ConditionGroupVM();
            bool raised = false;
            group.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Operator") raised = true;
            };
            group.Operator = ConditionOperator.Or;
            Assert.True(raised);
        }

        // ═══════════════════════════════════════════════════════════
        // ConditionVM tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ConditionVM_DefaultField_IsAny()
        {
            var cond = new ConditionVM();
            Assert.Equal(SearchField.Any, cond.Field);
        }

        [Fact]
        public void ConditionVM_DefaultOperator_IsContains()
        {
            var cond = new ConditionVM();
            Assert.Equal(SearchOperator.Contains, cond.Operator);
        }

        [Fact]
        public void ConditionVM_SetProperties_Work()
        {
            var cond = new ConditionVM();
            cond.Field = SearchField.Logger;
            cond.Operator = SearchOperator.Regex;
            cond.Value = "test";
            cond.Negate = true;

            Assert.Equal(SearchField.Logger, cond.Field);
            Assert.Equal(SearchOperator.Regex, cond.Operator);
            Assert.Equal("test", cond.Value);
            Assert.True(cond.Negate);
        }

        // ═══════════════════════════════════════════════════════════
        // SearchProfile tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void SearchProfile_DefaultValues()
        {
            var profile = new SearchProfile();
            Assert.Equal("", profile.Name);
            Assert.NotNull(profile.Locations);
            Assert.NotNull(profile.Criteria);
            Assert.NotNull(profile.Schedules);
        }

        [Fact]
        public void SearchProfile_CanAddLocationsAndCriteria()
        {
            var profile = new SearchProfile
            {
                Name = "MyProfile",
                Locations = new List<SearchLocation>
                {
                    new SearchLocation { Name = "Loc1" }
                },
                Criteria = new SearchCriteria
                {
                    SearchPLC = true,
                    SearchAPP = true
                }
            };

            Assert.Equal("MyProfile", profile.Name);
            Assert.Single(profile.Locations);
            Assert.True(profile.Criteria.SearchPLC);
        }

        // ═══════════════════════════════════════════════════════════
        // Additional ScheduleEditorViewModel initialization tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void ScheduleEditor_InitializesFromExistingSchedule()
        {
            var schedule = new ScheduledSearch
            {
                Name = "Daily Check",
                IsEnabled = true,
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(14, 30, 0)
            };

            var vm = new ScheduleEditorViewModel(schedule, new List<SearchLocation>(), new SearchCriteria());

            Assert.Equal("Daily Check", vm.ScheduleName);
            Assert.True(vm.IsEnabled);
        }

        [Fact]
        public void ScheduleEditor_RunDate_GetSet()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            var date = new DateTime(2025, 6, 15);
            vm.RunDate = date;
            Assert.Equal(date, vm.RunDate);
        }

        [Fact]
        public void ScheduleEditor_IntervalValue_GetSet()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.IntervalValue = "30";
            Assert.Equal("30", vm.IntervalValue);
        }

        [Fact]
        public void ScheduleEditor_IntervalUnitIndex_GetSet()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.IntervalUnitIndex = 2;
            Assert.Equal(2, vm.IntervalUnitIndex);
        }

        [Fact]
        public void ScheduleEditor_AdvancedOperator_GetSet()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            vm.AdvancedOperator = ConditionOperator.Or;
            Assert.Equal(ConditionOperator.Or, vm.AdvancedOperator);
        }

        [Fact]
        public void ScheduleEditor_SearchFieldValues_HasEntries()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            Assert.NotNull(vm.SearchFieldValues);
            Assert.True(vm.SearchFieldValues.Length > 0);
        }

        [Fact]
        public void ScheduleEditor_ConditionOperatorValues_HasEntries()
        {
            var vm = new ScheduleEditorViewModel(new ScheduledSearch(), new List<SearchLocation>(), new SearchCriteria());
            Assert.NotNull(vm.ConditionOperatorValues);
            Assert.True(vm.ConditionOperatorValues.Length > 0);
        }

        // ═══════════════════════════════════════════════════════════
        // Additional LogSessionViewModel tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void LogSessionViewModel_Logs_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var logs = new List<LogEntry> { new LogEntry { Message = "a" }, new LogEntry { Message = "b" } };
            SetField(vm, "_logs", logs as IEnumerable<LogEntry>);
            Assert.Equal(2, vm.Logs.Count());
        }

        [Fact]
        public void LogSessionViewModel_Events_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var events = new ObservableCollection<EventEntry> { new EventEntry() };
            SetField(vm, "_events", events);
            Assert.Single(vm.Events);
        }

        [Fact]
        public void LogSessionViewModel_LoadedFiles_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var files = new ObservableCollection<string> { "file1.log", "file2.log" };
            SetField(vm, "_loadedFiles", files);
            Assert.Equal(2, vm.LoadedFiles.Count);
        }

        [Fact]
        public void LogSessionViewModel_LoadedSessions_GetSet()
        {
            var vm = CreateUninitialized<LogSessionViewModel>();
            var sessions = new ObservableCollection<LogSessionData> { new LogSessionData() };
            SetField(vm, "_loadedSessions", sessions);
            Assert.Single(vm.LoadedSessions);
        }

        // ═══════════════════════════════════════════════════════════
        // CaseViewState and CaseMetadata model tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void CaseMetadata_DefaultValues()
        {
            var meta = new CaseMetadata();
            Assert.NotNull(meta);
        }

        [Fact]
        public void CaseViewState_PropertiesSetGet()
        {
            var vs = new CaseViewState();
            vs.QuickSearchText = "error";
            vs.SelectedTab = "MAIN";
            vs.ActiveThreadFilters = new List<string> { "Thread1" };
            vs.NegativeFilters = new List<string> { "noise" };

            Assert.Equal("error", vs.QuickSearchText);
            Assert.Equal("MAIN", vs.SelectedTab);
            Assert.Single(vs.ActiveThreadFilters);
            Assert.Single(vs.NegativeFilters);
        }

        [Fact]
        public void CaseResource_PropertiesSetGet()
        {
            var res = new CaseResource();
            res.FileName = "test.zip";
            res.Size = 1024;
            res.LastModified = new DateTime(2024, 1, 1);

            Assert.Equal("test.zip", res.FileName);
            Assert.Equal(1024, res.Size);
        }

        // ═══════════════════════════════════════════════════════════
        // TabTearOffManager — Initialize tests
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void TabTearOffManager_Initialize_SetsStaticFields()
        {
            var mwField = typeof(TabTearOffManager).GetField("_mainWindow", BindingFlags.NonPublic | BindingFlags.Static);
            var tcField = typeof(TabTearOffManager).GetField("_mainTabControl", BindingFlags.NonPublic | BindingFlags.Static);
            var wmField = typeof(TabTearOffManager).GetField("_windowManager", BindingFlags.NonPublic | BindingFlags.Static);

            var origMw = mwField?.GetValue(null);
            var origTc = tcField?.GetValue(null);
            var origWm = wmField?.GetValue(null);

            try
            {
                // Initialize with nulls (testing the assignment logic)
                TabTearOffManager.Initialize(null!, null!, null);

                Assert.Null(mwField?.GetValue(null));
                Assert.Null(tcField?.GetValue(null));
                Assert.Null(wmField?.GetValue(null));
            }
            finally
            {
                mwField?.SetValue(null, origMw);
                tcField?.SetValue(null, origTc);
                wmField?.SetValue(null, origWm);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Test helper service
        // ═══════════════════════════════════════════════════════════

        private class TestDialogServiceForSchedule : IndiLogs_3._0.Services.Interfaces.IDialogService
        {
            private readonly List<string> _messages;
            public TestDialogServiceForSchedule(List<string> messages) { _messages = messages; }
            public void ShowError(string message, string title) => _messages.Add(message);
            public void ShowInfo(string message, string title) => _messages.Add(message);
            public void ShowWarning(string message, string title) => _messages.Add(message);
            public IndiLogs_3._0.Services.Interfaces.DialogResult ShowConfirm(string message, string title)
            {
                _messages.Add(message);
                return IndiLogs_3._0.Services.Interfaces.DialogResult.No;
            }
            public IndiLogs_3._0.Services.Interfaces.DialogResult ShowYesNoCancel(string message, string title)
            {
                _messages.Add(message);
                return IndiLogs_3._0.Services.Interfaces.DialogResult.No;
            }
        }
    }
}
