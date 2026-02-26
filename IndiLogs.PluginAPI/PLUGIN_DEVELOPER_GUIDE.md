# IndiLogs 3.0 — Plugin Developer Guide

## Overview

IndiLogs 3.0 supports third-party log-parser plugins.
A plugin is a **.NET Framework 4.8 class library** (`.dll`) that implements one interface: `ILogFilePlugin`.

When IndiLogs opens a file (or a ZIP archive entry) that it does not recognise natively, it offers the file to each registered plugin in order. The first plugin that accepts the file parses it and the resulting log entries appear in the PLC or APP tab.

---

## What You Need

| Item | Where to get it |
|------|-----------------|
| `IndiLogs.PluginAPI.dll` | Distributed by the IndiLogs team (built from the `IndiLogs.PluginAPI` project) |
| Visual Studio 2019 or 2022 (or any .NET 4.8-compatible build tool) | — |
| `SampleCsvLogPlugin` folder | Distributed as a starter template |

---

## Quick Start (5 minutes)

### 1. Create a new Class Library project

- **Framework**: .NET Framework 4.8
- **Language**: C# 8.0 or later

### 2. Reference the contract DLL

Copy `IndiLogs.PluginAPI.dll` into your project folder, then add a reference:

**Old-style `.csproj`:**
```xml
<ItemGroup>
  <Reference Include="IndiLogs.PluginAPI">
    <HintPath>IndiLogs.PluginAPI.dll</HintPath>
  </Reference>
</ItemGroup>
```

**SDK-style `.csproj`:**
```xml
<ItemGroup>
  <Reference Include="IndiLogs.PluginAPI">
    <HintPath>IndiLogs.PluginAPI.dll</HintPath>
  </Reference>
</ItemGroup>
```

### 3. Implement `ILogFilePlugin`

Add a `public`, non-abstract class that implements `IndiLogs.PluginAPI.ILogFilePlugin`.
IndiLogs discovers **all** such classes in your DLL automatically.

```csharp
using IndiLogs.PluginAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MyCompany.MyLogPlugin
{
    public class MyDeviceLogPlugin : ILogFilePlugin
    {
        // ── Metadata ─────────────────────────────────────────────────
        public string Name    => "My Device Log Parser";
        public string Version => "1.0.0";

        // ── Detection ────────────────────────────────────────────────
        // Called for every file the built-in parsers rejected.
        // Return true if you can parse this file.
        // Keep this fast — do NOT open the stream here.
        public bool CanHandle(string fileName, string[] sampleLines)
        {
            // Accept any file named "device*.log"
            if (!fileName.StartsWith("device", StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                return false;

            // Confirm the first line looks like our format
            return sampleLines.Length > 0
                && sampleLines[0].StartsWith("[DEVICE]");
        }

        // ── Parsing ──────────────────────────────────────────────────
        // Stream is positioned at offset 0. Enumerate entries lazily.
        public IEnumerable<LogEntryDto> Parse(
            Stream stream,
            ParseContext context,
            IProgress<double> progress,
            CancellationToken ct)
        {
            long totalBytes = stream.CanSeek ? stream.Length : 0;

            using var reader = new StreamReader(stream,
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 65536,
                leaveOpen: true);

            string line;
            int lineNum = 0;

            while ((line = reader.ReadLine()) != null)
            {
                ct.ThrowIfCancellationRequested();
                lineNum++;

                // Report progress every 1 000 lines
                if (lineNum % 1000 == 0 && totalBytes > 0)
                    progress?.Report((double)stream.Position / totalBytes * 100);

                if (string.IsNullOrWhiteSpace(line)) continue;

                // --- Parse your format here ---
                // Example line: [DEVICE] 2026-01-15 10:32:01.123 ERROR Sensor timeout
                if (!TryParseLine(line, out var entry)) continue;

                yield return entry;
            }

            progress?.Report(100);
        }

        // ── Private helpers ───────────────────────────────────────────
        private static bool TryParseLine(string line, out LogEntryDto entry)
        {
            entry = null;
            // ... your parsing logic ...
            // Return false to skip unrecognised lines.
            return false;
        }
    }
}
```

