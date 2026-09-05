# IntervalTimer.Core — architecture

The shared library. No UI, no platform dependencies — just the timing engine, the settings
model, and the sound catalog. Referenced by both front ends and by the test project.

- **TFM:** `netstandard2.0`, `LangVersion` 7.3, nullable disabled.
- **Why `netstandard2.0`:** it's the only target that classic-UWP / .NET Native (C# 7.3) and
  modern .NET 8+ can both reference. Keep the code to C# 7.3 features — no records, no
  file-scoped namespaces, no `init` accessors.
- **Fixed `<ProjectGuid>`** `{7A1D9E22-…}` so the non-SDK widget `.csproj` can reference it.

## Files

| File | Contents |
| --- | --- |
| `IntervalTimerEngine.cs` | the scheduler |
| `IntervalTimerEvents.cs` | `IntervalTickEventArgs`, `IntervalElapsedEventArgs` |
| `TimerSettings.cs` | `ISettingsStore` interface + `TimerSettings` POCO with load/save |
| `SoundCatalog.cs` | `SoundInfo` + the static `SoundCatalog` list |
| `ISoundPlayer.cs` | one-method interface, implemented per app |

## `IntervalTimerEngine`

```csharp
public sealed class IntervalTimerEngine : IDisposable
{
    static readonly TimeSpan MinInterval = 1s;
    static readonly TimeSpan MaxInterval = 24h;

    TimeSpan Interval           { get; set; }   // clamped to [Min,Max]; also IntervalSeconds (int)
    bool     IsRunning          { get; }
    TimeSpan Remaining          { get; }        // full interval while stopped
    DateTime NextFireUtc        { get; }        // persist this across suspend

    event EventHandler<IntervalTickEventArgs>    Tick;      // ~4x/sec + on every state change
    event EventHandler<IntervalElapsedEventArgs> Elapsed;   // once per interval boundary

    void Start();  void Stop();  void Toggle();
    void Reset();                       // restart the current countdown
    void ResumeAt(DateTime nextFireUtc); // restore a schedule after process suspension
    void Dispose();
}
```

### How it keeps time

A `System.Timers.Timer` polls every **250 ms**. On each poll (`OnPoll`):

```
now = UtcNow
lock:
    if not running: return
    while now >= _nextFireUtc:
        missed++
        _nextFireUtc += _interval
        if missed > 1:                       # long gap: sleep / suspend / debugger pause
            _nextFireUtc = now + _interval   # realign to the wall clock, stop the loop
            break
if missed > 0:  raise Elapsed(now, resynced: missed > 1)
raise Tick
```

Consequences:

- **No drift.** The fire schedule is anchored to a wall-clock instant, not to an accumulating
  count of `-1s` operations.
- **Sleep / suspend safe.** If the machine (or a suspended widget) skips 40 intervals, you get
  **one** `Elapsed` with `Resynced = true`, then a clean interval from now — never 40 beeps.
- **250 ms granularity** on the fire time and the countdown redraw. `MinInterval` is 1 s so
  this is always tight enough.

### Threading

`Tick` and `Elapsed` are raised on a **thread-pool thread** (the `Timer.Elapsed` callback).
All mutable state (`_interval`, `_nextFireUtc`, `_running`) is guarded by a single `_gate`
lock. Consumers **must** marshal event handling to their own UI thread. The engine snapshots a
handler into a local before invoking it (`var h = Elapsed; if (h != null) h(...)`) to avoid a
race with unsubscription.

### Setters while running

Setting `Interval` while running rebases `_nextFireUtc = UtcNow + newInterval` — changing the
interval restarts the current countdown, it doesn't retroactively move the next fire.

## `TimerSettings` & `ISettingsStore`

```csharp
interface ISettingsStore
{
    bool TryGet(string key, out object value);   // returns int/double/bool/string, or false
    void Set(string key, object value);
}
```

`TimerSettings` fields: `IntervalSeconds` (default 60), `SoundKey` (default `"beep"`),
`Volume` (0–1, default 0.8), `AutoStart` (default false), `TransparentBackground` (default
true — used only by the widget).

- `Load(store)` reads four keys, coerces each with `Convert.ToInt32/ToDouble/ToBoolean` inside
  a try/catch (a bad value falls back to the default), then `Normalize()`.
- `Save(store)` calls `Normalize()` then writes the four keys as their native types.
- `Normalize()` clamps the interval to `[1, 86400]`, volume to `[0,1]`, and resolves
  `SoundKey` through `SoundCatalog.FromKey` (an unknown key becomes `"beep"`).

Keys used across the codebase: `IntervalSeconds`, `SoundKey`, `Volume`, `AutoStart`
(all apps); `WindowX`, `WindowY` (overlay only); `IsRunning`, `NextFireUtcTicks` (widget only).

## `SoundCatalog`

```csharp
sealed class SoundInfo { string Key; string DisplayName; string FileName; string AppxUri; }
static class SoundCatalog {
    ReadOnlyCollection<SoundInfo> All;   // beep, doublebeep, chime, bell, alarm
    SoundInfo Default;                   // All[0] == "beep"
    SoundInfo FromKey(string key);       // case-insensitive; unknown -> Default
}
```

`SoundInfo.ToString()` returns `DisplayName`, so a `ComboBox` bound directly to `All` shows
friendly names without a `DisplayMemberPath` (which matters for the widget — `DisplayMemberPath`
uses reflection). `AppxUri` (`ms-appx:///Assets/Sounds/<file>`) is used by the packaged widget;
the unpackaged overlay builds a filesystem path instead.

**To add a sound:** add a `.wav` to both apps' `Assets/Sounds/`, add a `SoundInfo` line here,
and add the `<Content Include>` line to `IntervalTimerWidget.csproj` (the overlay globs
`Assets\Sounds\*.wav` automatically).

## `ISoundPlayer`

```csharp
interface ISoundPlayer { void Play(SoundInfo sound, double volume); }
```

Not implemented in Core because the audio APIs (`Windows.Media.Playback`) aren't in
`netstandard2.0`. See `MediaSoundPlayer` (overlay) and `UwpSoundPlayer` (widget).

## Tests — `IntervalTimer.Core.Tests`

xUnit, `net8.0`, **not** in the solution (run with `dotnet test IntervalTimer.Core.Tests`).
Four tests in `IntervalTimerEngineTests.cs`:

| Test | Asserts |
| --- | --- |
| `Interval_is_clamped_to_the_supported_range` | `TimeSpan.Zero` → 1 s, `7 days` → 24 h |
| `Remaining_reports_the_full_interval_while_stopped` | `Remaining == Interval` before `Start()` |
| `Elapsed_fires_once_per_interval_without_drift` | ~4 `Elapsed` over 4.6 s at a 1 s interval |
| `Long_gap_produces_a_single_resynced_beep` | `ResumeAt(5 min ago)` → one `Elapsed`, `Resynced == true` |

The timing tests use real wall-clock sleeps, so they take a few seconds and assert ranges, not
exact counts.
