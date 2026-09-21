# Privacy Policy — Tiberius Interval Timer

**Effective date:** 2026-09-20

This policy covers both apps published from this repository under the publisher **TiberiusCN**:

- **Tiberius Interval Timer** (`IntervalTimerOverlay`) — the standalone desktop overlay.
- **Tiberius Interval Timer Widget** (`IntervalTimerWidget`) — the Xbox Game Bar widget.

They share one codebase and one policy because they share the same data practices: none.

## Summary

Neither app collects, transmits, stores remotely, or shares any personal data. Everything
either app does happens entirely on your own device.

## What data is stored, and where

Both apps save your preferences so they're remembered between launches:

| App | What's stored | Where |
| --- | --- | --- |
| Overlay | interval length, selected sound, volume, auto-start flag, transparent-background flag, window position | `%LOCALAPPDATA%\IntervalTimerOverlay\settings.json` (plain JSON, on your machine only) |
| Widget | interval length, selected sound, volume, auto-start flag | Windows' per-app `ApplicationData.LocalSettings` store (isolated to the widget's own package, on your machine only) |

None of these fields identify you personally — they're timer configuration, nothing else.
Nothing is logged, screenshotted, or recorded beyond this settings file.

**Removing this data:**
- Overlay: `.\install-overlay.ps1 -Uninstall` deletes `settings.json` automatically (pass
  `-KeepSettings` to keep it instead), or delete the folder yourself at any time.
- Widget: uninstalling it through Windows Settings removes its isolated settings storage
  automatically, standard for all UWP/MSIX apps.

## Network access

Neither app makes network requests. There is no telemetry, no analytics, no crash reporting,
no update checker, no ads, and no sign-in of any kind.

The widget's app manifest declares the `internetClient` Windows capability (a default left
over from the Game Bar widget project template), but no code path in the app actually uses
it — no network call exists anywhere in the codebase. This capability may be removed in a
future update.

## Third-party services and SDKs

Neither app uses any third-party analytics, advertising, or backend service. Dependencies are
limited to Microsoft's own platform libraries (Windows App SDK / WinUI, the classic UWP
framework, and `H.NotifyIcon.WinUI`, a UI-only system-tray-icon control with no network or
telemetry code of its own).

## Permissions

- **Overlay:** none beyond running as a normal desktop app.
- **Widget:** `internetClient` (declared, unused — see above) and standard Game Bar widget
  hosting permissions required by the `Microsoft.Gaming.XboxGameBar` SDK to appear in the
  `Win+G` overlay.

## Children's privacy

Neither app is directed at children, and neither collects any information from anyone,
including children, because neither app collects information at all.

## Changes to this policy

If this policy changes, the updated version will be committed to this repository
([PRIVACY.md](PRIVACY.md)) and reflected in the Store listing before the change takes effect.

## Contact

Questions about this policy or the apps: coeus899@gmail.com, or open an issue on this
repository.