### 4. Build your project

```
Build → Build Solution
```

Output: `bin\Debug\net48\MyDeviceLogPlugin.dll`

### 5. Install the plugin

Create the Plugins folder if it does not exist and copy your DLL there:

```
%AppData%\IndiLogs3.0\Plugins\MyDeviceLogPlugin.dll
```

> If your plugin depends on additional DLLs (e.g. a custom parser library), copy those into the same `Plugins\` folder.

### 6. Test it

1. Restart IndiLogs.
2. Open (or drag-and-drop) a file your plugin handles.
3. The log entries appear in the **PLC** tab by default (or **APP** tab if you set `ProcessName = "APP"`).

**To confirm loading**, check the Visual Studio Debug Output (or attach a debugger):
```
[PLUGINS] Scanning 1 DLL(s) in C:\Users\...\AppData\Roaming\IndiLogs3.0\Plugins
[PLUGINS]   ✅ My Device Log Parser v1.0.0  (MyCompany.MyLogPlugin.MyDeviceLogPlugin)
[PLUGINS] ✅ 1 plugin(s) ready: My Device Log Parser v1.0.0
...
[PLUGINS] File 'device_20260115.log' claimed by 'My Device Log Parser'
[PLUGINS] My Device Log Parser produced 2 341 entries from 'device_20260115.log'
```

---

## API Reference

### `ILogFilePlugin`

```csharp
public interface ILogFilePlugin
{
    string Name    { get; }   // e.g. "My Device Log Parser"
    string Version { get; }   // e.g. "1.0.0"

    bool CanHandle(string fileName, string[] sampleLines);

    IEnumerable<LogEntryDto> Parse(
        Stream stream,
        ParseContext context,
        IProgress<double> progress,
        CancellationToken ct);
}
```

---

### `bool CanHandle(string fileName, string[] sampleLines)`

| Parameter | Description |
|-----------|-------------|
| `fileName` | File name only, no path (e.g. `"device.log"` or `"data.csv"`). |
| `sampleLines` | First 20 lines of the file as plain text strings. May be empty for binary files. |

**Rules:**
- Must be **fast** — runs on the UI thread during file scanning.
- Must be **side-effect free** — do not open streams, write files, or allocate large objects.
- Return `true` only if you are confident you can parse the file.

---

### `IEnumerable<LogEntryDto> Parse(...)`

| Parameter | Description |
|-----------|-------------|
| `stream` | Readable stream, positioned at offset 0. |
| `context` | Metadata: file name, whether source is a ZIP, ZIP entry path. |
| `progress` | Report values in `[0, 100]`. May be `null` — always null-check before calling. |
| `ct` | Check periodically with `ct.ThrowIfCancellationRequested()` for large files. |

**Rules:**
- Yield entries lazily (`yield return`) for memory efficiency.
- Throw exceptions freely — IndiLogs catches them, logs the error, and skips the file.
- Do **not** close the stream — IndiLogs manages its lifetime.
- The method is called from a **Parallel.ForEach** worker thread. If you use any shared state, protect it with a lock.

---

### `LogEntryDto` — fields

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Date` | `DateTime` | ✅ | Log timestamp. |
| `Message` | `string` | ✅ | Main log text. |
| `Level` | `string` | — | `"Debug"`, `"Info"`, `"Warning"`, `"Error"`, `"Fatal"`. Defaults to `"Info"`. |
| `ThreadName` | `string` | — | Thread or subsystem name. |
| `Logger` | `string` | — | Component or logger name. |
| `ProcessName` | `string` | — | `"APP"` → APP tab. Anything else (or `null`) → PLC tab. |
| `Method` | `string` | — | Method or code location. |
| `Data` | `string` | — | Extra structured payload (e.g. JSON). |
| `Exception` | `string` | — | Exception text or stack trace. |

