<#
.SYNOPSIS
    Build everything: the modern solution (Core + Overlay + Tests) and, unless -SkipWidget,
    the classic UWP Game Bar widget.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER Platform
    x64, ARM64, or x86. Defaults to this machine's architecture.

.PARAMETER SkipWidget
    Don't build IntervalTimerWidget (it needs VS 2022 + the UWP workload).

.PARAMETER SkipTests
    Build only, don't run the xUnit tests.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release -SkipWidget
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [ValidateSet('x64', 'ARM64', 'x86')] [string]$Platform = '',
    [switch]$SkipWidget,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# A bare solution build used to select ARM64 (it was the first configuration) and
# produce an overlay exe that will not start on x64. Always pass a platform.
if (-not $Platform) {
    switch ($env:PROCESSOR_ARCHITECTURE) {
        'ARM64' { $Platform = 'ARM64' }
        'X86'   { $Platform = 'x86' }
        default { $Platform = 'x64' }
    }
}

Write-Host "== Building IntervalTimerWinUI.sln ($Configuration | $Platform) ==" -ForegroundColor Cyan
& dotnet build (Join-Path $root 'IntervalTimerWinUI.sln') -c $Configuration -p:Platform=$Platform --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }

if (-not $SkipTests) {
    Write-Host "`n== Running tests ==" -ForegroundColor Cyan
    & dotnet test (Join-Path $root 'IntervalTimer.Core.Tests') -c $Configuration --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (-not $SkipWidget) {
    Write-Host "`n== Building the Game Bar widget ==" -ForegroundColor Cyan
    & (Join-Path $root 'build-widget.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Widget build failed." }
}

Write-Host "`nAll green." -ForegroundColor Green
