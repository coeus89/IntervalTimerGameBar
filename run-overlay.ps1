<#
.SYNOPSIS
    Build and run IntervalTimerOverlay - the standalone WinUI 3 app.

    It builds unpackaged, so the output is a plain IntervalTimerOverlay.exe you can
    double-click. Needs the Windows App SDK 1.6 runtime on the machine (Visual Studio
    installs it). Settings are stored in %LOCALAPPDATA%\IntervalTimerOverlay\settings.json.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER Portable
    Publish a self-contained folder that runs on machines without the .NET or
    Windows App SDK runtimes (larger; goes to bin\<cfg>\...\publish).

.PARAMETER NoLaunch
    Build only, don't start the app.

.EXAMPLE
    .\run-overlay.ps1
    .\run-overlay.ps1 -Configuration Release
    .\run-overlay.ps1 -Portable
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [switch]$Portable,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'IntervalTimerOverlay\IntervalTimerOverlay.csproj'
$binRoot = Join-Path $root 'IntervalTimerOverlay\bin'
$rid = 'win-x64'

# so we can pick the exe produced by *this* run
$before = @{}
Get-ChildItem $binRoot -Recurse -Filter 'IntervalTimerOverlay.exe' -EA 0 |
    ForEach-Object { $before[$_.FullName] = $_.LastWriteTimeUtc }

if ($Portable) {
    Write-Host "Publishing portable build ($Configuration)..." -ForegroundColor Cyan
    & dotnet publish $project -c $Configuration -r $rid --self-contained `
        -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
        --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)." }
}
else {
    Write-Host "Building IntervalTimerOverlay ($Configuration)..." -ForegroundColor Cyan
    & dotnet build $project -c $Configuration -p:Platform=x64 --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}

# Find the exe (publish and build land in different folders depending on the SDK,
# so don't hard-code the path). Prefer one this run just wrote; else newest of the mode.
function Find-Exe([bool]$onlyFresh) {
    Get-ChildItem $binRoot -Recurse -Filter 'IntervalTimerOverlay.exe' -EA 0 |
        Where-Object {
            $isPublish = $_.FullName -like '*\publish\*'
            ($isPublish -eq [bool]$Portable) -and
            (-not $onlyFresh -or -not $before.ContainsKey($_.FullName) -or
             $_.LastWriteTimeUtc -gt $before[$_.FullName])
        } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$exe = Find-Exe $true
if (-not $exe) { $exe = Find-Exe $false }
if (-not $exe -or -not (Test-Path $exe)) { throw "IntervalTimerOverlay.exe not found under $binRoot" }
Write-Host "`nExe: $exe" -ForegroundColor Green

if (-not $NoLaunch) {
    Write-Host "Launching..." -ForegroundColor Cyan
    Start-Process $exe
}
