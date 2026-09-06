# Interval Timer

A configurable **"beep every X seconds"** timer for gaming, shipped two ways that share one
timing engine:

- **`IntervalTimerOverlay`** — a standalone always‑on‑top desktop window + system‑tray icon
  (modern WinUI 3, runs as a plain `.exe`). Does everything: set an interval, pick a sound,
  it floats over your game and beeps. No pinning, no Game Bar.
- **`IntervalTimerWidget`** — a real Xbox Game Bar widget (classic UWP). Lives in the `Win+G`
  overlay; **pin it** to keep it beeping once the overlay is dismissed.

Both are driven by **`IntervalTimer.Core`**, a small drift‑free scheduler.

| Project | Type | Solution | Build with | Docs |
| --- | --- | --- | --- | --- |
| `IntervalTimer.Core` | `netstandard2.0` class lib | both | `dotnet` | [docs/IntervalTimer.Core.md](docs/IntervalTimer.Core.md) |
| `IntervalTimerOverlay` | WinUI 3, **.NET 10**, unpackaged | `IntervalTimerWinUI.sln` | `run-overlay.ps1` / VS 2026 / `dotnet` | [docs/IntervalTimerOverlay.md](docs/IntervalTimerOverlay.md) |
| `IntervalTimer.Core.Tests` | xUnit, **net10.0** | `IntervalTimerWinUI.sln` | `dotnet test` | — |
| `IntervalTimerWidget` | Classic UWP (.NET Native) | `IntervalTimerWidget.sln` | **VS 2022** / `build-widget.ps1` | [docs/IntervalTimerWidget.md](docs/IntervalTimerWidget.md) |

**System overview:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) ·
**Everything at once:** `.\build.ps1`

Package versions are centralized in `Directory.Packages.props`; the SDK is pinned by
`global.json` (10.0.4xx). The non‑SDK widget opts out of both via its own nested
`Directory.*.props`.

---

## 1. Prerequisites

### For the overlay + core (`IntervalTimerWinUI.sln`)

