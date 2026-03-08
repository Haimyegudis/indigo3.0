# Plan: Interactive HTML Help Page for IndiLogs 3.0

## Goal
Replace the existing WPF HelpWindow with a comprehensive, interactive HTML help page (`docs/help.html`) that covers all features, UI layout, architecture, and includes interactive examples.

## Approach
Single self-contained HTML file (no external dependencies) with embedded CSS and JavaScript. Uses the same dark theme as the existing `docs/index.html` landing page for visual consistency.

## File to Create
- `docs/help.html` — ~3000-4000 lines, single self-contained file

## Sections

### 1. Navigation & Hero
- Fixed top nav with section links
- Hero header: "IndiLogs 3.0 - Complete User Guide"
- Quick stats: 13 tabs, 35+ windows, 20+ services

### 2. Interactive UI Layout Mockup
- Full replica of MainWindow layout:
  - **Toolbar**: Settings, OPEN, Mark Row, Next/Prev Marked, SYNC, Annotations, Failures, Live Mode buttons, JIRA/Kibana/Outlook/Help
  - **13 Tab bar**: PLC LOGS, APP, EVENTS, SCREENSHOTS, CONFIG, DB & CONFIG, SETUP INFO, GLOBALS, SYSTAB, CHARTS, CPR, STEP RECORDER, DIFFERENT LOGS
  - **Left Panel**: EXPLORER (session list) and LOGGERS (hierarchical tree with checkmarks)
  - **Center Panel**: Data grid with sample log rows (colored rows, error rows, state rows, marked rows)
  - **Right Panel**: SAVED CONFIGURATIONS list
  - **Status Bar**: Session info, log count, filters active
  - **Heatmap Scrollbar**: Color-coded ticks on right edge
- Clicking tabs changes the center panel content to show what each tab displays
- Interactive: click toolbar buttons to see tooltips/explanations

### 3. Features Deep-Dive (Interactive Tabs)

Each feature gets its own interactive demo section:

#### 3a. Search & Navigation
- Interactive search bar mockup (type to see highlighting)
- SYNC button explanation with animated diagram
- Global Grep workflow diagram

#### 3b. Filtering System
- Interactive filter demo: checkboxes for Filter/Filter Out
- Tree diagram showing filter types: Logger, Thread, Message, Time Focus, Advanced
- Active Filters panel mockup showing badges
- Advanced Filter Editor tree mockup (AND/OR/NOT groups)

#### 3c. Coloring
- Interactive color rule builder mockup
- Sample log rows with default colors (Error=red, State=blue, MechInit=orange, GetReady=light green, Print=green)
- Regex examples with live preview

#### 3d. Marking & Annotations
- Row marking demo (click Space to toggle)
- Annotation popup mockup
- Marked Lines window layout

#### 3e. Charts
- Signal chart mockup (SVG line chart)
- Gantt view mockup
- Thread view mockup
- State Timeline mockup
- Chart controls: signal list, Y-axis toggle, zoom/pan, reference lines

#### 3f. CPR Analysis
- Graph type selector (Colors, DFT, Histogram, Blanket Cycles, etc.)
- Station pair configuration mockup
- Filter controls (Revolution, Iteration, Cycle/Column range)
- Statistics table mockup

#### 3g. Analysis Tools
- Stats window mockup (error histogram, thread load, time gaps)
- States window (state transitions timeline)
- Failures analysis flow
- Compare window (dual pane with diff highlighting)

#### 3h. Export & Reports
- Export configuration dialog mockup
- Component selection (IO, AXIS, CHStep, Thread)
- Export presets

#### 3i. Case Management
- Save/Load case workflow
- Configuration panel (right sidebar)

#### 3j. Global Grep
- Multi-location search interface mockup
- Profile management
- Schedule editor
- Email notification config

### 4. All Windows & Dialogs Reference
- Card grid showing all 35+ windows with:
  - Name, icon, purpose
  - How to open (menu path or shortcut)
  - Key features

### 5. Architecture Section

#### 5a. Architecture Overview
- Interactive folder tree diagram
- MVVM pattern explanation with visual diagram (View <-> ViewModel <-> Service <-> Model)

#### 5b. Folder-by-Folder Breakdown
Each folder gets a collapsible section with file listing:

- **Views/** — XAML windows, 35+ files, zero business logic
- **ViewModels/** — Application logic hub
  - MainViewModel (orchestrator, 50+ commands, split into 10 partial files)
  - Components/ — child VMs (FilterSearch, CaseManagement, ConfigExplorer, etc.)
- **Services/** — Business logic layer
  - Interfaces/ — 12+ service contracts
  - Charts/ — ChartDataService, ChartDataTransferService, etc.
  - Grep/ — GlobalGrepService, SearchSchedulerService, etc.
  - Analysis/ — UniversalStateFailureAnalyzer
  - BuiltInPlugins/ — 4 built-in log parsers
  - Cpr/ — CPR data and analysis services
- **Models/** — Data structures (LogEntry, EventEntry, StateEntry, etc.)
  - Charts/, Grep/, Analysis/, Cpr/ subfolders
- **Controls/** — Custom WPF controls (heatmap, charts, CPR)
- **Converters/** — XAML value converters
- **Resources/** — Icons and assets

#### 5c. DI & Bootstrapper
- Bootstrapper.cs composition root diagram
- Service registration flow
- Constructor injection pattern

#### 5d. Performance Architecture
- StringPool, frozen brushes, memory-mapped files
- Parallel.ForEach for coloring/filtering
- ObservableRangeCollection batching
- Deferred parsing strategy

#### 5e. Security Architecture
- DPAPI encryption, regex timeout, JSON depth limits
- Plugin validation, SHA256 update verification

### 6. Keyboard Shortcuts Reference
- Clean table with all shortcuts grouped by context
- General, Compare Window, Context Menu

### 7. File Storage Locations
- Table of all AppData paths and what they store

## Interactive Features (JavaScript)
- **Tab switching**: Click any tab in mockups to see content change
- **Collapsible sections**: Expand/collapse architecture sections
- **Search**: Full-text search across all help content
- **Smooth scrolling**: Navigation links scroll smoothly
- **Theme toggle**: Dark/Light mode
- **Interactive tree**: Expand/collapse folder structure
- **Tooltips**: Hover over UI elements for explanations
- **Filter demo**: Interactive checkbox toggles showing/hiding sample rows
- **Color demo**: Click color rules to see rows change color

## Integration
- Update HelpWindow.xaml.cs to open the HTML file in the default browser instead of showing WPF content
- OR keep both and add a "Open Full Guide" button

## Steps
1. Create `docs/help.html` with full content
2. Build and test the project compiles
