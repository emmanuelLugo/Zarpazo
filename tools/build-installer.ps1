# Builds publish\Zarpazo-Setup-<version>.exe with Inno Setup 6.
# It publishes the self-contained Zarpazo.exe first, so the installer always carries the current code.
# Run from anywhere: pwsh tools\build-installer.ps1   (add -SkipPublish to reuse the existing publish\Zarpazo.exe)
param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed (winget install JRSoftware.InnoSetup).' }

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1')
}
if (-not (Test-Path (Join-Path $root 'publish\Zarpazo.exe'))) { throw 'publish\Zarpazo.exe is missing.' }

# One source of truth for the version: the project file.
$version = ([xml](Get-Content (Join-Path $root 'Zarpazo\Zarpazo.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\Zarpazo.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

Get-ChildItem (Join-Path $root 'publish') -Filter 'Zarpazo-Setup-*.exe' | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
