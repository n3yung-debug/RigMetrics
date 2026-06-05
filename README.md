# RigMetrics

A Windows desktop app for measuring **FPS and frame consistency** in any game
(originally built for **Rust**), alongside **CPU / GPU / RAM usage and
temperatures** — your whole rig's metrics in one place. It's built to answer one
question: *which of my settings changes actually give me the most FPS and the
smoothest experience?*

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

## Easiest way to run it (no building)

1. Grab the latest **`RigMetrics-win-x64.zip`** from the repository's
   **Actions → release** workflow artifacts (or the **Releases** page if a
   version tag has been published).
2. Unzip it anywhere. The ZIP already contains **everything** — the app, the
   .NET runtime (self-contained, no install needed), and **PresentMon**.
3. Right-click `RigMetrics.exe` → **Run as administrator**.

That's it — one folder, nothing else to download.

> Building from source instead? See **Build & run** below. If PresentMon isn't
> present, the app offers to download the official build automatically on first
> Start.

## Prerequisites (only if building from source)

1. **Windows 10 or 11** (64-bit).
2. **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**.
3. **PresentMon** — bundled in the release ZIP; for source builds the app will
   offer to fetch it for you, or you can drop `PresentMon.exe` next to the app
   manually from [PresentMon Releases](https://github.com/GameTechDev/PresentMon/releases).
4. Run the app **as Administrator** — required for ETW frame capture and for
   reading temperatures. (The app already requests elevation automatically.)

---

## Build & run

```powershell
# from the repository root
dotnet build RigMetrics.sln -c Release

# run it (will prompt for Administrator elevation)
dotnet run --project src/RigMetrics.App -c Release
```

### Make a single portable .exe

```powershell
dotnet publish src/RigMetrics.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The output lands in
`src/RigMetrics.App/bin/Release/net8.0-windows/win-x64/publish/`.
Copy that folder (plus `PresentMon.exe`) to any PC — no .NET install needed.

### Run the tests

```powershell
dotnet test
```

The unit tests cover the statistics math (averages, 1%/0.1% lows, stutter
detection) and the PresentMon CSV parser, and run on any OS.

---

## How to use it

1. Launch your game (Rust or anything else).
2. Launch **RigMetrics** (as Administrator).
3. Pick the **Game** from the dropdown (it lists running apps), or type its
   process name, e.g. `cs2.exe`. Your choice is remembered next time.
4. Type a **label** describing your current settings, e.g. `Shadows Low, AA Off`.
5. Press **Start**, play for a consistent test (same spot/activity works best),
   then press **Stop**. The session is saved automatically.
6. Change one setting in the game, then repeat with a new label.
7. Select **two** saved sessions and click **Compare 2** to see which settings
   helped — green deltas are improvements.
8. **Export report** writes a single Excel workbook for the selected session
   (see below).

Tip: enable **Always on top** and drag the window to a second monitor to watch
live numbers while you play.

> **Works with any game**, not just Rust. FPS is captured for whichever process
> you select in the **Game** dropdown; CPU/GPU/RAM load and temperatures are
> whole-system. Find a game's process name in **Task Manager → Details** if it
> isn't in the list.

Sessions are stored as JSON in
`%AppData%\RigMetrics\sessions`.

## Per-session report (Export report)

Selecting a session and clicking **Export report** writes one Excel workbook
(`<label>_report.xlsx`) with three sheets:

- **Summary** &mdash; whole-test averages: FPS (avg, 1% / 0.1% low, min/max,
  frame-time, stutters, dropped frames) and sensors (avg CPU/GPU/RAM load,
  avg + max CPU/GPU temps). No memory-used amounts.
- **FPS over time** &mdash; time buckets of **5 minutes**, automatically
  dropping to **30 seconds** whenever frames are dropped, and staying fine
  until **60 seconds** pass with no drops (then back to 5 minutes). Buckets
  with drops are highlighted.
- **Sensors over time** &mdash; the same adaptive buckets, but the trigger is
  **dangerous temperatures** (defaults: CPU &ge; 90 &deg;C, GPU &ge; 85 &deg;C).
  It samples every 30 seconds until temperatures **stabilize** (cool ~5 &deg;C
  below the threshold), then returns to 5-minute buckets. Loads and temps only.

All thresholds and bucket sizes are configurable in `appsettings.json`
(`reportCoarseMinutes`, `reportFineSeconds`, `reportDropHoldSeconds`,
`reportCpuDangerC`, `reportGpuDangerC`, `reportStabilizeMarginC`).

## Tracking results over time (and Google Sheets)

Every time you stop a session, its summary is automatically appended to a
running master spreadsheet:

```
%AppData%\RigMetrics\sessions\results-master.csv
```

Click **Results / Sheets** in the app to open a sortable table of every session
(avg, 1% / 0.1% low, frame-time consistency, stutters, CPU/GPU temps, etc.).
From there you can:

- **Export CSV** &mdash; a consolidated file of all results.
- **Export Excel (.xlsx)** &mdash; a formatted workbook.
- **Open master file** &mdash; the always-up-to-date running log.

### Getting it into Google Sheets

Both exports drop straight into Google Sheets &mdash; no account linking needed:

1. In Google Sheets: **File → Import → Upload**, and choose the exported
   `.csv` or `.xlsx`.
2. Or simply open the `.xlsx` from Google Drive.

> A direct "log in with Google and auto-push" integration was intentionally
> left out: it would require every user to set up their own Google Cloud OAuth
> client, since OAuth credentials can't be safely shipped inside a downloadable
> app. The import-a-file route above is instant and works for everyone. (If you
> later want the live-sync version, it can be added &mdash; it just needs that
> one-time OAuth client setup.)

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
  RigMetrics.Core/   Cross-platform engine: models, stats, CSV parser, storage (unit-tested)
  RigMetrics.App/    WPF dashboard, PresentMon + hardware services
tests/
  RigMetrics.Core.Tests/  xUnit tests for the engine
```

---

## Ideas for later

- Auto-detect when Rust launches and start/stop on its own
- Per-second timeline charts in the saved report (not just live)
- Tag sessions with the actual Rust config file so changes are captured automatically
- Thermal-throttle warnings
- Overlay via a second-monitor borderless window (still injection-free)
```
