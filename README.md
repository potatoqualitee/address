# Address Bar

A Windows 11 recreation of the classic Address Bar taskbar toolbar, built with C# WinForms.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **AppBar Docking** - Docks to top or bottom of screen as a proper Application Desktop Toolbar. Maximized windows won't overlap it.
- **Multi-Monitor Support** - Show on all monitors simultaneously, or pick a specific one
- **Smart Navigation**
  - URLs → Opens in default browser
  - File/folder paths → Opens in Explorer
  - Environment variables supported (`%USERPROFILE%\Documents`)
  - Everything else → Executes as shell command
- **Dark/Light Mode** - Auto-detects Windows theme and updates dynamically
- **System Tray** - Right-click for quick settings, monitor selection, and dock position
- **DPI Aware** - PerMonitorV2 scaling support
- **Auto-Reconfigure** - Detects display changes and adjusts automatically

## Installation

### Requirements
- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Download
Grab the latest release from [Releases](https://github.com/potatoqualitee/address/releases).

Extract and run `AddressBar.exe`.

## Configuration

### Quick Settings (Tray Menu)
Right-click the system tray icon for:
- **Monitor** → All Monitors, or select a specific display
- **Dock Position** → Top or Bottom
- **Settings Folder** → Opens the config directory

### Settings File
Settings are stored in `%APPDATA%\AddressBar\settings.json`:

```json
{
  "MultiMonitor": false,
  "MonitorIndex": 0,
  "BarHeight": 30,
  "DockPosition": "Top"
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `MultiMonitor` | Show address bar on all monitors | `false` |
| `MonitorIndex` | Which monitor to dock to (0 = primary, 1 = second, etc.) | `0` |
| `BarHeight` | Height of the address bar in pixels | `30` |
| `DockPosition` | Where to dock: `Top` or `Bottom` | `Top` |

## Usage

1. Type in the address box and press **Enter** (or click **→**)
2. **Escape** clears the text
3. Right-click the system tray icon for options
4. Double-click tray icon to show if hidden

### Examples

| Input | Action |
|-------|--------|
| `https://github.com` | Opens in browser |
| `github.com` | Opens in browser (auto-adds https://) |
| `C:\Windows` | Opens folder in Explorer |
| `%APPDATA%` | Opens AppData folder |
| `notepad` | Launches Notepad |
| `cmd` | Opens Command Prompt |

## Building

```bash
git clone https://github.com/potatoqualitee/address.git
cd address
dotnet build
dotnet run
```

### Publish

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

## License

MIT
