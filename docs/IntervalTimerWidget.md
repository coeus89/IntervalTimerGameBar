# IntervalTimerWidget — architecture

A real Xbox Game Bar widget: a UWP XAML page rendered inside the `Win+G` overlay. Same timer,
same sounds as the standalone overlay, plus the Game Bar plumbing.

> ## ⚠️ Does not run on this machine (Windows 11 build 26200)
>
> The project builds — with VS 2022's MSBuild + the UWP workload, in Debug *or* Release — but
> the resulting app **cannot be loaded** by the classic‑UWP .NET runtime on this box.
> Launching it (standalone or via Game Bar) fails during CLR bootstrap, before any managed
> code runs:
>
> - **Debug (CoreCLR):** `System.BadImageFormatException: Could not load 'System.Private.CoreLib,
>   Version=4.0.0.0' … An attempt was made to load a program with an incorrect format`
> - **Release (.NET Native):** `System.IO.FileNotFoundException: Could not load
>   'System.Private.CoreLib, … b03f5f7f11d50a3a'`
>
> Verified via crash‑dump analysis: `Microsoft.NET.CoreRuntime.2.2` (2.2.31331.1) is
> installed, correct arch (x64), file intact (hash matches the package), valid PE — and the
> CoreCLR still rejects its own `System.Private.CoreLib`. The classic UWP .NET runtime
> (frozen at .NET Core 2.2, ~2020) is not compatible with this Windows Insider/Canary build
> (`10.0.26200`). Nothing in the project fixes this — it needs a stable Windows build with a
> working UWP framework runtime, or the widget model is simply a dead end here.
>
> **Use the standalone overlay** (`install-overlay.ps1` → "Interval Timer" in the Start
> menu). It does everything the widget does — beep every X seconds, always on top over a
> game — and it's modern, so it actually runs. The widget project is kept for reference / a
> future machine.

- **Type:** classic UWP, **.NET Native** toolchain, non-SDK `.csproj`.
- **TFM:** `UAP 10.0`, `TargetPlatformVersion 10.0.26100.0`, min `10.0.17763.0`.
- **Not in the solution** — VS 2026 can't load legacy UWP projects
  (`Unexpected null value of type 'IVsHierarchy'`). Built by `build-widget.ps1` (MSBuild, not
  `dotnet`).

## Why classic UWP (and not modern .NET UWP)

`Microsoft.Gaming.XboxGameBar` ships `lib/uap10.0/Microsoft.Gaming.XboxGameBar.winmd`. A
modern `UseUwp` project consumes WinRT through CsWinRT, which **refuses to project a
third-party `.winmd`** — `error NETSDK1130: Referencing a Windows Metadata component directly
when targeting .NET 5 or higher is not supported`. Classic UWP references the `.winmd`
natively. It's the only configuration the Game Bar SDK works in. The "`.NET Native` is
deprecated" build message is expected — it still receives security/reliability fixes.

## Files

| File | Role |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | Game Bar protocol activation, `XboxGameBarWidget` lifetime |
| `WidgetPage.xaml` / `.cs` | the widget UI + timer wiring |
| `AppDataSettingsStore.cs` | `ISettingsStore` over `LocalFolder\settings.json` |
| `UwpSoundPlayer.cs` | `ISoundPlayer` via `Windows.Media.Playback.MediaPlayer`, plus `PlayAndWaitAsync` |
| `CustomSoundStore.cs` | imports/lists/deletes user sound files under `LocalFolder\Sounds` |
| `ISpeechProvider.cs` / `WindowsSpeechProvider.cs` | text-to-speech seam; offline `SpeechSynthesizer` impl |
| `VoiceOption.cs` | one selectable TTS voice for the UI |
| `AlertPlayer.cs` | plays sound and/or speech per `TimerSettings.AnnouncementMode` |
| `Package.appxmanifest` | the widget declaration + proxy/stub registration |
| `Properties/AssemblyInfo.cs` | assembly attributes (non-SDK projects need this explicitly) |
| `Properties/Default.rd.xml` | .NET Native runtime directives (minimal) |
| `Assets/…` | tile logos + `Sounds/*.wav`, listed one-by-one in the `.csproj` |

## Activation — `App.xaml.cs`

Game Bar launches the widget as a **protocol activation**, not a normal launch:

```csharp
protected override void OnActivated(IActivatedEventArgs args)
{
    // args.Kind == Protocol, Uri.Scheme == "ms-gamebarwidget"
    var widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
    if (widgetArgs?.IsLaunchActivation == true)
    {
        var frame = new Frame();
        Window.Current.Content = frame;
        _widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, frame); // IPC bootstrap
        frame.Navigate(typeof(WidgetPage), _widget);
        Window.Current.Closed += OnWidgetWindowClosed;
        Window.Current.Activate();
    }
    // repeat activations (widget already open): ignored
}
```

