# IntervalTimerWidget — architecture

A real Xbox Game Bar widget: a UWP XAML page rendered inside the `Win+G` overlay. Same timer,
same sounds as the standalone overlay, plus the Game Bar plumbing.

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
| `AppDataSettingsStore.cs` | `ISettingsStore` over `ApplicationData.LocalSettings` |
| `UwpSoundPlayer.cs` | `ISoundPlayer` via `Windows.Media.Playback.MediaPlayer` |
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
- `OnLaunched` (a plain launch — rare, since `AppListEntry="none"`) just shows the same page
  with no widget object.
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
enumerates. `AppListEntry="none"` on `VisualElements` keeps it out of the Start menu.

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
Grid (transparent)
├─ Countdown row     CountdownText (Consolas 40)   Start/Stop button
├─ Interval          [ − ]  TextBox (InputScope Number)  [ + ]     (RepeatButtons + commit on LostFocus/Enter)
├─ Sound             ComboBox (bound to SoundCatalog.All)   Test button
└─ Volume            Slider 0–100
```

The `ComboBox` has **no `DisplayMemberPath`** — it relies on `SoundInfo.ToString()`, because
`DisplayMemberPath` binding uses reflection that .NET Native / a future AOT pass can trim.

## `AppDataSettingsStore`

Thin wrapper over `Windows.Storage.ApplicationData.Current.LocalSettings.Values` — a WinRT
property set that natively stores `int` / `double` / `bool` / `string`, exactly what
`ISettingsStore` needs. No file handling, no identity problem (the widget is always packaged).

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
3. `Add-AppxPackage -Register bin\x64\Debug\AppxManifest.xml` — registers the loose build,
   **no signing needed** (just Developer Mode). This is why you must not double-click the
   `.msix` (test-signed → `0x800B010A`).
4. Kill `GameBar.exe` / `XboxGameBarWidgets` / `GameBarFTServer` so Game Bar re-enumerates
   widgets on next `Win+G`.

Verify registration independent of Game Bar's UI:

```powershell
[Windows.ApplicationModel.AppExtensions.AppExtensionCatalog]::Open('microsoft.gameBarUIExtension')
# should list Id=IntervalTimerWidget, DisplayName='Interval Timer'
```
