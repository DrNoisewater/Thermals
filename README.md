# Thermals

**by Noisewater Labs**

A lightweight hardware monitor for Windows 10/11: a full sensor list with min/max/average,
a system inventory, CSV logging, and a click-through on-screen overlay that sits in a corner
of any monitor.

## Run it

1. **Install PawnIO once** from <https://pawnio.eu/>. It is the signed, Defender-friendly kernel
   driver that replaced WinRing0. Without it CPU temperatures, motherboard fans and voltages are
   missing (GPU, storage, memory and network still work).
2. **Build** (needs the .NET 8 SDK: `winget install Microsoft.DotNet.SDK.8`), then double-click
   `build.cmd`. You get a single self-contained `dist\Thermals.exe` — no runtime or installer needed.
   *Or* push this folder to GitHub: the included workflow builds the exe and attaches it to the run.
3. Run `Thermals.exe`. It asks for admin rights because low-level sensors require them.

## Features

- **Dark, custom-drawn interface** — every screen (sensor list, system info, settings, menus,
  dialogs) is drawn in the Noisewater Labs dark style with amber/red accents and a dark title bar.
- **Sensors tab** — every sensor LibreHardwareMonitor exposes (CPU, GPU, RAM, motherboard
  Super-I/O, storage, network, battery, optional AIO/fan controllers and digital PSUs), grouped by
  device with current / min / max / average. Click a device header to collapse it; each header
  shows how long that device takes to read. Filter (Ctrl+F), copy (Ctrl+C), export (Ctrl+E),
  reset statistics (Ctrl+R).
- **System tab** — OS edition and exact build, board and BIOS, CPU and caches, each RAM module
  (type, configured vs. rated speed, part number), GPUs with true VRAM size, physical disks,
  volumes, network adapters and displays. Right-click to copy.
- **Overlay** — always on top, click-through, never steals focus or shows in Alt-Tab.
  Choose corner, monitor, font, size, background opacity and colour; temperatures turn amber/red
  above your thresholds. Toggle anywhere with **Ctrl+Shift+O**.
  Pin or unpin any sensor by double-clicking it (or pressing Space) in the Sensors list; reorder
  and rename in *Settings → Overlay items*. Changes in *Settings → Overlay* show on screen live
  before you save.
- **Tray icon** shows the first overlay value live; tooltip shows the first four.
- **CSV logging** of every sensor at the polling interval (Ctrl+L). A new file starts if hardware changes.
- **Start with Windows** via a scheduled task with highest privileges, so there's no UAC prompt at logon.
- Single instance: launching it again just brings the window back.

## Accuracy

Readings come straight from the hardware through LibreHardwareMonitor (the open-source engine
behind tools like FanControl), polled on one dedicated thread. Min/max/average are Thermals' own
statistics over every sample since start or last reset. Where Windows' own data is known to be
wrong, the reliable source is used instead: VRAM from the driver's 64-bit registry value (WMI caps
it at 4 GB), Windows version from WMI + registry build/UBR (the registry alone reports Windows 11 as 10).

## Performance

- One background thread at below-normal priority; the UI only touches changed cells.
- **When the main window is closed, only the devices feeding the overlay are polled.** Everything
  else is skipped until you open the window again (or start logging).
- The overlay redraws only when a displayed value changes, using a single layered-window blit.
- Drives are read less often: SSDs every 5 seconds, hard drives every minute. A spun-down
  hard drive is never read, because reading its health data would wake it; its header shows
  "Asleep · not woken". Change this, or turn off hardware groups you don't need, in
  *Settings → Hardware*. (When Thermals first starts, the sensor library's one-time drive scan may wake a
  sleeping drive once.)
- The status bar shows how long each poll takes.

## Files

Settings, the error log and CSV logs live in `%AppData%\Noisewater Labs\Thermals`
(settings from an older `%AppData%\Thermals` folder are copied over automatically).

## Notes

- Exclusive-fullscreen games bypass the desktop compositor, so no normal window (this overlay
  included) can draw over them. Use borderless/windowed fullscreen.
- Some anti-cheat systems block kernel drivers, including PawnIO; sensors that need it disappear
  while such a game is running.

## Project layout

```
src/
  Program.cs            entry point, single-instance handling
  Core/SensorHub.cs     LibreHardwareMonitor polling thread, statistics, snapshots
  Core/SensorFormat.cs  units and formatting
  Core/SystemInfo.cs    WMI / registry inventory
  Core/CsvLogger.cs     logging
  Core/AppSettings.cs   JSON settings (atomic save)
  Core/Platform.cs      elevation, PawnIO detection, start-with-Windows task
  UI/AppHost.cs         app root: tray, hotkey, wiring
  UI/Theme.cs           Noisewater Labs palette, fonts, dark menus and title bar
  UI/Controls.cs        custom-drawn buttons, tabs, inputs, toggles, sliders, pickers
  UI/SensorGrid.cs      virtualised grouped sensor list
  UI/FormPage.cs        settings page layout and dark dialogs
  UI/MainForm.cs        sensor and system window
  UI/OverlayForm.cs     per-pixel-alpha click-through overlay
  UI/SettingsForm.cs    settings dialog
```

© 2026 Noisewater Labs. Sensor engine: LibreHardwareMonitorLib (MPL-2.0).