- **.NET SDK 10.0.4xx** (`dotnet --version`; pinned by `global.json`).
- **Windows App SDK 2.4 runtime** on the machine — Visual Studio installs it; a standalone
  installer is [here](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
  (Not needed if you use `run-overlay.ps1 -Portable` or `install-overlay.ps1`, which bundle it.)
- Optional: **Visual Studio 2026** with the *WinUI application development* workload for
  F5 debugging and the XAML designer.

### Extra prerequisites for the Game Bar widget

> **The widget currently does not run on this machine.** It compiles (VS 2022 + UWP
> workload), but the classic‑UWP .NET runtime — frozen at .NET Core 2.2 — can't load the app
> on Windows Insider build 26200 (`BadImageFormatException` on `System.Private.CoreLib` during
> CLR bootstrap, before Game Bar can host it). See
> [docs/IntervalTimerWidget.md](docs/IntervalTimerWidget.md) for the full crash‑dump
> analysis. **Use the standalone overlay** — it does everything the widget does and actually
> runs. The widget build steps below are kept for a stable‑Windows machine.

- **Visual Studio 2022** with the **Universal Windows Platform development** workload
  (`Microsoft.VisualStudio.Workload.Universal`). This brings the .NET Native compiler, the
  classic UWP targeting pack, and the UWP MSBuild targets. VS 2026 will *not* work (it
  compiles but the output can't be loaded by the UWP runtime).

  ```powershell
  # elevated shell — add the workload to an existing VS 2022 install
  & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe" modify `
    --installPath "$env:ProgramFiles\Microsoft Visual Studio\2022\Community" `
    --add Microsoft.VisualStudio.Workload.Universal --includeRecommended --norestart --passive
  ```

  Opening `IntervalTimerWidget.csproj` in VS 2022 before the workload is installed shows
  **"Unsupported — the project type may not be installed"**. That's the missing workload.
  `build-widget.ps1` builds it via VS 2022's MSBuild (it refuses VS 2026 automatically).

  ```powershell
  # elevated shell — one-time
  & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\setup.exe" modify `
    --installPath "C:\Program Files\Microsoft Visual Studio\18\Community" `
    --add Microsoft.VisualStudio.Workload.Universal --includeRecommended --norestart --passive
  ```

- **Windows 11 SDK 10.0.26100** — must match `<TargetPlatformVersion>` in
  `IntervalTimerWidget.csproj`.

  ```powershell
  winget install Microsoft.WindowsSDK.10.0.26100
  ```

  Do **not** target a newer SDK (e.g. 28000): the .NET 10 SDK only accepts platform versions
  up to 26100 for UWP, and classic UWP needs that exact version installed on disk.

- **Developer Mode** on (`Settings → System → For developers`) so an unsigned dev package can
  be side‑loaded.
- **Xbox Game Bar** (the Store app, `Microsoft.XboxGamingOverlay`) for testing.

---

## 2. Build & test

### Everything

```powershell
.\build.ps1                       # solution + tests + widget
.\build.ps1 -Configuration Release -SkipWidget
```

### Overlay + core + tests

```powershell
dotnet build IntervalTimerWinUI.sln -c Debug
dotnet test  IntervalTimer.Core.Tests
```

Or open `IntervalTimerWinUI.sln` in Visual Studio 2026 and build/F5 `IntervalTimerOverlay`.

### The Game Bar widget

Open **`IntervalTimerWidget.sln`** in **Visual Studio 2022** (with the *Universal Windows
Platform development* workload), or drive it from the command line:

```powershell
.\build-widget.ps1                                  # Restore + Build, Debug|x64
.\build-widget.ps1 -Rebuild                          # clean first
.\build-widget.ps1 -Configuration Release -Platform ARM64
```

Output: `IntervalTimerWidget\bin\x64\Debug\IntervalTimerWidget.exe`, a loose AppX layout at
`IntervalTimerWidget\bin\x64\Debug\AppxManifest.xml`, and an `.msix` under
`IntervalTimerWidget\AppPackages\`.

---

## 3. Run

### Overlay

```powershell
.\run-overlay.ps1                     # build Debug + launch
.\run-overlay.ps1 -Configuration Release
.\run-overlay.ps1 -Portable           # self-contained folder (no runtimes needed anywhere)
.\run-overlay.ps1 -NoLaunch           # build only
```

Or double‑click the built exe, or F5 in Visual Studio.

Once running:

| Control | What it does |
| --- | --- |
| Drag the **"INTERVAL TIMER"** strip | move the window (position is remembered) |
| **⚙** gear button | show/hide the settings panel (interval / sound / volume / auto‑start) |
| **–** button | hide to the tray — the timer keeps running |
| **✕** button | quit |
| **Start / Stop** | start or stop the countdown |
| **Test** (settings panel) | play the selected sound once |
| Tray icon (right‑click) | Start/Stop, Show/Hide window, Exit |
| `Alt+F4` | hides to tray (does not quit — use ✕ or tray → Exit) |

Only one instance runs at a time: launching again just brings the existing window to the front.
Settings are stored in `%LOCALAPPDATA%\IntervalTimerOverlay\settings.json`.

### Game Bar widget

```powershell
.\build-widget.ps1 -Deploy
```

`-Deploy` installs the .NET Core UWP framework dependency packages, registers the loose build
output (`Add-AppxPackage -Register` — no signing needed, just Developer Mode), and restarts
Game Bar so it re‑scans its widget list.

Then:

1. Press `Win+G`.
2. Click the **widget menu** button in the Game Bar toolbar.
3. Pick **Interval Timer** (click the ⭐ to keep it on the bar).
4. Set interval / sound / volume, press **Start**.
5. **Pin** the widget (pin icon in its title bar) so it keeps beeping after you dismiss Game Bar.

> **Do not double‑click the `.msix`.** It's signed with a throwaway test certificate, so a
> plain install fails with `0x800B010A`. Always deploy via `build-widget.ps1 -Deploy`.

> If the widget never appears in the menu, Game Bar didn't re‑scan. Fully quit it and reopen:
> ```powershell
> taskkill /f /im GameBar.exe
> ```

---

## 4. Install the overlay as a Start‑menu app

```powershell
.\install-overlay.ps1              # install for the current user (no admin)
.\install-overlay.ps1 -Startup     # + run automatically at sign-in
.\install-overlay.ps1 -Uninstall   # remove it   (add -KeepSettings to keep settings.json)
```

It publishes a self‑contained Release build, copies it to
`%LOCALAPPDATA%\Programs\IntervalTimerOverlay`, and adds a Start‑menu shortcut ("Interval
Timer") you can pin to the taskbar. Nothing else is needed on the machine — no .NET runtime,
no Windows App SDK, no MSIX, no certificate. Settings survive reinstalls.

If the Start menu shows a stale/generic icon after installing, refresh the icon cache:

```powershell
ie4uinit.exe -show
```

---

## 5. Iterate

- **Overlay:** edit code → `.\run-overlay.ps1` (or F5). Fast.
- **Widget:** edit code → `.\build-widget.ps1 -Deploy` → reopen the widget in Game Bar.
  No XAML designer or F5 debugging (the project won't load in the IDE). Attach the VS
  debugger to the `IntervalTimerWidget.exe` process if you need to step through it.
- **Engine changes:** `dotnet test IntervalTimer.Core.Tests` before deploying either app.

---

## 6. Regenerate assets

Both the sounds and the icons are generated by Python scripts (pure Pillow, no source art):

```powershell
python tools/gen_sounds.py     # 5 beep tones  -> <app>/Assets/Sounds/*.wav
python tools/gen_icons.py      # stopwatch icon -> <app>/Assets/*.png  + Overlay/Assets/app.ico
```

Tweak the tone list in `gen_sounds.py` / the colours and shape at the top of `gen_icons.py`,
rerun, then rebuild. If you add or rename a sound, also update `SoundCatalog.All` in
`IntervalTimer.Core/SoundCatalog.cs` and the `<Content Include="Assets\Sounds\...">` list in
`IntervalTimerWidget.csproj`.

---

## 7. Distribution

| Target | How |
| --- | --- |
| This machine, Start menu | `install-overlay.ps1` |
| Another machine, no installs | `run-overlay.ps1 -Portable` → zip the `…\win-x64\publish\` folder |
| MSIX (overlay) | `dotnet publish IntervalTimerOverlay -p:WindowsPackageType=MSIX` — you'll have to trust its signing cert |
| MSIX (widget) / Store | build Release with `build-widget.ps1 -Configuration Release`, sign the `.msix` under `AppPackages\`, submit via Partner Center |

---

## 8. Known limits

- **Exclusive‑fullscreen** games (not borderless‑windowed) hide *every* overlay — the custom
  overlay and Game Bar alike. Use borderless windowed mode.
- An **unpinned** Game Bar widget is suspended by Windows when the overlay closes, so the
  timer stops until you reopen it. Pin it, or use the standalone overlay. There is no
  supported "run in the background while unpinned" API. The widget restores its countdown
  from disk on resume so it doesn't lose its place.
- The widget's Release build uses the **.NET Native** toolchain (slow to compile; the
  "deprecated" build message is expected — it still gets security fixes). This is the only
  configuration the Game Bar SDK supports.

---

## 9. Repository layout

```
IntervalTimerWinUI.sln             Core + Overlay + Tests   (dotnet / VS 2026)
IntervalTimerWidget.sln            Core + Widget            (VS 2022 only)
global.json                        pins the .NET SDK
Directory.Build.props              shared props for the SDK-style projects
Directory.Packages.props           central NuGet versions
build.ps1                          build the solution + tests + widget
build-widget.ps1                   build/deploy just the Game Bar widget
run-overlay.ps1                    build + run the overlay exe
install-overlay.ps1                install the overlay as a Start-menu app
tools/gen_sounds.py                generate Assets/Sounds/*.wav
tools/gen_icons.py                 generate the icon set + app.ico
docs/                              architecture documentation
IntervalTimer.Core/                shared engine, settings, sound catalog
IntervalTimer.Core.Tests/          xUnit tests
IntervalTimerOverlay/              WinUI 3 standalone app  (Program.cs = custom Main)
IntervalTimerWidget/               classic UWP Game Bar widget (nested Directory.*.props opt-out)
```
