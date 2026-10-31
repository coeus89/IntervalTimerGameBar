# IntervalTimerOverlay — architecture

A standalone always-on-top desktop overlay + system-tray icon. Modern WinUI 3, but built
**unpackaged** so it ships as a plain `IntervalTimerOverlay.exe`.

- **TFM:** `net10.0-windows10.0.26100.0` (min OS `10.0.17763.0`), `UseWinUI`,
  `WindowsPackageType=None`.
- **In `IntervalTimerWinUI.sln`.** Build with `dotnet`, Visual Studio F5, `run-overlay.ps1`,
  or `build.ps1`.
- **Runtime need:** the Windows App SDK 2.4 runtime on the machine — *unless* published
  self-contained (`run-overlay.ps1 -Portable`, `install-overlay.ps1`), which bundle it.
- Package versions come from the repo-root `Directory.Packages.props`.

## Files

| File | Role |
| --- | --- |
| `Program.cs` | custom `Main` — single-instance redirection before the XAML runtime starts |
| `App.xaml` / `App.xaml.cs` | app object; `BringToFront()` |
| `MainWindow.xaml` / `.cs` | the whole overlay UI + behaviour |
| `JsonSettingsStore.cs` | `ISettingsStore` over a JSON file in `%LOCALAPPDATA%` (source-gen serializer) |
| `MediaSoundPlayer.cs` | `ISoundPlayer` via `Windows.Media.Playback.MediaPlayer` |
| `TransparentBackdrop.cs` | fully transparent `SystemBackdrop` for the "Transparent background" option |
| `app.manifest` | `PerMonitorV2` DPI awareness, Win10+ compat GUIDs |
| `Package.appxmanifest` | only used if you publish `-p:WindowsPackageType=MSIX` |
| `Assets/app.ico` | embedded via `<ApplicationIcon>`; also the tray icon |
| `Assets/Sounds/*.wav` | copied next to the exe (`CopyToOutputDirectory`) |

## Single instance (`Program.Main`)

The csproj sets `DISABLE_XAML_GENERATED_MAIN`, so `Program.Main` is the real entry point:

1. `WinRT.ComWrappersSupport.InitializeComWrappers()`.
2. `AppInstance.FindOrRegisterForKey("IntervalTimerOverlay")`.
   - **Not current** → another copy owns the key: `RedirectActivationToAsync(...)` then
     **`return`** (the process exits normally — nothing was initialized yet, so no `Kill()`).
   - **Current** → subscribe `keyInstance.Activated` (raised on a future launch) to
     `App.BringToFront()`, then `Application.Start(...)` with the standard
     `DispatcherQueueSynchronizationContext` boilerplate.
3. `App.OnLaunched` just creates `MainWindow` (plus a `Window is not null` re-entrancy guard).

`App.BringToFront()` is `public static` and thread-safe — it marshals to the window's
`DispatcherQueue` before `AppWindow.Show()` + `Activate()` + `MoveInZOrderAtTop()`.

This matters because the window **hides to the tray instead of exiting** — without this, every
relaunch (including `run-overlay.ps1` auto-launching over a tray instance) would stack a new
window.

## `MainWindow` — the overlay

### Window chrome (`ConfigureWindow`)

Gets the `AppWindow` via `WindowNative.GetWindowHandle` → `Win32Interop.GetWindowIdFromWindow`
→ `AppWindow.GetFromWindowId`, then on the `OverlappedPresenter`:

- `IsAlwaysOnTop = true`
- `IsResizable / IsMaximizable / IsMinimizable = false`
- `SetBorderAndTitleBar(false, false)` — frameless
- `IsShownInSwitchers = false` — no taskbar button / Alt-Tab entry (the tray icon is the
  handle instead)

Size is content-driven, like the widget's `ResizeToFitContent()`: `ContentGrid` is measured
at the 320-DIP design width with an unconstrained height, clamped to 88–440 DIPs, then scaled
by `XamlRoot.RasterizationScale` (`AppWindow.Resize` takes physical pixels). It runs on
`Root.Loaded`, whenever ⚙ toggles the settings panel, and on `XamlRoot.Changed` (dragging to a
monitor with a different scale). Position is restored from the `WindowX` / `WindowY` settings
keys, but only if the saved rect still overlaps a connected display.

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
_engine.Elapsed += (_, _) => _ui.TryEnqueue(() => { _sound.Play(_settings.Sound, _settings.Volume); PersistRunState(); });
```

`_ui` is `this.DispatcherQueue`. `UpdateUi` sets the countdown text (`mm:ss`, or `h:mm:ss`
past an hour), the Start/Stop button label, and the tray menu's toggle label.

### Resuming a running timer

Same scheme as the widget's suspend/resume, using the same keys:

- On every `Elapsed`, Start/Stop (button or tray), interval change, and in `ExitApp()`
  (*before* the engine is stopped) → `PersistRunState()` writes `IsRunning` (bool) and
  `NextFireUtcTicks` (long).
- On launch (`RestoreRunState()`), if `IsRunning` was true and `NextFireUtcTicks` is still in
  the future → `_engine.ResumeAt(next)`; if it's already past → `_engine.Start()` fresh;
  otherwise honour `AutoStart`.

So quitting mid-countdown (✕ or tray **Exit**) and relaunching picks up where it left off.

### Transparent background (`TransparentBackdrop`)

"Transparent background" (`TimerSettings.TransparentBackground`, default **off**) swaps
`Root.Background` between the `#EE1B1B1F` panel and `Transparent`, and sets
`Window.SystemBackdrop` to a `TransparentBackdrop` (or back to `null`).

