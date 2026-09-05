# IntervalTimerOverlay — architecture

A standalone always-on-top desktop overlay + system-tray icon. Modern WinUI 3, but built
**unpackaged** so it ships as a plain `IntervalTimerOverlay.exe`.

- **TFM:** `net8.0-windows10.0.19041.0`, `UseWinUI`, `WindowsPackageType=None`.
- **In the solution.** Build with `dotnet`, Visual Studio F5, or `run-overlay.ps1`.
- **Runtime need:** the Windows App SDK 1.6 runtime on the machine — *unless* published
  self-contained (`run-overlay.ps1 -Portable`, `install-overlay.ps1`), which bundle it.

## Files

| File | Role |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | app entry, single-instance guard |
| `MainWindow.xaml` / `.cs` | the whole overlay UI + behaviour |
| `JsonSettingsStore.cs` | `ISettingsStore` over a JSON file in `%LOCALAPPDATA%` |
| `MediaSoundPlayer.cs` | `ISoundPlayer` via `Windows.Media.Playback.MediaPlayer` |
| `app.manifest` | `PerMonitorV2` DPI awareness, Win10+ compat GUIDs |
| `Package.appxmanifest` | only used if you publish `-p:WindowsPackageType=MSIX` |
| `Assets/app.ico` | embedded via `<ApplicationIcon>`; also the tray icon |
| `Assets/Sounds/*.wav` | copied next to the exe (`CopyToOutputDirectory`) |

## `App` — single instance

`OnLaunched`:

1. If `Window` is already set (OnLaunched fired twice in one process) → bring it to front, return.
2. `AppInstance.FindOrRegisterForKey("IntervalTimerOverlay")`. If **not** current, another copy
   owns the key → `RedirectActivationToAsync(GetActivatedEventArgs())`, then
   `Process.GetCurrentProcess().Kill()` (a graceful exit can hang mid-XAML-init).
3. Otherwise subscribe `keyInstance.Activated` → marshal to the UI thread and
   `AppWindow.Show()` + `Activate()` + `MoveInZOrderAtTop()`.
4. Create `MainWindow`, activate.

This matters because the window **hides to the tray instead of exiting** — without the guard,
every relaunch (including `run-overlay.ps1` auto-launching over a tray instance) would stack a
new window.

## `MainWindow` — the overlay

### Window chrome (`ConfigureWindow`)

Gets the `AppWindow` via `WindowNative.GetWindowHandle` → `Win32Interop.GetWindowIdFromWindow`
→ `AppWindow.GetFromWindowId`, then on the `OverlappedPresenter`:

- `IsAlwaysOnTop = true`
- `IsResizable / IsMaximizable / IsMinimizable = false`
- `SetBorderAndTitleBar(false, false)` — frameless
- `IsShownInSwitchers = false` — no taskbar button / Alt-Tab entry (the tray icon is the
  handle instead)

Size: 320×148 collapsed, 320×340 with the settings panel open. Position is restored from the
`WindowX` / `WindowY` settings keys (wrapped in try/catch — a removed monitor is ignored).

`AppWindow.Closing` is cancelled and turned into `Hide()` — the process only ends via the ✕
button or the tray **Exit** item (both call `ExitApp()`, which stops+disposes the engine,
disposes the sound player and tray icon, then `Application.Current.Exit()`).

### Dragging (`WireDrag`)

The window has no title bar, so dragging is manual — and it must use **screen coordinates**,
not element-relative ones:

```
PointerPressed (left button):  GetCursorPos(out _dragCursorOrigin);  _dragWindowOrigin = AppWindow.Position
PointerMoved   (while dragging): GetCursorPos(out cur)
                                 AppWindow.Move(_dragWindowOrigin + (cur - _dragCursorOrigin))
PointerReleased / CaptureLost:  persist WindowX/WindowY
```

`GetCursorPos` is a `user32.dll` P/Invoke returning a `POINT` in physical screen pixels.
An earlier version measured `e.GetCurrentPoint(Header).Position` (relative to the header
element) — but the header moves with the window, so each `Move` changed the reference frame
the next event was measured against, and the window oscillated across the screen. Screen
coordinates are independent of the window position, so the delta is stable.

### Wiring the engine (`WireEngine`)

```csharp
_engine.Tick    += (_, e) => _ui.TryEnqueue(() => UpdateUi(e.Remaining, e.IsRunning));
_engine.Elapsed += (_, _) => _ui.TryEnqueue(() => _sound.Play(_settings.Sound, _settings.Volume));
```