- `new XboxGameBarWidget(args, CoreWindow, frame)` opens the private IPC channel to Game Bar
  and **must be held for the process lifetime** (`_widget` field).
- `OnLaunched` handles a **plain launch from Start** — the same `WidgetPage` in an ordinary
  app window, with no widget object (`e.Parameter` is null). It also calls
  `SetPreferredMinSize(260×88)`, because UWP's default 500×320 minimum would stop the page
  shrinking to the compact layout, and sets `PreferredLaunchViewSize` so the window opens
  small instead of at UWP's default size.
- `OnSuspending` nulls `_widget`. When the last widget window closes Game Bar suspends the
  process; there's no `Window.Closed` for the final window, only `OnSuspending`.

## `Package.appxmanifest`

Two things beyond a normal UWP manifest:

### 1. The widget extension (inside `<Application><Extensions>`)

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension Name="microsoft.gameBarUIExtension" Id="IntervalTimerWidget"
                     DisplayName="Interval Timer" PublicFolder="GameBar">
    <uap3:Properties>
      <GameBarWidget Type="Standard">
        <HomeMenuVisible>true</HomeMenuVisible>
        <PinningSupported>true</PinningSupported>
        <ActivateAfterInstall>true</ActivateAfterInstall>
        <Window>
          <AllowForegroundTransparency>true</AllowForegroundTransparency>
          <Size> 300×320, min 260×260, max 380×440 </Size>
          <ResizeSupported><Horizontal>false</Horizontal><Vertical>true</Vertical></ResizeSupported>
        </Window>
      </GameBarWidget>
    </uap3:Properties>
  </uap3:AppExtension>
</uap3:Extension>
```

`Name` **must** be exactly `microsoft.gameBarUIExtension` — that's the contract Game Bar
enumerates. Game Bar finds the widget through this extension, *not* through the app list, so
`VisualElements` deliberately carries **no `AppListEntry="none"`**: the app shows up in Start
and runs as a normal window too. Both entry points work at once.

### 2. Proxy/stub registration (a **package-level** `<Extensions>`, after `</Applications>`)

```xml
<Extension Category="windows.activatableClass.proxyStub">
  <ProxyStub ClassId="00000355-0000-0000-C000-000000000046">   <!-- Metadata-Based Marshalling -->
    <Path>Microsoft.Gaming.XboxGameBar.winmd</Path>
    <Interface Name="Microsoft.Gaming.XboxGameBar.Private.IXboxGameBar…" InterfaceId="…" />
    …
  </ProxyStub>
</Extension>
```

This tells COM how to marshal the Game Bar private interfaces across the widget↔Game Bar
apartment boundary. The interface list comes from the `Microsoft.Gaming.XboxGameBar` NuGet
package's `readme.txt` — **re-copy it if you upgrade the package.**

## `WidgetPage`

### Timer wiring

Same shape as the overlay, but marshalling uses the UWP `CoreDispatcher`:

```csharp
void RunOnUi(Action a) =>
    Dispatcher.HasThreadAccess ? a() : Dispatcher.RunAsync(Normal, () => a());

