# IndiLogs 3.0 — Claude Code Project Instructions

> These rules are **mandatory** for every Claude Code session working on this codebase.

## Project Overview

- **Application:** WPF desktop app for HP Indigo digital press log analysis
- **Framework:** .NET 10.0 Windows, C# latest, WPF + WinForms interop
- **Architecture:** MVVM with manual DI (Bootstrapper.cs), no external DI container
- **Solution:** 3 projects — `Indilogs 3.0` (main), `IndiLogs.PluginAPI` (plugin contract), `IndiLogs.Tests` (xUnit)

## Configuration Types

- **S4-5:** ZIP contains a **binary** APP file
- **S6:** APP is **not binary** (text/other format)

---

## Build & Test Commands

```bash
# Build (from repo root)
dotnet build "Indilogs 3.0/Indilogs 3.0.csproj" --no-restore

# Run tests
dotnet test IndiLogs.Tests/ --no-restore

# Build + test (must pass before any commit)
dotnet build "Indilogs 3.0/Indilogs 3.0.csproj" --no-restore && dotnet test IndiLogs.Tests/ --no-restore
```

## Commit & Push Rules

- **ALWAYS** run `dotnet build` and `dotnet test` before committing. Zero errors, all tests green.
- **Commit after each significant change** — do NOT accumulate 100+ file changes in one commit.
- Group related changes logically (e.g., "Add IViewFactory abstraction" is one commit, "Nullable migration" is another).
- Use conventional commit messages: `feat:`, `fix:`, `refactor:`, `test:`, `chore:`, `perf:`, `docs:`.
- Push to remote after committing unless explicitly told not to.

---

## Architecture Rules

### No God Classes
- **Maximum ~400 lines per file.** If a ViewModel or Service exceeds this, split it into partial classes by concern.
- MainViewModel is already split: `.cs`, `.FileOps.cs`, `.Filtering.cs`, `.Windows.cs`, `.Tabs.cs`, `.StateAnalysis.cs`, `.Globals.cs`, `.Systab.cs`, `.Theme.cs`, `.TimeSync.cs`. Follow this pattern.
- CaseManagementViewModel: `.cs`, `.CaseFiles.cs`, `.Configs.cs`, `.MarkedLogs.cs`.
- FilterSearchViewModel: `.cs`, `.Filtering.cs`, `.LoggerTree.cs`, `.SpecialFilters.cs`.
- GlobalGrepViewModel: `.cs`, `.Search.cs`, `.Config.cs`, `.Locations.cs`, `.Schedule.cs`, `.ScheduleDialog.cs`.

### MVVM Separation
- **Views** contain ZERO business logic. Only XAML + minimal code-behind for UI plumbing (event routing, visual tree helpers).
- **ViewModels** never reference concrete Window types. Use `IViewFactory.Create<T>()` for window creation.
- **ViewModels** never call `MessageBox.Show()`. Use `IDialogService` instead.
- **Services** are stateless or singleton. All service dependencies injected via constructor.

### Dependency Injection
- All services registered in `Bootstrapper.cs` as singletons.
- Use interfaces for every service: `ILogFileService`, `IDialogService`, `IViewFactory`, etc.
- ViewModels receive dependencies via constructor parameters — never via `Bootstrapper.Resolve<T>()` inside a VM.
- 12 service interfaces live in `Services/Interfaces/`.

### Folder Structure (do not deviate)
```
Indilogs 3.0/
  Views/              — XAML windows and dialogs
  ViewModels/         — Application logic
    Components/       — Child ViewModels (FilterSearch, CaseManagement, ConfigExplorer, etc.)
  Services/           — Business logic, data loading, export
    Interfaces/       — Service contracts (ILogFileService, etc.)
    Grep/             — Global search services
    Charts/           — Chart data services
    Analysis/         — Analysis engine
    BuiltInPlugins/   — Built-in log parsers
    Cpr/              — CPR analysis services
  Models/             — Data structures and domain objects
    Grep/             — Search/schedule models
    Charts/           — Chart data models
    Analysis/         — Analysis result models
    Cpr/              — CPR models
  Controls/           — Custom WPF controls
    Charts/           — SkiaSharp chart controls
    Cpr/              — CPR controls
  Converters/         — XAML value converters
  Resources/          — Icons and assets
  Interfaces/         — Non-service interfaces (ITabHost)
  Properties/         — Assembly metadata
```

---

## Code Quality Standards

### Nullable Reference Types
- **Project-wide `<Nullable>enable</Nullable>`.** Never add `#nullable disable`.
- Mark nullable fields/params/returns with `?`. Use `= ""` for non-null string defaults, `= new()` for collections.
- Prefer null checks over `!` suppression. Use `!` only when you are 100% certain the value is non-null.