`_ui` is `this.DispatcherQueue`. `UpdateUi` sets the countdown text (`mm:ss`, or `h:mm:ss`
past an hour), the Start/Stop button label, and the tray menu's toggle label.

### Settings flow

`LoadSettings()` runs in the ctor with a `_loading` guard so populating the controls doesn't
trigger their change handlers. Each control handler (`OnIntervalChanged`, `OnSoundChanged`,
`OnVolumeChanged`, `OnAutoStartChanged`) updates `_settings` + the engine and calls
`SaveSettings()` → `TimerSettings.Save(_store)`. There's no explicit "save" — every change is
written immediately.

### UI map (`MainWindow.xaml`)

```
Root Grid  (#EE1B1B1F — near-opaque dark)
├─ TaskbarIcon (H.NotifyIcon)   context menu: Start/Stop · Show/hide · Exit
└─ Grid
   ├─ Header  (drag strip)      "INTERVAL TIMER"  ⚙(toggle)  –(hide)  ✕(quit)
   ├─ Countdown row             CountdownText (Consolas 34)   Start/Stop button
   └─ SettingsPanel (collapsed) NumberBox interval · ComboBox sound · Slider volume + Test · AutoStart check
```

Button glyphs are Segoe MDL2 Assets: `E713` gear, `E921` hide, `E8BB` close.

## `JsonSettingsStore`

`ISettingsStore` over `%LOCALAPPDATA%\IntervalTimerOverlay\settings.json`. Used instead of
`ApplicationData.LocalSettings` because the app is **unpackaged** — `ApplicationData.Current`
throws without package identity.

- Holds a `Dictionary<string, JsonElement>`; loads on construction (corrupt file → start empty).
- `TryGet` maps `JsonValueKind` → `double` / `bool` / `string` (numbers always come back as
  `double`; `TimerSettings` and the position restore use `Convert.*` so that's fine).
- `Set` writes the whole dictionary to disk on every call (try/catch — a locked file keeps the
  in-memory value).

`System.Text.Json` here is reflection-based, which is why the project disables trimming
(see below).

## `MediaSoundPlayer`

A reused `MediaPlayer` with `AudioCategory = Alerts`. `Play`:

```csharp
var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound.FileName);
_player.Volume = clamp(volume, 0, 1);
_player.Source = MediaSource.CreateFromUri(new Uri(path));   // file:// URI — ms-appx: needs a package
_player.Position = TimeSpan.Zero;
_player.Play();
```

Wrapped in try/catch so a missing audio device can't take down the timer.

## Build / publish notes

`IntervalTimerOverlay.csproj`:

- `<WindowsPackageType>None</WindowsPackageType>` — the switch that makes it build a runnable
  exe instead of an MSIX. Overridable: `dotnet publish -p:WindowsPackageType=MSIX`.
- `<PublishTrimmed>false</PublishTrimmed>` — trimming strips types that reflection-based
  `System.Text.Json` needs at runtime.
- `<PublishReadyToRun>false</PublishReadyToRun>` — R2R precompilation triggers
  `System.TypeLoadException: Could not load type 'ComInterfaceEntry'` from CsWinRT in
  self-contained publishes. (Framework-dependent R2R is fine, but off everywhere for safety.)
- `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` — embeds the multi-res icon so the exe,
  Start-menu shortcut, and taskbar all show the stopwatch.

Output paths differ by command:

| Command | exe path |
| --- | --- |
| `dotnet build` / `run-overlay.ps1` | `bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\` |
| `dotnet publish` / `run-overlay.ps1 -Portable` | `bin\Debug\net8.0-…\win-x64\publish\` |

`run-overlay.ps1` and `install-overlay.ps1` glob for `IntervalTimerOverlay.exe` rather than
hard-coding these.

## Scripts

- **`run-overlay.ps1`** — `dotnet build` (or `publish` with `-Portable`), then launch. Finds
  the exe by globbing.
- **`install-overlay.ps1`** — `dotnet publish` self-contained Release →
  `%LOCALAPPDATA%\Programs\IntervalTimerOverlay` → Start-menu `.lnk` (via `WScript.Shell`).
  `-Startup` adds a Startup-folder shortcut; `-Uninstall` (+ `-KeepSettings`) reverses it.
