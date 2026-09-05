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

# --- Locate MSBuild (needs VS MSBuild, not 'dotnet' - classic UWP targets) ---
function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -prerelease -products * `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path -and (Test-Path $path)) { return $path }
    }
    $fallbacks = @(
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($f in $fallbacks) { if (Test-Path $f) { return $f } }
    throw "MSBuild not found. Install Visual Studio with the 'Universal Windows Platform development' workload."
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

    Write-Host "`nRegistering widget for the current user..." -ForegroundColor Cyan
    Add-AppxPackage -Register $appxManifest -ForceUpdateFromAnyVersion

    # Game Bar caches its widget list - restart it so the (re)deployed widget shows up.
    Get-Process -EA 0 |
        Where-Object { $_.Name -match 'GameBar|XboxGameBarWidgets|GameBarFTServer' } |
        Stop-Process -Force -EA 0
    Write-Host "Restarted Game Bar." -ForegroundColor DarkGray

    Write-Host "`nDeployed. Press Win+G, open the widget menu (the '::' icon), pick 'Interval Timer'." -ForegroundColor Green
    Write-Host "Pin it (pin icon in its title bar) to keep it beeping after the overlay closes." -ForegroundColor Green
}