---

### `ParseContext` — fields

| Property | Type | Description |
|----------|------|-------------|
| `FileName` | `string` | File name only (no path). |
| `FilePath` | `string` | Full path on disk. `null` when source is inside a ZIP. |
| `IsInsideZip` | `bool` | `true` when the stream comes from a ZIP archive entry. |
| `ZipEntryPath` | `string` | Full entry path inside the ZIP. `null` when not a ZIP. |

---

## Routing: PLC tab vs APP tab

IndiLogs has two main log tabs. Set `LogEntryDto.ProcessName` to control routing:

```csharp
// → PLC tab (default)
yield return new LogEntryDto { ..., ProcessName = null };

// → APP tab
yield return new LogEntryDto { ..., ProcessName = "APP" };
```

---

## Handling ZIP archives

IndiLogs also passes ZIP archive entries to plugins. If your log files are typically delivered inside a ZIP, your `CanHandle` will still be called with just the **entry name** (not the full ZIP path), so your filename-based checks work unchanged.

```csharp
public bool CanHandle(string fileName, string[] sampleLines)
{
    // Works for both loose files and ZIP entries
    return fileName.EndsWith(".mylog", StringComparison.OrdinalIgnoreCase);
}
```

Use `context.IsInsideZip` and `context.ZipEntryPath` inside `Parse` if you need to distinguish the two cases.

---

## Packaging and distribution

To distribute your plugin:

```
MyDeviceLogPlugin\
├── MyDeviceLogPlugin.dll      ← your compiled plugin
└── SomeDependency.dll         ← any private dependency DLLs (if needed)
```

**Do NOT include `IndiLogs.PluginAPI.dll`** in the distribution package.
That DLL is already shipped with IndiLogs and will conflict if duplicated.

Users copy the entire folder contents into:
```
%AppData%\IndiLogs3.0\Plugins\
```

---

## Tips and best practices

| Tip | Details |
|-----|---------|
| **Be specific in `CanHandle`** | Check both the filename AND the first line of content. A false positive means your plugin tries to parse a file it cannot handle. |
| **Handle encoding** | Use `StreamReader` with `detectEncodingFromByteOrderMarks: true` to support UTF-8, UTF-16, and Latin-1 files. |
| **Don't intern strings yourself** | IndiLogs interns `Level`, `ThreadName`, `Logger`, `ProcessName`, and `Method` automatically. Set `Message`, `Data`, and `Exception` to raw strings. |
| **Skip blank/header lines** | Your parser should return `false` (or continue) for lines it does not recognise rather than yielding a malformed entry. |
| **Support cancellation** | Call `ct.ThrowIfCancellationRequested()` inside the main loop, especially for files larger than a few MB. |
| **Report progress** | Call `progress?.Report(pct)` every 500–1000 lines so the loading bar stays responsive. |
| **Thread safety** | `Parse` runs on a thread-pool thread. Avoid static mutable state; if unavoidable, use `lock`. |

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Plugin not loaded at all | DLL not in `%AppData%\IndiLogs3.0\Plugins\` | Check the folder path |
| Plugin not loaded at all | DLL targets wrong framework | Must be .NET Framework 4.8 |
| `CanHandle` returns `true` but no entries appear | `Parse` throws an exception | Check the VS Debug Output for `[PLUGINS]` error lines |
| Entries appear in wrong tab | `ProcessName` not set correctly | Use `"APP"` for APP tab, anything else for PLC tab |
| Plugin loads but IndiLogs crashes on startup | Dependency DLL missing | Copy all required DLLs to `Plugins\` folder |

---

## Sample plugin — full source

See the `SampleCsvLogPlugin` project included in this repository.
It parses CSV files whose header contains `Timestamp, Level, Thread, Message` columns
and serves as a ready-to-use starting point for your own parser.
