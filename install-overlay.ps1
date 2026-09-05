<#
.SYNOPSIS
    Install IntervalTimerOverlay for the current user (no admin, no MSIX).

    Publishes a self-contained build, copies it to
    %LOCALAPPDATA%\Programs\IntervalTimerOverlay, and adds a Start-menu shortcut so you
    can search for "Interval Timer", pin it to the taskbar, etc. Settings live in
    %LOCALAPPDATA%\IntervalTimerOverlay\settings.json and survive reinstalls.

.PARAMETER Startup
    Also launch it automatically when you sign in.

.PARAMETER Uninstall
    Remove the app, the shortcuts, and (unless -KeepSettings) the settings file.

.PARAMETER KeepSettings
    With -Uninstall, leave settings.json in place.

.EXAMPLE
    .\install-overlay.ps1
    .\install-overlay.ps1 -Startup
    .\install-overlay.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Startup,
    [switch]$Uninstall,
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$appName   = 'Interval Timer'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\IntervalTimerOverlay'
$exeName   = 'IntervalTimerOverlay.exe'
$startMenu = Join-Path $env:APPDATA   'Microsoft\Windows\Start Menu\Programs'
$startupDir = Join-Path $env:APPDATA  'Microsoft\Windows\Start Menu\Programs\Startup'
$smLink    = Join-Path $startMenu "$appName.lnk"
$suLink    = Join-Path $startupDir "$appName.lnk"
$settings  = Join-Path $env:LOCALAPPDATA 'IntervalTimerOverlay'

function Stop-Running {
    Get-Process IntervalTimerOverlay -EA 0 | Stop-Process -Force -EA 0
    Start-Sleep -Milliseconds 400
}

function New-Shortcut($linkPath, $targetExe) {
    $sh = New-Object -ComObject WScript.Shell
    $sc = $sh.CreateShortcut($linkPath)
    $sc.TargetPath       = $targetExe
    $sc.WorkingDirectory = Split-Path $targetExe
    $sc.IconLocation     = $targetExe
    $sc.Description       = 'Beeps every X seconds - always on top'
    $sc.Save()
}

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
    Stop-Running
    Remove-Item $smLink, $suLink -Force -EA 0
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
    if (-not $KeepSettings -and (Test-Path $settings)) { Remove-Item $settings -Recurse -Force }
    Write-Host "Uninstalled." -ForegroundColor Green
    return
}

# ---------------------------------------------------------------- install
$project = Join-Path $root 'IntervalTimerOverlay\IntervalTimerOverlay.csproj'
$pubDir  = Join-Path $root 'IntervalTimerOverlay\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish'

Write-Host "Publishing self-contained build..." -ForegroundColor Cyan
& dotnet publish $project -c Release -r win-x64 --self-contained `
    -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
    --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $pubDir $exeName))) { throw "Published exe not found in $pubDir" }

Write-Host "Installing to $installDir ..." -ForegroundColor Cyan
Stop-Running
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $pubDir '*') $installDir -Recurse -Force

$exe = Join-Path $installDir $exeName
New-Shortcut $smLink $exe
Write-Host "Added Start-menu entry '$appName'." -ForegroundColor Green

if ($Startup) {
    New-Shortcut $suLink $exe
    Write-Host "Will start automatically at sign-in." -ForegroundColor Green
}
else {
    Remove-Item $suLink -Force -EA 0
}

Write-Host "`nDone. Launching..." -ForegroundColor Green
Start-Process $exe
Write-Host "Uninstall later with:  .\install-overlay.ps1 -Uninstall"
