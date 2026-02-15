# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build    # Build the project
dotnet run      # Run the app
```

No tests or linting configured.

## Architecture

Active window tracker WPF app (.NET 9, WPF only). Records the foreground window's process name and title every 1 minute, stores as daily CSV, and displays aggregated activity in a topmost chromeless overlay.

**Key components:**

- **ActivityLogger** (`ActivityLogger.cs`) — 1-minute timer using `System.Threading.Timer`. Calls Win32 APIs (`GetForegroundWindow`, `GetWindowText`, `GetWindowThreadProcessId` via P/Invoke) to capture the active window. Appends to daily CSV in `%LOCALAPPDATA%\Proc\logs\YYYY-MM-DD.csv`. Fires `OnRecorded` event for UI refresh.
- **ActivityRecord** (`ActivityRecord.cs`) — Data record with CSV serialization/deserialization (handles quoting/escaping).
- **MainWindow** (`MainWindow.xaml/.cs`) — Chromeless WPF window (`WindowStyle=None`, `AllowsTransparency`, `SizeToContent=WidthAndHeight`). Topmost, no taskbar entry. Draggable anywhere. Context menu toggles title visibility (exe-only vs exe+title aggregation). System tray icon via `Hardcodet.NotifyIcon.Wpf` (left-click toggle visibility, right-click exit).
- **App** (`App.xaml/.cs`) — Standard WPF application entry point.

**Dependencies:** `Hardcodet.NotifyIcon.Wpf` for system tray icon (pure WPF, no WinForms dependency).