Windows App SDK has Mica/Acrylic backdrops but no fully transparent one, so
`TransparentBackdrop` is a custom `SystemBackdrop` (same technique as WinUIEx's
`TransparentTintBackdrop`). A transparent composition brush alone still renders **black** —
the HWND's redirection surface is opaque — so on connect it also:

1. `DwmEnableBlurBehindWindow` with an off-screen 1×1 region — turns on per-pixel alpha in DWM
   without actually blurring anything.
2. Subclasses the window (`SetWindowSubclass`) and answers `WM_ERASEBKGND` by filling the
   client rect with a black GDI brush (alpha 0 = clear); re-applies the DWM config on
   `WM_DWMCOMPOSITIONCHANGED`.

Disconnect undoes all of it (removes the subclass, disables blur-behind, frees the GDI brush).

### Settings flow

`LoadSettings()` runs in the ctor with a `_loading` guard so populating the controls doesn't
trigger their change handlers. Each control handler (`OnIntervalChanged`, `OnSoundChanged`,
`OnVolumeChanged`, `OnTransparentChanged`, `OnAutoStartChanged`) updates `_settings` + the engine and calls
`SaveSettings()` → `TimerSettings.Save(_store)`. There's no explicit "save" — every change is
written immediately.

### UI map (`MainWindow.xaml`)

```
Root Grid  (Background swapped Transparent <-> #EE1B1B1F at runtime)
├─ TaskbarIcon (H.NotifyIcon)   context menu: Start/Stop · Show/hide · Exit
└─ ContentGrid (measured by ResizeToFitContent)
   ├─ Header  (drag strip)      "INTERVAL TIMER"  ⚙(toggle)  –(hide)  ✕(quit)
   ├─ Countdown row             CountdownText (Consolas 34)   Start/Stop button
   └─ SettingsPanel (collapsed) NumberBox interval · ComboBox sound · Slider volume + Test
                                · Transparent background check · AutoStart check
```

The sound `ComboBox` has no `DisplayMemberPath` — like the widget it relies on
`SoundInfo.ToString()`, which keeps it working under trimming.

Button glyphs are Segoe MDL2 Assets: `E713` gear, `E921` hide, `E8BB` close.

## `JsonSettingsStore`

`ISettingsStore` over `%LOCALAPPDATA%\IntervalTimerOverlay\settings.json`. Used instead of
`ApplicationData.LocalSettings` because the app is **unpackaged** — `ApplicationData.Current`
throws without package identity.

- Holds a `Dictionary<string, JsonElement>`; loads on construction (corrupt file → start empty).
- `TryGet` maps `JsonValueKind` → `long` (integral numbers, so `NextFireUtcTicks` keeps full
  precision) / `double` / `bool` / `string`; `TimerSettings` and the position restore use
  `Convert.*` with `InvariantCulture`.
- `Set` writes the whole dictionary to disk on every call (try/catch — a locked file keeps the
  in-memory value).
- Serialization goes through **`SettingsJsonContext`**, a `[JsonSerializable]`
  source-generated `JsonSerializerContext` (for `Dictionary<string, JsonElement>` +
  `int`/`long`/`double`/`bool`/`string`). No runtime reflection → the app is trim/AOT-safe, so
  `PublishTrimmed=true` can be re-enabled for a smaller `-Portable` build (validate per build).

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
  exe instead of an MSIX. To build an MSIX use `.\package-overlay.ps1` (repo root) — it runs
  `dotnet build -p:Platform=<arch> -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true`
  per architecture. `dotnet publish -p:WindowsPackageType=MSIX` alone does **not** produce a
  package — `dotnet publish` ignores `GenerateAppxPackageOnBuild` and just emits a loose exe.
- `<DefineConstants>...;DISABLE_XAML_GENERATED_MAIN</DefineConstants>` — hands the entry point
  to `Program.cs`.
- `<PublishTrimmed>false</PublishTrimmed>` — off by default; safe to opt in per build now that
  JSON is source-generated. `<PublishReadyToRun>false</PublishReadyToRun>` — R2R triggers a
  CsWinRT `ComInterfaceEntry` `TypeLoadException` in self-contained publishes.
- `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` — embeds the multi-res icon so the exe,
  Start-menu shortcut, and taskbar all show the stopwatch.

Output paths differ by command:

| Command | exe path |
| --- | --- |
| `dotnet build` / `run-overlay.ps1` | `bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\` |
| `dotnet publish` / `run-overlay.ps1 -Portable` | `bin\Debug\net10.0-…\win-x64\publish\` |

`run-overlay.ps1` and `install-overlay.ps1` glob for `IntervalTimerOverlay.exe` rather than
hard-coding these.

## Scripts

- **`run-overlay.ps1`** — `dotnet build` (or `publish` with `-Portable`), then launch. Finds
  the exe by globbing.
- **`install-overlay.ps1`** — `dotnet publish` self-contained Release →
  `%LOCALAPPDATA%\Programs\IntervalTimerOverlay` → Start-menu `.lnk` (via `WScript.Shell`).
  `-Startup` adds a Startup-folder shortcut; `-Uninstall` (+ `-KeepSettings`) reverses it.
