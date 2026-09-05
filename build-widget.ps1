<#
.SYNOPSIS
    Build (and optionally deploy) IntervalTimerWidget - the classic UWP Xbox Game Bar widget.

    This project is intentionally NOT in IntervalTimerGameBar.sln: it's a legacy non-SDK
    UWP project that Visual Studio 2026's IDE cannot load ("Unexpected null value of type
    'IVsHierarchy'"), but which builds fine from MSBuild. Use this script instead of the IDE.

.PARAMETER Configuration
    Debug (default) or Release. Release uses the .NET Native toolchain and is slow.

.PARAMETER Platform
    x64 (default) or ARM64.

.PARAMETER Deploy
    After building, register the loose AppX layout for the current user (equivalent to
    Visual Studio's F5 deploy). Then open Game Bar with Win+G and pick "Interval Timer".

.PARAMETER Rebuild
    Clean before building.

.EXAMPLE
    .\build-widget.ps1
    .\build-widget.ps1 -Deploy
    .\build-widget.ps1 -Configuration Release -Platform ARM64
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$Deploy,

    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'IntervalTimerWidget\IntervalTimerWidget.csproj'

if (-not (Test-Path $project)) {
    throw "Can't find $project"
}

# --- Locate MSBuild ---
# MUST be Visual Studio 2022's MSBuild. VS 2026 (18.x) compiles the project but its Roslyn
# emits an assembly the classic-UWP .NET Core 2.2 runtime can't load (BadImageFormatException
# at launch -> the widget never appears in Game Bar). VS 2022 (17.x) is compatible.
function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        # -version "[17.0,18.0)" restricts to VS 2022
        $path = & $vswhere -products * -version '[17.0,18.0)' `
            -requires Microsoft.VisualStudio.Workload.Universal `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path -and (Test-Path $path)) { return $path }

        $any2022 = & $vswhere -products * -version '[17.0,18.0)' `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($any2022 -and (Test-Path $any2022)) {
            Write-Warning "VS 2022 found but without the Universal Windows Platform workload - the build may fail. Install it via the VS Installer."
            return $any2022
        }
    }
    foreach ($f in @(
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )) { if (Test-Path $f) { return $f } }

    throw @"
Visual Studio 2022 with the 'Universal Windows Platform development' workload is required to
build this widget (VS 2026 produces an assembly the UWP runtime can't load). Install it:

  winget install Microsoft.VisualStudio.2022.Community
  & "`${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe" modify ``
    --installPath "`$env:ProgramFiles\Microsoft Visual Studio\2022\Community" ``
    --add Microsoft.VisualStudio.Workload.Universal --includeRecommended --norestart --passive
"@
}

$msbuild = Find-MSBuild
Write-Host "MSBuild : $msbuild" -ForegroundColor Cyan
Write-Host "Project : $project"
Write-Host "Config  : $Configuration | $Platform`n"

# Restore and build MUST be separate MSBuild invocations for classic UWP - a combined
# '/t:Restore;Build' reuses pre-restore evaluation state and fails with spurious
# ".NET Framework could not be found" errors.
$common = @(
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    '/nologo',
    '/verbosity:minimal'
)

Write-Host "Restoring..." -ForegroundColor Cyan
& $msbuild $project '/t:Restore' @common
if ($LASTEXITCODE -ne 0) { throw "Restore failed (exit $LASTEXITCODE)." }

$buildTarget = if ($Rebuild) { 'Rebuild' } else { 'Build' }
Write-Host "`nBuilding ($buildTarget)..." -ForegroundColor Cyan
& $msbuild $project "/t:$buildTarget" @common
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

$binDir = Join-Path $root "IntervalTimerWidget\bin\$Platform\$Configuration"
$appxManifest = Join-Path $binDir 'AppxManifest.xml'
$msix = Get-ChildItem (Join-Path $root 'IntervalTimerWidget\AppPackages') -Recurse -Filter '*.msix' -EA 0 |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host "`nBuild OK." -ForegroundColor Green
if (Test-Path $appxManifest) { Write-Host "Loose layout : $appxManifest" }
if ($msix)                   { Write-Host "MSIX package : $($msix.FullName)" }

if ($Deploy) {
    if (-not (Test-Path $appxManifest)) {
        throw "Expected loose layout not found at $appxManifest"
    }

    # The Debug package depends on the .NET Core UWP framework packages; install them
    # from the generated Dependencies folder if they're not already on the machine.
    $depDir = Join-Path $root "IntervalTimerWidget\AppPackages"
    Get-ChildItem $depDir -Recurse -Filter "*.appx" -EA 0 |
        Where-Object { $_.FullName -match "\\Dependencies\\$Platform\\" } |
        ForEach-Object {
            Write-Host "Dependency: $($_.Name)" -ForegroundColor DarkGray
            try { Add-AppxPackage -Path $_.FullName -EA Stop } catch { }  # already-installed is fine
        }

    # A dev (loose) registration can't be replaced in place when the manifest changed but the
    # version didn't ("package is already installed. Increment the version number..."), so
    # remove the old registration first.
    Get-AppxPackage -Name 'JKara.IntervalTimerWidget' -EA 0 | ForEach-Object {
        Write-Host "Removing previous registration $($_.Version)..." -ForegroundColor DarkGray
        Remove-AppxPackage -Package $_.PackageFullName -EA 0
    }

    Write-Host "`nRegistering widget for the current user..." -ForegroundColor Cyan
    Add-AppxPackage -Register $appxManifest

    # Game Bar caches its widget list - restart it so the (re)deployed widget shows up.
    Get-Process -EA 0 |
        Where-Object { $_.Name -match 'GameBar|XboxGameBarWidgets|GameBarFTServer' } |
        Stop-Process -Force -EA 0
    Write-Host "Restarted Game Bar." -ForegroundColor DarkGray

    Write-Host "`nDeployed. Press Win+G, open the widget menu (the '::' icon), pick 'Interval Timer'." -ForegroundColor Green
    Write-Host "Pin it (pin icon in its title bar) to keep it beeping after the overlay closes." -ForegroundColor Green
}