### Naming Conventions
- Private fields: `_camelCase` (e.g., `_dialogService`, `_viewFactory`)
- Properties: `PascalCase`
- Methods: `PascalCase`
- Async methods: suffix with `Async` (e.g., `LoadSessionAsync`)
- Constants: `PascalCase` or `UPPER_SNAKE` for tab indices (e.g., `TAB_PLC = 0`)
- Interfaces: `IPascalCase` (e.g., `ILogFileService`)

### Code Style
- Use `var` when the type is obvious from the right side.
- Prefer expression-bodied members for simple one-liners.
- Use `RelayCommand` for ICommand implementations.
- Use `ViewModelBase` as the base class for all ViewModels.
- `OnPropertyChanged()` for property change notifications (CallerMemberName).
- `SetField(ref field, value)` pattern where available.

### What NOT To Do
- Do NOT add comments for self-evident code. Only comment complex algorithms or non-obvious business rules.
- Do NOT add XML doc comments to private methods.
- Do NOT over-engineer. No feature flags, no backwards-compat shims, no speculative abstractions.
- Do NOT create helper/utility classes for one-time operations.
- Do NOT add error handling for scenarios that can't happen. Only validate at system boundaries.
- Do NOT use `Dispatcher.Invoke` (blocking). Use `Dispatcher.InvokeAsync` or `Dispatcher.BeginInvoke`.
- Do NOT wrap already-async methods in `Task.Run`. Only use `Task.Run` for CPU-bound synchronous work.
- Do NOT use LINQ `Skip/Take` on `List<T>` — use `GetRange()` instead.

---

## Performance Rules

- **StringPool:** Use `StringPool.Intern()` for repeated strings during parsing.
- **Frozen brushes:** Share WPF Brush instances across LogEntry objects.
- **ObservableRangeCollection:** Use `AddRange()` for batch UI updates (500 items/batch via `AppConstants.UiUpdateBatchSize`).
- **Parallel.ForEach:** For coloring and filtering large log collections.
- **ConfigureAwait(false):** In all service-layer async methods that don't touch UI.
- **Compiled Regex:** Use `RegexOptions.Compiled` for patterns used in hot paths.
- **Regex Timeout:** Always use `AppConstants.RegexTimeout` (2 seconds) for user-supplied patterns.
- **Deferred parsing:** Don't load nested ZIP data until the user opens that tab.
- **Memory-mapped files:** For large chart CSV data (ChartDataService).

---

## Security Rules

- **NEVER** hardcode passwords, API keys, or server paths in source code.
- Server paths go in `appsettings.json` (shipped empty) or `appsettings.local.json` (gitignored).
- Passwords encrypted with **DPAPI** (`DataProtectionScope.CurrentUser`).
- `AppConstants.RegexTimeout` = 2 seconds for all user-supplied regex (ReDoS prevention).
- `AppConstants.JsonMaxDepth` = 64 for JSON deserialization (stack overflow prevention).
- SHA256 verification for downloaded update installers.
- Plugin loading uses optional strong-name validation.

---

## Testing Standards

- **Framework:** xUnit with XPlat Code Coverage (Cobertura).
- **CI threshold:** 20% minimum line coverage (`.github/workflows/ci.yml`).
- **Test doubles:** Use `TestDialogService` for IDialogService mocks. NSubstitute available for other interfaces.
- All new service logic should have corresponding unit tests.
- ViewModel tests should use interface mocks, not concrete types.
- Tests must pass on CI (GitHub Actions, Windows runner, .NET 10.0).

---

## File Storage Locations

| Location | Content |
|---|---|
| `%AppData%\IndiLogs3.0\Configs\` | Filter configs, search profiles, schedules, defaults |
| `%AppData%\IndiLogs3.0\Plugins\` | External plugin DLLs |
| `%AppData%\IndiLogs3.0\GridColumnSettings\` | Column layout persistence |
| `%LocalAppData%\IndiLogs3.0\` | DPAPI credentials, rotating app log |
| Next to EXE | `appsettings.json`, `appsettings.local.json` (gitignored) |

---

## Git & CI Rules

- `.claude/` is **gitignored** — all Claude settings are local only.
- `appsettings.local.json` is **gitignored** — per-site server paths.
- CI pipeline: Restore -> Build (Release) -> Test -> Coverage check -> Artifact upload.
- Never force-push to `main`.
- Never skip pre-commit hooks.

---

## Key Files Reference

| File | Purpose |
|---|---|
| `Bootstrapper.cs` | Composition root — wires all services and ViewModels |
| `AppConstants.cs` | Application-wide constants (regex timeout, JSON depth, tab indices, batch size) |
| `App.xaml` | Theme, styles, color palette, resource dictionaries |
| `MainWindow.xaml` | Main layout (toolbar, 13 tabs, left/right panels, status bar) |
| `MainViewModel.cs` | Orchestrator — owns 10+ child ViewModels, 50+ commands |
| `LogFileService.cs` | Central file loading hub (ZIP processing, parsing, format detection) |
| `GlobalGrepService.cs` | Multi-location search with streaming results |
| `LogColoringService.cs` | Rule-based row coloring (parallel, cached regex) |
