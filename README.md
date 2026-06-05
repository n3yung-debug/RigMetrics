# Rust FPS Tracker

A Windows desktop app for measuring **FPS and frame consistency** in the Steam
game **Rust**, alongside **CPU / GPU / RAM usage and temperatures**. It's built
to answer one question: *which of my settings changes actually give me the most
FPS and the smoothest experience?*

Record a session, change a setting, record another, and compare them side by
side.

---

## How it measures FPS (and why it's anti-cheat safe)

Rust runs **Easy Anti-Cheat (EAC)**. Many FPS overlays work by *injecting* code
into the game (like the Steam or Discord overlay). That carries a ban risk, so
this tool **does not** do that.

Instead it uses **[Intel PresentMon](https://github.com/GameTechDev/PresentMon)**,
which reads frame-presentation timings through **ETW (Event Tracing for
Windows)** at the operating-system level. It never touches Rust's memory, so it
is safe to use with EAC and works for any DirectX/Vulkan game.

Hardware sensors come from
**[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)**,
the standard open-source library for reading CPU/GPU/RAM load and temperatures
(Intel, AMD, and NVIDIA).

> **Disclaimer:** This tool only *reads* publicly available OS telemetry and
> never modifies the game. It is, to the best of our knowledge, safe to use with
> EAC, but you use it at your own risk.

---

## What it tracks

**FPS / smoothness**

| Metric | Why it matters |
| --- | --- |
| Average FPS | Overall performance |
| **1% low / 0.1% low FPS** | The real measure of *consistency* — these expose stutter that average FPS hides. **Watch these when comparing settings.** |
| Frame time (ms) + std-dev | Smoothness; a flat frame-time graph feels better than a high but spiky average |
| Stutter count | Number of frames that took dramatically longer than usual |

**Hardware**

- CPU load % and temperature
- GPU load %, temperature, and VRAM used
- RAM used (GB) and load %

---

## Prerequisites

1. **Windows 10 or 11** (64-bit).
2. **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** to build
   (end users of a published build only need the app itself).
3. **PresentMon.exe** — download the latest release from
   [PresentMon Releases](https://github.com/GameTechDev/PresentMon/releases),
   rename it to `PresentMon.exe`, and place it **next to the app's exe** (or set
   `presentMonPath` in `appsettings.json`).
4. Run the app **as Administrator** — required for ETW frame capture and for
   reading temperatures. (The app already requests elevation automatically.)

---

## Build & run

```powershell
# from the repository root
dotnet build RustFpsTracker.sln -c Release

# run it (will prompt for Administrator elevation)
dotnet run --project src/RustFpsTracker.App -c Release
```

### Make a single portable .exe

```powershell
dotnet publish src/RustFpsTracker.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The output lands in
`src/RustFpsTracker.App/bin/Release/net8.0-windows/win-x64/publish/`.
Copy that folder (plus `PresentMon.exe`) to any PC — no .NET install needed.

### Run the tests

```powershell
dotnet test
```

The unit tests cover the statistics math (averages, 1%/0.1% lows, stutter
detection) and the PresentMon CSV parser, and run on any OS.

---

## How to use it

1. Launch Rust.
2. Launch **Rust FPS Tracker** (as Administrator).
3. Type a **label** describing your current settings, e.g. `Shadows Low, AA Off`.
4. Press **Start**, play for a consistent test (same spot/activity works best),
   then press **Stop**. The session is saved automatically.
5. Change one setting in Rust, then repeat with a new label.
6. Select **two** saved sessions and click **Compare 2** to see which settings
   helped — green deltas are improvements.
7. **Export CSV** writes per-frame and per-second data for Excel.

Tip: enable **Always on top** and drag the window to a second monitor to watch
live numbers while you play.

Sessions are stored as JSON in
`%AppData%\RustFpsTracker\sessions`.

---

## Configuration (`appsettings.json`)

```json
{
  "presentMonPath": "PresentMon.exe",   // path to PresentMon (relative = next to the app)
  "gameProcessName": "RustClient.exe",  // Rust's process; change to profile another game
  "presentMonExtraArgs": [],            // extra PresentMon CLI flags, if needed
  "sensorPollMs": 1000,                 // how often hardware sensors are read
  "stutterMultiplier": 2.0,             // frame counts as a stutter above N x median frame time
  "liveWindowSeconds": 60               // rolling window for the live dashboard
}
```

> The CSV parser keys off **column names**, so it adapts to both PresentMon 1.x
> and 2.x output. If a future PresentMon build changes flags, adjust
> `presentMonExtraArgs`.

---

## Project layout

```
src/
  RustFpsTracker.Core/   Cross-platform engine: models, stats, CSV parser, storage (unit-tested)
  RustFpsTracker.App/    WPF dashboard, PresentMon + hardware services
tests/
  RustFpsTracker.Core.Tests/  xUnit tests for the engine
```

---

## Ideas for later

- Auto-detect when Rust launches and start/stop on its own
- Per-second timeline charts in the saved report (not just live)
- Tag sessions with the actual Rust config file so changes are captured automatically
- Thermal-throttle warnings
- Overlay via a second-monitor borderless window (still injection-free)
```
