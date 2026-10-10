# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build              # Build the app
dotnet run                # Run the app
dotnet test Proc.Tests    # Run xUnit tests (use -c Release if a Debug Proc.exe is running and locks bin/Debug)
```

No linting configured. `Proc.csproj` excludes `Proc.Tests/**` from its compile globs.

## Architecture

Active window tracker WPF app (.NET 9, WPF only). Samples the foreground window every second, writes one record per minute (the window used longest in that minute) to daily CSVs, shows today's totals in a topmost chromeless overlay, and has an analysis window for day/week/month views.

**Logging**

- **ActivityLogger** — Two `System.Threading.Timer`s: a 1s sampler (idle detection via `GetLastInputInfo`, foreground window sampling into `_minuteSamples`) and a 1-minute tick that writes the dominant window, or an `Idle` record when idle 1–5 min (nothing when idle ≥5 min or the session is locked). Also a `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` for instant overlay updates. Keeps today's records in memory (`GetTodayRecords`; call `InvalidateCache` after external file changes). Applies title exclusion and log retention from settings (`ReloadSettings`). Events (`OnRecorded`, `OnActiveChanged`, `OnIdleChanged`) fire on non-UI threads.
- **ActivityRecord** / **CsvUtil** — Record model and the shared CSV escape/parse. Log line: `HH:mm:ss,process,title[,idle]` in `%LOCALAPPDATA%\Proc\logs\YYYY-MM-DD.csv`.
- **LogAnalyzer** — Pure functions: read logs, aggregate (top N + "Other"), timeline blocks, daily stacks, retention cleanup, formatting.

**UI**

- **MainWindow** — Chromeless overlay (`WindowStyle=None`, `AllowsTransparency`, `SizeToContent`), topmost, no taskbar entry, draggable (position saved after drag). Tray icon via `Hardcodet.NotifyIcon.Wpf`. Optional auto-show while idle.
- **AnalysisWindow** + **AnalysisViewModel** — Period navigation, Day timeline with range selection, Week/Month daily stacked chart, app filter (persisted `HiddenApps`), group mode (App / Category / Site — implemented by relabeling `ProcessName` before aggregation), CSV/Markdown export via **ReportExporter**.
- **SettingsWindow** — Startup, title display, admin, idle display, theme colors, privacy (title-excluded apps, retention days), category rules. Saves immediately; closes on deactivate.

**Other**

- **AppSettings** — JSON at `%LOCALAPPDATA%\Proc\settings.json`, atomic save; use `AppSettings.Update(...)` to avoid clobbering other windows' changes.
- **CategoryRules** / **SiteExtractor** — Category rule parsing (`Name = process, title:text`) and browser-title → site heuristics.
- **StartupManager** — HKCU Run entry, or an elevated logon task (schtasks XML) when Run-as-admin is on and Proc is elevated.
- **AutoUpdater** — GitHub Releases; requires `Proc.exe.sha256` asset (produced by `.github/workflows/release.yml`). Silent at startup, asks on the 12-hour periodic check.
- **ErrorLog** — `%LOCALAPPDATA%\Proc\error.log`, deduplicated, rotated at 1 MB. Use it instead of empty `catch { }` for anything worth diagnosing.
