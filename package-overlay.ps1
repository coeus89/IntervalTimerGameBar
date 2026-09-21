<#
.SYNOPSIS
    Build MSIX packages of the overlay for one or more architectures - the Store-submission
    artifact, not the plain unpackaged .exe that run-overlay.ps1 produces.

    Equivalent to Visual Studio's "Create App Packages" wizard's architecture-selection step,
    run from the command line (useful because that VS 2026 menu has been flaky).

.PARAMETER Platforms
    x64, ARM64, x86 - any combination. Default: x64, ARM64 (what Partner Center expects).

.PARAMETER Configuration
    Debug or Release (default).

.EXAMPLE
    .\package-overlay.ps1
    .\package-overlay.ps1 -Platforms x64
    .\package-overlay.ps1 -Platforms x64,ARM64,x86 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64', 'ARM64')]
    [string[]]$Platforms = @('x64', 'ARM64'),

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'IntervalTimerOverlay\IntervalTimerOverlay.csproj'

$results = foreach ($plat in $Platforms) {
    Write-Host "`n== Packaging $plat ($Configuration) ==" -ForegroundColor Cyan
    dotnet build $project -c $Configuration `
        "-p:Platform=$plat" `
        '-p:WindowsPackageType=MSIX' `
        '-p:GenerateAppxPackageOnBuild=true' `
        --nologo -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$plat package failed (exit $LASTEXITCODE)." }

    Get-ChildItem (Join-Path $root 'IntervalTimerOverlay\AppPackages') -Recurse -Filter '*.msix' -EA 0 |
        Where-Object { $_.FullName -match "_$plat(\.|_)" -or $_.FullName -match "_$plat\\" } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

Write-Host "`nPackages:" -ForegroundColor Green
$results | ForEach-Object { Write-Host "  $($_.FullName)" }

Write-Host "`nUpload each .msix to Partner Center's Packages tab together (same version," -ForegroundColor DarkGray
Write-Host "one file per architecture). Identity in Package.appxmanifest must already be" -ForegroundColor DarkGray
Write-Host "associated with your reserved Store app name before Partner Center will accept them." -ForegroundColor DarkGray