_engine.Tick    += (s,e) => RunOnUi(() => UpdateUi(e.Remaining, e.IsRunning));
_engine.Elapsed += (s,e) => RunOnUi(() => { _sound.Play(_settings.Sound, _settings.Volume); PersistRunState(); });
```

### Suspend / resume

Because Game Bar suspends the process when the widget is unpinned and the overlay closes, the
page persists enough to resume mid-countdown:

- On every `Elapsed`, `Start/Stop`, and interval change → `PersistRunState()` writes
  `IsRunning` (bool) and `NextFireUtcTicks` (long) to `LocalSettings`.
- In `OnLoaded`, if `IsRunning` was true and the saved `NextFireUtcTicks` is still in the
  future → `_engine.ResumeAt(next)`; if it's already past → `_engine.Start()` fresh;
  otherwise honour `AutoStart`.

So a pinned widget keeps beeping; an unpinned one picks up where it left off when you reopen
Game Bar (it just doesn't beep while suspended — a platform limit, not a bug).

### UI (`WidgetPage.xaml`)

Plain UWP controls only (no WinUI 2 `NumberBox`):

```
RootGrid  (Background swapped Transparent <-> #EE1B1B1F at runtime)
└─ Grid
   ├─ Header           CountdownText (Consolas 40)   Start/Stop   ⚙ SettingsToggle
   └─ SettingsPanel  (ScrollViewer, collapsed by default; ⚙ toggles it)
      ├─ Interval        [ − ]  TextBox (InputScope Number)  [ + ]
      ├─ Sound           ComboBox (built-ins + imports)   Test
      │                  Import sound file...   Remove (custom sounds only)
      ├─ Volume          Slider 0–100
      ├─ Announcement    mode ComboBox, voice ComboBox, phrase TextBox,
      │                  speaking-rate Slider, Test announcement
      ├─ [ ] Transparent background
      └─ [ ] Start timer when widget opens
```

### Keyboard focus for text entry (`IntervalBox`, `SpeechTextBox`)

Game Bar can leave real OS keyboard focus on the hosting game even while a pinned widget's
pointer-driven controls (buttons, sliders, combo boxes) keep working normally. Mouse clicks are
delivered to whatever window is under the cursor purely by hit-testing, but typed characters
(`WM_CHAR`) are only delivered to whichever window currently owns keyboard focus — and the
`XboxGameBarWidget` handshake doesn't always hand that back to the widget just because XAML
thinks a `TextBox` has focus. The symptom is exactly what it looks like from the outside: the
"+/-" interval buttons work fine, but typing digits into the interval box (or the announcement
phrase box) silently does nothing.

Fix: both text-entry boxes handle `PointerPressed` → `Window.Current.Activate()` to force Windows
to hand keyboard focus back to the widget's window on every tap/click, before the user starts
typing (`OnTextEntryPointerPressed` in `WidgetPage.xaml.cs`). They also `SelectAll()` on
`GotFocus` so a single tap + type replaces the old value. See Microsoft's
[click-through guide](https://learn.microsoft.com/en-us/xbox/game-bar/guide/click-through) for
the related (but distinct) scenario of pinned widgets losing *all* input when click-through is
enabled by the user — that case is still a real limitation (there is no API to opt out of
click-through; the mitigation there is to hide/disable controls while `ClickThroughEnabled` is
true, which this widget does not yet do).

### Custom sounds

`CustomSoundStore` imports one user-picked audio file (`.wav .mp3 .m4a .aac .wma .flac`) into
`ApplicationData.Current.LocalFolder\Sounds`. The file must be 50 MB or smaller and 5 seconds or
shorter; anything else is rejected before it is copied. That local folder holds the one custom
file only — a new import deletes the previous custom file, and `ListAsync` does the same if an
older build left more than one behind. The five packaged defaults (`Assets\Sounds`) are not in
that folder, are never deleted, and always stay at the top of the picker.
`SoundCatalog.Custom(fileName)` builds the catalog entry (key `"custom:<fileName>"`);
`SoundInfo.PlaybackUri` resolves custom sounds to `ms-appdata:///local/Sounds/<fileName>` instead of
the packaged `ms-appx:///Assets/Sounds/<fileName>` used for built-ins. `TimerSettings.Normalize()`
leaves a `custom:` key alone (unlike a built-in key, it can't be validated without knowing which
files currently exist); `SoundCatalog.Resolve(key, customSounds)` falls back to `Default` at
load/playback time if the file was deleted.

**Known risk:** the file picker's behavior inside the Game Bar overlay (vs. running standalone from
the Start menu) hasn't been verified on real hardware — pickers can behave oddly when hosted inside
another app's overlay surface. Test `Import sound file...` both ways.

### Text-to-speech announcements

`TimerSettings.AnnouncementMode` picks between `Sound` (default, unchanged behavior), `Speech`, or
`SoundThenSpeech`. `AlertPlayer` (in the widget project) orchestrates the two: it plays through the
existing `UwpSoundPlayer`/`MediaPlayer`, and — when speech is involved — synthesizes the configured
`SpeechText` template via `ISpeechProvider` and plays the result through a second, dedicated
`MediaPlayer` so sound and speech never fight over one player's `Source`. In `SoundThenSpeech` mode,
`UwpSoundPlayer.PlayAndWaitAsync` waits for the sound's `MediaEnded` (or a 3-second timeout) before
speaking.

`AnnouncementFormatter` (in `IntervalTimer.Core`, pure string logic, unit-tested) expands the
template's tokens — `{interval}` (humanized, e.g. "1 minute 30 seconds"), `{count}` (announcements
since the timer was last started), `{time}` (local clock) — before handing the literal text to the
speech provider.

`WindowsSpeechProvider` is the only implementation today: it wraps the offline, built-in
`Windows.Media.SpeechSynthesis.SpeechSynthesizer` (`SpeechSynthesizer.AllVoices`), needs no network
and no manifest capability. It only sees the classic OneCore voices (David, Zira, Mark, plus any
installed language packs) — Narrator's newer "Natural" voices aren't exposed through this public
WinRT API. A cloud provider (e.g. Azure Speech) could implement `ISpeechProvider` the same way for
higher-quality neural voices; that's out of scope for now.

- **Collapsed by default** so the settings aren't on screen during a game — only the
  countdown + Start/Stop show. The ⚙ `ToggleButton` expands/collapses `SettingsPanel`, then
  `ResizeToFitContent()` grows/shrinks the Game Bar window to match.
