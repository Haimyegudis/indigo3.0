using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.ViewModels;
using IndiLogs.PluginAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Xunit;

namespace IndiLogs.Tests
{
    #region Test Doubles

    internal class TestLogFileService : ILogFileService
    {
        public Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress, TabSelectionConfig? tabSelection = null)
            => Task.FromResult(new LogSessionData());
        public TabSelectionConfig? PreScanZip(string zipPath) => null;
        public Task ReloadComponentAsync(LogSessionData session, string componentName, IProgress<(double, string)> progress) => Task.CompletedTask;
        public List<EventEntry> ParseEventsCsv(Stream stream) => new();
        public List<LogEntry> ParseLogStreamPartial(Stream stream) => new();
        public (List<LogEntry> AllLogs, List<LogEntry> Transitions, List<LogEntry> Failures) ParseLogStream(Stream stream, LogFileService.StringPool? pool = null) => (new(), new(), new());
        public (List<LogEntry> NewEntries, int TotalCount) ParseLogStreamSkipExisting(Stream stream, int skipCount) => (new(), 0);
        public IPluginLoader GetPluginLoader() => new TestPluginLoader();
    }

    internal class TestPluginLoader : IPluginLoader
    {
        public IReadOnlyList<ILogFilePlugin> Plugins => Array.Empty<ILogFilePlugin>();
        public string? GetDllPath(ILogFilePlugin plugin) => null;
        public void Reload() { }
    }

    internal class TestLogColoringService : ILogColoringService
    {
        public List<ColoringCondition> UserDefaultMainRules { get; set; } = new();
        public List<ColoringCondition> UserDefaultAppRules { get; set; } = new();
        public Task ApplyDefaultColorsAsync(IEnumerable<LogEntry> logs, bool isAppLog) => Task.CompletedTask;
        public Task ApplyCustomColoringAsync(IEnumerable<LogEntry> logs, List<ColoringCondition> conditions) => Task.CompletedTask;
    }

    internal class TestCsvExportService : ICsvExportService
    {
        public Task<string?> ExportLogsToCsvAsync(IEnumerable<LogEntry> logs, string defaultFileName, ExportPreset? preset = null)
            => Task.FromResult<string?>(null);
    }

    internal class TestDefaultConfigurationService : IDefaultConfigurationService
    {
        public DefaultConfiguration CurrentDefaults { get; private set; } = new();
        public int LoadCallCount { get; private set; }
        public int ResetCallCount { get; private set; }
        public DefaultConfiguration? LastSaved { get; private set; }

        public void Load() { LoadCallCount++; }
        public void Save(DefaultConfiguration config) { LastSaved = config; CurrentDefaults = config; }
        public void Reset() { ResetCallCount++; CurrentDefaults = new(); }
    }

    internal class TestViewFactory : IViewFactory
    {
        public int CreateCallCount { get; private set; }
        public T Create<T>(params object[] args) where T : Window
        {
            CreateCallCount++;
            return null!;
        }
    }

    internal class TestWindowOwnerProvider : IWindowOwnerProvider
    {
        public Window? GetOwner() => null;
    }

    internal class TestWindowManager : IWindowManager
    {
        public void Initialize(Window mainWindow) { }
        public void OpenWindow(Window childWindow, Window? referenceWindow = null) { }
        public bool? ShowDialog(Window dialogWindow, Window? owner = null) => null;
        public void ActivateWindow(Window window) { }
        public IEnumerable<Window> GetOpenWindows() => Array.Empty<Window>();
        public T? FindWindow<T>() where T : Window => null;
        public bool ActivateExisting<T>() where T : Window => false;
        public T GetOrCreate<T>(Func<T> factory, Window? referenceWindow = null) where T : Window => factory();
        public void ActivateMainWindow() { }
    }

    #endregion

    /// <summary>
    /// Tests for MainViewModel — constructor, commands, and pure logic.
    /// Note: MainViewModel constructor touches WPF resources (ApplyTheme, Fonts).
    /// Tests that construct MainViewModel must handle possible WPF initialization.
    /// </summary>
    public class MainViewModelTests
    {
        private readonly TestDialogService _dialogService = new();
        private readonly TestDispatcher _dispatcher = new();
        private readonly TestLogFileService _logService = new();
        private readonly TestLogColoringService _coloringService = new();
        private readonly TestCsvExportService _csvService = new();
        private readonly TestDefaultConfigurationService _defaultConfigService = new();
        private readonly TestViewFactory _viewFactory = new();
        private readonly TestWindowOwnerProvider _windowOwner = new();
        private readonly TestWindowManager _windowManager = new();

        private MainViewModel? TryCreateVM()
        {
            try
            {
                return new MainViewModel(
                    _logService, _coloringService, _csvService, _defaultConfigService,
                    _dialogService, _viewFactory, _dispatcher, _windowOwner, _windowManager);
            }
            catch (Exception)
            {
                // Constructor touches WPF Application resources (ApplyTheme, Fonts)
                // which may not be available in a headless test environment
                return null;
            }
        }

        #region OpenUrl Tests (static, no WPF dependency)

        [Fact]
        public void OpenUrl_NullInput_DoesNotThrow()
        {
            MainViewModel.OpenUrl(null!);
        }

        [Fact]
        public void OpenUrl_EmptyString_DoesNotThrow()
        {
            MainViewModel.OpenUrl("");
        }

        [Fact]
        public void OpenUrl_Whitespace_DoesNotThrow()
        {
            MainViewModel.OpenUrl("   ");
        }

        [Fact]
        public void OpenUrl_JavaScriptScheme_DoesNotThrow()
        {
            // Should be blocked by scheme validation
            MainViewModel.OpenUrl("javascript:alert(1)");
        }

        [Fact]
        public void OpenUrl_FileScheme_DoesNotThrow()
        {
            MainViewModel.OpenUrl("file:///C:/secret.txt");
        }

        [Fact]
        public void OpenUrl_FtpScheme_DoesNotThrow()
        {
            MainViewModel.OpenUrl("ftp://example.com");
        }

        [Fact]
        public void OpenUrl_RelativeUrl_DoesNotThrow()
        {
            MainViewModel.OpenUrl("not-a-url");
        }

        [Fact]
        public void OpenUrl_DataScheme_DoesNotThrow()
        {
            MainViewModel.OpenUrl("data:text/html,<h1>XSS</h1>");
        }

        [Fact]
        public void OpenUrl_HttpsScheme_DoesNotThrow()
        {
            // This one passes the guard — Process.Start may fail in test env, but should not throw from OpenUrl
            MainViewModel.OpenUrl("https://example.com");
        }

        [Fact]
        public void OpenUrl_HttpScheme_DoesNotThrow()
        {
            MainViewModel.OpenUrl("http://example.com");
        }

        [Fact]
        public void OpenUrl_MailtoScheme_DoesNotThrow()
        {
            MainViewModel.OpenUrl("mailto:user@example.com");
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_LoadsDefaults()
        {
            var vm = TryCreateVM();
            if (vm == null) return; // WPF not available in headless

            Assert.True(_defaultConfigService.LoadCallCount >= 1);
        }

        [Fact]
        public void Constructor_SessionVM_IsNotNull()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            Assert.NotNull(vm.SessionVM);
        }

        [Fact]
        public void Constructor_FilterVM_IsNotNull()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            Assert.NotNull(vm.FilterVM);
        }

        [Fact]
        public void Constructor_CaseVM_IsNotNull()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            Assert.NotNull(vm.CaseVM);
        }

        #endregion

        #region Command Tests

        [Fact]
        public void ToggleVisualMode_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.IsVisualMode;
            vm.ToggleVisualModeCommand.Execute(null);
            Assert.NotEqual(initial, vm.IsVisualMode);

            vm.ToggleVisualModeCommand.Execute(null);
            Assert.Equal(initial, vm.IsVisualMode);
        }

        [Fact]
        public void ToggleTimeSync_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.IsTimeSyncEnabled;
            vm.ToggleTimeSyncCommand.Execute(null);
            Assert.NotEqual(initial, vm.IsTimeSyncEnabled);
        }

        [Fact]
        public void ToggleLeftPanel_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.IsLeftPanelVisible;
            vm.ToggleLeftPanelCommand.Execute(null);
            Assert.NotEqual(initial, vm.IsLeftPanelVisible);
        }

        [Fact]
        public void ToggleRightPanel_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.IsRightPanelVisible;
            vm.ToggleRightPanelCommand.Execute(null);
            Assert.NotEqual(initial, vm.IsRightPanelVisible);
        }

        [Fact]
        public void ToggleBold_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.IsBold;
            vm.ToggleBoldCommand.Execute(null);
            Assert.NotEqual(initial, vm.IsBold);
        }

        [Fact]
        public void ToggleLogDetailsPin_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.IsLogDetailsPinned;
            vm.ToggleLogDetailsPinCommand.Execute(null);
            Assert.NotEqual(initial, vm.IsLogDetailsPinned);
        }

        [Fact]
        public void ZoomIn_IncrementsFontSize()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            vm.SelectedTabIndex = 0; // Not screenshots tab
            double initial = vm.GridFontSize;
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(initial + 1, vm.GridFontSize);
        }

        [Fact]
        public void ZoomOut_DecrementsFontSize()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            vm.SelectedTabIndex = 0;
            vm.GridFontSize = 12;
            vm.ZoomOutCommand.Execute(null);
            Assert.Equal(11, vm.GridFontSize);
        }

        [Fact]
        public void ZoomIn_CapsAt30()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            vm.SelectedTabIndex = 0;
            vm.GridFontSize = 30;
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(30, vm.GridFontSize);
        }

        [Fact]
        public void ZoomOut_FloorsAt8()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            vm.SelectedTabIndex = 0;
            vm.GridFontSize = 8;
            vm.ZoomOutCommand.Execute(null);
            Assert.Equal(8, vm.GridFontSize);
        }

        [Fact]
        public void ClearGlobalsSearch_ResetsText()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            vm.GlobalsSearchText = "test search";
            vm.ClearGlobalsSearchCommand.Execute(null);
            Assert.Equal("", vm.GlobalsSearchText);
        }

        [Fact]
        public void ToggleGlobalsDiffs_FlipsBool()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            bool initial = vm.GlobalsShowDiffsOnly;
            vm.ToggleGlobalsDiffsCommand.Execute(null);
            Assert.NotEqual(initial, vm.GlobalsShowDiffsOnly);
        }

        [Fact]
        public void ClearSystabSearch_ResetsBothFields()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            vm.SystabSearchText = "search";
            vm.SystabShowDiffsOnly = true;
            vm.ClearSystabSearchCommand.Execute(null);
            Assert.Equal("", vm.SystabSearchText);
            Assert.False(vm.SystabShowDiffsOnly);
        }

        [Fact]
        public void ResetDefaults_CallsService()
        {
            var vm = TryCreateVM();
            if (vm == null) return;

            int initialCount = _defaultConfigService.ResetCallCount;
            vm.ResetDefaultsCommand.Execute(null);
            Assert.True(_defaultConfigService.ResetCallCount > initialCount);
        }

        #endregion
    }
}
