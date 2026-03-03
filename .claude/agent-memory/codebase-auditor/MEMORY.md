# Codebase Auditor Memory - IndiLogs 3.0

## Project Overview
- **Type**: WPF Desktop Application (.NET 10, C#)
- **Purpose**: Log viewer/analyzer for HP Indigo printer diagnostics
- **Size**: ~63,858 LOC C# + ~12,394 LOC XAML | Tests: 1,635 LOC (130 [Fact]/[Theory] methods, ~142 test cases with InlineData)
- **Architecture**: MVVM with manual DI (Bootstrapper pattern)
- **Solution**: 3 projects (main app, PluginAPI, Tests)

## Audit #5: 2026-03-03 (Delta from Audit #4)
### Scores
- Architecture: 7.5/10 (SAME)
- Code Quality: 7.5/10 (SAME)
- Security: 7.5/10 (UP from 7 -- UNC paths removed from appsettings.json, IDialogService fully in place)
- Performance: 7.5/10 (SAME)
- Features: 8/10 (SAME)
- Overall SW Quality: 7.5/10 (UP from 7 -- CI/CD added, IDialogService abstraction)
- **Weighted Overall: 7.55/10** (UP from 7.4)

### Improvements Since Audit #4
1. **CI/CD pipeline**: GitHub Actions workflow (.github/workflows/ci.yml) with build+test
2. **IDialogService abstraction**: Interface + DialogService impl, injected via Bootstrapper
3. **appsettings.json sanitized**: UNC paths removed, empty defaults, local overlay pattern
4. **FilterEditorViewModel extracted**: Separate file, was previously in Services/
5. **Analysis models relocated**: Services/AnalysisModels.cs -> Models/Analysis/AnalysisModels.cs

### Remaining Issues
1. **MessageBox.Show in Views**: ~50+ calls in code-behind (Views/*.xaml.cs) -- acceptable in View layer but inconsistent
2. **ScheduleDialog builds UI in code**: 1476 lines of C# creating WPF controls programmatically
3. **async void**: 7 methods (3 debounce, 2 timer, 1 WPF override, 1 button click) -- all try-caught
4. **Large files**: StatsWindow.xaml.cs(1588), GrepVM.ScheduleDialog(1476), MainWindow.xaml.cs(1347), ChartGraphView(1330)
5. **WindowManager dual static+instance** pattern (static class + WindowManagerAdapter)
6. **ViewModels reference System.Windows**: Many VMs use Dispatcher.Invoke, create Window instances
7. **Nullable annotations only** (not `enable`) -- no compiler enforcement
8. **Duplicate OpenUrl methods**: MainViewModel.Windows.cs:270 AND ToolsViewModel.cs:105

### Key File Locations
- Entry: `App.xaml.cs` | DI: `Bootstrapper.cs` | Constants: `AppConstants.cs`
- MainVM: `ViewModels/MainViewModel.cs` (981 lines + 9 partial files = 3090 total)
- Core: `Services/LogFileService.cs` (1263 + 3 partial files)
- Plugin: `Services/PluginLoader.cs` | Update: `Services/UpdateService.cs`
- Tests: `IndiLogs.Tests/` (8 files, 1635 LOC, 130 tests)
- Settings: `Services/AppSettingsService.cs` | Analyzers: `.editorconfig`
- Interfaces: 11 in `Services/Interfaces/`
- CI: `.github/workflows/ci.yml`
- Dialog: `Services/DialogService.cs` + `Services/Interfaces/IDialogService.cs`

### Architecture Notes
- Manual DI via static Bootstrapper (no IoC container)
- MainViewModel composition: SessionVM, FilterVM, LiveVM, CaseVM, ConfigVM, ChartVM, CprVM, DiffLogsVM, StepRecorderVM
- Plugin: %AppData%\IndiLogs3.0\Plugins\ with Authenticode + strong name whitelist
- Update: UNC share with DPAPI credentials, Authenticode-verified binaries
- ChartDataService: unsafe code with memory-mapped files
- ZIP bomb protection | ArrayPool for LOH reduction | StringPool interning
- ScheduleDialog: 1476 lines building WPF UI programmatically (should be XAML)