- **`ResizeToFitContent()`** measures `RootGrid` with an unconstrained height and asks the
  SDK to resize to that. Two things matter here:
  - It must use **`XboxGameBarWidget.TryResizeWindowAsync`**, not
    `ApplicationView.TryResizeView` — Game Bar hosts the widget in its own frame, so the
    generic UWP view-resize call is ignored.
  - It has to **measure**, not read `ActualHeight` — the window is still at the old height
    when the toggle fires, so the live layout reports the clipped size, not the wanted one.

  The result is clamped to the widget's `MinWindowSize` / `MaxWindowSize` (which Game Bar
  populates from the manifest's `<Size>` block: 88–440 high), so the content drives the size
  instead of hardcoded constants.
- **"Transparent background"** (persisted as `TimerSettings.TransparentBackground`, default
  **off**) switches `RootGrid.Background` between `Colors.Transparent` (the game shows
  through — needs `<AllowForegroundTransparency>` in the manifest, which is set) and a solid
  dark panel. It starts opaque so the widget is visible on first run; the choice persists
  once changed.
- The `ComboBox` has **no `DisplayMemberPath`** — it relies on `SoundInfo.ToString()`,
  because `DisplayMemberPath` binding uses reflection that .NET Native / a future AOT pass
  can trim.

## `AppDataSettingsStore`

Writes `settings.json` in `ApplicationData.Current.LocalFolder` (same local app-data folder as
imported sounds). Every `Set` rewrites the file, so interval, sound, volume, announcement mode,
spoken phrase, voice, speaking rate, and the running countdown all come back on the next launch.
Integers are stored as JSON integers (not doubles) so `NextFireUtcTicks` keeps full precision.

If the file is missing, existing `LocalSettings` values are copied in once. After that the file
is the source of truth.

## `UwpSoundPlayer`

Reused `MediaPlayer` (`AudioCategory = Alerts`), plays
`MediaSource.CreateFromUri("ms-appx:///Assets/Sounds/<file>")`. `ms-appx:` resolves because
the widget is a packaged app.

## `.csproj` gotchas

- **Restore and Build must be separate MSBuild invocations.** A combined `/t:Restore;Build`
  reuses pre-restore evaluation state and fails with a spurious *".NET Framework could not be
  found"*. `build-widget.ps1` does them in two calls.
- **`<SetPlatform>Platform=AnyCPU</SetPlatform>`** on the `ProjectReference` to
  `IntervalTimer.Core` — the widget builds `x64`/`ARM64`, the netstandard lib is AnyCPU.
- `<TargetPlatformVersion>` must be a Windows SDK **installed on disk** (26100 here). The .NET
  SDK caps UWP platform versions at 26100 regardless of what's installed.
- `Microsoft.NETCore.UniversalWindowsPlatform 6.2.14` provides the classic UWP BCL /
  netstandard2.0 facades.

## Deploying (`build-widget.ps1 -Deploy`)

1. Build → loose AppX layout at `bin\x64\Debug\AppxManifest.xml`.
2. `Add-AppxPackage` the `Microsoft.NET.CoreRuntime/CoreFramework/Native` dependency `.appx`
   files from `AppPackages\…\Dependencies\x64\`.
3. **`Remove-AppxPackage`** any existing `22905TiberiusCN.TiberiusIntervalTimerWidget` registration first (and the older `JKara.IntervalTimerWidget` identity) — a
   dev (loose) registration cannot be replaced in place when the manifest changed but the
   `Version` didn't (*"the package is already installed. Increment the version number…"*).
4. `Add-AppxPackage -Register bin\x64\Debug\AppxManifest.xml` — registers the loose build,
   **no signing needed** (just Developer Mode). This is why you must not double-click the
   `.msix` (test-signed → `0x800B010A`).
5. Kill `GameBar.exe` / `XboxGameBarWidgets` / `GameBarFTServer` so Game Bar re-enumerates
   widgets on next `Win+G`.

If Game Bar's widget menu doesn't list it after that, Game Bar's own widget cache is stale —
it stores per-widget state in
`%LOCALAPPDATA%\Packages\Microsoft.XboxGamingOverlay_8wekyb3d8bbwe\LocalState\profileDataSettings.txt`
(`profile.settingsStorage.widget_<PFN>_App_IntervalTimerWidget`). A full Game Bar restart
(not just `GameBar.exe`) usually forces a re-enumeration.

Verify registration independent of Game Bar's UI:

```powershell
[Windows.ApplicationModel.AppExtensions.AppExtensionCatalog]::Open('microsoft.gameBarUIExtension')
# should list Id=IntervalTimerWidget, DisplayName='Interval Timer'
```
